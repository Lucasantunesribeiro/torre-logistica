using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TorreLogistica.Application.Retencao;

namespace TorreLogistica.Infrastructure.Retencao;

/// <summary>
/// Roda em segundo plano a limpeza do histórico bruto de localização.
/// </summary>
/// <remarks>
/// <para>
/// A primeira rodada acontece no primeiro tique, não na subida: apagar durante a inicialização
/// competiria com o tráfego de quem está entrando, e o dado vencido pode esperar mais alguns minutos —
/// o que não pode é ficar para sempre.
/// </para>
/// <para>
/// Falha de uma rodada não derruba o serviço. O que não foi apagado continua vencido no banco e sai na
/// rodada seguinte; o que não pode acontecer é a limpeza morrer em silêncio e ninguém perceber que o
/// prazo de retenção virou ficção.
/// </para>
/// </remarks>
public sealed class ProcessadorDeRetencao(
    IServiceScopeFactory escopos,
    IOptions<OpcoesDeRetencao> opcoes,
    TimeProvider tempo,
    ILogger<ProcessadorDeRetencao> log) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A decisão de rodar é lida aqui, e não no registro: no registro a configuração do host ainda
        // não está completa, e uma chave que chega depois seria ignorada em silêncio.
        if (!opcoes.Value.LimparEmSegundoPlano)
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
                    await LimparAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception excecao)
                {
                    log.LogError(excecao, "Falha na rodada de limpeza por retenção.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Encerramento do processo.
        }
    }

    private async Task LimparAsync(CancellationToken parada)
    {
        await using var escopo = escopos.CreateAsyncScope();
        var limpeza = escopo.ServiceProvider.GetRequiredService<LimpezaPorRetencao>();
        var resultado = await limpeza.ExecutarAsync(parada).ConfigureAwait(false);

        if (resultado.RestouTrabalho)
        {
            // Teto de lotes atingido com dado vencido ainda no banco: não é erro, mas precisa aparecer.
            // Se repetir toda rodada, a limpeza não está acompanhando o volume de entrada.
            log.LogWarning(
                "Retenção: rodada encerrada no teto de {Lotes} lote(s) com dado vencido restante.",
                opcoes.Value.LotesPorRodada);
        }
    }
}

/// <summary>Registro da retenção.</summary>
public static class ConfiguracaoDeRetencao
{
    /// <summary>Registra opções, métricas, a limpeza e o processador em segundo plano.</summary>
    public static IServiceCollection AdicionarRetencao(this IServiceCollection servicos, IConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(servicos);
        ArgumentNullException.ThrowIfNull(configuracao);

        servicos
            .AddOptions<OpcoesDeRetencao>()
            .Bind(configuracao.GetSection(OpcoesDeRetencao.Secao))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        servicos.AddSingleton<MetricasDeRetencao>();
        servicos.AddScoped<LimpezaPorRetencao>();

        return servicos;
    }

    /// <summary>Liga a limpeza por retenção neste processo.</summary>
    public static IServiceCollection AdicionarProcessamentoDeRetencao(this IServiceCollection servicos)
    {
        ArgumentNullException.ThrowIfNull(servicos);

        servicos.AddHostedService<ProcessadorDeRetencao>();

        return servicos;
    }
}
