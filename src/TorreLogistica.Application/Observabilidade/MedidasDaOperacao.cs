using System.ComponentModel.DataAnnotations;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Application.Alertas;
using TorreLogistica.Domain.Abstracoes.Tempo;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Previsao;
using TorreLogistica.Domain.Rotas;
using TorreLogistica.Domain.Webhooks;

namespace TorreLogistica.Application.Observabilidade;

/// <summary>Com que frequência o estado da operação é medido.</summary>
public sealed class OpcoesDeMedidas
{
    /// <summary>Seção correspondente na configuração.</summary>
    public const string Secao = "Torre:Medidas";

    /// <summary>Intervalo entre leituras do estado.</summary>
    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan Intervalo { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Medir o estado em segundo plano neste processo.</summary>
    public bool MedirEmSegundoPlano { get; set; } = true;
}

/// <summary>Fotografia do estado da operação num instante.</summary>
public sealed record RetratoDaOperacao(
    int MotoristasOnline,
    int MotoristasOffline,
    int EntregasEmRota,
    int EntregasEmRisco,
    int EntregasAtrasadas,
    int OutboxPendente,
    int WebhooksFalhados)
{
    /// <summary>Retrato vazio, antes da primeira leitura.</summary>
    public static readonly RetratoDaOperacao Vazio = new(0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// Medidas do estado da operação: quantos, agora.
/// </summary>
/// <remarks>
/// <para>
/// Contador conta evento; estas medem <b>situação</b> — quantos motoristas estão online neste instante,
/// quantas entregas correm risco de atraso, quanto ainda espera no outbox. São as perguntas que alguém faz
/// às três da manhã, e nenhuma delas se responde somando eventos passados.
/// </para>
/// <para>
/// Os instrumentos leem um retrato em memória, atualizado por um serviço em segundo plano. Consultar o
/// banco no momento em que o coletor raspa a métrica ligaria a saúde do sistema à frequência de raspagem
/// de quem observa — e um coletor mal configurado viraria carga.
/// </para>
/// </remarks>
public sealed class MedidasDaOperacao : IDisposable
{
    /// <summary>Nome do medidor.</summary>
    public const string NomeDoMedidor = "TorreLogistica.Operacao";

    private readonly Meter _medidor = new(NomeDoMedidor);
    private readonly UpDownCounter<long> _conexoesDoConsole;
    private volatile RetratoDaOperacao _retrato = RetratoDaOperacao.Vazio;

    /// <summary>Cria os instrumentos.</summary>
    public MedidasDaOperacao()
    {
        _medidor.CreateObservableGauge("drivers.online", () => _retrato.MotoristasOnline, "{motorista}", "Motoristas em rota com posição recente.");
        _medidor.CreateObservableGauge("drivers.offline", () => _retrato.MotoristasOffline, "{motorista}", "Motoristas em rota sem posição recente.");
        _medidor.CreateObservableGauge("deliveries.in_route", () => _retrato.EntregasEmRota, "{entrega}", "Entregas a caminho ou próximas do destino.");
        _medidor.CreateObservableGauge("deliveries.at_risk", () => _retrato.EntregasEmRisco, "{entrega}", "Entregas com risco de furar a janela prometida.");
        _medidor.CreateObservableGauge("deliveries.late", () => _retrato.EntregasAtrasadas, "{entrega}", "Entregas que já passaram da janela prometida.");
        _medidor.CreateObservableGauge("outbox.pending", () => _retrato.OutboxPendente, "{mensagem}", "Eventos no outbox ainda não despachados.");
        _medidor.CreateObservableGauge("webhook.failures", () => _retrato.WebhooksFalhados, "{entrega}", "Entregas de webhook que esgotaram as tentativas.");

        _conexoesDoConsole = _medidor.CreateUpDownCounter<long>(
            "signalr.connections", "{conexao}", "Consoles conectados ao canal de tempo real.");
    }

    /// <summary>O último retrato medido.</summary>
    public RetratoDaOperacao Retrato => _retrato;

    /// <summary>Publica um retrato novo.</summary>
    public void Atualizar(RetratoDaOperacao retrato) => _retrato = retrato ?? RetratoDaOperacao.Vazio;

    /// <summary>Um console entrou no canal de tempo real.</summary>
    public void ConexaoAberta() => _conexoesDoConsole.Add(1);

    /// <summary>Um console saiu do canal de tempo real.</summary>
    public void ConexaoFechada() => _conexoesDoConsole.Add(-1);

    /// <inheritdoc />
    public void Dispose()
    {
        _medidor.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Lê o estado da operação no banco.
/// </summary>
/// <remarks>
/// As consultas ignoram o filtro de organização de propósito: a medida é do processo, não de um tenant —
/// não há sessão por trás dela, e o filtro, que falha fechado, devolveria zero para tudo.
/// </remarks>
public sealed class LeituraDoEstadoDaOperacao(
    IContextoDePersistencia contexto,
    IRelogio relogio,
    IOptions<OpcoesDeAlertas> alertas)
{
    /// <summary>Tira o retrato.</summary>
    public async Task<RetratoDaOperacao> LerAsync(CancellationToken cancelamento)
    {
        var agora = relogio.AgoraUtc;
        var corteDeOffline = agora - alertas.Value.TempoSemPosicaoParaOffline;

        // Motorista "em operação" é o que está com rota em andamento; online é o que, além disso, deu
        // notícia há pouco. Quem não está em rota não é offline — está fora da jornada, e contá-lo como
        // problema encheria o painel de alarme falso todo fim de expediente.
        var motoristasEmRota = await contexto.Rotas
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(rota => rota.Status == StatusDaRota.EmAndamento && rota.MotoristaId != null)
            .Select(rota => rota.MotoristaId!.Value)
            .Distinct()
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

        var online = motoristasEmRota.Count == 0
            ? 0
            : await contexto.PosicoesAtuais
                .IgnoreQueryFilters()
                .AsNoTracking()
                .CountAsync(
                    posicao => motoristasEmRota.Contains(posicao.MotoristaId) && posicao.CapturadaEm >= corteDeOffline,
                    cancelamento)
                .ConfigureAwait(false);

        var emRota = await contexto.Entregas
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(
                entrega => entrega.Status == StatusDaEntrega.EmRota || entrega.Status == StatusDaEntrega.ProximaDoDestino,
                cancelamento)
            .ConfigureAwait(false);

        var previsoes = await contexto.PrevisoesDaEntrega
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(previsao => previsao.Ativa
                && (previsao.Situacao == SituacaoDoSla.Risco || previsao.Situacao == SituacaoDoSla.Atrasada))
            .GroupBy(previsao => previsao.Situacao)
            .Select(grupo => new { Situacao = grupo.Key, Quantidade = grupo.Count() })
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

        var pendentesNoOutbox = await contexto.Outbox
            .AsNoTracking()
            .CountAsync(mensagem => mensagem.DespachadaEm == null, cancelamento)
            .ConfigureAwait(false);

        var webhooksFalhados = await contexto.EntregasDeWebhook
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(entrega => entrega.Estado == EstadoDaEntregaDeWebhook.Falhada, cancelamento)
            .ConfigureAwait(false);

        return new RetratoDaOperacao(
            online,
            motoristasEmRota.Count - online,
            emRota,
            previsoes.FirstOrDefault(linha => linha.Situacao == SituacaoDoSla.Risco)?.Quantidade ?? 0,
            previsoes.FirstOrDefault(linha => linha.Situacao == SituacaoDoSla.Atrasada)?.Quantidade ?? 0,
            pendentesNoOutbox,
            webhooksFalhados);
    }
}
