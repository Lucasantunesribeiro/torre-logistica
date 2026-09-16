using TorreLogistica.Api.Autenticacao;
using TorreLogistica.Application.Comprovantes;

namespace TorreLogistica.Api.Comprovantes;

/// <summary>
/// Leitura do comprovante pelo console.
/// </summary>
/// <remarks>
/// Só consulta: quem prova a entrega é quem entregou, e o registro vem pelo canal do motorista. A resposta
/// traz metadados e URLs assinadas de curta duração — nunca o arquivo, nunca um link permanente.
/// </remarks>
public static class EndpointsDeComprovantes
{
    /// <summary>Registra os endpoints.</summary>
    public static IEndpointRouteBuilder MapearEndpointsDeComprovantes(this IEndpointRouteBuilder rotas)
    {
        ArgumentNullException.ThrowIfNull(rotas);

        rotas.MapGet("/api/entregas/{id:guid}/comprovante", (Guid id, GestaoDeComprovantes gestao, CancellationToken cancelamento) =>
                gestao.ObterDaEntregaAsync(id, cancelamento))
            .WithTags("Comprovantes")
            .RequireAuthorization(Politicas.LeituraDaOperacao);

        return rotas;
    }
}
