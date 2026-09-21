using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using TorreLogistica.Application;
using TorreLogistica.Infrastructure;
using TorreLogistica.Infrastructure.Observabilidade;
using TorreLogistica.Infrastructure.Previsao;
using TorreLogistica.Infrastructure.Retencao;
using TorreLogistica.Infrastructure.Webhooks;
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

    // As duas camadas, como a API faz: os casos de uso que os laços executam são os mesmos que a borda
    // expõe. Registrar só a infraestrutura deixaria o processo subir e morrer ao montar o primeiro
    // serviço de fundo — foi exatamente o que aconteceu na primeira tentativa de rodar esta imagem.
    construtor.Services.AdicionarCamadaDeApplication();
    construtor.Services.AdicionarCamadaDeInfrastructure(construtor.Configuration);

    // Dependências: as mesmas que a API registra.
    construtor.Services.AdicionarPrevisaoDeChegada(construtor.Configuration);
    construtor.Services.AdicionarAlertasOperacionais(construtor.Configuration);
    construtor.Services.AdicionarWebhooks(construtor.Configuration);
    construtor.Services.AdicionarRetencao(construtor.Configuration);
    construtor.Services.AdicionarMedidasDaOperacao(construtor.Configuration);

    // Os laços: só aqui.
    construtor.Services.AdicionarProcessamentoDePrevisoes();
    construtor.Services.AdicionarProcessamentoDeWebhooks();
    construtor.Services.AdicionarProcessamentoDeRetencao();
    construtor.Services.AdicionarProcessamentoDeMedidas();

    construtor.Services.AddHostedService<ServicoDeVerificacaoDeInfraestrutura>();

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
