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
                ["Torre:BancoDeDados:CadeiaDeConexao"] = banco.CadeiaDeConexao,
                ["Torre:BancoDeDados:AplicarMigrationsAoIniciar"] = "false",
                ["Torre:Cors:OrigensPermitidas:0"] = OrigemAutorizada,

                // Limites altos por padrão: a suíte faz muitos logins seguidos do mesmo
                // "endereço". O teste de limite configura o seu próprio.
                ["Torre:LimiteDeRequisicoes:TentativasDeLoginPorMinuto"] = "100000",
                ["Torre:LimiteDeRequisicoes:RenovacoesPorMinuto"] = "100000",
                ["Torre:LimiteDeRequisicoes:EnviosDePosicaoPorMinuto"] = "100000",
                ["Torre:LimiteDeRequisicoes:SincronizacoesPorMinuto"] = "100000",

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
