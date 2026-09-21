using System.ComponentModel.DataAnnotations;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using TorreLogistica.Application.Observabilidade;

namespace TorreLogistica.Api.Observabilidade;

/// <summary>
/// Para onde a telemetria vai, e quanto dela.
/// </summary>
/// <remarks>
/// Sem <see cref="EnderecoOtlp"/> configurado, nada sai do processo: os instrumentos continuam
/// funcionando e ninguém contrata coletor para rodar o projeto na própria máquina. O protocolo é OTLP,
/// aberto — trocar de coletor é trocar um endereço, não reinstrumentar o sistema.
/// </remarks>
public sealed class OpcoesDeObservabilidade
{
    /// <summary>Seção correspondente na configuração.</summary>
    public const string Secao = "Torre:Observabilidade";

    /// <summary>Nome do serviço na telemetria.</summary>
    public string NomeDoServico { get; set; } = "torre-logistica-api";

    /// <summary>Endereço do coletor OTLP. Vazio desliga a exportação.</summary>
    public string? EnderecoOtlp { get; set; }

    /// <summary>
    /// Proporção de rastros amostrados, de 0 a 1.
    /// </summary>
    /// <remarks>
    /// Existe por causa da telemetria de GPS: a 33 posições por segundo, amostrar tudo produz volume que
    /// custa mais que o problema que ele ajudaria a achar. A decisão é por ambiente, e a amostragem
    /// respeita o pai — um rastro que começou amostrado continua inteiro, sem buraco no meio.
    /// </remarks>
    [Range(0d, 1d)]
    public double ProporcaoDeAmostragem { get; set; } = 1d;
}

/// <summary>Registro da observabilidade.</summary>
public static class ConfiguracaoDeObservabilidade
{
    private static readonly string[] CaminhosSemRastro = ["/health/live", "/health/ready"];

    /// <summary>Registra rastros e métricas.</summary>
    public static IServiceCollection AdicionarObservabilidade(
        this IServiceCollection servicos,
        IConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(servicos);
        ArgumentNullException.ThrowIfNull(configuracao);

        servicos
            .AddOptions<OpcoesDeObservabilidade>()
            .Bind(configuracao.GetSection(OpcoesDeObservabilidade.Secao))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var opcoes = new OpcoesDeObservabilidade();
        configuracao.GetSection(OpcoesDeObservabilidade.Secao).Bind(opcoes);

        var versao = typeof(ConfiguracaoDeObservabilidade).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        var recurso = ResourceBuilder.CreateDefault().AddService(opcoes.NomeDoServico, serviceVersion: versao);

        servicos
            .AddOpenTelemetry()
            .WithTracing(rastros =>
            {
                rastros
                    .SetResourceBuilder(recurso)
                    .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(opcoes.ProporcaoDeAmostragem)))
                    .AddAspNetCoreInstrumentation(instrumentacao =>
                    {
                        // Sonda de saúde bate a cada poucos segundos e não conta história nenhuma:
                        // rastreá-la só afoga o que importa.
                        instrumentacao.Filter = contexto =>
                            !CaminhosSemRastro.Any(caminho =>
                                contexto.Request.Path.StartsWithSegments(caminho, StringComparison.OrdinalIgnoreCase));
                        instrumentacao.RecordException = true;
                    })
                    .AddHttpClientInstrumentation()

                    // O Npgsql publica a própria fonte desde a versão 7: o comando SQL entra no rastro
                    // sem pacote de instrumentação e sem envelopar o provedor.
                    .AddSource("Npgsql")
                    .AddSource(RastroDaOperacao.Prefixo);

                if (!string.IsNullOrWhiteSpace(opcoes.EnderecoOtlp))
                {
                    rastros.AddOtlpExporter(exportador => exportador.Endpoint = new Uri(opcoes.EnderecoOtlp));
                }
            })
            .WithMetrics(metricas =>
            {
                metricas
                    .SetResourceBuilder(recurso)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter(RastroDaOperacao.Prefixo);

                if (!string.IsNullOrWhiteSpace(opcoes.EnderecoOtlp))
                {
                    metricas.AddOtlpExporter(exportador => exportador.Endpoint = new Uri(opcoes.EnderecoOtlp));
                }
            });

        return servicos;
    }
}
