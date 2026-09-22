using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TorreLogistica.Application.Abstracoes.Identidade;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Application.Abstracoes.Previsao;
using TorreLogistica.Application.Abstracoes.Roteamento;
using TorreLogistica.Application.Alertas;
using TorreLogistica.Application.Previsao;
using TorreLogistica.Domain.Abstracoes.Tempo;
using TorreLogistica.Domain.Comum;
using TorreLogistica.Domain.Previsao;
using TorreLogistica.Infrastructure.Persistencia;

namespace TorreLogistica.Infrastructure.Previsao;

/// <summary>
/// Provedor de rotas simulado: distância geodésica do PostGIS, multiplicada por um fator de sinuosidade
/// urbana e dividida por uma velocidade média configurada.
/// </summary>
/// <remarks>
/// Implementação controlada permitida pelo ROADMAP enquanto um provedor real exigiria custo e conta
/// externa. Não sabe de ruas, trânsito nem sentido de via — e diz isso: a previsão registra o nome
/// <c>simulado</c>. Um provedor real entra implementando <see cref="IProvedorDeRotas"/>, sem mudar a
/// composição nem a explicação.
/// </remarks>
public sealed class ProvedorDeRotasSimulado(IContextoDePersistencia contexto, IOptions<OpcoesDePrevisao> opcoes) : IProvedorDeRotas
{
    /// <inheritdoc />
    public string Nome => OpcoesDoProvedorSimulado.Nome;

    /// <inheritdoc />
    public async Task<IReadOnlyList<TrechoDeTrajeto>> EstimarTrajetoAsync(IReadOnlyList<CoordenadaGeografica> pontos, CancellationToken cancelamento)
    {
        var parametros = opcoes.Value.ProvedorSimulado;
        var distancias = await contexto.CalcularDistanciasDoTrajetoAsync(pontos, cancelamento).ConfigureAwait(false);

        return
        [
            .. distancias.Select(distancia =>
            {
                var percorrida = distancia * parametros.FatorDeSinuosidade;
                return TrechoDeTrajeto.Criar(TimeSpan.FromSeconds(Math.Round(percorrida / parametros.VelocidadeMediaEmMetrosPorSegundo)), percorrida);
            }),
        ];
    }
}

/// <summary>Tenant explícito de um processamento em segundo plano, recebido do pedido que o originou.</summary>
public sealed class ContextoDeTenantDoProcessamento(Guid organizacaoId) : IContextoDoTenant
{
    /// <inheritdoc />
    public Guid? OrganizacaoId { get; } = organizacaoId == Guid.Empty
        ? throw new ArgumentException("Organização inválida.", nameof(organizacaoId))
        : organizacaoId;
}

/// <summary>
/// Fila em memória dos pedidos de recálculo, com pedidos repetidos colapsados enquanto esperam.
/// </summary>
/// <remarks>
/// Memória da instância: um pedido perdido numa queda do processo é coberto pela reavaliação periódica,
/// que relê do banco as rotas em andamento. Por isso não há outbox aqui — previsão se recalcula; não é
/// efeito que não pode se perder.
/// </remarks>
public sealed class FilaDeRecalculoDePrevisoes(ILogger<FilaDeRecalculoDePrevisoes> log) : ISolicitacoesDeRecalculoDePrevisao
{
    /// <summary>Pedidos distintos à espera.</summary>
    public const int Capacidade = 10_000;

    private readonly Channel<SolicitacaoDeRecalculo> _canal = Channel.CreateBounded<SolicitacaoDeRecalculo>(
        new BoundedChannelOptions(Capacidade) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    private readonly ConcurrentDictionary<SolicitacaoDeRecalculo, byte> _aguardando = new();

    /// <inheritdoc />
    public void Solicitar(IReadOnlyCollection<SolicitacaoDeRecalculo> solicitacoes)
    {
        ArgumentNullException.ThrowIfNull(solicitacoes);

        var descartados = 0;
        foreach (var solicitacao in solicitacoes)
        {
            if (!_aguardando.TryAdd(solicitacao, 0))
            {
                continue;
            }

            if (!_canal.Writer.TryWrite(solicitacao))
            {
                _aguardando.TryRemove(solicitacao, out _);
                descartados++;
            }
        }

        if (descartados > 0)
        {
            log.LogWarning("{Quantidade} pedido(s) de recálculo de previsão descartado(s) com a fila cheia; a reavaliação periódica os recupera.", descartados);
        }
    }

    /// <summary>Lê os pedidos, liberando cada um para ser pedido de novo assim que sai da fila.</summary>
    public async IAsyncEnumerable<SolicitacaoDeRecalculo> LerAsync([EnumeratorCancellation] CancellationToken cancelamento)
    {
        await foreach (var solicitacao in _canal.Reader.ReadAllAsync(cancelamento).ConfigureAwait(false))
        {
            _aguardando.TryRemove(solicitacao, out _);
            yield return solicitacao;
        }
    }
}

/// <summary>
/// Processa os pedidos de recálculo e reavalia periodicamente as rotas em andamento.
/// </summary>
/// <remarks>
/// <para>
/// Um consumidor só por instância: os recálculos da instância acontecem em série, então a mesma rota não é
/// recalculada duas vezes ao mesmo tempo aqui. Entre instâncias, a versão da linha e o índice único do
/// histórico decidem, e o cálculo perdedor é descartado.
/// </para>
/// <para>
/// Cada pedido roda num escopo próprio, com o tenant da organização do pedido — nunca sem tenant. A
/// reavaliação periódica é a única leitura sem tenant, e só de identificadores.
/// </para>
/// <para>
/// Posição GPS chega a cada poucos segundos; recalcular a cada uma não muda a decisão e custa consulta ao
/// provedor. Pedidos por posição da mesma rota respeitam um intervalo mínimo; pedidos por mudança de
/// entrega e pela reavaliação não.
/// </para>
/// </remarks>
public sealed class ProcessadorDePrevisoes(
    FilaDeRecalculoDePrevisoes fila,
    IServiceScopeFactory escopos,
    TimeProvider tempo,
    IRelogio relogio,
    IOptions<OpcoesDePrevisao> opcoes,
    ILogger<ProcessadorDePrevisoes> log) : BackgroundService
{
    private const int LimiteDeRotasLembradas = 10_000;

    private readonly Dictionary<Guid, DateTimeOffset> _ultimoRecalculoPorPosicao = [];

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(ConsumirAsync(stoppingToken), ReavaliarPeriodicamenteAsync(stoppingToken));

    private async Task ConsumirAsync(CancellationToken parada)
    {
        try
        {
            await foreach (var solicitacao in fila.LerAsync(parada).ConfigureAwait(false))
            {
                try
                {
                    await ProcessarAsync(solicitacao, parada).ConfigureAwait(false);
                }
                catch (Exception) when (parada.IsCancellationRequested)
                {
                    // Encerramento no meio de um recálculo: o cancelamento pode chegar como falha de leitura
                    // do Npgsql, e não como OperationCanceledException. Nada foi confirmado; a reavaliação
                    // da próxima subida recalcula.
                    break;
                }
                catch (Exception excecao)
                {
                    log.LogError(
                        excecao,
                        "Falha ao recalcular a previsão pedida por {Origem} na organização {OrganizacaoId}.",
                        solicitacao.Origem,
                        solicitacao.OrganizacaoId);
                }
            }
        }
        catch (OperationCanceledException) when (parada.IsCancellationRequested)
        {
            // Encerramento do processo.
        }
    }

    private async Task ProcessarAsync(SolicitacaoDeRecalculo solicitacao, CancellationToken parada)
    {
        await using var escopo = escopos.CreateAsyncScope();
        var servicos = escopo.ServiceProvider;

        await using var contexto = ActivatorUtilities.CreateInstance<TorreLogisticaDbContext>(
            servicos, (IContextoDoTenant)new ContextoDeTenantDoProcessamento(solicitacao.OrganizacaoId));
        var recalculo = ActivatorUtilities.CreateInstance<RecalculoDePrevisoes>(servicos, (IContextoDePersistencia)contexto);

        if (await recalculo.ResolverRotaAsync(solicitacao, parada).ConfigureAwait(false) is not { } rotaId)
        {
            return;
        }

        if (solicitacao.Origem == OrigemDoRecalculo.Posicao && !LiberarRecalculoPorPosicao(rotaId))
        {
            // Descarte previsto, e ainda assim registrado: trabalho que some sem deixar rastro custa uma
            // investigação inteira quando alguém pergunta por que a previsão não mudou.
            log.LogDebug(
                "Recálculo da rota {RotaId} pedido por posição foi dispensado: o anterior rodou há menos de {Intervalo}.",
                rotaId,
                opcoes.Value.IntervaloMinimoEntreRecalculosPorPosicao);
            return;
        }

        await recalculo.RecalcularRotaAsync(rotaId, parada).ConfigureAwait(false);

        // Alertas depois da previsão: risco e atraso são lidos da previsão que acabou de ser confirmada.
        var monitoramento = ActivatorUtilities.CreateInstance<MonitoramentoOperacional>(servicos, (IContextoDePersistencia)contexto);
        await monitoramento.AvaliarRotaAsync(rotaId, parada).ConfigureAwait(false);
    }

    private bool LiberarRecalculoPorPosicao(Guid rotaId)
    {
        var agora = relogio.AgoraUtc;
        var intervalo = opcoes.Value.IntervaloMinimoEntreRecalculosPorPosicao;

        if (_ultimoRecalculoPorPosicao.TryGetValue(rotaId, out var ultimo) && agora - ultimo < intervalo)
        {
            return false;
        }

        if (_ultimoRecalculoPorPosicao.Count >= LimiteDeRotasLembradas)
        {
            foreach (var antiga in _ultimoRecalculoPorPosicao.Where(item => agora - item.Value >= intervalo).Select(item => item.Key).ToList())
            {
                _ultimoRecalculoPorPosicao.Remove(antiga);
            }
        }

        _ultimoRecalculoPorPosicao[rotaId] = agora;
        return true;
    }

    private async Task ReavaliarPeriodicamenteAsync(CancellationToken parada)
    {
        using var temporizador = new PeriodicTimer(opcoes.Value.IntervaloDeReavaliacao, tempo);

        try
        {
            while (await temporizador.WaitForNextTickAsync(parada).ConfigureAwait(false))
            {
                try
                {
                    await using var escopo = escopos.CreateAsyncScope();
                    await using var contexto = ActivatorUtilities.CreateInstance<TorreLogisticaDbContext>(
                        escopo.ServiceProvider, (IContextoDoTenant)new ContextoDeTenantAusente());

                    var rotas = await new ConsultaDeRotasParaReavaliacao(contexto).ListarAsync(parada).ConfigureAwait(false);
                    fila.Solicitar([.. rotas.Select(rota => new SolicitacaoDeRecalculo(rota.OrganizacaoId, OrigemDoRecalculo.Reavaliacao, rota.RotaId, null, null))]);
                }
                catch (Exception) when (parada.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception excecao)
                {
                    log.LogError(excecao, "Falha na reavaliação periódica das previsões.");
                }
            }
        }
        catch (OperationCanceledException) when (parada.IsCancellationRequested)
        {
            // Encerramento do processo.
        }
    }
}

/// <summary>Registro da previsão de chegada no processo que atende a operação.</summary>
public static class ConfiguracaoDePrevisao
{
    /// <summary>
    /// Registra opções, provedor de rotas simulado, fila de recálculo e o processador em segundo plano.
    /// </summary>
    /// <remarks>
    /// Os dois processos chamam: a API porque lê a previsão que os workers produzem, e os workers porque
    /// são eles que a recalculam. O que só os workers registram é o laço, em
    /// <see cref="AdicionarProcessamentoDePrevisoes"/>.
    /// </remarks>
    public static IServiceCollection AdicionarPrevisaoDeChegada(this IServiceCollection servicos, IConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(servicos);
        ArgumentNullException.ThrowIfNull(configuracao);

        servicos
            .AddOptions<OpcoesDePrevisao>()
            .Bind(configuracao.GetSection(OpcoesDePrevisao.Secao))
            .ValidateOnStart();
        servicos.AddSingleton<IValidateOptions<OpcoesDePrevisao>, ValidacaoDeOpcoesDePrevisao>();

        servicos.AddScoped<IProvedorDeRotas, ProvedorDeRotasSimulado>();

        // A fila e o laço ficam em AdicionarProcessamentoDePrevisoes, e não aqui: a fila é um atalho de
        // latência dentro do processo que a consome. Registrada na API, acumularia pedidos que ninguém
        // leria — e a fila cheia passaria a avisar sobre descarte de trabalho que nunca foi dela.
        return servicos;
    }

    /// <summary>
    /// Liga o recálculo de previsão neste processo: a fila de pedidos e o laço que a consome.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Os dois andam juntos porque a fila é memória da instância. Quem garante que nenhuma previsão fica
    /// velha não é ela, e sim a reavaliação periódica, que relê do banco as rotas em andamento — está
    /// escrito na própria fila, e é o que torna possível tirar este laço da API sem perder recálculo.
    /// </para>
    /// <para>
    /// O motor de alertas vem junto: as regras rodam dentro deste mesmo processador, logo depois da
    /// previsão de cada rota. Separá-los exigiria uma segunda leitura das mesmas rotas para chegar à
    /// mesma conclusão.
    /// </para>
    /// </remarks>
    public static IServiceCollection AdicionarProcessamentoDePrevisoes(this IServiceCollection servicos)
    {
        ArgumentNullException.ThrowIfNull(servicos);

        servicos.AddSingleton<FilaDeRecalculoDePrevisoes>();
        servicos.AddSingleton<ISolicitacoesDeRecalculoDePrevisao>(
            provedor => provedor.GetRequiredService<FilaDeRecalculoDePrevisoes>());
        servicos.AddHostedService<ProcessadorDePrevisoes>();

        return servicos;
    }

    /// <summary>
    /// Registra os limites do motor de alertas. As regras rodam no mesmo processador, logo depois da previsão
    /// de cada rota.
    /// </summary>
    public static IServiceCollection AdicionarAlertasOperacionais(this IServiceCollection servicos, IConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(servicos);
        ArgumentNullException.ThrowIfNull(configuracao);

        servicos
            .AddOptions<OpcoesDeAlertas>()
            .Bind(configuracao.GetSection(OpcoesDeAlertas.Secao))
            .ValidateOnStart();
        servicos.AddSingleton<IValidateOptions<OpcoesDeAlertas>, ValidacaoDeOpcoesDeAlertas>();

        return servicos;
    }
}
