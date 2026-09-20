using System.ComponentModel.DataAnnotations;
using TorreLogistica.Api.Autenticacao;
using TorreLogistica.Application.Comum;
using TorreLogistica.Application.Webhooks;
using TorreLogistica.Domain.Webhooks;

namespace TorreLogistica.Api.Webhooks;

/// <summary>Criação de assinatura de webhook.</summary>
public sealed record RequisicaoDeAssinaturaDeWebhook(
    [property: Required(ErrorMessage = "Informe o nome da assinatura.")]
    [property: StringLength(PoliticaDeWebhook.TamanhoMaximoDoNome)]
    string? Nome,
    [property: Required(ErrorMessage = "Informe o endereço de destino.")]
    [property: StringLength(PoliticaDeWebhook.TamanhoMaximoDaUrl)]
    string? Url,
    IReadOnlyList<string>? Eventos);

/// <summary>
/// Assinaturas de webhook, situação das entregas e reenvio manual.
/// </summary>
/// <remarks>
/// Tudo sob a política do administrador: assinar webhook cria um efeito que sai da nossa rede para um
/// endereço escolhido, e reenviar dispara esse efeito de novo. É decisão de segurança, não de operação.
/// </remarks>
public static class EndpointsDeWebhooks
{
    /// <summary>Registra os endpoints.</summary>
    public static IEndpointRouteBuilder MapearEndpointsDeWebhooks(this IEndpointRouteBuilder rotas)
    {
        ArgumentNullException.ThrowIfNull(rotas);

        var grupo = rotas.MapGroup("/api/webhooks").WithTags("Webhooks");

        // O segredo aparece só nesta resposta: perdido, revoga-se e cria-se outra assinatura.
        grupo.MapPost("/assinaturas", async (
                RequisicaoDeAssinaturaDeWebhook requisicao,
                GestaoDeAssinaturasDeWebhook gestao,
                CancellationToken cancelamento) =>
            {
                var criada = await gestao
                    .CriarAsync(requisicao.Nome, requisicao.Url, requisicao.Eventos, cancelamento)
                    .ConfigureAwait(false);

                return Results.Created($"/api/webhooks/assinaturas/{criada.Id}", criada);
            })
            .RequireAuthorization(Politicas.GestaoDeUsuarios)
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDeAssinaturaDeWebhook>>();

        grupo.MapGet("/assinaturas", (
                int? pagina,
                int? tamanhoDaPagina,
                GestaoDeAssinaturasDeWebhook gestao,
                CancellationToken cancelamento) =>
                gestao.ListarAsync(PaginaEfetiva(pagina), TamanhoEfetivo(tamanhoDaPagina), cancelamento))
            .RequireAuthorization(Politicas.GestaoDeUsuarios);

        grupo.MapGet("/assinaturas/{id:guid}", (Guid id, GestaoDeAssinaturasDeWebhook gestao, CancellationToken cancelamento) =>
                gestao.ObterAsync(id, cancelamento))
            .RequireAuthorization(Politicas.GestaoDeUsuarios);

        grupo.MapPost("/assinaturas/{id:guid}/revogacao", (Guid id, GestaoDeAssinaturasDeWebhook gestao, CancellationToken cancelamento) =>
                gestao.RevogarAsync(id, cancelamento))
            .RequireAuthorization(Politicas.GestaoDeUsuarios);

        grupo.MapGet("/entregas", (
                int? pagina,
                int? tamanhoDaPagina,
                EstadoDaEntregaDeWebhook? estado,
                Guid? assinaturaId,
                ConsultaDeWebhooks consulta,
                CancellationToken cancelamento) =>
                consulta.ListarAsync(
                    new FiltroDeEntregasDeWebhook(PaginaEfetiva(pagina), TamanhoEfetivo(tamanhoDaPagina), estado, assinaturaId),
                    cancelamento))
            .RequireAuthorization(Politicas.GestaoDeUsuarios);

        grupo.MapGet("/entregas/{id:guid}", (Guid id, ConsultaDeWebhooks consulta, CancellationToken cancelamento) =>
                consulta.ObterAsync(id, cancelamento))
            .RequireAuthorization(Politicas.GestaoDeUsuarios);

        // Único caminho de volta para a fila depois de esgotadas as tentativas — e ele passa por uma pessoa.
        grupo.MapPost("/entregas/{id:guid}/reenvio", (Guid id, ConsultaDeWebhooks consulta, CancellationToken cancelamento) =>
                consulta.ReenviarAsync(id, cancelamento))
            .RequireAuthorization(Politicas.GestaoDeUsuarios);

        return rotas;
    }

    private static int PaginaEfetiva(int? pagina) => pagina is > 0 ? pagina.Value : 1;

    private static int TamanhoEfetivo(int? tamanho) =>
        Math.Clamp(tamanho ?? LimitesDePaginacao.TamanhoPadrao, 1, LimitesDePaginacao.TamanhoMaximo);
}
