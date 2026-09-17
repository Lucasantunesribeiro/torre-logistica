using TorreLogistica.Api.Autenticacao;
using TorreLogistica.Application.Rastreamento;

namespace TorreLogistica.Api.Rastreamento;

/// <summary>
/// Emissão do link de acompanhamento e a página pública que ele abre.
/// </summary>
/// <remarks>
/// <para>
/// A emissão é do console e exige autorização. A consulta é anônima por natureza — o destinatário não tem
/// conta — e por isso carrega três defesas: token forte guardado só como hash, limite de requisições por
/// endereço e resposta idêntica para qualquer token que não abra nada.
/// </para>
/// <para>
/// Ver <c>docs/adr/0024-rastreamento-publico.md</c>.
/// </para>
/// </remarks>
public static class EndpointsDeRastreamentoPublico
{
    /// <summary>Registra os endpoints.</summary>
    public static IEndpointRouteBuilder MapearEndpointsDeRastreamentoPublico(this IEndpointRouteBuilder rotas)
    {
        ArgumentNullException.ThrowIfNull(rotas);

        // O valor emitido aparece uma única vez, aqui. Só o hash fica guardado: link perdido é reemitido,
        // nunca recuperado — e reemitir revoga o anterior.
        rotas.MapPost("/api/entregas/{id:guid}/link-de-rastreamento", async (
                Guid id,
                GestaoDoRastreamentoPublico rastreamento,
                CancellationToken cancelamento) =>
            {
                var link = await rastreamento.EmitirAsync(id, cancelamento).ConfigureAwait(false);
                return Results.Ok(new { link.Token, link.ExpiraEm });
            })
            .WithTags("Rastreamento público")
            .RequireAuthorization(Politicas.OperacaoDeEntregas);

        rotas.MapGet("/api/publico/rastreamento/{token}", async (
                string token,
                HttpContext http,
                GestaoDoRastreamentoPublico rastreamento,
                CancellationToken cancelamento) =>
            {
                var acompanhamento = await rastreamento.ObterAsync(token, cancelamento).ConfigureAwait(false);

                // Página de dado pessoal: nada de cache em navegador ou proxy compartilhado.
                http.Response.Headers.CacheControl = "no-store";

                return Results.Ok(acompanhamento);
            })
            .WithTags("Rastreamento público")
            .AllowAnonymous()
            .RequireRateLimiting(PoliticasDeLimite.RastreamentoPublico);

        return rotas;
    }
}
