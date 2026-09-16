using System.ComponentModel.DataAnnotations;
using TorreLogistica.Api.Autenticacao;
using TorreLogistica.Api.Comum;
using TorreLogistica.Application.Comum;
using TorreLogistica.Application.Ocorrencias;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Ocorrencias;

namespace TorreLogistica.Api.Ocorrencias;

/// <summary>Registro de ocorrência pelo console.</summary>
public sealed record RequisicaoDeOcorrencia(
    [property: Required(ErrorMessage = "Informe o tipo da ocorrência.")] TipoDeOcorrencia? Tipo,
    SeveridadeDaOcorrencia? Severidade,
    [property: StringLength(Ocorrencia.TamanhoMaximoDaObservacao)] string? Observacao,
    double? Latitude,
    double? Longitude,
    DateTimeOffset? OcorridaEm);

/// <summary>
/// Endpoints das ocorrências.
/// </summary>
/// <remarks>
/// Só criação e consulta: ocorrência é fato registrado, e fato não se edita nem se apaga. Corrigir é
/// registrar outra ocorrência — as duas ficam na história da entrega.
/// </remarks>
public static class EndpointsDeOcorrencias
{
    private const int QuantidadeMaximaDeTipos = 6;

    /// <summary>Registra os endpoints.</summary>
    public static IEndpointRouteBuilder MapearEndpointsDeOcorrencias(this IEndpointRouteBuilder rotas)
    {
        ArgumentNullException.ThrowIfNull(rotas);

        var grupo = rotas.MapGroup("/api/ocorrencias").WithTags("Ocorrências");

        grupo.MapGet("/", async (
                int? pagina,
                int? tamanhoDaPagina,
                Guid? entregaId,
                Guid? rotaId,
                Guid? motoristaId,
                string[]? tipo,
                string? severidade,
                DateTimeOffset? aPartirDe,
                DateTimeOffset? ate,
                GestaoDeOcorrencias gestao,
                CancellationToken cancelamento) =>
                MontarFiltro(pagina, tamanhoDaPagina, entregaId, rotaId, motoristaId, tipo, severidade, aPartirDe, ate) is { } filtro
                    ? Results.Ok(await gestao.ListarAsync(filtro, cancelamento).ConfigureAwait(false))
                    : FiltroInvalido())
            .RequireAuthorization(Politicas.LeituraDaOperacao);

        grupo.MapGet("/{id:guid}", (Guid id, GestaoDeOcorrencias gestao, CancellationToken cancelamento) =>
                gestao.ObterAsync(id, cancelamento))
            .RequireAuthorization(Politicas.LeituraDaOperacao);

        var daEntrega = rotas.MapGroup("/api/entregas/{id:guid}/ocorrencias").WithTags("Ocorrências");

        daEntrega.MapGet("/", (Guid id, GestaoDeOcorrencias gestao, CancellationToken cancelamento) =>
                gestao.ListarDaEntregaAsync(id, cancelamento))
            .RequireAuthorization(Politicas.LeituraDaOperacao);

        daEntrega.MapPost("/", async (Guid id, RequisicaoDeOcorrencia requisicao, GestaoDeOcorrencias gestao, CancellationToken cancelamento) =>
            {
                var registrada = await gestao
                    .RegistrarAsync(
                        id,
                        new DadosDeOcorrencia(
                            requisicao.Tipo!.Value,
                            requisicao.Severidade,
                            // Motivo tipado é da tentativa, e a tentativa vem pelo canal do motorista.
                            null,
                            requisicao.Observacao,
                            requisicao.Latitude,
                            requisicao.Longitude,
                            requisicao.OcorridaEm),
                        cancelamento)
                    .ConfigureAwait(false);

                return Results.Created($"/api/ocorrencias/{registrada.Id}", registrada);
            })
            .RequireAuthorization(Politicas.OperacaoDeEntregas)
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDeOcorrencia>>();

        return rotas;
    }

    private static FiltroDeOcorrencias? MontarFiltro(
        int? pagina,
        int? tamanhoDaPagina,
        Guid? entregaId,
        Guid? rotaId,
        Guid? motoristaId,
        string[]? tipo,
        string? severidade,
        DateTimeOffset? aPartirDe,
        DateTimeOffset? ate)
    {
        var numero = pagina ?? 1;
        var tamanho = tamanhoDaPagina ?? LimitesDePaginacao.TamanhoPadrao;

        if (numero < 1
            || tamanho is < 1 or > LimitesDePaginacao.TamanhoMaximo
            || (aPartirDe is { } de && ate is { } limite && de > limite)
            || severidade is { Length: > 40 }
            || (severidade is not null && !Enum.GetNames<SeveridadeDaOcorrencia>().Contains(severidade, StringComparer.Ordinal)))
        {
            return null;
        }

        return LeituraDeStatus.Ler<TipoDeOcorrencia>(tipo, QuantidadeMaximaDeTipos) is { } tipos
            ? new FiltroDeOcorrencias(
                numero,
                tamanho,
                entregaId,
                rotaId,
                motoristaId,
                tipos,
                severidade is null ? null : Enum.Parse<SeveridadeDaOcorrencia>(severidade),
                aPartirDe,
                ate)
            : null;
    }

    private static IResult FiltroInvalido() =>
        Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["filtro"] =
                [
                    $"A página começa em 1, o tamanho vai de 1 a {LimitesDePaginacao.TamanhoMaximo}, cada tipo e a severidade "
                    + "são nomes válidos e o início do período não passa do fim.",
                ],
            },
            title: "Requisição inválida");
}
