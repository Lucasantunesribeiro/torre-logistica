using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using TorreLogistica.Simulator.Cliente;
using TorreLogistica.Simulator.Configuracao;

// Simulador da operação logística.
// Na Fase 0 ele exercita o único contrato que a API já publica — a prontidão —
// para provar que o caminho "simulador → HTTP → aplicação real" está de pé.
// Os cenários narrativos chegam na Fase 23.
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

    construtor.Services
        .AddOptions<OpcoesDoSimulador>()
        .Bind(construtor.Configuration.GetSection(OpcoesDoSimulador.Secao))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    construtor.Services
        .AddHttpClient<ClienteDaApiDaTorre>(ClienteDaApiDaTorre.NomeDoCliente, (provedor, http) =>
        {
            var opcoes = provedor.GetRequiredService<IOptions<OpcoesDoSimulador>>().Value;

            // A barra final é obrigatória: sem ela o caminho relativo substituiria o
            // último segmento do endereço base em vez de ser acrescentado a ele.
            http.BaseAddress = new Uri(opcoes.UrlBaseDaApi.TrimEnd('/') + '/');
            http.Timeout = TimeSpan.FromSeconds(opcoes.TimeoutEmSegundos);
        });

    using var host = construtor.Build();

    var cliente = host.Services.GetRequiredService<ClienteDaApiDaTorre>();
    var enderecoDaApi = host.Services
        .GetRequiredService<IOptions<OpcoesDoSimulador>>().Value.UrlBaseDaApi;

    using var cancelamento = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var prontidao = await cliente.ConsultarProntidaoAsync(cancelamento.Token).ConfigureAwait(false);

    if (prontidao is null)
    {
        Log.Error("A API em {EnderecoDaApi} respondeu sem corpo reconhecível.", enderecoDaApi);
        return 1;
    }

    Log.Information(
        "API em {EnderecoDaApi} respondeu prontidão {Status} em {DuracaoEmMs} ms.",
        enderecoDaApi,
        prontidao.Status,
        prontidao.DuracaoEmMs);

    return string.Equals(prontidao.Status, "Healthy", StringComparison.Ordinal) ? 0 : 2;
}
catch (Exception excecao)
{
    Log.Fatal(excecao, "O simulador encerrou por falha.");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync().ConfigureAwait(false);
}
