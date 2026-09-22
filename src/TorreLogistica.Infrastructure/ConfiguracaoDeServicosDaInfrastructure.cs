using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TorreLogistica.Application.Abstracoes.Armazenamento;
using TorreLogistica.Application.Abstracoes.Identidade;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Application.Abstracoes.Seguranca;
using TorreLogistica.Application.Comprovantes;
using TorreLogistica.Application.Rastreamento;
using TorreLogistica.Domain.Abstracoes.Identificadores;
using TorreLogistica.Domain.Abstracoes.Tempo;
using TorreLogistica.Infrastructure.Armazenamento;
using TorreLogistica.Infrastructure.Configuracao;
using TorreLogistica.Infrastructure.Desenvolvimento;
using TorreLogistica.Infrastructure.Identificadores;
using TorreLogistica.Infrastructure.Persistencia;
using TorreLogistica.Infrastructure.Seguranca;
using TorreLogistica.Infrastructure.Tempo;

namespace TorreLogistica.Infrastructure;

/// <summary>
/// Ponto único de registro da infraestrutura no contêiner de DI.
/// </summary>
public static class ConfiguracaoDeServicosDaInfrastructure
{
    /// <summary>Nome do health check que representa o banco de dados.</summary>
    public const string NomeDoHealthCheckDeBanco = "banco-de-dados";

    /// <summary>Marcação dos health checks que compõem a prontidão do processo.</summary>
    public const string EtiquetaDePronto = "pronto";

    /// <summary>
    /// Registra persistência, relógio, geração de identificador, hash de senha e health check.
    /// </summary>
    /// <remarks>
    /// O contexto de tenant é registrado com <c>TryAdd</c>: a API registra antes o dela,
    /// derivado da sessão; processos sem requisição autenticada ficam com o contexto
    /// ausente, que não enxerga dado de organização alguma.
    /// </remarks>
    public static IServiceCollection AdicionarCamadaDeInfrastructure(
        this IServiceCollection servicos,
        IConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(servicos);
        ArgumentNullException.ThrowIfNull(configuracao);

        servicos
            .AddOptions<OpcoesDeBancoDeDados>()
            .Bind(configuracao.GetSection(OpcoesDeBancoDeDados.Secao))
            .ValidateDataAnnotations()
            // Falha na subida, não na primeira requisição: configuração inválida é
            // erro de implantação e precisa aparecer enquanto ainda há quem observe.
            .ValidateOnStart();

        servicos
            .AddOptions<OpcoesDeSemeaduraDeDesenvolvimento>()
            .Bind(configuracao.GetSection(OpcoesDeSemeaduraDeDesenvolvimento.Secao));

        // Storage de objeto (ADR 0007): nesta fase, disco local com URL assinada servida pela própria API.
        // A troca por S3 ou compatível é da Fase 25 e não passa do adaptador.
        servicos
            .AddOptions<OpcoesDeArmazenamento>()
            .Bind(configuracao.GetSection(OpcoesDeArmazenamento.Secao))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        servicos.AddSingleton<IValidateOptions<OpcoesDeArmazenamento>, ValidacaoDeOpcoesDeArmazenamento>();
        servicos.AddSingleton<AssinaturaDeUrlDeArmazenamento>();
        servicos.AddSingleton<ArmazenamentoLocalDeObjetos>();
        servicos.AddSingleton<ArmazenamentoBlobDeObjetos>();

        // Qual adaptador atende é decidido uma vez, aqui, pela configuração já validada na subida. O
        // registro do local continua existindo sempre porque o endpoint de arquivos da API precisa dele
        // para conferir assinatura própria — mas quem responde por IObjectStorage é um só.
        servicos.AddSingleton<IObjectStorage>(provedor =>
            string.Equals(
                provedor.GetRequiredService<IOptions<OpcoesDeArmazenamento>>().Value.Provedor,
                OpcoesDeArmazenamento.ProvedorBlob,
                StringComparison.OrdinalIgnoreCase)
                ? provedor.GetRequiredService<ArmazenamentoBlobDeObjetos>()
                : provedor.GetRequiredService<ArmazenamentoLocalDeObjetos>());

        // Anúncio na subida, e não na primeira foto: qual storage está atendendo é a informação que
        // alguém procura quando o comprovante não aparece, e procurar não pode depender de ter havido
        // um comprovante. Resolver aqui também constrói o cliente cedo — configuração que só quebraria
        // no primeiro uso quebra agora.
        servicos.AddHostedService<AnuncioDoArmazenamento>();

        servicos
            .AddOptions<OpcoesDeComprovantes>()
            .Bind(configuracao.GetSection(OpcoesDeComprovantes.Secao))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        servicos
            .AddOptions<OpcoesDeRastreamentoPublico>()
            .Bind(configuracao.GetSection(OpcoesDeRastreamentoPublico.Secao))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        servicos.TryAddTimeProvider();
        servicos.AddSingleton<IRelogio, RelogioDoSistema>();
        servicos.AddSingleton<IGeradorDeIdentificador, GeradorDeIdentificadorUuidV7>();
        servicos.AddSingleton<IHasherDeSenha, HasherDeSenha>();
        servicos.TryAddScoped<IContextoDoTenant, ContextoDeTenantAusente>();

        servicos.AddDbContext<TorreLogisticaDbContext>((provedor, construtor) =>
        {
            var opcoes = provedor.GetRequiredService<IOptions<OpcoesDeBancoDeDados>>().Value;

            construtor
                .UseNpgsql(opcoes.CadeiaDeConexao, npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(TorreLogisticaDbContext).Assembly.FullName);
                    npgsql.CommandTimeout(opcoes.TimeoutDeComandoEmSegundos);
                    npgsql.UseNetTopologySuite();

                    if (opcoes.TentativasEmFalhaTransitoria > 0)
                    {
                        npgsql.EnableRetryOnFailure(opcoes.TentativasEmFalhaTransitoria);
                    }
                })
                .UseSnakeCaseNamingConvention();
        });

        servicos.AddScoped<IContextoDePersistencia>(provedor =>
            provedor.GetRequiredService<TorreLogisticaDbContext>());

        servicos
            .AddHealthChecks()
            .AddDbContextCheck<TorreLogisticaDbContext>(
                name: NomeDoHealthCheckDeBanco,
                tags: [EtiquetaDePronto]);

        return servicos;
    }

    /// <summary>
    /// Aplica as migrations pendentes quando a configuração autorizar.
    /// </summary>
    /// <remarks>
    /// Ligado apenas em desenvolvimento e em teste automatizado. Em produção a
    /// migration é passo explícito de implantação: aplicá-la no start faz instâncias
    /// concorrerem pela mesma alteração de schema.
    /// </remarks>
    /// <returns><see langword="true"/> se as migrations foram aplicadas.</returns>
    public static async Task<bool> AplicarMigrationsSeConfiguradoAsync(
        IServiceProvider provedor,
        CancellationToken cancelamento = default)
    {
        ArgumentNullException.ThrowIfNull(provedor);

        await using var escopo = provedor.CreateAsyncScope();

        var opcoes = escopo.ServiceProvider
            .GetRequiredService<IOptions<OpcoesDeBancoDeDados>>().Value;

        if (!opcoes.AplicarMigrationsAoIniciar)
        {
            return false;
        }

        var contexto = escopo.ServiceProvider.GetRequiredService<TorreLogisticaDbContext>();
        await contexto.Database.MigrateAsync(cancelamento).ConfigureAwait(false);
        return true;
    }

    private static void TryAddTimeProvider(this IServiceCollection servicos)
    {
        if (servicos.Any(descricao => descricao.ServiceType == typeof(TimeProvider)))
        {
            return;
        }

        servicos.AddSingleton(TimeProvider.System);
    }
}
