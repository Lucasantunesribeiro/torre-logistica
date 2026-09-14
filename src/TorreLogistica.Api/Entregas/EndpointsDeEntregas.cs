using System.ComponentModel.DataAnnotations;
using TorreLogistica.Api.Autenticacao;
using TorreLogistica.Api.Cadastros;
using TorreLogistica.Api.Comum;
using TorreLogistica.Application.Comum;
using TorreLogistica.Application.Entregas;
using TorreLogistica.Domain.Entregas;

namespace TorreLogistica.Api.Entregas;

/// <summary>Criação de entrega. Sem endereço, vale o do destinatário.</summary>
public sealed record RequisicaoDeEntrega(
    [property: Required(ErrorMessage = "Informe o cliente.")] Guid? ClienteId,
    [property: Required(ErrorMessage = "Informe o destinatário.")] Guid? DestinatarioId,
    RequisicaoDeEndereco? Endereco,
    double? Latitude,
    double? Longitude,
    [property: Required(ErrorMessage = "Informe o início da janela prometida.")] DateTimeOffset? PrometidaDe,
    [property: Required(ErrorMessage = "Informe o fim da janela prometida.")] DateTimeOffset? PrometidaAte,
    [property: StringLength(Entrega.TamanhoMaximoDasObservacoes)] string? Observacoes);

/// <summary>Alteração de entrega: o estado completo desejado, com a versão lida.</summary>
public sealed record RequisicaoDeAtualizacaoDeEntrega(
    [property: Required(ErrorMessage = "Informe a versão lida.")] uint? Versao,
    [property: Required(ErrorMessage = "Informe o cliente.")] Guid? ClienteId,
    [property: Required(ErrorMessage = "Informe o destinatário.")] Guid? DestinatarioId,
    [property: Required(ErrorMessage = "Informe o endereço.")] RequisicaoDeEndereco? Endereco,
    double? Latitude,
    double? Longitude,
    [property: Required(ErrorMessage = "Informe o início da janela prometida.")] DateTimeOffset? PrometidaDe,
    [property: Required(ErrorMessage = "Informe o fim da janela prometida.")] DateTimeOffset? PrometidaAte,
    [property: StringLength(Entrega.TamanhoMaximoDasObservacoes)] string? Observacoes);

/// <summary>Cancelamento de entrega.</summary>
public sealed record RequisicaoDeCancelamento(
    [property: Required(ErrorMessage = "Informe o motivo.")] MotivoDeCancelamento? Motivo,
    [property: StringLength(Entrega.TamanhoMaximoDaDescricaoDoCancelamento)] string? Descricao);

/// <summary>
/// Endpoints das entregas.
/// </summary>
/// <remarks>
/// <para>
/// Não há rota que receba status: o status muda só pelas operações — nesta fase, criar e
/// cancelar. Um <c>status</c> no corpo é campo desconhecido e responde 400.
/// </para>
/// <para>
/// Não há <c>DELETE</c>. Entrega que não vai acontecer é cancelada, e o cancelamento entra
/// na timeline.
/// </para>
/// </remarks>
public static class EndpointsDeEntregas
{
    private const int TamanhoMaximoDoCodigo = 30;
    private const int QuantidadeMaximaDeStatus = 9;

    /// <summary>Registra os endpoints.</summary>
    public static IEndpointRouteBuilder MapearEndpointsDeEntregas(this IEndpointRouteBuilder rotas)
    {
        ArgumentNullException.ThrowIfNull(rotas);

        var grupo = rotas.MapGroup("/api/entregas").WithTags("Entregas");

        grupo.MapGet("/", async (
                int? pagina,
                int? tamanhoDaPagina,
                string[]? status,
                Guid? clienteId,
                Guid? destinatarioId,
                string? codigo,
                DateTimeOffset? janelaAPartirDe,
                DateTimeOffset? janelaAte,
                GestaoDeEntregas gestao,
                CancellationToken cancelamento) =>
                MontarFiltro(pagina, tamanhoDaPagina, status, clienteId, destinatarioId, codigo, janelaAPartirDe, janelaAte) is { } filtro
                    ? Results.Ok(await gestao.ListarAsync(filtro, cancelamento).ConfigureAwait(false))
                    : FiltroInvalido())
            .RequireAuthorization(Politicas.LeituraDaOperacao);

        grupo.MapGet("/{id:guid}", (Guid id, GestaoDeEntregas gestao, CancellationToken cancelamento) =>
                gestao.ObterAsync(id, cancelamento))
            .RequireAuthorization(Politicas.LeituraDaOperacao);

        grupo.MapGet("/{id:guid}/eventos", (Guid id, GestaoDeEntregas gestao, CancellationToken cancelamento) =>
                gestao.ListarEventosAsync(id, cancelamento))
            .RequireAuthorization(Politicas.LeituraDaOperacao);

        grupo.MapPost("/", async (RequisicaoDeEntrega requisicao, GestaoDeEntregas gestao, CancellationToken cancelamento) =>
            {
                var criada = await gestao
                    .CriarAsync(
                        new DadosDeEntrega(
                            requisicao.ClienteId!.Value,
                            requisicao.DestinatarioId!.Value,
                            requisicao.Endereco?.ParaDados(),
                            requisicao.Latitude,
                            requisicao.Longitude,
                            requisicao.PrometidaDe!.Value,
                            requisicao.PrometidaAte!.Value,
                            requisicao.Observacoes),
                        cancelamento)
                    .ConfigureAwait(false);
                return Results.Created($"/api/entregas/{criada.Id}", criada);
            })
            .RequireAuthorization(Politicas.OperacaoDeEntregas)
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDeEntrega>>();

        grupo.MapPut("/{id:guid}", (Guid id, RequisicaoDeAtualizacaoDeEntrega requisicao, GestaoDeEntregas gestao, CancellationToken cancelamento) =>
                gestao.AtualizarAsync(
                    id,
                    requisicao.Versao!.Value,
                    new DadosDeEntrega(
                        requisicao.ClienteId!.Value,
                        requisicao.DestinatarioId!.Value,
                        requisicao.Endereco!.ParaDados(),
                        requisicao.Latitude,
                        requisicao.Longitude,
                        requisicao.PrometidaDe!.Value,
                        requisicao.PrometidaAte!.Value,
                        requisicao.Observacoes),
                    cancelamento))
            .RequireAuthorization(Politicas.OperacaoDeEntregas)
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDeAtualizacaoDeEntrega>>();

        grupo.MapPost("/{id:guid}/cancelamento", (Guid id, RequisicaoDeCancelamento requisicao, GestaoDeEntregas gestao, CancellationToken cancelamento) =>
                gestao.CancelarAsync(id, requisicao.Motivo!.Value, requisicao.Descricao, cancelamento))
            .RequireAuthorization(Politicas.OperacaoDeEntregas)
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDeCancelamento>>();

        return rotas;
    }

    private static FiltroDeEntregas? MontarFiltro(
        int? pagina,
        int? tamanhoDaPagina,
        string[]? status,
        Guid? clienteId,
        Guid? destinatarioId,
        string? codigo,
        DateTimeOffset? janelaAPartirDe,
        DateTimeOffset? janelaAte)
    {
        var numero = pagina ?? 1;
        var tamanho = tamanhoDaPagina ?? LimitesDePaginacao.TamanhoPadrao;

        if (numero < 1
            || tamanho is < 1 or > LimitesDePaginacao.TamanhoMaximo
            || codigo is { Length: > TamanhoMaximoDoCodigo }
            || status is { Length: > QuantidadeMaximaDeStatus }
            || (janelaAPartirDe is { } de && janelaAte is { } ate && de > ate))
        {
            return null;
        }

        return LeituraDeStatus.Ler<StatusDaEntrega>(status, QuantidadeMaximaDeStatus) is { } statusAceitos
            ? new FiltroDeEntregas(numero, tamanho, statusAceitos, clienteId, destinatarioId, codigo, janelaAPartirDe, janelaAte)
            : null;
    }

    private static IResult FiltroInvalido() =>
        Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["filtro"] =
                [
                    $"A página começa em 1, o tamanho vai de 1 a {LimitesDePaginacao.TamanhoMaximo}, o código tem no máximo "
                    + $"{TamanhoMaximoDoCodigo} caracteres, cada status é um nome válido e o início do período não passa do fim.",
                ],
            },
            title: "Requisição inválida");
}
