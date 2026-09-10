using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TorreLogistica.Domain.Abstracoes.Erros;

namespace TorreLogistica.Api.Erros;

/// <summary>
/// Converte <see cref="ExcecaoDeDominio"/> em resposta ProblemDetails.
/// </summary>
/// <remarks>
/// Erro de domínio é resposta prevista, não incidente: por isso vira log de aviso
/// com o código do erro, e não log de erro com pilha de chamada.
/// </remarks>
public sealed class ManipuladorDeExcecaoDeDominio(
    IProblemDetailsService servicoDeProblemDetails,
    ILogger<ManipuladorDeExcecaoDeDominio> log) : IExceptionHandler
{
    private readonly IProblemDetailsService _servicoDeProblemDetails =
        servicoDeProblemDetails ?? throw new ArgumentNullException(nameof(servicoDeProblemDetails));

    private readonly ILogger<ManipuladorDeExcecaoDeDominio> _log =
        log ?? throw new ArgumentNullException(nameof(log));

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (exception is not ExcecaoDeDominio erroDeDominio)
        {
            return false;
        }

        var status = MapeamentoDeErrosDeDominio.ParaStatusHttp(erroDeDominio.Categoria);
        httpContext.Response.StatusCode = status;

        _log.LogWarning(
            "Erro de domínio {CodigoDoErro} ({CategoriaDoErro}) em {Metodo} {Caminho}.",
            erroDeDominio.Codigo,
            erroDeDominio.Categoria,
            httpContext.Request.Method,
            httpContext.Request.Path.Value);

        return await _servicoDeProblemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = erroDeDominio,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Type = MapeamentoDeErrosDeDominio.ParaTipo(erroDeDominio.Codigo),
                Title = MapeamentoDeErrosDeDominio.ParaTitulo(erroDeDominio.Categoria),
                Detail = erroDeDominio.Message,
                Extensions = { ["codigo"] = erroDeDominio.Codigo },
            },
        }).ConfigureAwait(false);
    }
}
