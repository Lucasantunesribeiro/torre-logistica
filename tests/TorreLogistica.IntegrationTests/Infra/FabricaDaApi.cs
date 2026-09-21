using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog.Core;
using Serilog.Events;

namespace TorreLogistica.IntegrationTests.Infra;

/// <summary>
/// Sobe a API real contra o PostgreSQL da coleção.
/// </summary>
/// <remarks>
/// Uma fábrica por teste: cada uma tem sua própria chave de assinatura efêmera, seus
/// próprios limites de requisição e, quando pedido, seu próprio relógio controlado.
/// </remarks>
public sealed class FabricaDaApi(
    ContainerPostgis banco,
    IReadOnlyDictionary<string, string?>? configuracaoAdicional = null,
    TimeProvider? relogio = null,
    Action<IServiceCollection>? servicosDeTeste = null) : WebApplicationFactory<Program>
{
    private static readonly string ChaveDeWebhookDoProcesso =
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    /// <summary>Nome com que a API de teste se identifica no banco.</summary>
    public const string NomeDaAplicacao = "torre-api-teste";

    /// <summary>Origem que os testes tratam como autorizada.</summary>
    public const string OrigemAutorizada = "https://console.torre.teste";

    /// <summary>Origem que os testes tratam como não autorizada.</summary>
    public const string OrigemNaoAutorizada = "https://site-qualquer.teste";

    /// <summary>Eventos de log emitidos pela API durante o teste.</summary>
    public SinkEmMemoria Logs { get; } = new();

    /// <summary>Diretório do storage de objeto deste teste.</summary>
    public string DiretorioDoArmazenamento { get; } =
        Path.Combine(Path.GetTempPath(), "torre-armazenamento", Guid.CreateVersion7().ToString("N"));

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // "Testing" e não "Development": exercita o caminho de produção — sem página de
        // exceção do desenvolvedor, sem OpenAPI, sem semeadura, sem CORS de localhost.
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration(configuracao =>
        {
            var valores = new Dictionary<string, string?>
            {
                // O nome da aplicação identifica as conexões desta API no banco. É o que permite ao
                // teste de resiliência derrubar só as conexões dela, sem atingir as que a própria suíte
                // usa para montar cenário — matar tudo faria o teste seguinte falhar por um estrago que
                // não causou.
                ["Torre:BancoDeDados:CadeiaDeConexao"] = $"{banco.CadeiaDeConexao};Application Name={NomeDaAplicacao}",
                ["Torre:BancoDeDados:AplicarMigrationsAoIniciar"] = "false",
                ["Torre:Cors:OrigensPermitidas:0"] = OrigemAutorizada,

                // Limites altos por padrão: a suíte faz muitos logins seguidos do mesmo
                // "endereço". O teste de limite configura o seu próprio.
                ["Torre:LimiteDeRequisicoes:TentativasDeLoginPorMinuto"] = "100000",
                ["Torre:LimiteDeRequisicoes:RenovacoesPorMinuto"] = "100000",
                ["Torre:LimiteDeRequisicoes:EnviosDePosicaoPorMinuto"] = "100000",
                ["Torre:LimiteDeRequisicoes:SincronizacoesPorMinuto"] = "100000",
                ["Torre:LimiteDeRequisicoes:ConsultasPublicasPorMinuto"] = "100000",
                ["Torre:LimiteDeRequisicoes:RequisicoesDeIntegracaoPorMinuto"] = "100000",

                // O processador de webhooks fica desligado: o teste chama despacho e entrega quando quer,
                // e assim uma rodada de fundo não consome o outbox de outro teste no meio da asserção.
                ["Torre:Webhooks:ProcessarEmSegundoPlano"] = "false",

                // Uma chave por processo, e não por fábrica: o banco é compartilhado pela coleção, e com
                // chave efêmera por API o segredo gravado por um teste não abriria no seguinte. Sorteada
                // em tempo de execução, para que nenhum valor de chave exista no repositório.
                ["Torre:Webhooks:ChaveDeCriptografia"] = ChaveDeWebhookDoProcesso,

                // O assinante dos testes é um servidor na própria máquina; sem isto a trava de SSRF o recusa.
                ["Torre:Webhooks:PermitirDestinoLocal"] = "true",

                // A limpeza por retenção varre por idade e atravessa organizações: uma rodada de fundo
                // apagaria a telemetria de outro teste no meio da asserção. Os testes de retenção chamam
                // a limpeza quando querem.
                ["Torre:Retencao:LimparEmSegundoPlano"] = "false",

                // Storage de objeto em disco, isolado por teste e apagado no fim.
                ["Torre:Armazenamento:Diretorio"] = DiretorioDoArmazenamento,
                ["Torre:Armazenamento:EnderecoBase"] = "http://localhost",

                // O banco é compartilhado pela suíte: a reavaliação periódica de uma API varreria as rotas
                // em andamento de todos os testes. Os testes de previsão configuram o intervalo deles.
                ["Torre:Previsao:IntervaloDeReavaliacao"] = "00:30:00",

                // Tudo a partir de Information chega ao sink de teste; o console só mostra
                // aviso e erro, para a saída do teste continuar legível.
                ["Serilog:MinimumLevel:Default"] = "Information",
                ["Serilog:WriteTo:0:Args:restrictedToMinimumLevel"] = "Warning",
            };

            foreach (var (chave, valor) in configuracaoAdicional ?? new Dictionary<string, string?>())
            {
                valores[chave] = valor;
            }

            configuracao.AddInMemoryCollection(valores);
        });

        builder.ConfigureTestServices(servicos =>
        {
            servicos.AddSingleton<IStartupFilter, FiltroDeEndpointsDeTeste>();
            servicos.AddSingleton<ILogEventSink>(Logs);

            if (relogio is not null)
            {
                servicos.RemoveAll<TimeProvider>();
                servicos.AddSingleton(relogio);
            }

            servicosDeTeste?.Invoke(servicos);
        });
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(DiretorioDoArmazenamento))
        {
            try
            {
                Directory.Delete(DiretorioDoArmazenamento, recursive: true);
            }
            catch (IOException)
            {
                // Arquivo ainda em uso por um handle do teste: o diretório é temporário e some com o sistema.
            }
        }
    }

    /// <summary>Cliente sem armazenamento automático de cookie: os testes controlam o cabeçalho.</summary>
    public HttpClient Cliente() =>
        CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
}

/// <summary>Captura os eventos de log da API para inspeção.</summary>
public sealed class SinkEmMemoria : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _eventos = new();

    /// <summary>Eventos capturados.</summary>
    public IReadOnlyCollection<LogEvent> Eventos => _eventos;

    /// <inheritdoc />
    public void Emit(LogEvent logEvent) => _eventos.Enqueue(logEvent);

    /// <summary>Todo o texto que um leitor de log veria: mensagem, propriedades e exceção.</summary>
    public string TextoCompleto() => string.Join(
        Environment.NewLine,
        _eventos.Select(evento => string.Join(
            " | ",
            evento.RenderMessage(System.Globalization.CultureInfo.InvariantCulture),
            string.Join(" ", evento.Properties.Select(propriedade => $"{propriedade.Key}={propriedade.Value}")),
            evento.Exception?.ToString() ?? string.Empty)));
}
