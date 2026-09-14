using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TorreLogistica.Api.Erros;
using TorreLogistica.Api.Seguranca;

namespace TorreLogistica.Api.Autenticacao;

/// <summary>
/// Exige que a requisição venha de uma origem conhecida.
/// </summary>
/// <remarks>
/// <para>
/// Aplicado aos endpoints que leem ou gravam o cookie de renovação. O cookie é
/// <c>SameSite=Strict</c>, o que já impede o navegador de enviá-lo a partir de outro
/// site; esta checagem é a segunda camada, que não depende de o navegador implementar
/// <c>SameSite</c> corretamente.
/// </para>
/// <para>
/// Sem cabeçalho <c>Origin</c> a requisição é recusada. Navegadores sempre enviam
/// <c>Origin</c> em <c>POST</c>; quem não envia não é um navegador, e cliente fora do
/// navegador não tem motivo para usar autenticação por cookie.
/// </para>
/// </remarks>
public sealed class FiltroDeOrigemConfiavel(
    IOptions<OpcoesDeCors> opcoesDeCors,
    ILogger<FiltroDeOrigemConfiavel> log) : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var origem = context.HttpContext.Request.Headers.Origin.ToString();
        var permitidas = opcoesDeCors.Value.OrigensPermitidas;

        if (string.IsNullOrWhiteSpace(origem)
            || !permitidas.Contains(origem, StringComparer.OrdinalIgnoreCase))
        {
            log.LogWarning(
                "Requisição com cookie de sessão recusada por origem não confiável em {Caminho}.",
                context.HttpContext.Request.Path.Value);

            return RespostasDeProblema.Resultado(
                StatusCodes.Status403Forbidden,
                "origem_nao_autorizada",
                "Origem não autorizada",
                "Esta operação só é aceita a partir das aplicações da Torre Logística.");
        }

        return await next(context).ConfigureAwait(false);
    }
}

/// <summary>
/// Valida as anotações de dados do corpo antes de chegar ao caso de uso.
/// </summary>
/// <remarks>
/// Entrada malformada é <c>400</c>, com a lista de campos; regra de negócio violada é
/// <c>422</c>, vinda do domínio. Separar os dois diz ao cliente se o problema é o
/// formulário ou a operação.
/// </remarks>
public sealed class FiltroDeValidacao<T> : IEndpointFilter
    where T : class
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var corpo = context.Arguments.OfType<T>().FirstOrDefault();

        if (corpo is null)
        {
            return Invalida(new Dictionary<string, string[]>
            {
                ["corpo"] = ["O corpo da requisição é obrigatório."],
            });
        }

        var resultados = new List<ValidationResult>();

        if (!Validator.TryValidateObject(corpo, new ValidationContext(corpo), resultados, validateAllProperties: true))
        {
            var erros = resultados
                .SelectMany(resultado => (resultado.MemberNames.Any() ? resultado.MemberNames : ["corpo"])
                    .Select(membro => (Campo: JsonNamingPolicy.CamelCase.ConvertName(membro),
                        Mensagem: resultado.ErrorMessage ?? "Valor inválido.")))
                .GroupBy(erro => erro.Campo, StringComparer.Ordinal)
                .ToDictionary(grupo => grupo.Key, grupo => grupo.Select(erro => erro.Mensagem).ToArray(), StringComparer.Ordinal);

            return Invalida(erros);
        }

        return await next(context).ConfigureAwait(false);
    }

    private static IResult Invalida(IDictionary<string, string[]> erros) =>
        Results.ValidationProblem(
            erros,
            title: "Requisição inválida",
            type: MapeamentoDeErrosDeDominio.ParaTipo("requisicao_invalida"),
            extensions: new Dictionary<string, object?> { ["codigo"] = "requisicao_invalida" });
}

/// <summary>Impede que resposta com token seja guardada em cache por navegador ou proxy.</summary>
public sealed class FiltroSemCache : IEndpointFilter
{
    /// <inheritdoc />
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.HttpContext.Response.Headers.Pragma = "no-cache";
        return next(context);
    }
}
