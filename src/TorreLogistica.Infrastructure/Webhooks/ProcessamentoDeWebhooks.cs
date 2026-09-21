using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TorreLogistica.Application.Abstracoes.Webhooks;
using TorreLogistica.Application.Webhooks;

namespace TorreLogistica.Infrastructure.Webhooks;

/// <summary>
/// Roda em segundo plano o despacho do outbox e a entrega dos webhooks.
/// </summary>
/// <remarks>
/// <para>
/// Um serviço só, com duas rodadas independentes: despachar é rápido e fica no banco; entregar depende de
/// rede alheia. Separá-los em temporizadores próprios evita que um assinante lento atrase a saída dos
/// eventos das outras organizações.
/// </para>
/// <para>
/// Cada rodada abre o próprio escopo de injeção: o contexto de persistência tem tempo de vida de escopo, e
/// prender um único contexto pela vida do processo acumularia rastreamento de mudanças para sempre.
/// </para>
/// </remarks>
public sealed class ProcessadorDeWebhooks(
    IServiceScopeFactory escopos,
    IOptions<OpcoesDeWebhooks> opcoes,
    TimeProvider tempo,
    ILogger<ProcessadorDeWebhooks> log) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Lido aqui, e não no registro: no registro a configuração do host ainda não está completa, e
        // desligar o processador por configuração não teria efeito nenhum.
        if (!opcoes.Value.ProcessarEmSegundoPlano)
        {
            return;
        }

        await Task.WhenAll(
            RodarAsync(opcoes.Value.IntervaloDeDespacho, DespacharAsync, "despacho do outbox", stoppingToken),
            RodarAsync(opcoes.Value.IntervaloDeEntrega, EntregarAsync, "entrega de webhooks", stoppingToken))
            .ConfigureAwait(false);
    }

    private async Task RodarAsync(
        TimeSpan intervalo,
        Func<CancellationToken, Task> rodada,
        string nome,
        CancellationToken parada)
    {
        using var temporizador = new PeriodicTimer(intervalo, tempo);

        try
        {
            while (await temporizador.WaitForNextTickAsync(parada).ConfigureAwait(false))
            {
                try
                {
                    await rodada(parada).ConfigureAwait(false);
                }
                catch (Exception) when (parada.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception excecao)
                {
                    // Falha de uma rodada não derruba o serviço: o que ficou pendente continua no banco e
                    // a próxima rodada tenta de novo. É essa a diferença entre fila durável e fila em memória.
                    log.LogError(excecao, "Falha na rodada de {Rodada}.", nome);
                }
            }
        }
        catch (OperationCanceledException) when (parada.IsCancellationRequested)
        {
            // Encerramento do processo.
        }
    }

    private async Task DespacharAsync(CancellationToken parada)
    {
        await using var escopo = escopos.CreateAsyncScope();
        var despacho = escopo.ServiceProvider.GetRequiredService<DespachoDeWebhooks>();

        while (await despacho.DespacharLoteAsync(parada).ConfigureAwait(false) == DespachoDeWebhooks.TamanhoDoLote)
        {
            // Lote cheio significa que havia mais: continua até esvaziar, em vez de esperar o próximo tique.
        }
    }

    private async Task EntregarAsync(CancellationToken parada)
    {
        await using var escopo = escopos.CreateAsyncScope();
        var entrega = escopo.ServiceProvider.GetRequiredService<EntregaDeWebhooks>();
        await entrega.EntregarLoteAsync(parada).ConfigureAwait(false);
    }
}

/// <summary>Registro dos webhooks de saída.</summary>
public static class ConfiguracaoDeWebhooks
{
    /// <summary>Registra opções, cifra de segredo, cliente HTTP e o processador em segundo plano.</summary>
    public static IServiceCollection AdicionarWebhooks(this IServiceCollection servicos, IConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(servicos);
        ArgumentNullException.ThrowIfNull(configuracao);

        servicos
            .AddOptions<OpcoesDeWebhooks>()
            .Bind(configuracao.GetSection(OpcoesDeWebhooks.Secao))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        servicos.AddSingleton<IProtecaoDeSegredo, ProtecaoDeSegredoAesGcm>();

        servicos
            .AddHttpClient<IClienteDeWebhook, ClienteDeWebhookHttp>(ClienteDeWebhookHttp.NomeDoCliente)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // Redirecionamento seguido às cegas contorna a checagem de destino: o assinante
                // responderia 302 para um endereço interno e nós iríamos atrás.
                AllowAutoRedirect = false,
                ConnectTimeout = TimeSpan.FromSeconds(5),
            });

        servicos.AddHostedService<ProcessadorDeWebhooks>();

        return servicos;
    }
}
