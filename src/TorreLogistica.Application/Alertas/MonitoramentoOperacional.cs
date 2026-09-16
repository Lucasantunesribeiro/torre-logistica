using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Application.Previsao;
using TorreLogistica.Domain.Abstracoes.Identificadores;
using TorreLogistica.Domain.Abstracoes.Tempo;
using TorreLogistica.Domain.Alertas;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Ocorrencias;
using TorreLogistica.Domain.Previsao;
using TorreLogistica.Domain.Rastreamento;
using TorreLogistica.Domain.Rotas;

namespace TorreLogistica.Application.Alertas;

/// <summary>
/// O motor de alertas: avalia as regras tipadas para a rota e aplica o ciclo de vida de cada alerta.
/// </summary>
/// <remarks>
/// <para>
/// Roda no processador em segundo plano, logo depois do recálculo da previsão da mesma rota — risco e
/// atraso vêm da previsão — e na reavaliação periódica, que é quem percebe motorista offline: o problema,
/// aí, é justamente a ausência de evento.
/// </para>
/// <para>
/// Cada regra produz uma constatação por alvo, valendo ou não. Toda constatação passa pelo alerta da mesma
/// chave: o que vale abre ou mantém, o que deixou de valer resolve. Alerta aberto desta rota que nenhuma
/// regra constatou — o motorista trocado, a entrega que saiu — é constatado como ausente e resolve.
/// </para>
/// </remarks>
public sealed class MonitoramentoOperacional(
    IContextoDePersistencia contexto,
    IGeradorDeIdentificador identificadores,
    IRelogio relogio,
    IOptions<OpcoesDeAlertas> opcoes,
    MetricasDeAlertas metricas,
    ILogger<MonitoramentoOperacional> log)
{
    /// <summary>Avalia os alertas da rota.</summary>
    /// <returns>Quantos eventos de alerta foram registrados.</returns>
    public async Task<int> AvaliarRotaAsync(Guid rotaId, CancellationToken cancelamento)
    {
        var valores = opcoes.Value;
        var agora = relogio.AgoraUtc;

        var rota = await contexto.Rotas
            .AsNoTracking()
            .Include(item => item.Paradas)
            .SingleOrDefaultAsync(item => item.Id == rotaId, cancelamento)
            .ConfigureAwait(false);

        if (rota is null)
        {
            return 0;
        }

        var constatacoes = await ConstatarAsync(rota, valores, agora, cancelamento).ConfigureAwait(false);

        try
        {
            return await contexto.ExecutarEmTransacaoAsync(
                cancelamentoDaTentativa => AplicarAsync(rota.Id, constatacoes, valores, agora, cancelamentoDaTentativa),
                cancelamento).ConfigureAwait(false);
        }
        catch (DbUpdateException excecao) when (excecao is DbUpdateConcurrencyException
            || contexto.EhViolacaoDeUnicidade(excecao, NomesDeRestricoes.AlertaAbertoPorChave)
            || contexto.EhViolacaoDeUnicidade(excecao, NomesDeRestricoes.SequenciaDoEventoDoAlerta))
        {
            // Outro processo, ou o operador, gravou o mesmo alerta ao mesmo tempo. A próxima avaliação parte do
            // estado confirmado.
            log.LogInformation("Alertas da rota {RotaId} alterados ao mesmo tempo por outra gravação; esta avaliação foi descartada.", rota.Id);
            return 0;
        }
    }

    private async Task<List<Constatacao>> ConstatarAsync(Rota rota, OpcoesDeAlertas valores, DateTimeOffset agora, CancellationToken cancelamento)
    {
        var emAndamento = rota.Status == StatusDaRota.EmAndamento;
        var ativas = rota.ObterParadasAtivas().Select(parada => parada.EntregaId).ToHashSet();

        // Entregas com alerta aberto nesta rota continuam avaliadas mesmo depois de a rota encerrar as paradas.
        var comAlertaAberto = await contexto.AlertasOperacionais
            .AsNoTracking()
            .Where(alerta => alerta.RotaId == rota.Id && alerta.Estado == EstadoDoAlerta.Aberto && alerta.EntregaId != null)
            .Select(alerta => alerta.EntregaId!.Value)
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

        var ids = ativas.Concat(comAlertaAberto).Distinct().ToArray();
        var entregas = await contexto.Entregas
            .AsNoTracking()
            .Where(entrega => ids.Contains(entrega.Id))
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);
        var previsoes = await contexto.PrevisoesDaEntrega
            .AsNoTracking()
            .Where(previsao => ids.Contains(previsao.EntregaId))
            .ToDictionaryAsync(previsao => previsao.EntregaId, cancelamento)
            .ConfigureAwait(false);

        // A crítica mais recente de cada entrega: é ela que a torre precisa ver.
        var criticas = await contexto.Ocorrencias
            .AsNoTracking()
            .Where(ocorrencia => ids.Contains(ocorrencia.EntregaId) && ocorrencia.Severidade == SeveridadeDaOcorrencia.Critica)
            .GroupBy(ocorrencia => ocorrencia.EntregaId)
            .Select(grupo => grupo.OrderByDescending(ocorrencia => ocorrencia.OcorridaEm).ThenByDescending(ocorrencia => ocorrencia.Id).First())
            .ToDictionaryAsync(ocorrencia => ocorrencia.EntregaId, cancelamento)
            .ConfigureAwait(false);

        var constatacoes = new List<Constatacao>();

        foreach (var entrega in entregas)
        {
            var previsao = previsoes.GetValueOrDefault(entrega.Id);
            var explicacao = previsao is { Ativa: true } ? ExplicacaoDaPrevisao.DaPrevisaoAtual(previsao) : null;

            constatacoes.Add(RegrasDeAlerta.RiscoDeAtraso(entrega.Id, rota.Id, previsao, explicacao));
            constatacoes.Add(RegrasDeAlerta.EntregaAtrasada(entrega.Id, rota.Id, previsao, explicacao));
            constatacoes.Add(RegrasDeAlerta.TentativasExcedidas(entrega, rota.Id, valores.LimiteDeTentativas));
            constatacoes.Add(RegrasDeAlerta.OcorrenciaCritica(entrega, rota.Id, criticas.GetValueOrDefault(entrega.Id)));
        }

        if (rota.MotoristaId is { } motoristaId && rota.IniciadaEm is { } iniciadaEm)
        {
            var pendentes = entregas.Where(entrega => ativas.Contains(entrega.Id) && RegrasDaEntrega.EstaEmExecucao(entrega.Status)).ToList();
            var inicioDaColeta = iniciadaEm - PoliticaDeLocalizacao.ToleranciaAntesDoInicioDaRota;

            var ultimaCaptura = emAndamento
                ? await contexto.PosicoesAtuais
                    .AsNoTracking()
                    .Where(posicao => posicao.MotoristaId == motoristaId && posicao.CapturadaEm >= inicioDaColeta)
                    .Select(posicao => (DateTimeOffset?)posicao.CapturadaEm)
                    .SingleOrDefaultAsync(cancelamento)
                    .ConfigureAwait(false)
                : null;

            var offline = RegrasDeAlerta.MotoristaOffline(
                motoristaId, rota.Id, emAndamento, iniciadaEm, ultimaCaptura, pendentes.Count, agora, valores.TempoSemPosicaoParaOffline);
            constatacoes.Add(offline);

            var permanencia = emAndamento && ultimaCaptura is not null && !offline.Condicao
                ? await contexto.AvaliarPermanenciaAsync(motoristaId, inicioDaColeta, valores.RaioDeImobilidadeEmMetros, cancelamento).ConfigureAwait(false)
                : null;

            constatacoes.Add(RegrasDeAlerta.ParadoTempoExcessivo(
                motoristaId,
                rota.Id,
                emAndamento && pendentes.Count > 0,
                offline.Condicao,
                permanencia,
                pendentes.Any(entrega => entrega.Status == StatusDaEntrega.ProximaDoDestino),
                valores.TempoParadoParaAlerta,
                valores.TempoParadoAtendendoParadaParaAlerta,
                valores.RaioDeImobilidadeEmMetros));
        }

        return constatacoes;
    }

    private async Task<int> AplicarAsync(
        Guid rotaId,
        IReadOnlyList<Constatacao> constatacoes,
        OpcoesDeAlertas valores,
        DateTimeOffset agora,
        CancellationToken cancelamento)
    {
        var chaves = constatacoes.Select(constatacao => constatacao.Chave).ToArray();

        var existentes = await contexto.AlertasOperacionais
            .Where(alerta => chaves.Contains(alerta.Chave) || (alerta.RotaId == rotaId && alerta.Estado == EstadoDoAlerta.Aberto))
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

        // Por chave, o aberto; sem aberto, o mais recente — que pode reabrir.
        var vigentes = existentes
            .GroupBy(alerta => alerta.Chave)
            .ToDictionary(
                grupo => grupo.Key,
                grupo => grupo.OrderByDescending(alerta => alerta.Estado == EstadoDoAlerta.Aberto).ThenByDescending(alerta => alerta.AbertoEm).First());

        var eventos = 0;

        foreach (var constatacao in constatacoes)
        {
            eventos += Aplicar(vigentes.GetValueOrDefault(constatacao.Chave), constatacao, valores, agora);
        }

        var constatadas = chaves.ToHashSet(StringComparer.Ordinal);
        foreach (var orfao in existentes.Where(alerta => alerta.Estado == EstadoDoAlerta.Aberto && !constatadas.Contains(alerta.Chave)))
        {
            var ausente = Constatacao.Criar(orfao.Tipo, orfao.EntregaId, orfao.MotoristaId, orfao.RotaId, false, new { foraDaAvaliacaoDaRota = true });
            eventos += Aplicar(orfao, ausente, valores, agora);
        }

        await contexto.SaveChangesAsync(cancelamento).ConfigureAwait(false);
        return eventos;
    }

    private int Aplicar(AlertaOperacional? alerta, Constatacao constatacao, OpcoesDeAlertas valores, DateTimeOffset agora)
    {
        if (alerta is null)
        {
            return constatacao.Condicao ? Abrir(constatacao, agora) : 0;
        }

        var (resultado, evento) = alerta.Constatar(constatacao, valores.JanelaDeReabertura, identificadores.Novo(), agora);

        switch (resultado)
        {
            case ResultadoDaConstatacao.ExigeNovoAlerta:
                return Abrir(constatacao, agora);
            case ResultadoDaConstatacao.Resolvido:
                metricas.RegistrarResolucao(alerta.Tipo, FormaDeResolucao.Automatica);
                break;
            case ResultadoDaConstatacao.Reaberto:
                metricas.RegistrarReabertura(alerta.Tipo);
                log.LogInformation("Alerta {AlertaId} ({Tipo}) reaberto.", alerta.Id, alerta.Tipo);
                break;
            default:
                break;
        }

        if (evento is null)
        {
            return 0;
        }

        contexto.EventosDeAlerta.Add(evento);
        return 1;
    }

    private int Abrir(Constatacao constatacao, DateTimeOffset agora)
    {
        var organizacaoId = contexto.OrganizacaoDoTenant
            ?? throw new InvalidOperationException("Abertura de alerta exige tenant definido.");

        var (alerta, evento) = AlertaOperacional.Abrir(identificadores.Novo(), organizacaoId, constatacao, identificadores.Novo(), agora);
        contexto.AlertasOperacionais.Add(alerta);
        contexto.EventosDeAlerta.Add(evento);
        metricas.RegistrarAbertura(alerta.Tipo);

        log.LogInformation(
            "Alerta {AlertaId} aberto: {Tipo} ({Severidade}), entrega {EntregaId}, motorista {MotoristaId}.",
            alerta.Id, alerta.Tipo, alerta.Severidade, alerta.EntregaId, alerta.MotoristaId);

        return 1;
    }
}

/// <summary>Métricas de alertas, no medidor <see cref="NomeDoMedidor"/>, sem organização como dimensão.</summary>
public sealed class MetricasDeAlertas : IDisposable
{
    /// <summary>Nome do medidor.</summary>
    public const string NomeDoMedidor = "TorreLogistica.Alertas";

    private readonly Meter _medidor = new(NomeDoMedidor);
    private readonly Counter<long> _abertos;
    private readonly Counter<long> _resolvidos;
    private readonly Counter<long> _reabertos;

    /// <summary>Cria os instrumentos.</summary>
    public MetricasDeAlertas()
    {
        _abertos = _medidor.CreateCounter<long>("alerts.opened", "{alerta}", "Alertas abertos, por tipo.");
        _resolvidos = _medidor.CreateCounter<long>("alerts.resolved", "{alerta}", "Alertas resolvidos, por tipo e forma.");
        _reabertos = _medidor.CreateCounter<long>("alerts.reopened", "{alerta}", "Alertas reabertos, por tipo.");
    }

    /// <summary>Conta uma abertura.</summary>
    public void RegistrarAbertura(TipoDeAlerta tipo) => _abertos.Add(1, new KeyValuePair<string, object?>("tipo", tipo.ToString()));

    /// <summary>Conta uma reabertura.</summary>
    public void RegistrarReabertura(TipoDeAlerta tipo) => _reabertos.Add(1, new KeyValuePair<string, object?>("tipo", tipo.ToString()));

    /// <summary>Conta uma resolução.</summary>
    public void RegistrarResolucao(TipoDeAlerta tipo, FormaDeResolucao forma) =>
        _resolvidos.Add(1, new KeyValuePair<string, object?>("tipo", tipo.ToString()), new KeyValuePair<string, object?>("forma", forma.ToString()));

    /// <inheritdoc />
    public void Dispose() => _medidor.Dispose();
}
