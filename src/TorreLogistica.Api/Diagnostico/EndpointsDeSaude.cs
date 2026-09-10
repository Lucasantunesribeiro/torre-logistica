using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TorreLogistica.Infrastructure;

namespace TorreLogistica.Api.Diagnostico;

/// <summary>
/// Endpoints de saúde do processo.
/// </summary>
/// <remarks>
/// A distinção entre os dois é operacional e proposital:
/// <c>/health/live</c> responde "o processo está de pé" e não toca em dependência
/// alguma — se ele consultasse o banco, uma indisponibilidade momentânea do banco
/// faria o orquestrador matar e recriar processos saudáveis, transformando uma falha
/// externa em derrubada geral. <c>/health/ready</c> responde "dá para me mandar
/// tráfego" e é aí que o banco é verificado.
/// </remarks>
public static class EndpointsDeSaude
{
    /// <summary>Caminho do endpoint de vivacidade.</summary>
    public const string CaminhoDeVivacidade = "/health/live";

    /// <summary>Caminho do endpoint de prontidão.</summary>
    public const string CaminhoDeProntidao = "/health/ready";

    /// <summary>Registra os endpoints de saúde na aplicação.</summary>
    public static IEndpointRouteBuilder MapearEndpointsDeSaude(this IEndpointRouteBuilder rotas)
    {
        ArgumentNullException.ThrowIfNull(rotas);

        rotas.MapHealthChecks(CaminhoDeVivacidade, new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = EscreverRespostaAsync,
        }).AllowAnonymous();

        rotas.MapHealthChecks(CaminhoDeProntidao, new HealthCheckOptions
        {
            Predicate = verificacao =>
                verificacao.Tags.Contains(ConfiguracaoDeServicosDaInfrastructure.EtiquetaDePronto),
            ResponseWriter = EscreverRespostaAsync,
        }).AllowAnonymous();

        return rotas;
    }

    /// <summary>
    /// Escreve o resultado como JSON contendo apenas nome, estado e duração.
    /// </summary>
    /// <remarks>
    /// Descrição e exceção de cada verificação ficam de fora de propósito: elas
    /// carregam host, banco e usuário da cadeia de conexão, e este endpoint é
    /// acessível sem autenticação.
    /// </remarks>
    private static async Task EscreverRespostaAsync(HttpContext contexto, HealthReport relatorio)
    {
        contexto.Response.ContentType = "application/json; charset=utf-8";
        contexto.Response.Headers.CacheControl = "no-store, no-cache";

        var corpo = new
        {
            status = relatorio.Status.ToString(),
            duracaoEmMs = Math.Round(relatorio.TotalDuration.TotalMilliseconds, 1),
            verificacoes = relatorio.Entries.Select(entrada => new
            {
                nome = entrada.Key,
                status = entrada.Value.Status.ToString(),
                duracaoEmMs = Math.Round(entrada.Value.Duration.TotalMilliseconds, 1),
            }),
        };

        await contexto.Response
            .WriteAsync(JsonSerializer.Serialize(corpo), contexto.RequestAborted)
            .ConfigureAwait(false);
    }
}
