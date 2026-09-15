using TorreLogistica.Api.Autenticacao;
using TorreLogistica.Application.Previsao;

namespace TorreLogistica.Api.Previsao;

/// <summary>
/// Previsão de chegada e SLA de uma entrega, para o console.
/// </summary>
/// <remarks>
/// Só leitura: a previsão é calculada pelo sistema a partir da execução, nunca informada por quem consulta.
/// A resposta traz a explicação de cada mudança de situação — o que responde "por que esta entrega passou
/// de Normal para Risco" — sem coordenada nem dado do destinatário.
/// </remarks>
public static class EndpointsDePrevisao
{
    /// <summary>Registra os endpoints.</summary>
    public static IEndpointRouteBuilder MapearEndpointsDePrevisao(this IEndpointRouteBuilder rotas)
    {
        ArgumentNullException.ThrowIfNull(rotas);

        rotas.MapGet("/api/entregas/{id:guid}/previsao", (Guid id, ConsultaDePrevisao consulta, CancellationToken cancelamento) =>
                consulta.ObterAsync(id, cancelamento))
            .WithTags("Previsão")
            .RequireAuthorization(Politicas.LeituraDaOperacao);

        return rotas;
    }
}
