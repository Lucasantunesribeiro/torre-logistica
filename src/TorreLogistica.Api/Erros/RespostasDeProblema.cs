using Microsoft.AspNetCore.Mvc;

namespace TorreLogistica.Api.Erros;

/// <summary>
/// Respostas de erro da borda HTTP que não nascem de exceção de domínio: 401, 403, 429.
/// </summary>
/// <remarks>
/// Passam pelo mesmo <see cref="IProblemDetailsService"/> das demais, então recebem
/// <c>traceId</c> e <c>idDeCorrelacao</c> igual a qualquer outro erro da API.
/// </remarks>
public static class RespostasDeProblema
{
    /// <summary>Escreve um ProblemDetails diretamente na resposta.</summary>
    public static async Task EscreverAsync(HttpContext http, int status, string codigo, string titulo, string detalhe)
    {
        ArgumentNullException.ThrowIfNull(http);

        http.Response.StatusCode = status;

        var servico = http.RequestServices.GetRequiredService<IProblemDetailsService>();
        await servico.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = titulo,
                Detail = detalhe,
                Type = MapeamentoDeErrosDeDominio.ParaTipo(codigo),
                Extensions = { ["codigo"] = codigo },
            },
        }).ConfigureAwait(false);
    }

    /// <summary>Resultado de endpoint com ProblemDetails.</summary>
    public static IResult Resultado(int status, string codigo, string titulo, string detalhe) =>
        Results.Problem(
            detail: detalhe,
            statusCode: status,
            title: titulo,
            type: MapeamentoDeErrosDeDominio.ParaTipo(codigo),
            extensions: new Dictionary<string, object?> { ["codigo"] = codigo });

    /// <summary>401 neutro de credenciais: o mesmo para qualquer causa de falha de login.</summary>
    public static IResult CredenciaisInvalidas() => Resultado(
        StatusCodes.Status401Unauthorized,
        "credenciais_invalidas",
        "Não autenticado",
        "Organização, e-mail ou senha incorretos.");

    /// <summary>401 de sessão: token de renovação ausente, inválido, expirado ou revogado.</summary>
    public static IResult SessaoInvalida() => Resultado(
        StatusCodes.Status401Unauthorized,
        "sessao_invalida",
        "Não autenticado",
        "A sessão não é mais válida. Faça login novamente.");
}
