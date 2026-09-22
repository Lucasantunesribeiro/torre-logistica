using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace TorreLogistica.Api.Erros;

/// <summary>
/// Converte falha de leitura da requisição em erro de cliente, com o contrato da API.
/// </summary>
/// <remarks>
/// <para>
/// O ASP.NET Core lança <see cref="BadHttpRequestException"/> quando não consegue transformar o corpo
/// recebido no parâmetro tipado do endpoint: JSON com sintaxe quebrada, campo com tipo incompatível,
/// corpo vazio onde ele é obrigatório, corpo maior que o limite. Todos são erro de quem chamou.
/// </para>
/// <para>
/// Sem este manipulador, a exceção caía no
/// <see cref="ManipuladorDeExcecaoNaoTratada"/> e virava <c>500</c> — dizer "a culpa foi minha" para uma
/// requisição que o cliente montou errado, além de esconder o problema real dele atrás de uma mensagem
/// que manda procurar o suporte.
/// </para>
/// <para>
/// O manipulador é deliberadamente estreito: trata **só** <see cref="BadHttpRequestException"/>, que a
/// plataforma só lança ao ler a requisição, e respeita o status que ela mesma carrega. Um
/// <c>JsonException</c> nosso, ao serializar uma resposta, continua sendo <c>500</c> — que é o que ele é.
/// </para>
/// <para>
/// A mensagem da exceção **não** vai para o corpo: ela nomeia o parâmetro do endpoint, o caminho dentro do
/// JSON e a posição do byte. Isso é mapa da implementação para quem estiver sondando a API. O detalhe fica
/// no log, amarrado ao identificador de correlação que o cliente recebeu.
/// </para>
/// </remarks>
public sealed class ManipuladorDeCorpoInvalido(
    IProblemDetailsService servicoDeProblemDetails,
    ILogger<ManipuladorDeCorpoInvalido> log) : IExceptionHandler
{
    private const string DetalheDeCorpoInvalido =
        "O corpo da requisição não pôde ser lido. Envie JSON válido, com os campos nos tipos esperados.";

    private const string DetalheDeCorpoGrande =
        "O corpo da requisição passou do tamanho aceito.";

    private readonly IProblemDetailsService _servicoDeProblemDetails =
        servicoDeProblemDetails ?? throw new ArgumentNullException(nameof(servicoDeProblemDetails));

    private readonly ILogger<ManipuladorDeCorpoInvalido> _log =
        log ?? throw new ArgumentNullException(nameof(log));

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (exception is not BadHttpRequestException requisicaoInvalida)
        {
            return false;
        }

        var (status, codigo, detalhe) = requisicaoInvalida.StatusCode switch
        {
            StatusCodes.Status413PayloadTooLarge =>
                (StatusCodes.Status413PayloadTooLarge, "corpo_grande_demais", DetalheDeCorpoGrande),
            _ => (StatusCodes.Status400BadRequest, "corpo_invalido", DetalheDeCorpoInvalido),
        };

        // Aviso, não erro: a API se comportou como devia. Quem errou foi a requisição — e o texto da
        // exceção só existe aqui, no log, nunca na resposta.
        _log.LogWarning(
            "Requisição recusada na leitura do corpo em {Metodo} {Caminho}: {Motivo}",
            httpContext.Request.Method,
            httpContext.Request.Path.Value,
            requisicaoInvalida.Message);

        httpContext.Response.StatusCode = status;

        return await _servicoDeProblemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Type = MapeamentoDeErrosDeDominio.ParaTipo(codigo),
                Title = "Requisição inválida",
                Detail = detalhe,
                Extensions = { ["codigo"] = codigo },
            },
        }).ConfigureAwait(false);
    }
}
