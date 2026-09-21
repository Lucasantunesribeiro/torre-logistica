using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TorreLogistica.Application.Observabilidade;

namespace TorreLogistica.Infrastructure.Observabilidade;

/// <summary>
/// Mede o estado da operação de tempos em tempos e publica o retrato para os instrumentos.
/// </summary>
/// <remarks>
/// Uma rodada que falha não derruba o serviço nem apaga o retrato anterior: métrica velha, com o aviso no
/// log, informa mais do que métrica zerada — zero é um valor plausível, e quem olha o painel não tem como
/// saber que ele significa "não consegui medir".
/// </remarks>
public sealed class ProcessadorDeMedidas(
    IServiceScopeFactory escopos,
    MedidasDaOperacao medidas,
    IOptions<OpcoesDeMedidas> opcoes,
    TimeProvider tempo,
    ILogger<ProcessadorDeMedidas> log) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!opcoes.Value.MedirEmSegundoPlano)
        {
            return;
        }

        using var temporizador = new PeriodicTimer(opcoes.Value.Intervalo, tempo);

        try
        {
            while (await temporizador.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await MedirAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception excecao)
                {
                    log.LogError(excecao, "Falha ao medir o estado da operação. O retrato anterior continua publicado.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Encerramento do processo.
        }
    }

    private async Task MedirAsync(CancellationToken parada)
    {
        await using var escopo = escopos.CreateAsyncScope();
        var leitura = escopo.ServiceProvider.GetRequiredService<LeituraDoEstadoDaOperacao>();
        medidas.Atualizar(await leitura.LerAsync(parada).ConfigureAwait(false));
    }
}

/// <summary>Registro das medidas de estado.</summary>
public static class ConfiguracaoDeMedidas
{
    /// <summary>Registra as opções e o medidor em segundo plano.</summary>
    public static IServiceCollection AdicionarMedidasDaOperacao(
        this IServiceCollection servicos,
        IConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(servicos);
        ArgumentNullException.ThrowIfNull(configuracao);

        servicos
            .AddOptions<OpcoesDeMedidas>()
            .Bind(configuracao.GetSection(OpcoesDeMedidas.Secao))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        servicos.AddHostedService<ProcessadorDeMedidas>();

        return servicos;
    }
}
