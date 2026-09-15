using System.ComponentModel.DataAnnotations;
using TorreLogistica.Api.Autenticacao;
using TorreLogistica.Application.Alertas;
using TorreLogistica.Application.Comum;
using TorreLogistica.Domain.Alertas;

namespace TorreLogistica.Api.Alertas;

/// <summary>Resolução de alerta pelo operador.</summary>
public sealed record RequisicaoDeResolucaoDeAlerta(
    [property: StringLength(AlertaOperacional.TamanhoMaximoDaObservacao)] string? Observacao);

/// <summary>
/// Endpoints dos alertas operacionais.
/// </summary>
/// <remarks>
/// Não há criação nem alteração de alerta pela API: o motor abre e resolve pelas regras. O operador só
/// consulta e resolve — e a resolução fica no ciclo de vida com quem resolveu.
/// </remarks>
public static class EndpointsDeAlertas
{
    /// <summary>Registra os endpoints.</summary>
    public static IEndpointRouteBuilder MapearEndpointsDeAlertas(this IEndpointRouteBuilder rotas)
    {
        ArgumentNullException.ThrowIfNull(rotas);

        var grupo = rotas.MapGroup("/api/alertas").WithTags("Alertas");

        grupo.MapGet("/", async (
                int? pagina,
                int? tamanhoDaPagina,
                string? estado,
                string? tipo,
                string? severidade,
                Guid? entregaId,
                Guid? motoristaId,
                GestaoDeAlertas gestao,
                CancellationToken cancelamento) =>
                MontarFiltro(pagina, tamanhoDaPagina, estado, tipo, severidade, entregaId, motoristaId) is { } filtro
                    ? Results.Ok(await gestao.ListarAsync(filtro, cancelamento).ConfigureAwait(false))
                    : Results.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["filtro"] =
                            [
                                $"A página começa em 1, o tamanho vai de 1 a {LimitesDePaginacao.TamanhoMaximo}, e estado, tipo e severidade são nomes válidos.",
                            ],
                        },
                        title: "Requisição inválida"))
            .RequireAuthorization(Politicas.LeituraDaOperacao);

        grupo.MapGet("/{id:guid}", (Guid id, GestaoDeAlertas gestao, CancellationToken cancelamento) =>
                gestao.ObterAsync(id, cancelamento))
            .RequireAuthorization(Politicas.LeituraDaOperacao);

        grupo.MapPost("/{id:guid}/resolucao", (Guid id, RequisicaoDeResolucaoDeAlerta requisicao, GestaoDeAlertas gestao, CancellationToken cancelamento) =>
                gestao.ResolverAsync(id, requisicao.Observacao, cancelamento))
            .RequireAuthorization(Politicas.OperacaoDeEntregas)
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDeResolucaoDeAlerta>>();

        return rotas;
    }

    private static FiltroDeAlertas? MontarFiltro(
        int? pagina,
        int? tamanhoDaPagina,
        string? estado,
        string? tipo,
        string? severidade,
        Guid? entregaId,
        Guid? motoristaId)
    {
        var numero = pagina ?? 1;
        var tamanho = tamanhoDaPagina ?? LimitesDePaginacao.TamanhoPadrao;

        if (numero < 1 || tamanho is < 1 or > LimitesDePaginacao.TamanhoMaximo
            || !Ler<EstadoDoAlerta>(estado, out var estadoLido)
            || !Ler<TipoDeAlerta>(tipo, out var tipoLido)
            || !Ler<SeveridadeDoAlerta>(severidade, out var severidadeLida))
        {
            return null;
        }

        return new FiltroDeAlertas(numero, tamanho, estadoLido, tipoLido, severidadeLida, entregaId, motoristaId);
    }

    // Enumeração só por nome exato: número ou nome inexistente é recusado, nunca ignorado.
    private static bool Ler<T>(string? texto, out T? valor)
        where T : struct, Enum
    {
        valor = null;

        if (texto is null)
        {
            return true;
        }

        if (texto.Length > 40 || !Enum.GetNames<T>().Contains(texto, StringComparer.Ordinal))
        {
            return false;
        }

        valor = Enum.Parse<T>(texto);
        return true;
    }
}
