using TorreLogistica.Api.Autenticacao;
using TorreLogistica.Application.Operacao;

namespace TorreLogistica.Api.Operacao;

/// <summary>
/// Leituras do estado da operação como um todo, não de uma entrega ou motorista.
/// </summary>
/// <remarks>
/// O pulso existe para o console distinguir "estou falando com a API" de "há operação acontecendo agora".
/// A primeira é a prontidão; a segunda é a idade do dado mais recente. Confundir as duas foi o que fez a
/// demonstração parecer ao vivo quando mostrava uma encenação encerrada.
/// </remarks>
public static class EndpointsDeOperacao
{
    /// <summary>Registra os endpoints.</summary>
    public static IEndpointRouteBuilder MapearEndpointsDeOperacao(this IEndpointRouteBuilder rotas)
    {
        ArgumentNullException.ThrowIfNull(rotas);

        var grupo = rotas.MapGroup("/api/operacao").WithTags("Operação");

        grupo.MapGet("/pulso", (ConsultaDoPulsoDaOperacao consulta, CancellationToken cancelamento) =>
                consulta.ObterAsync(cancelamento))
            .RequireAuthorization(Politicas.LeituraDaOperacao);

        return rotas;
    }
}
