using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using TorreLogistica.Simulator.Cenario;
using TorreLogistica.Simulator.Cliente;
using TorreLogistica.Simulator.Configuracao;

// Simulador da operação logística.
//
// Ele encena seis histórias contra a API real, autenticado, como qualquer integrador faria. Não há
// atalho para o banco — e é isso que faz a demonstração valer: o que a plateia vê acontecendo é o
// mesmo caminho que a operação de verdade percorre.
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

    var opcoesDoSimulador = host.Services.GetRequiredService<IOptions<OpcoesDoSimulador>>().Value;
    var cliente = new ClienteDaApiDaTorre(
        host.Services.GetRequiredService<IHttpClientFactory>().CreateClient(ClienteDaApiDaTorre.NomeDoCliente),
        opcoesDoSimulador.OrigemDeclarada);
    var enderecoDaApi = opcoesDoSimulador.UrlBaseDaApi;

    using var cancelamento = new CancellationTokenSource(TimeSpan.FromMinutes(30));

    var prontidao = await cliente.ConsultarProntidaoAsync(cancelamento.Token).ConfigureAwait(false);

    if (prontidao is null || !string.Equals(prontidao.Status, "Healthy", StringComparison.Ordinal))
    {
        Log.Error(
            "A API em {EnderecoDaApi} não está pronta ({Status}). A demonstração não começa contra um sistema doente.",
            enderecoDaApi,
            prontidao?.Status ?? "sem resposta");
        return 2;
    }

    Log.Information(
        "API em {EnderecoDaApi} pronta em {DuracaoEmMs} ms. Semente {Semente}, tempo {Multiplicador}× mais rápido.",
        enderecoDaApi,
        prontidao.DuracaoEmMs,
        opcoesDoSimulador.Semente,
        opcoesDoSimulador.MultiplicadorDeTempo);

    var encenacao = new EncenacaoDaDemonstracao(
        cliente,
        opcoesDoSimulador,
        host.Services.GetRequiredService<ILogger<EncenacaoDaDemonstracao>>());

    var desfechos = await encenacao.EncenarAsync(cancelamento.Token).ConfigureAwait(false);

    Log.Information("Demonstração encerrada. {Quantidade} histórias encenadas:", desfechos.Count);

    foreach (var desfecho in desfechos)
    {
        Log.Information(
            "  {Codigo} · {Historia}: {Status} — {Eventos}",
            desfecho.Codigo,
            desfecho.Historia,
            desfecho.StatusFinal,
            string.Join(" → ", desfecho.Eventos));
    }

    return 0;
}
catch (ErroDaApiDaTorre excecao)
{
    Log.Fatal("A API recusou uma chamada da demonstração: {Mensagem}", excecao.Message);
    return 1;
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
