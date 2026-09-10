namespace TorreLogistica.Api.Seguranca;

/// <summary>
/// Aplica os cabeçalhos de segurança mínimos em toda resposta da API.
/// </summary>
/// <remarks>
/// Esta API serve apenas JSON — nunca HTML, script ou frame. Por isso a política
/// aqui é a mais restritiva possível: <c>default-src 'none'</c> e nenhuma
/// permissão de recurso do navegador. As aplicações web têm política própria,
/// definida no host que serve os arquivos estáticos.
/// </remarks>
public sealed class MiddlewareDeCabecalhosDeSeguranca(RequestDelegate proximo)
{
    private static readonly (string Nome, string Valor)[] Cabecalhos =
    [
        // Impede o navegador de "adivinhar" um tipo diferente do declarado.
        ("X-Content-Type-Options", "nosniff"),

        // Resposta de API não deve ser embutida em página alguma.
        ("X-Frame-Options", "DENY"),
        ("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; sandbox"),

        // Não vazar a URL da API — que pode conter identificadores — para terceiros.
        ("Referrer-Policy", "no-referrer"),

        // Nenhum recurso sensível do dispositivo é usado a partir da própria API.
        ("Permissions-Policy", "geolocation=(), camera=(), microphone=(), payment=(), usb=()"),

        ("Cross-Origin-Resource-Policy", "same-origin"),
        ("X-Permitted-Cross-Domain-Policies", "none"),
    ];

    private readonly RequestDelegate _proximo =
        proximo ?? throw new ArgumentNullException(nameof(proximo));

    /// <summary>Executa o middleware.</summary>
    public Task InvokeAsync(HttpContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        contexto.Response.OnStarting(static estado =>
        {
            var resposta = (HttpResponse)estado;

            foreach (var (nome, valor) in Cabecalhos)
            {
                resposta.Headers[nome] = valor;
            }

            // Nome e versão do servidor só ajudam quem está procurando alvo.
            resposta.Headers.Remove("Server");
            resposta.Headers.Remove("X-Powered-By");

            return Task.CompletedTask;
        }, contexto.Response);

        return _proximo(contexto);
    }
}
