using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Application.Abstracoes.Previsao;
using TorreLogistica.Application.Abstracoes.Roteamento;
using TorreLogistica.Domain.Abstracoes.Identificadores;
using TorreLogistica.Domain.Abstracoes.Tempo;
using TorreLogistica.Domain.Comum;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Previsao;
using TorreLogistica.Domain.Rastreamento;
using TorreLogistica.Domain.Rotas;

namespace TorreLogistica.Application.Previsao;

/// <summary>
/// Recalcula a previsão de chegada e a situação do SLA de todas as entregas pendentes de uma rota.
/// </summary>
/// <remarks>
/// <para>
/// Roda fora da requisição que mudou o estado, no tenant da rota. Lê o estado confirmado, pede o
/// deslocamento ao provedor de rotas com tempo limite — caindo na contingência em linha reta se ele
/// faltar, demorar ou falhar —, compõe a chegada de cada parada e grava numa transação a previsão atual
/// e os registros relevantes do histórico.
/// </para>
/// <para>
/// Rota inteira, e não entrega por entrega: a chegada de uma parada depende das anteriores. Entregas que
/// deixaram a execução têm a previsão encerrada no mesmo cálculo.
/// </para>
/// </remarks>
public sealed class RecalculoDePrevisoes(
    IContextoDePersistencia contexto,
    IEnumerable<IProvedorDeRotas> provedores,
    IGeradorDeIdentificador identificadores,
    IRelogio relogio,
    IOptions<OpcoesDePrevisao> opcoes,
    MetricasDePrevisao metricas,
    ILogger<RecalculoDePrevisoes> log)
{
    /// <summary>Rota a recalcular para o pedido, ou <see langword="null"/> quando não há rota em execução.</summary>
    public async Task<Guid?> ResolverRotaAsync(SolicitacaoDeRecalculo solicitacao, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(solicitacao);

        if (solicitacao.RotaId is { } rotaId)
        {
            return rotaId;
        }

        if (solicitacao.EntregaId is { } entregaId)
        {
            var daParada = await contexto.Paradas
                .AsNoTracking()
                .Where(parada => parada.EntregaId == entregaId && parada.Ativa)
                .Select(parada => (Guid?)parada.RotaId)
                .FirstOrDefaultAsync(cancelamento)
                .ConfigureAwait(false);

            return daParada ?? await contexto.PrevisoesDaEntrega
                .AsNoTracking()
                .Where(previsao => previsao.EntregaId == entregaId && previsao.Ativa)
                .Select(previsao => (Guid?)previsao.RotaId)
                .FirstOrDefaultAsync(cancelamento)
                .ConfigureAwait(false);
        }

        if (solicitacao.MotoristaId is { } motoristaId)
        {
            return await contexto.Rotas
                .AsNoTracking()
                .Where(rota => rota.MotoristaId == motoristaId && rota.Status == StatusDaRota.EmAndamento)
                .Select(rota => (Guid?)rota.Id)
                .FirstOrDefaultAsync(cancelamento)
                .ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>Recalcula a rota.</summary>
    /// <returns>Quantos registros entraram no histórico.</returns>
    public async Task<int> RecalcularRotaAsync(Guid rotaId, CancellationToken cancelamento)
    {
        var inicio = Stopwatch.GetTimestamp();
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

        IReadOnlyList<Parada> paradas = rota.Status == StatusDaRota.EmAndamento ? rota.ObterParadasAtivas() : [];
        var ids = paradas.Select(parada => parada.EntregaId).ToArray();
        var entregas = ids.Length == 0
            ? []
            : await contexto.Entregas
                .AsNoTracking()
                .Where(entrega => ids.Contains(entrega.Id))
                .ToDictionaryAsync(entrega => entrega.Id, cancelamento)
                .ConfigureAwait(false);

        var pendentes = paradas
            .Select(parada => entregas.GetValueOrDefault(parada.EntregaId))
            .OfType<Entrega>()
            .Where(entrega => RegrasDaEntrega.EstaEmExecucao(entrega.Status))
            .ToList();

        var posicao = await PosicaoDaRotaAsync(rota, pendentes.Count, cancelamento).ConfigureAwait(false);
        var destinos = pendentes
            .Where(entrega => entrega.Status == StatusDaEntrega.EmRota && entrega.Localizacao is not null)
            .Select(entrega => entrega.Localizacao!)
            .ToList();

        var trajeto = posicao is null || destinos.Count == 0
            ? Trajeto.SemDeslocamento
            : await EstimarAsync([posicao.Localizacao, .. destinos], valores, cancelamento).ConfigureAwait(false);

        var composicoes = CalculadoraDeChegada.Calcular(
            agora,
            posicao is not null,
            [.. pendentes.Select(entrega => new ParadaParaPrevisao(entrega.Id, entrega.Status, entrega.Localizacao is not null, entrega.ChegadaRegistradaEm))],
            trajeto.Trechos,
            valores.TempoMedioPorParada);

        var limiares = LimiaresDeSla.Criar(valores.FolgaParaAtencao, valores.FolgaParaRisco);
        var novasSituacoes = new List<SituacaoDoSla>();

        int registros;
        try
        {
            registros = await contexto.ExecutarEmTransacaoAsync(
                async cancelamentoDaTentativa =>
                {
                    novasSituacoes.Clear();

                    var existentes = await contexto.PrevisoesDaEntrega
                        .Where(previsao => ids.Contains(previsao.EntregaId) || (previsao.RotaId == rotaId && previsao.Ativa))
                        .ToDictionaryAsync(previsao => previsao.EntregaId, cancelamentoDaTentativa)
                        .ConfigureAwait(false);

                    var gravados = 0;

                    foreach (var composicao in composicoes)
                    {
                        var entrega = entregas[composicao.EntregaId];
                        var calculo = new CalculoDaPrevisao(
                            rotaId,
                            composicao,
                            RegrasDeSla.Classificar(entrega.Janela, composicao.ChegadaPrevista, agora, limiares),
                            entrega.Janela,
                            limiares,
                            valores.TempoMedioPorParada,
                            trajeto.Fonte,
                            trajeto.Provedor,
                            trajeto.MotivoDaContingencia,
                            posicao?.CapturadaEm,
                            agora);

                        RegistroDePrevisao? registro;
                        if (existentes.TryGetValue(entrega.Id, out var previsao))
                        {
                            registro = previsao.Atualizar(calculo, valores.MudancaRelevanteDaChegadaPrevista, identificadores.Novo());
                        }
                        else
                        {
                            (previsao, registro) = PrevisaoDaEntrega.Iniciar(entrega.OrganizacaoId, calculo, identificadores.Novo());
                            contexto.PrevisoesDaEntrega.Add(previsao);
                        }

                        if (registro is not null)
                        {
                            contexto.RegistrosDePrevisao.Add(registro);
                            gravados++;

                            if (registro.SituacaoAnterior != registro.Situacao)
                            {
                                novasSituacoes.Add(registro.Situacao);
                            }
                        }
                    }

                    var calculadas = composicoes.Select(composicao => composicao.EntregaId).ToHashSet();
                    foreach (var previsao in existentes.Values.Where(previsao => previsao.Ativa && !calculadas.Contains(previsao.EntregaId)))
                    {
                        StatusDaEntrega? status = entregas.TryGetValue(previsao.EntregaId, out var entrega) ? entrega.Status : null;
                        if (previsao.Encerrar(status, identificadores.Novo(), agora) is { } encerramento)
                        {
                            contexto.RegistrosDePrevisao.Add(encerramento);
                            gravados++;
                        }
                    }

                    await contexto.SaveChangesAsync(cancelamentoDaTentativa).ConfigureAwait(false);
                    return gravados;
                },
                cancelamento).ConfigureAwait(false);
        }
        catch (DbUpdateException excecao) when (EhCalculoConcorrente(excecao))
        {
            // Outro processo recalculou a mesma rota ao mesmo tempo e gravou primeiro. O estado dele é tão
            // novo quanto o deste; o próximo gatilho recalcula de novo.
            log.LogInformation("Previsão da rota {RotaId} recalculada ao mesmo tempo por outro processo; este cálculo foi descartado.", rotaId);
            return 0;
        }

        metricas.RegistrarCalculo(Stopwatch.GetElapsedTime(inicio), novasSituacoes);

        if (registros > 0)
        {
            log.LogInformation(
                "Previsão da rota {RotaId}: {Entregas} entrega(s) calculada(s), {Registros} registro(s) no histórico, fonte {Fonte}.",
                rotaId, composicoes.Count, registros, trajeto.Fonte);
        }

        return registros;
    }

    private bool EhCalculoConcorrente(DbUpdateException excecao) =>
        excecao is DbUpdateConcurrencyException
        || contexto.EhViolacaoDeUnicidade(excecao, NomesDeRestricoes.SequenciaDoRegistroDePrevisao)
        || contexto.EhViolacaoDeUnicidade(excecao, NomesDeRestricoes.PrevisaoDaEntrega);

    /// <summary>
    /// Posição atual do motorista da rota, se ela pertence a esta execução: a posição de ontem, do fim de
    /// outra rota, não serve de origem.
    /// </summary>
    private async Task<PosicaoAtual?> PosicaoDaRotaAsync(Rota rota, int pendentes, CancellationToken cancelamento)
    {
        if (pendentes == 0 || rota.MotoristaId is not { } motoristaId || rota.IniciadaEm is not { } iniciadaEm)
        {
            return null;
        }

        var posicao = await contexto.PosicoesAtuais
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.MotoristaId == motoristaId, cancelamento)
            .ConfigureAwait(false);

        return posicao is not null && posicao.CapturadaEm >= iniciadaEm - PoliticaDeLocalizacao.ToleranciaAntesDoInicioDaRota
            ? posicao
            : null;
    }

    private async Task<Trajeto> EstimarAsync(IReadOnlyList<CoordenadaGeografica> pontos, OpcoesDePrevisao valores, CancellationToken cancelamento)
    {
        var provedor = provedores.FirstOrDefault(item => string.Equals(item.Nome, valores.Provedor, StringComparison.OrdinalIgnoreCase));
        MotivoDaContingencia motivo;

        if (provedor is null)
        {
            motivo = MotivoDaContingencia.ProvedorAusente;
        }
        else
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancelamento);
            limite.CancelAfter(valores.TempoLimiteDoProvedor);

            try
            {
                // WaitAsync impõe o limite mesmo a um provedor que ignore o cancelamento.
                var trechos = await provedor.EstimarTrajetoAsync(pontos, limite.Token).WaitAsync(limite.Token).ConfigureAwait(false);
                if (trechos.Count == pontos.Count - 1)
                {
                    return new Trajeto(trechos, FonteDaPrevisao.Provedor, provedor.Nome, null);
                }

                motivo = MotivoDaContingencia.RespostaInvalida;
                log.LogWarning(
                    "Provedor de rotas {Provedor} devolveu {Recebidos} trecho(s) para {Esperados}; previsão pela contingência.",
                    provedor.Nome, trechos.Count, pontos.Count - 1);
            }
            catch (OperationCanceledException) when (!cancelamento.IsCancellationRequested)
            {
                motivo = MotivoDaContingencia.TempoLimite;
                log.LogWarning(
                    "Provedor de rotas {Provedor} não respondeu em {TempoLimite}; previsão pela contingência.",
                    provedor.Nome, valores.TempoLimiteDoProvedor);
            }
            catch (Exception excecao) when (excecao is not OperationCanceledException)
            {
                motivo = MotivoDaContingencia.FalhaDoProvedor;
                log.LogWarning(excecao, "Provedor de rotas {Provedor} falhou; previsão pela contingência.", provedor.Nome);
            }
        }

        metricas.RegistrarContingencia(motivo);

        var distancias = await contexto.CalcularDistanciasDoTrajetoAsync(pontos, cancelamento).ConfigureAwait(false);
        var fator = valores.FatorDeSinuosidadeDeContingencia;
        var velocidade = valores.VelocidadeDeContingenciaEmMetrosPorSegundo;

        return new Trajeto(
            [.. distancias.Select(distancia => TrechoDeTrajeto.Criar(TimeSpan.FromSeconds(Math.Round(distancia * fator / velocidade)), distancia * fator))],
            FonteDaPrevisao.Contingencia,
            null,
            motivo);
    }

    private sealed record Trajeto(
        IReadOnlyList<TrechoDeTrajeto> Trechos,
        FonteDaPrevisao Fonte,
        string? Provedor,
        MotivoDaContingencia? MotivoDaContingencia)
    {
        public static readonly Trajeto SemDeslocamento = new([], FonteDaPrevisao.SemDeslocamento, null, null);
    }
}

/// <summary>Rota a reavaliar, com a organização em que o recálculo vai rodar.</summary>
public sealed record RotaParaReavaliar(Guid OrganizacaoId, Guid RotaId);

/// <summary>
/// Rotas cuja previsão precisa ser reavaliada com o passar do tempo: em andamento, ou com previsão ainda
/// ativa (para encerrar a que sobrou de um gatilho perdido).
/// </summary>
/// <remarks>
/// Única leitura de previsão sem tenant: devolve só identificadores, e cada rota é recalculada depois no
/// tenant dela. Consultar outro tenant exige <c>IgnoreQueryFilters()</c> explícito, como aqui.
/// </remarks>
public sealed class ConsultaDeRotasParaReavaliacao(IContextoDePersistencia contexto)
{
    /// <summary>Rotas por ciclo de reavaliação.</summary>
    public const int Limite = 5_000;

    /// <summary>Lista as rotas.</summary>
    public async Task<IReadOnlyList<RotaParaReavaliar>> ListarAsync(CancellationToken cancelamento)
    {
        var emAndamento = await contexto.Rotas
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(rota => rota.Status == StatusDaRota.EmAndamento)
            .OrderBy(rota => rota.Id)
            .Select(rota => new { rota.OrganizacaoId, RotaId = rota.Id })
            .Take(Limite)
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

        var comPrevisaoAtiva = await contexto.PrevisoesDaEntrega
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(previsao => previsao.Ativa)
            .Select(previsao => new { previsao.OrganizacaoId, previsao.RotaId })
            .Distinct()
            .Take(Limite)
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

        return
        [
            .. emAndamento
                .Concat(comPrevisaoAtiva)
                .Select(item => new RotaParaReavaliar(item.OrganizacaoId, item.RotaId))
                .Distinct(),
        ];
    }
}

/// <summary>
/// Métricas de previsão (CLAUDE.md, seção 51), no medidor <see cref="NomeDoMedidor"/>.
/// </summary>
/// <remarks>Sem organização, entrega ou rota como dimensão: só vocabulário fechado.</remarks>
public sealed class MetricasDePrevisao : IDisposable
{
    /// <summary>Nome do medidor.</summary>
    public const string NomeDoMedidor = "TorreLogistica.Previsao";

    private readonly Meter _medidor = new(NomeDoMedidor);
    private readonly Histogram<double> _duracao;
    private readonly Counter<long> _contingencias;
    private readonly Counter<long> _mudancasDeSituacao;

    /// <summary>Cria os instrumentos.</summary>
    public MetricasDePrevisao()
    {
        _duracao = _medidor.CreateHistogram<double>("eta.calculation.duration", "s", "Duração do recálculo da previsão de uma rota.");
        _contingencias = _medidor.CreateCounter<long>("eta.provider.fallbacks", "{calculo}", "Cálculos que usaram a contingência em linha reta, por motivo.");
        _mudancasDeSituacao = _medidor.CreateCounter<long>("sla.situation.changes", "{mudanca}", "Mudanças de situação do SLA, pela situação nova.");
    }

    /// <summary>Conta uma contingência.</summary>
    public void RegistrarContingencia(MotivoDaContingencia motivo) =>
        _contingencias.Add(1, new KeyValuePair<string, object?>("motivo", motivo.ToString()));

    /// <summary>Registra um recálculo confirmado.</summary>
    public void RegistrarCalculo(TimeSpan duracao, IEnumerable<SituacaoDoSla> novasSituacoes)
    {
        ArgumentNullException.ThrowIfNull(novasSituacoes);

        _duracao.Record(duracao.TotalSeconds);
        foreach (var situacao in novasSituacoes)
        {
            _mudancasDeSituacao.Add(1, new KeyValuePair<string, object?>("situacao", situacao.ToString()));
        }
    }

    /// <inheritdoc />
    public void Dispose() => _medidor.Dispose();
}
