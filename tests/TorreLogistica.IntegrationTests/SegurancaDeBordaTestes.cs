using System.Net.Http.Headers;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

[Collection(ColecaoDeIntegracao.Nome)]
public sealed class SegurancaDeBordaTestes(FabricaDaApi fabrica)
{
    private readonly FabricaDaApi _fabrica = fabrica;

    [Theory]
    [InlineData("X-Content-Type-Options", "nosniff")]
    [InlineData("X-Frame-Options", "DENY")]
    [InlineData("Referrer-Policy", "no-referrer")]
    [InlineData("Cross-Origin-Resource-Policy", "same-origin")]
    [InlineData("X-Permitted-Cross-Domain-Policies", "none")]
    public async Task CabecalhoDeSegurancaAcompanhaAResposta(string nome, string valorEsperado)
    {
        using var cliente = _fabrica.CreateClient();

        using var resposta = await cliente.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.True(resposta.Headers.TryGetValues(nome, out var valores), $"Falta o cabeçalho {nome}.");
        Assert.Equal(valorEsperado, valores!.Single());
    }

    [Fact]
    public async Task PoliticaDeConteudoBloqueiaTudoNaApi()
    {
        using var cliente = _fabrica.CreateClient();

        using var resposta = await cliente.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);
        var politica = resposta.Headers.GetValues("Content-Security-Policy").Single();

        Assert.Contains("default-src 'none'", politica, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", politica, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CabecalhoDeSegurancaAcompanhaTambemARespostaDeErro()
    {
        using var cliente = _fabrica.CreateClient();

        using var resposta = await cliente.GetAsync(
            new Uri(FiltroDeEndpointsDeTeste.RotaDeErroInesperado, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.True(resposta.Headers.Contains("X-Content-Type-Options"));
        Assert.True(resposta.Headers.Contains("X-Frame-Options"));
    }

    [Fact]
    public async Task RespostaNaoAnunciaOServidor()
    {
        using var cliente = _fabrica.CreateClient();

        using var resposta = await cliente.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.False(resposta.Headers.Contains("Server"));
        Assert.False(resposta.Headers.Contains("X-Powered-By"));
    }

    [Fact]
    public async Task OrigemAutorizadaRecebeLiberacaoDeCors()
    {
        using var cliente = _fabrica.CreateClient();
        using var requisicao = new HttpRequestMessage(
            HttpMethod.Get, new Uri("/health/live", UriKind.Relative));
        requisicao.Headers.Add("Origin", FabricaDaApi.OrigemAutorizada);

        using var resposta = await cliente.SendAsync(requisicao, TestContext.Current.CancellationToken);

        Assert.True(resposta.Headers.TryGetValues("Access-Control-Allow-Origin", out var valores));
        Assert.Equal(FabricaDaApi.OrigemAutorizada, valores!.Single());
    }

    /// <summary>
    /// A política é fechada por padrão: origem que não está na lista do ambiente
    /// não recebe autorização nenhuma, nem em requisição simples nem em preflight.
    /// </summary>
    [Fact]
    public async Task OrigemDesconhecidaNaoRecebeLiberacaoDeCors()
    {
        using var cliente = _fabrica.CreateClient();
        using var requisicao = new HttpRequestMessage(
            HttpMethod.Get, new Uri("/health/live", UriKind.Relative));
        requisicao.Headers.Add("Origin", FabricaDaApi.OrigemNaoAutorizada);

        using var resposta = await cliente.SendAsync(requisicao, TestContext.Current.CancellationToken);

        Assert.False(resposta.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task PreflightDeOrigemDesconhecidaNaoLiberaMetodo()
    {
        using var cliente = _fabrica.CreateClient();
        using var requisicao = new HttpRequestMessage(
            HttpMethod.Options, new Uri("/health/live", UriKind.Relative));
        requisicao.Headers.Add("Origin", FabricaDaApi.OrigemNaoAutorizada);
        requisicao.Headers.Add("Access-Control-Request-Method", "POST");

        using var resposta = await cliente.SendAsync(requisicao, TestContext.Current.CancellationToken);

        Assert.False(resposta.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(resposta.Headers.Contains("Access-Control-Allow-Methods"));
    }

    [Fact]
    public async Task CorrelacaoEhGeradaQuandoOClienteNaoEnviaNenhuma()
    {
        using var cliente = _fabrica.CreateClient();

        using var resposta = await cliente.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        var identificador = resposta.Headers.GetValues("X-Correlation-Id").Single();
        Assert.False(string.IsNullOrWhiteSpace(identificador));
        Assert.Equal(32, identificador.Length);
    }

    [Fact]
    public async Task CorrelacaoEnviadaPeloClienteEhPreservada()
    {
        using var cliente = _fabrica.CreateClient();
        using var requisicao = new HttpRequestMessage(
            HttpMethod.Get, new Uri("/health/live", UriKind.Relative));
        requisicao.Headers.Add("X-Correlation-Id", "console-op-4821");

        using var resposta = await cliente.SendAsync(requisicao, TestContext.Current.CancellationToken);

        Assert.Equal("console-op-4821", resposta.Headers.GetValues("X-Correlation-Id").Single());
    }

    /// <summary>
    /// O identificador recebido volta no cabeçalho e entra no log. Devolver texto
    /// arbitrário do cliente seria aceitar injeção de cabeçalho e de linha de log.
    /// </summary>
    [Theory]
    [InlineData("valor com espaco")]
    [InlineData("<script>alert(1)</script>")]
    public async Task CorrelacaoMalFormadaEhDescartadaEmFavorDeUmaNova(string suspeita)
    {
        using var cliente = _fabrica.CreateClient();
        using var requisicao = new HttpRequestMessage(
            HttpMethod.Get, new Uri("/health/live", UriKind.Relative));
        requisicao.Headers.TryAddWithoutValidation("X-Correlation-Id", suspeita);

        using var resposta = await cliente.SendAsync(requisicao, TestContext.Current.CancellationToken);

        var devolvido = resposta.Headers.GetValues("X-Correlation-Id").Single();
        Assert.NotEqual(suspeita, devolvido);
        Assert.Equal(32, devolvido.Length);
    }

    [Fact]
    public async Task CorrelacaoAcimaDoLimiteDeTamanhoEhDescartada()
    {
        using var cliente = _fabrica.CreateClient();
        using var requisicao = new HttpRequestMessage(
            HttpMethod.Get, new Uri("/health/live", UriKind.Relative));
        requisicao.Headers.TryAddWithoutValidation("X-Correlation-Id", new string('a', 200));

        using var resposta = await cliente.SendAsync(requisicao, TestContext.Current.CancellationToken);

        Assert.Equal(32, resposta.Headers.GetValues("X-Correlation-Id").Single().Length);
    }

    [Fact]
    public async Task DocumentacaoOpenApiNaoEhPublicadaForaDeDesenvolvimento()
    {
        using var cliente = _fabrica.CreateClient();

        using var resposta = await cliente.GetAsync(
            new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task RespostaDeSaudeUsaJson()
    {
        using var cliente = _fabrica.CreateClient();

        using var resposta = await cliente.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(
            new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" }.MediaType,
            resposta.Content.Headers.ContentType?.MediaType);
    }
}
