using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TorreLogistica.Api.Correlacao;

namespace TorreLogistica.Api.Erros;

/// <summary>
/// Última linha de defesa: transforma qualquer exceção inesperada em ProblemDetails 500.
/// </summary>
/// <remarks>
/// A mensagem da exceção nunca vai para o corpo da resposta. Texto de exceção
/// costuma carregar caminho de arquivo, nome de tabela e trecho de configuração —
/// material de reconhecimento para quem estiver testando a API. O detalhe fica no
/// log, amarrado ao identificador de correlação que o cliente recebeu.
/// </remarks>
public sealed class ManipuladorDeExcecaoNaoTratada(
    IProblemDetailsService servicoDeProblemDetails,
    ILogger<ManipuladorDeExcecaoNaoTratada> log) : IExceptionHandler
{
    private const string MensagemNeutra =
        "Ocorreu um erro inesperado ao processar a requisição. "
        + "Informe o identificador de correlação ao suporte.";

    private readonly IProblemDetailsService _servicoDeProblemDetails =
        servicoDeProblemDetails ?? throw new ArgumentNullException(nameof(servicoDeProblemDetails));

    private readonly ILogger<ManipuladorDeExcecaoNaoTratada> _log =
        log ?? throw new ArgumentNullException(nameof(log));

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var idDeCorrelacao = httpContext.Items.TryGetValue(CorrelacaoHttp.ChaveNoContexto, out var valor)
            && valor is string identificador
                ? identificador
                : ContextoDeCorrelacaoHttp.SemCorrelacao;

        _log.LogError(
            exception,
            "Falha não tratada em {Metodo} {Caminho} (correlação {IdDeCorrelacao}).",
            httpContext.Request.Method,
            httpContext.Request.Path.Value,
            idDeCorrelacao);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await _servicoDeProblemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Type = MapeamentoDeErrosDeDominio.ParaTipo("erro_inesperado"),
                Title = "Erro inesperado",
                Detail = MensagemNeutra,
                Extensions = { ["codigo"] = "erro_inesperado" },
            },
        }).ConfigureAwait(false);
    }
}
