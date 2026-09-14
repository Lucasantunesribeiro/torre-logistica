using System.ComponentModel.DataAnnotations;
using TorreLogistica.Api.Autenticacao;
using TorreLogistica.Api.Comum;
using TorreLogistica.Application.Comum;
using TorreLogistica.Application.Rotas;
using TorreLogistica.Domain.Rotas;

namespace TorreLogistica.Api.Rotas;

/// <summary>Criação de rota.</summary>
public sealed record RequisicaoDeRota(
    [property: Required(ErrorMessage = "Informe a data da rota.")] DateOnly? Data,
    Guid? HubId);

/// <summary>Inclusão de entregas na rota, na ordem em que devem entrar.</summary>
public sealed record RequisicaoDeInclusaoDeEntregas(
    [property: Required(ErrorMessage = "Informe as entregas.")]
    [property: MinLength(1, ErrorMessage = "Informe ao menos uma entrega.")]
    [property: MaxLength(RegrasDaRota.QuantidadeMaximaPorInclusao, ErrorMessage = "Muitas entregas numa inclusão só.")]
    Guid[]? Entregas);

/// <summary>Nova ordem das paradas, com a versão lida da rota.</summary>
public sealed record RequisicaoDeOrdem(
    [property: Required(ErrorMessage = "Informe a versão lida.")] uint? Versao,
    [property: Required(ErrorMessage = "Informe a ordem.")]
    [property: MinLength(1, ErrorMessage = "Informe a ordem.")]
    [property: MaxLength(RegrasDaRota.QuantidadeMaximaDeParadas, ErrorMessage = "Ordem maior que o limite de paradas.")]
    Guid[]? Entregas);

/// <summary>Motorista da rota.</summary>
public sealed record RequisicaoDeMotoristaDaRota(
    [property: Required(ErrorMessage = "Informe o motorista.")] Guid? MotoristaId);

/// <summary>Veículo da rota.</summary>
public sealed record RequisicaoDeVeiculoDaRota(
    [property: Required(ErrorMessage = "Informe o veículo.")] Guid? VeiculoId);

/// <summary>Saída planejada da rota.</summary>
public sealed record RequisicaoDeSaidaDaRota(
    [property: Required(ErrorMessage = "Informe a saída planejada.")] DateTimeOffset? SaidaPlanejada);

/// <summary>
/// Endpoints de montagem de rotas.
/// </summary>
/// <remarks>
/// <para>
/// Cada mudança é uma operação com nome — incluir entregas, retirar, reordenar, atribuir,
/// planejar saída, confirmar, cancelar — e não um <c>PUT</c> da rota inteira: são mudanças com
/// regras e efeitos diferentes sobre as entregas, e cada uma entra na timeline como o que é.
/// </para>
/// <para>
/// Só a reordenação exige a versão lida: é a única que substitui algo que outra pessoa pode ter
/// mudado desde a leitura. As demais são comandos cujo resultado é exatamente o pedido.
/// </para>
/// </remarks>
public static class EndpointsDeRotas
{
    private const int QuantidadeMaximaDeStatus = 5;

    /// <summary>Registra os endpoints.</summary>
    public static IEndpointRouteBuilder MapearEndpointsDeRotas(this IEndpointRouteBuilder rotas)
    {
        ArgumentNullException.ThrowIfNull(rotas);

        var grupo = rotas.MapGroup("/api/rotas").WithTags("Rotas");

        grupo.MapGet("/", async (
                int? pagina,
                int? tamanhoDaPagina,
                DateOnly? data,
                string[]? status,
                Guid? motoristaId,
                GestaoDeRotas gestao,
                CancellationToken cancelamento) =>
                MontarFiltro(pagina, tamanhoDaPagina, data, status, motoristaId) is { } filtro
                    ? Results.Ok(await gestao.ListarAsync(filtro, cancelamento).ConfigureAwait(false))
                    : FiltroInvalido())
            .RequireAuthorization(Politicas.LeituraDaOperacao);

        grupo.MapGet("/{id:guid}", (Guid id, GestaoDeRotas gestao, CancellationToken cancelamento) =>
                gestao.ObterAsync(id, cancelamento))
            .RequireAuthorization(Politicas.LeituraDaOperacao);

        grupo.MapGet("/{id:guid}/eventos", (Guid id, GestaoDeRotas gestao, CancellationToken cancelamento) =>
                gestao.ListarEventosAsync(id, cancelamento))
            .RequireAuthorization(Politicas.LeituraDaOperacao);

        grupo.MapPost("/", async (RequisicaoDeRota requisicao, GestaoDeRotas gestao, CancellationToken cancelamento) =>
            {
                var criada = await gestao.CriarAsync(requisicao.Data!.Value, requisicao.HubId, cancelamento).ConfigureAwait(false);
                return Results.Created($"/api/rotas/{criada.Id}", criada);
            })
            .RequireAuthorization(Politicas.GestaoDaOperacao)
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDeRota>>();

        grupo.MapPost("/{id:guid}/paradas", (Guid id, RequisicaoDeInclusaoDeEntregas requisicao, GestaoDeRotas gestao, CancellationToken cancelamento) =>
                gestao.AdicionarEntregasAsync(id, requisicao.Entregas!, cancelamento))
            .RequireAuthorization(Politicas.GestaoDaOperacao)
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDeInclusaoDeEntregas>>();

        grupo.MapDelete("/{id:guid}/paradas/{entregaId:guid}", (Guid id, Guid entregaId, GestaoDeRotas gestao, CancellationToken cancelamento) =>
                gestao.RemoverEntregaAsync(id, entregaId, cancelamento))
            .RequireAuthorization(Politicas.GestaoDaOperacao);

        grupo.MapPut("/{id:guid}/ordem", (Guid id, RequisicaoDeOrdem requisicao, GestaoDeRotas gestao, CancellationToken cancelamento) =>
                gestao.ReordenarAsync(id, requisicao.Versao!.Value, requisicao.Entregas!, cancelamento))
            .RequireAuthorization(Politicas.GestaoDaOperacao)
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDeOrdem>>();

        grupo.MapPut("/{id:guid}/motorista", (Guid id, RequisicaoDeMotoristaDaRota requisicao, GestaoDeRotas gestao, CancellationToken cancelamento) =>
                gestao.AtribuirMotoristaAsync(id, requisicao.MotoristaId!.Value, cancelamento))
            .RequireAuthorization(Politicas.GestaoDaOperacao)
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDeMotoristaDaRota>>();

        grupo.MapPut("/{id:guid}/veiculo", (Guid id, RequisicaoDeVeiculoDaRota requisicao, GestaoDeRotas gestao, CancellationToken cancelamento) =>
                gestao.AtribuirVeiculoAsync(id, requisicao.VeiculoId!.Value, cancelamento))
            .RequireAuthorization(Politicas.GestaoDaOperacao)
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDeVeiculoDaRota>>();

        grupo.MapPut("/{id:guid}/saida", (Guid id, RequisicaoDeSaidaDaRota requisicao, GestaoDeRotas gestao, CancellationToken cancelamento) =>
                gestao.PlanejarSaidaAsync(id, requisicao.SaidaPlanejada!.Value, cancelamento))
            .RequireAuthorization(Politicas.GestaoDaOperacao)
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDeSaidaDaRota>>();

        grupo.MapPost("/{id:guid}/planejamento", (Guid id, GestaoDeRotas gestao, CancellationToken cancelamento) =>
                gestao.PlanejarAsync(id, cancelamento))
            .RequireAuthorization(Politicas.GestaoDaOperacao);

        grupo.MapPost("/{id:guid}/cancelamento", (Guid id, GestaoDeRotas gestao, CancellationToken cancelamento) =>
                gestao.CancelarAsync(id, cancelamento))
            .RequireAuthorization(Politicas.GestaoDaOperacao);

        return rotas;
    }

    private static FiltroDeRotas? MontarFiltro(int? pagina, int? tamanhoDaPagina, DateOnly? data, string[]? status, Guid? motoristaId)
    {
        var numero = pagina ?? 1;
        var tamanho = tamanhoDaPagina ?? LimitesDePaginacao.TamanhoPadrao;

        if (numero < 1 || tamanho is < 1 or > LimitesDePaginacao.TamanhoMaximo)
        {
            return null;
        }

        return LeituraDeStatus.Ler<StatusDaRota>(status, QuantidadeMaximaDeStatus) is { } statusLidos
            ? new FiltroDeRotas(numero, tamanho, data, statusLidos, motoristaId)
            : null;
    }

    private static IResult FiltroInvalido() =>
        Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["filtro"] =
                [
                    $"A página começa em 1, o tamanho vai de 1 a {LimitesDePaginacao.TamanhoMaximo} e cada status é um nome válido.",
                ],
            },
            title: "Requisição inválida");
}
