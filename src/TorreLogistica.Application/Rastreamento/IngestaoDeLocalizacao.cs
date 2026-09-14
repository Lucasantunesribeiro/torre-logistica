using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TorreLogistica.Application.Abstracoes.Identidade;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Abstracoes.Identificadores;
using TorreLogistica.Domain.Abstracoes.Tempo;
using TorreLogistica.Domain.Rastreamento;

namespace TorreLogistica.Application.Rastreamento;

/// <summary>Posição como enviada pelo aplicativo. Campos opcionais para recusar item a item.</summary>
public sealed record PosicaoEnviada(
    Guid? EventoDeLocalizacaoId,
    double? Latitude,
    double? Longitude,
    double? PrecisaoEmMetros,
    DateTimeOffset? CapturadaEm,
    long? Sequencia,
    double? VelocidadeEmMetrosPorSegundo,
    double? DirecaoEmGraus);

/// <summary>Destino de uma posição enviada.</summary>
public enum ResultadoDaPosicao
{
    /// <summary>Entrou no histórico.</summary>
    Aceita = 1,

    /// <summary>Já tinha sido recebida; nada foi gravado de novo.</summary>
    Duplicada = 2,

    /// <summary>Recusada pela política; o motivo acompanha.</summary>
    Rejeitada = 3,
}

/// <summary>Resultado de uma posição do lote.</summary>
/// <param name="EventoDeLocalizacaoId">Identificador enviado.</param>
/// <param name="Resultado">Aceita, duplicada ou rejeitada.</param>
/// <param name="Qualidade">Qualidade, quando aceita.</param>
/// <param name="ForaDeOrdem">Aceita e confiável, mas mais antiga que a posição atual.</param>
/// <param name="AtualizouPosicaoAtual">Avançou a posição atual.</param>
/// <param name="Motivo">Código da recusa.</param>
public sealed record ResultadoDePosicaoResumo(
    Guid? EventoDeLocalizacaoId,
    ResultadoDaPosicao Resultado,
    QualidadeDaPosicao? Qualidade,
    bool ForaDeOrdem,
    bool AtualizouPosicaoAtual,
    string? Motivo);

/// <summary>Resultado do lote, na ordem do envio.</summary>
public sealed record ResultadoDoLote(
    int Recebidas,
    int Aceitas,
    int Duplicadas,
    int Rejeitadas,
    IReadOnlyList<ResultadoDePosicaoResumo> Resultados);

/// <summary>
/// Recebe telemetria do motorista: uma posição ou um lote de recuperação offline.
/// </summary>
/// <remarks>
/// <para>
/// O motorista vem da conta da sessão; o corpo não informa motorista nem organização. Cada posição
/// é avaliada pela política e ganha resultado próprio: um item ruim não derruba o lote, e o
/// aplicativo sabe exatamente o que reenviar e o que descartar.
/// </para>
/// <para>
/// O lote grava numa transação. Se ela falhar, o aplicativo reenvia o lote inteiro — e o que já
/// tinha sido gravado numa tentativa confirmada volta como duplicata, sem efeito repetido.
/// </para>
/// </remarks>
public sealed class IngestaoDeLocalizacao(
    IContextoDePersistencia contexto,
    IContextoDoUsuario usuarioAtual,
    IGeradorDeIdentificador identificadores,
    IRelogio relogio,
    MetricasDeRastreamento metricas,
    ILogger<IngestaoDeLocalizacao> log)
{
    /// <summary>Recebe as posições enviadas pelo motorista da sessão.</summary>
    public async Task<ResultadoDoLote> ReceberAsync(IReadOnlyList<PosicaoEnviada> enviadas, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(enviadas);

        var usuarioId = usuarioAtual.UsuarioId;
        var motorista = await contexto.Motoristas
            .AsNoTracking()
            .Where(item => item.UsuarioId == usuarioId)
            .Select(item => new { item.Id, item.OrganizacaoId })
            .SingleOrDefaultAsync(cancelamento)
            .ConfigureAwait(false)
            ?? throw ExcecaoDeDominio.NaoEncontrado("motorista_nao_associado", "Esta conta não está associada a um motorista.");

        var recebidaEm = relogio.AgoraUtc;
        var inicioDoHorizonte = recebidaEm - PoliticaDeLocalizacao.IdadeMaximaAceita - PoliticaDeLocalizacao.ToleranciaDepoisDaConclusaoDaRota;

        var janelas = (await contexto.Rotas
                .AsNoTracking()
                .Where(rota => rota.MotoristaId == motorista.Id
                    && rota.IniciadaEm != null
                    && (rota.ConcluidaEm == null || rota.ConcluidaEm >= inicioDoHorizonte))
                .Select(rota => new { rota.Id, rota.IniciadaEm, rota.ConcluidaEm })
                .ToListAsync(cancelamento)
                .ConfigureAwait(false))
            .Select(rota => new JanelaDeExecucaoDaRota(rota.Id, rota.IniciadaEm!.Value, rota.ConcluidaEm))
            .ToList();

        var itens = await contexto.ExecutarEmTransacaoAsync(
            async cancelamentoDaTentativa =>
            {
                await contexto.SerializarRastreamentoDoMotoristaAsync(motorista.Id, cancelamentoDaTentativa).ConfigureAwait(false);

                var resultados = new List<(ResultadoDePosicaoResumo Resumo, DateTimeOffset? CapturadaEm)>(enviadas.Count);

                foreach (var enviada in enviadas)
                {
                    PosicaoDoMotorista posicao;
                    try
                    {
                        posicao = Avaliar(enviada, motorista.Id, motorista.OrganizacaoId, recebidaEm, janelas);
                    }
                    catch (ExcecaoDeDominio recusa)
                    {
                        resultados.Add((new ResultadoDePosicaoResumo(
                            enviada.EventoDeLocalizacaoId, ResultadoDaPosicao.Rejeitada, null, false, false, recusa.Codigo), null));
                        continue;
                    }

                    var gravacao = await contexto.RegistrarPosicaoAsync(posicao, cancelamentoDaTentativa).ConfigureAwait(false);

                    resultados.Add(gravacao.Inserida
                        ? (new ResultadoDePosicaoResumo(
                            posicao.EventoDeLocalizacaoId,
                            ResultadoDaPosicao.Aceita,
                            posicao.Qualidade,
                            ForaDeOrdem: posicao.Qualidade == QualidadeDaPosicao.Confiavel && !gravacao.AtualizouPosicaoAtual,
                            gravacao.AtualizouPosicaoAtual,
                            null), posicao.CapturadaEm)
                        : (new ResultadoDePosicaoResumo(
                            posicao.EventoDeLocalizacaoId, ResultadoDaPosicao.Duplicada, null, false, false, null), null));
                }

                return resultados;
            },
            cancelamento).ConfigureAwait(false);

        // Métricas depois da confirmação: uma tentativa desfeita não conta.
        metricas.Registrar(itens, recebidaEm);

        var lote = new ResultadoDoLote(
            itens.Count,
            itens.Count(item => item.Resumo.Resultado == ResultadoDaPosicao.Aceita),
            itens.Count(item => item.Resumo.Resultado == ResultadoDaPosicao.Duplicada),
            itens.Count(item => item.Resumo.Resultado == ResultadoDaPosicao.Rejeitada),
            [.. itens.Select(item => item.Resumo)]);

        // Um registro por lote, sem coordenada: posição individual não vai para log (CLAUDE.md, seção 52).
        log.LogInformation(
            "Lote de {Quantidade} posições do motorista {MotoristaId}: {Aceitas} aceitas, {Duplicadas} duplicadas, {Rejeitadas} rejeitadas.",
            lote.Recebidas, motorista.Id, lote.Aceitas, lote.Duplicadas, lote.Rejeitadas);

        return lote;
    }

    private PosicaoDoMotorista Avaliar(
        PosicaoEnviada enviada,
        Guid motoristaId,
        Guid organizacaoId,
        DateTimeOffset recebidaEm,
        IReadOnlyCollection<JanelaDeExecucaoDaRota> janelas)
    {
        if (enviada is not
            {
                EventoDeLocalizacaoId: { } eventoId,
                Latitude: { } latitude,
                Longitude: { } longitude,
                PrecisaoEmMetros: { } precisao,
                CapturadaEm: { } capturadaEm,
                Sequencia: { } sequencia,
            })
        {
            throw ExcecaoDeDominio.RegraViolada(
                "campo_obrigatorio",
                "Informe identificador do evento, latitude, longitude, precisão, captura e sequência.");
        }

        return PosicaoDoMotorista.Registrar(
            identificadores.Novo(),
            organizacaoId,
            motoristaId,
            eventoId,
            sequencia,
            latitude,
            longitude,
            precisao,
            enviada.VelocidadeEmMetrosPorSegundo,
            enviada.DirecaoEmGraus,
            capturadaEm,
            recebidaEm,
            janelas);
    }
}

/// <summary>
/// Métricas de ingestão de GPS (CLAUDE.md, seção 51), no medidor <see cref="NomeDoMedidor"/>.
/// </summary>
/// <remarks>
/// Nomes no padrão do OpenTelemetry; a exportação entra na Fase 21. Nenhuma métrica leva
/// motorista ou organização como dimensão — só o motivo da recusa, de vocabulário fechado.
/// </remarks>
public sealed class MetricasDeRastreamento : IDisposable
{
    /// <summary>Nome do medidor.</summary>
    public const string NomeDoMedidor = "TorreLogistica.Rastreamento";

    private readonly Meter _medidor = new(NomeDoMedidor);
    private readonly Counter<long> _recebidas;
    private readonly Counter<long> _duplicadas;
    private readonly Counter<long> _foraDeOrdem;
    private readonly Counter<long> _imprecisas;
    private readonly Counter<long> _rejeitadas;
    private readonly Histogram<double> _atraso;

    /// <summary>Cria os instrumentos.</summary>
    public MetricasDeRastreamento()
    {
        _recebidas = _medidor.CreateCounter<long>("tracking.positions.received", "{posicao}", "Posições recebidas.");
        _duplicadas = _medidor.CreateCounter<long>("tracking.positions.duplicate", "{posicao}", "Posições reenviadas, sem novo registro.");
        _foraDeOrdem = _medidor.CreateCounter<long>("tracking.positions.out_of_order", "{posicao}", "Posições confiáveis mais antigas que a posição atual.");
        _imprecisas = _medidor.CreateCounter<long>("tracking.positions.inaccurate", "{posicao}", "Posições aceitas sem precisão para mover a posição atual.");
        _rejeitadas = _medidor.CreateCounter<long>("tracking.positions.rejected", "{posicao}", "Posições recusadas pela política, por motivo.");
        _atraso = _medidor.CreateHistogram<double>("tracking.ingestion.lag", "s", "Tempo entre captura e recebimento das posições aceitas.");
    }

    /// <summary>Registra o resultado de um lote confirmado.</summary>
    public void Registrar(IReadOnlyCollection<(ResultadoDePosicaoResumo Resumo, DateTimeOffset? CapturadaEm)> itens, DateTimeOffset recebidaEm)
    {
        ArgumentNullException.ThrowIfNull(itens);

        _recebidas.Add(itens.Count);

        foreach (var (resumo, capturadaEm) in itens)
        {
            switch (resumo.Resultado)
            {
                case ResultadoDaPosicao.Duplicada:
                    _duplicadas.Add(1);
                    break;
                case ResultadoDaPosicao.Rejeitada:
                    _rejeitadas.Add(1, new KeyValuePair<string, object?>("motivo", resumo.Motivo));
                    break;
                default:
                    if (resumo.ForaDeOrdem)
                    {
                        _foraDeOrdem.Add(1);
                    }

                    if (resumo.Qualidade == QualidadeDaPosicao.Imprecisa)
                    {
                        _imprecisas.Add(1);
                    }

                    if (capturadaEm is { } capturada)
                    {
                        _atraso.Record(Math.Max(0, (recebidaEm - capturada).TotalSeconds));
                    }

                    break;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => _medidor.Dispose();
}
