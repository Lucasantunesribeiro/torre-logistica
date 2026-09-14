using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using TorreLogistica.Infrastructure;
using TorreLogistica.Workers;

// Composition root das cargas assíncronas.
// Na Fase 0 o host existe, valida a dependência crítica e não registra job algum:
// cada job nasce junto com a fase que define o seu comportamento.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    // ContentRootPath ancorado no diretório do próprio executável, e não no
    // diretório de trabalho de quem chamou. Sem isto, iniciar o processo de outra
    // pasta faz o appsettings.json não ser encontrado — e o sintoma é silêncio:
    // o Serilog configurado por arquivo fica sem sink algum e não há erro nenhum.
    var construtor = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory,
    });

    construtor.Services.AddSerilog((provedor, configuracao) => configuracao
        .ReadFrom.Configuration(construtor.Configuration)
        .ReadFrom.Services(provedor)
        .Enrich.FromLogContext());

    construtor.Services.AdicionarCamadaDeInfrastructure(construtor.Configuration);
    construtor.Services.AddHostedService<ServicoDeVerificacaoDeInfraestrutura>();

    var host = construtor.Build();
    await host.RunAsync().ConfigureAwait(false);
    return 0;
}
// O filtro é obrigatório: WebApplicationFactory e as ferramentas do EF Core
// interrompem o Main de propósito depois que o host é construído. Sem ele, um
// catch genérico transformaria essa interrupção normal em "falha na inicialização".
catch (Exception excecao) when (excecao is not HostAbortedException
                                && excecao.GetType().Name != "StopTheHostException")
{
    Log.Fatal(excecao, "O host de workers encerrou por falha durante a inicialização.");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync().ConfigureAwait(false);
}
