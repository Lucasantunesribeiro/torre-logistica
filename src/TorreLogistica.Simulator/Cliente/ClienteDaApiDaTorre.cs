using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace TorreLogistica.Simulator.Cliente;

/// <summary>
/// Resposta do endpoint de prontidão da API.
/// </summary>
/// <param name="Status">Estado agregado das verificações.</param>
/// <param name="DuracaoEmMs">Tempo total gasto pelas verificações.</param>
public sealed record RespostaDeProntidao(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("duracaoEmMs")] double DuracaoEmMs);

/// <summary>
/// Cliente HTTP do simulador para a API da Torre Logística.
/// </summary>
/// <remarks>
/// O simulador conversa com a aplicação exclusivamente por HTTP, como qualquer
/// integrador externo — ele não referencia Domain, Application nem Infrastructure e,
/// por construção, não alcança o banco. Isso é o que faz a demonstração validar os
/// mesmos caminhos que a produção usa. Ver <c>docs/adr/0006-simulador-externo.md</c>.
/// </remarks>
public sealed class ClienteDaApiDaTorre(HttpClient http)
{
    /// <summary>Nome do cliente nomeado registrado na DI.</summary>
    public const string NomeDoCliente = "api-torre";

    private readonly HttpClient _http = http ?? throw new ArgumentNullException(nameof(http));

    /// <summary>Consulta o endpoint de prontidão da API.</summary>
    /// <returns>A resposta lida, ou <see langword="null"/> se a API respondeu sem corpo válido.</returns>
    public async Task<RespostaDeProntidao?> ConsultarProntidaoAsync(CancellationToken cancelamento)
    {
        using var resposta = await _http
            .GetAsync(new Uri("health/ready", UriKind.Relative), cancelamento)
            .ConfigureAwait(false);

        // Um 503 aqui é informação legítima — a API está de pé e diz que não está
        // pronta. O simulador precisa distinguir isso de "não consegui falar com ela".
        return await resposta.Content
            .ReadFromJsonAsync<RespostaDeProntidao>(cancelamento)
            .ConfigureAwait(false);
    }
}
