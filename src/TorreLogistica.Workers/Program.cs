using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using TorreLogistica.Workers;

// Processo de trabalho da Torre Logística.
//
// Ele existe porque parte do sistema precisa acontecer quando ninguém está olhando: o outbox despacha,
// a previsão é reavaliada, os alertas nascem e o rastro vencido é apagado — tudo isso sem requisição
// nenhuma chegando.
//
// Antes esses laços moravam dentro da API, e isso amarrava as duas coisas: a API não podia escalar sem
// duplicar o trabalho de fundo, e o trabalho de fundo não podia parar sem derrubar a API. Aqui eles são
// dois processos, com ciclos de vida próprios.
//
// A API continua registrando as MESMAS dependências (opções, clientes, serviços de consulta), porque ela
// lê o estado que este processo produz. O que ela não registra mais são os laços.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var construtor = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory,
    });

    construtor.Services.AddSerilog((provedor, configuracao) => configuracao
        .ReadFrom.Configuration(construtor.Configuration)
        .ReadFrom.Services(provedor)
        .Enrich.FromLogContext());

    // Todo o registro vive em ComposicaoDoProcessoDeTrabalho, e não aqui: assim o teste de composição
    // monta exatamente o mesmo grafo que este processo monta, em vez de uma cópia que sai de sincronia.
    construtor.Services.AdicionarProcessoDeTrabalho(construtor.Configuration);

    using var host = construtor.Build();

    Log.Information("Processo de trabalho iniciado: previsão, alertas, webhooks, retenção e medidas.");
    await host.RunAsync().ConfigureAwait(false);

    return 0;
}
catch (Exception excecao)
{
    Log.Fatal(excecao, "O processo de trabalho encerrou por falha.");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync().ConfigureAwait(false);
}
