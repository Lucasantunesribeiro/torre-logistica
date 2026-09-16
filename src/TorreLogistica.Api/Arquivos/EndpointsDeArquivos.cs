using Microsoft.AspNetCore.Http.Features;
using TorreLogistica.Api.Erros;
using TorreLogistica.Infrastructure.Armazenamento;

namespace TorreLogistica.Api.Arquivos;

/// <summary>
/// Envio e leitura de objetos por URL assinada.
/// </summary>
/// <remarks>
/// <para>
/// Estes endpoints são o storage desta fase, não a API de negócio: a credencial é a própria assinatura da
/// URL, emitida por quem já passou por autenticação e autorização. Por isso são anônimos — exatamente como
/// seriam num S3 com URL pré-assinada, onde o bucket não conhece a sessão do usuário.
/// </para>
/// <para>
/// A assinatura cobre operação, chave, tipo de conteúdo, tamanho máximo e expiração. Sem ela, ou fora do
/// prazo, a resposta é 403 sem revelar se o objeto existe.
/// </para>
/// </remarks>
public static class EndpointsDeArquivos
{
    /// <summary>Registra os endpoints.</summary>
    public static IEndpointRouteBuilder MapearEndpointsDeArquivos(this IEndpointRouteBuilder rotas)
    {
        ArgumentNullException.ThrowIfNull(rotas);

        var grupo = rotas.MapGroup(ArmazenamentoLocalDeObjetos.CaminhoBase).WithTags("Arquivos").AllowAnonymous();

        // Os parâmetros da assinatura são opcionais no contrato de propósito: ausentes, a resposta é a mesma
        // de assinatura inválida (403). Exigi-los faria o binding responder 400 e revelar que a rota existe
        // e o que ela espera.
        grupo.MapPut("/{**chave}", async (
            string chave,
            long? expiraEm,
            string? assinatura,
            string? tipoDeConteudo,
            long? tamanhoMaximo,
            HttpContext http,
            ArmazenamentoLocalDeObjetos armazenamento,
            CancellationToken cancelamento) =>
        {
            if (expiraEm is not { } prazo || tipoDeConteudo is not { } tipo || tamanhoMaximo is not { } limiteAutorizado
                || !armazenamento.Autorizado(ArmazenamentoLocalDeObjetos.OperacaoDeEnvio, chave, tipo, limiteAutorizado, prazo, assinatura))
            {
                return NaoAutorizado();
            }

            if (!string.Equals(http.Request.ContentType, tipoDeConteudo, StringComparison.OrdinalIgnoreCase))
            {
                return RespostasDeProblema.Resultado(
                    StatusCodes.Status415UnsupportedMediaType,
                    "tipo_de_arquivo_nao_aceito",
                    "Tipo de arquivo não aceito",
                    "O conteúdo enviado não é do tipo autorizado para esta URL.");
            }

            // O limite global do Kestrel é de 1 MB porque a API troca JSON; só esta rota aceita o tamanho
            // autorizado na assinatura, e nada além dele.
            if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limite)
            {
                limite.MaxRequestBodySize = tamanhoMaximo;
            }

            try
            {
                var objeto = await armazenamento
                    .GravarAsync(chave, tipoDeConteudo, http.Request.Body, tamanhoMaximo.Value, cancelamento)
                    .ConfigureAwait(false);

                return Results.Created($"{ArmazenamentoLocalDeObjetos.CaminhoBase}/{objeto.Chave}", new
                {
                    objeto.Chave,
                    objeto.TamanhoEmBytes,
                    objeto.HashSha256,
                });
            }
            catch (ArmazenamentoRecusouException recusa)
            {
                return RespostasDeProblema.Resultado(
                    recusa.Codigo == "arquivo_grande_demais" ? StatusCodes.Status413PayloadTooLarge : StatusCodes.Status400BadRequest,
                    recusa.Codigo,
                    "Arquivo recusado",
                    recusa.Message);
            }
            catch (BadHttpRequestException)
            {
                return RespostasDeProblema.Resultado(
                    StatusCodes.Status413PayloadTooLarge,
                    "arquivo_grande_demais",
                    "Arquivo recusado",
                    "O arquivo passou do tamanho autorizado.");
            }
        });

        grupo.MapGet("/{**chave}", (
            string chave,
            long? expiraEm,
            string? assinatura,
            HttpContext http,
            ArmazenamentoLocalDeObjetos armazenamento) =>
        {
            if (expiraEm is not { } prazo
                || !armazenamento.Autorizado(ArmazenamentoLocalDeObjetos.OperacaoDeLeitura, chave, string.Empty, 0, prazo, assinatura))
            {
                return NaoAutorizado();
            }

            var conteudo = armazenamento.Abrir(chave);
            if (conteudo is null)
            {
                return RespostasDeProblema.Resultado(
                    StatusCodes.Status404NotFound,
                    "arquivo_nao_encontrado",
                    "Arquivo não encontrado",
                    "O arquivo não existe no armazenamento.");
            }

            // Comprovante é dado privado: nada de cache em navegador ou proxy, e nunca renderizado em página.
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers.ContentDisposition = "attachment";

            return Results.Stream(conteudo, "application/octet-stream");
        });

        return rotas;
    }

    private static IResult NaoAutorizado() =>
        RespostasDeProblema.Resultado(
            StatusCodes.Status403Forbidden,
            "assinatura_invalida",
            "Assinatura inválida",
            "A autorização deste arquivo é inválida ou expirou.");
}
