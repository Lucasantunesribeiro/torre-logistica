using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using TorreLogistica.Api.Alertas;
using TorreLogistica.Api.Arquivos;
using TorreLogistica.Api.Autenticacao;
using TorreLogistica.Api.Cadastros;
using TorreLogistica.Api.Comprovantes;
using TorreLogistica.Api.Correlacao;
using TorreLogistica.Api.Diagnostico;
using TorreLogistica.Api.Entregas;
using TorreLogistica.Api.Erros;
using TorreLogistica.Api.Execucao;
using TorreLogistica.Api.Indicadores;
using TorreLogistica.Api.Integracoes;
using TorreLogistica.Api.Ocorrencias;
using TorreLogistica.Api.Previsao;
using TorreLogistica.Api.Rastreamento;
using TorreLogistica.Api.Rotas;
using TorreLogistica.Api.Seguranca;
using TorreLogistica.Api.TempoReal;
using TorreLogistica.Api.Usuarios;
using TorreLogistica.Api.Webhooks;
using TorreLogistica.Application;
using TorreLogistica.Application.Abstracoes.Correlacao;
using TorreLogistica.Application.Abstracoes.Identidade;
using TorreLogistica.Infrastructure;
using TorreLogistica.Infrastructure.Desenvolvimento;
using TorreLogistica.Infrastructure.Previsao;
using TorreLogistica.Infrastructure.Webhooks;

// Logger provisório: garante que uma falha durante a própria construção do host
// ainda apareça em algum lugar, em vez de morrer silenciosamente.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var construtor = WebApplication.CreateBuilder(args);

    // preserveStaticLogger: o host usa o próprio logger, e não o Log.Logger global. Sem isto, o
    // CloseAndFlush do finally de um host que termina fecha o logger global que outro host do mesmo
    // processo — como as APIs em sequência dos testes de integração — ainda está usando, e os logs dele somem.
    construtor.Host.UseSerilog(
        (contexto, provedor, configuracao) => configuracao
            .ReadFrom.Configuration(contexto.Configuration)
            .ReadFrom.Services(provedor)
            .Enrich.FromLogContext(),
        preserveStaticLogger: true);

    construtor.WebHost.ConfigureKestrel(kestrel =>
    {
        // Versão e nome do servidor só interessam a quem procura alvo.
        kestrel.AddServerHeader = false;

        // Limite de corpo deliberadamente baixo: esta API troca JSON. Arquivo de
        // comprovante sobe direto para o storage por URL assinada (Fase 14).
        kestrel.Limits.MaxRequestBodySize = 1 * 1024 * 1024;
    });

    construtor.Services.ConfigureHttpJsonOptions(json =>
    {
        // Campo desconhecido no corpo é recusado, não ignorado. É a defesa estrutural
        // contra mass assignment: mandar "organizacaoId" num cadastro vira 400, e não
        // um campo silenciosamente descartado que alguém um dia passe a ler.
        json.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;

        // Enumeração só por nome. Aceitar número deixaria "perfil": 99 chegar ao domínio.
        json.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
    });

    construtor.Services.AddHttpContextAccessor();
    construtor.Services.AddSingleton<IContextoDeCorrelacao, ContextoDeCorrelacaoHttp>();

    // Registrado antes da infraestrutura, que só completa com o contexto sem tenant o
    // que ainda não estiver registrado.
    construtor.Services.AddScoped<ContextoDoUsuarioHttp>();
    construtor.Services.AddScoped<IContextoDoUsuario>(provedor => provedor.GetRequiredService<ContextoDoUsuarioHttp>());
    construtor.Services.AddScoped<IContextoDoTenant>(provedor => provedor.GetRequiredService<ContextoDoUsuarioHttp>());

    construtor.Services.AddProblemDetails(opcoes =>
        opcoes.CustomizeProblemDetails = contexto =>
        {
            var http = contexto.HttpContext;

            contexto.ProblemDetails.Instance ??= $"{http.Request.Method} {http.Request.Path}";
            contexto.ProblemDetails.Extensions["traceId"] =
                Activity.Current?.Id ?? http.TraceIdentifier;

            if (http.Items.TryGetValue(CorrelacaoHttp.ChaveNoContexto, out var correlacao)
                && correlacao is string idDeCorrelacao)
            {
                contexto.ProblemDetails.Extensions["idDeCorrelacao"] = idDeCorrelacao;
            }
        });

    // A ordem importa: o manipulador específico decide antes do genérico.
    construtor.Services.AddExceptionHandler<ManipuladorDeExcecaoDeDominio>();
    construtor.Services.AddExceptionHandler<ManipuladorDeExcecaoNaoTratada>();

    construtor.Services
        .AddOptions<OpcoesDeCors>()
        .Bind(construtor.Configuration.GetSection(OpcoesDeCors.Secao));

    construtor.Services.AddCors();

    // A política é montada a partir das opções resolvidas pela DI, e não de uma
    // leitura direta da configuração aqui. Ler agora congelaria o valor antes de as
    // demais fontes de configuração do ambiente entrarem em vigor.
    construtor.Services
        .AddOptions<CorsOptions>()
        .Configure<IOptions<OpcoesDeCors>>((cors, opcoesDaTorre) =>
            cors.AddPolicy(OpcoesDeCors.NomeDaPolitica, politica =>
            {
                var origens = opcoesDaTorre.Value.OrigensPermitidas;

                // Sem origem configurada para o ambiente, nada é liberado. Falha fechada.
                if (origens.Count == 0)
                {
                    return;
                }

                politica
                    .WithOrigins([.. origens])
                    // X-Requested-With e X-SignalR-User-Agent: enviados pelo cliente SignalR do navegador.
                    .WithHeaders("Content-Type", "Authorization", CorrelacaoHttp.NomeDoCabecalho, "X-Requested-With", "X-SignalR-User-Agent")
                    .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
                    .WithExposedHeaders(CorrelacaoHttp.NomeDoCabecalho, "Retry-After")
                    .AllowCredentials();
            }));

    construtor.Services.AddOpenApi();

    construtor.Services.AdicionarAutenticacaoDaTorre(construtor.Configuration);
    construtor.Services.AdicionarCamadaDeApplication();
    construtor.Services.AdicionarCamadaDeInfrastructure(construtor.Configuration);
    construtor.Services.AdicionarTempoRealDaOperacao();
    construtor.Services.AdicionarPrevisaoDeChegada(construtor.Configuration);
    construtor.Services.AdicionarAlertasOperacionais(construtor.Configuration);
    construtor.Services.AdicionarWebhooks(construtor.Configuration);

    var aplicacao = construtor.Build();

    // Resolver agora faz chave ausente ou inválida derrubar a subida, e não a
    // primeira requisição autenticada.
    _ = aplicacao.Services.GetRequiredService<ChaveDeAssinaturaDeToken>();

    var migrationsAplicadas = await ConfiguracaoDeServicosDaInfrastructure
        .AplicarMigrationsSeConfiguradoAsync(aplicacao.Services)
        .ConfigureAwait(false);

    if (migrationsAplicadas)
    {
        aplicacao.Logger.LogInformation("Migrations pendentes aplicadas durante a inicialização da API.");
    }

    if (aplicacao.Environment.IsDevelopment())
    {
        await SemeadorDeDesenvolvimento.SemearSeHabilitadoAsync(aplicacao.Services).ConfigureAwait(false);
    }

    aplicacao.UseMiddleware<MiddlewareDeCabecalhosDeSeguranca>();
    aplicacao.UseMiddleware<MiddlewareDeCorrelacao>();

    if (!aplicacao.Environment.IsDevelopment())
    {
        aplicacao.UseHsts();
    }

    aplicacao.UseSerilogRequestLogging(opcoes =>
        opcoes.EnrichDiagnosticContext = (diagnostico, http) =>
        {
            if (http.Items.TryGetValue(CorrelacaoHttp.ChaveNoContexto, out var correlacao)
                && correlacao is string idDeCorrelacao)
            {
                diagnostico.Set("IdDeCorrelacao", idDeCorrelacao);
            }
        });

    // Antes do tratamento de erro: assim a resposta de falha também sai com os
    // cabeçalhos de CORS e o navegador consegue ler o motivo em vez de um erro opaco.
    aplicacao.UseCors(OpcoesDeCors.NomeDaPolitica);

    aplicacao.UseExceptionHandler();
    aplicacao.UseStatusCodePages();

    aplicacao.UseAuthentication();
    aplicacao.UseAuthorization();

    // Depois da autorização: o limite de telemetria particiona pela conta autenticada. Os limites de
    // login e renovação continuam por endereço e continuam antes de qualquer conferência de senha,
    // porque o limitador roda antes do endpoint.
    aplicacao.UseRateLimiter();

    if (aplicacao.Environment.IsDevelopment())
    {
        aplicacao.MapOpenApi().AllowAnonymous();
    }

    aplicacao.MapearEndpointsDeSaude();
    aplicacao.MapearEndpointsDeAutenticacao();
    aplicacao.MapearEndpointsDeUsuarios();
    aplicacao.MapearEndpointsDeCadastros();
    aplicacao.MapearEndpointsDeEntregas();
    aplicacao.MapearEndpointsDeRotas();
    aplicacao.MapearEndpointsDeExecucao();
    aplicacao.MapearEndpointsDeRastreamento();
    aplicacao.MapearEndpointsDeRastreamentoPublico();
    aplicacao.MapearEndpointsDeIntegracoes();
    aplicacao.MapearEndpointsDeWebhooks();
    aplicacao.MapearEndpointsDePrevisao();
    aplicacao.MapearEndpointsDeIndicadores();
    aplicacao.MapearEndpointsDeOcorrencias();
    aplicacao.MapearEndpointsDeComprovantes();
    aplicacao.MapearEndpointsDeArquivos();
    aplicacao.MapearEndpointsDeAlertas();
    aplicacao.MapearTempoRealDaOperacao();

    await aplicacao.RunAsync().ConfigureAwait(false);
    return 0;
}
// O filtro é obrigatório: WebApplicationFactory e as ferramentas do EF Core
// interrompem o Main de propósito depois que o host é construído. Sem ele, um
// catch genérico transformaria essa interrupção normal em "falha na inicialização".
catch (Exception excecao) when (excecao is not HostAbortedException
                                && excecao.GetType().Name != "StopTheHostException")
{
    Log.Fatal(excecao, "A API encerrou por falha durante a inicialização.");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync().ConfigureAwait(false);
}

/// <summary>
/// Exposto para que os testes de integração possam hospedar a API real.
/// </summary>
public partial class Program;
