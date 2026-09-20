using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TorreLogistica.IntegrationTests.Infra;

/// <summary>Como o assinante responde à próxima entrega.</summary>
public enum ModoDoAssinante
{
    /// <summary>Confirma o recebimento com 204.</summary>
    Confirma = 0,

    /// <summary>Responde 500, como um assinante quebrado.</summary>
    Quebra = 1,

    /// <summary>Demora mais que o tempo limite da tentativa.</summary>
    Demora = 2,
}

/// <summary>Uma entrega recebida pelo assinante.</summary>
public sealed record WebhookRecebido(string Tipo, string EventoId, string Assinatura, string Corpo);

/// <summary>
/// Assinante de webhook de verdade: um servidor HTTP em porta efêmera, levantado pelo teste.
/// </summary>
/// <remarks>
/// A API dos testes roda em servidor de memória, que não escuta porta — um cliente HTTP real nunca
/// chegaria a uma rota dentro dela. Com um servidor próprio, a entrega percorre rede de verdade: é assim
/// que assinatura, cabeçalhos, status 500 e tempo limite ficam realmente provados, em vez de simulados por
/// um duplo de teste que aceitaria qualquer coisa.
/// </remarks>
public sealed class AssinanteDeWebhookDeTeste : IAsyncDisposable
{
    private readonly ConcurrentQueue<WebhookRecebido> _recebidos = new();
    private readonly WebApplication _aplicacao;

    private AssinanteDeWebhookDeTeste(WebApplication aplicacao, string url)
    {
        _aplicacao = aplicacao;
        Url = url;
    }

    /// <summary>Endereço que a assinatura deve apontar.</summary>
    public string Url { get; }

    /// <summary>Resposta que o assinante dará.</summary>
    public ModoDoAssinante Modo { get; set; } = ModoDoAssinante.Confirma;

    /// <summary>Quanto demorar no modo <see cref="ModoDoAssinante.Demora"/>.</summary>
    public TimeSpan Demora { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Tudo que chegou, na ordem.</summary>
    public IReadOnlyList<WebhookRecebido> Recebidos => [.. _recebidos];

    /// <summary>Sobe o assinante numa porta livre.</summary>
    public static async Task<AssinanteDeWebhookDeTeste> IniciarAsync()
    {
        var construtor = WebApplication.CreateSlimBuilder();
        construtor.WebHost.UseUrls("http://127.0.0.1:0");
        construtor.Logging.ClearProviders();

        var aplicacao = construtor.Build();
        AssinanteDeWebhookDeTeste? assinante = null;

        aplicacao.MapPost("/hooks", async (HttpContext contexto) =>
        {
            using var leitor = new StreamReader(contexto.Request.Body);
            var corpo = await leitor.ReadToEndAsync(contexto.RequestAborted).ConfigureAwait(false);

            assinante!._recebidos.Enqueue(new WebhookRecebido(
                contexto.Request.Headers["X-Torre-Event-Type"].ToString(),
                contexto.Request.Headers["X-Torre-Event-Id"].ToString(),
                contexto.Request.Headers["X-Torre-Signature"].ToString(),
                corpo));

            switch (assinante.Modo)
            {
                case ModoDoAssinante.Quebra:
                    return Results.StatusCode(StatusCodes.Status500InternalServerError);

                case ModoDoAssinante.Demora:
                    await Task.Delay(assinante.Demora, contexto.RequestAborted).ConfigureAwait(false);
                    return Results.NoContent();

                default:
                    return Results.NoContent();
            }
        });

        await aplicacao.StartAsync().ConfigureAwait(false);

        var endereco = aplicacao.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        assinante = new AssinanteDeWebhookDeTeste(aplicacao, $"{endereco}/hooks");
        return assinante;
    }

    /// <summary>Esquece o que chegou até agora.</summary>
    public void Limpar() => _recebidos.Clear();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _aplicacao.StopAsync().ConfigureAwait(false);
        await _aplicacao.DisposeAsync().ConfigureAwait(false);
    }
}
