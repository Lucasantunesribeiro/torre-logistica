using System.Net;
using System.Text.Json;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

[Collection(ColecaoDeIntegracao.Nome)]
public sealed class ContratoDeErrosHttpTestes(FabricaDaApi fabrica)
{
    private readonly FabricaDaApi _fabrica = fabrica;

    [Theory]
    [InlineData(FiltroDeEndpointsDeTeste.RotaDeRegraViolada, 422, "regra_de_teste")]
    [InlineData(FiltroDeEndpointsDeTeste.RotaDeConflito, 409, "conflito_de_teste")]
    [InlineData(FiltroDeEndpointsDeTeste.RotaDeNaoEncontrado, 404, "recurso_de_teste")]
    public async Task ErroDeDominioViraProblemDetailsComCodigoEstavel(
        string rota,
        int statusEsperado,
        string codigoEsperado)
    {
        using var cliente = _fabrica.CreateClient();

        using var resposta = await cliente.GetAsync(new Uri(rota, UriKind.Relative), TestContext.Current.CancellationToken);
        var corpo = await resposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(statusEsperado, (int)resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);

        using var json = JsonDocument.Parse(corpo);
        Assert.Equal(codigoEsperado, json.RootElement.GetProperty("codigo").GetString());
        Assert.Equal(
            $"urn:torre-logistica:erro:{codigoEsperado}",
            json.RootElement.GetProperty("type").GetString());
        Assert.Equal(statusEsperado, json.RootElement.GetProperty("status").GetInt32());
    }

    /// <summary>
    /// Item do Security Gate 0: fora de desenvolvimento, nada da exceção original
    /// pode chegar ao cliente — nem mensagem, nem tipo, nem pilha de chamada.
    /// </summary>
    [Fact]
    public async Task ErroInesperadoNaoVazaMensagemNemPilhaDeChamada()
    {
        using var cliente = _fabrica.CreateClient();

        using var resposta = await cliente.GetAsync(
            new Uri(FiltroDeEndpointsDeTeste.RotaDeErroInesperado, UriKind.Relative), TestContext.Current.CancellationToken);
        var corpo = await resposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, resposta.StatusCode);

        Assert.DoesNotContain(FiltroDeEndpointsDeTeste.SegredoDaExcecao, corpo, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", corpo, StringComparison.Ordinal);
        Assert.DoesNotContain("at TorreLogistica", corpo, StringComparison.Ordinal);
        Assert.DoesNotContain("stackTrace", corpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".cs:line", corpo, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ErroInesperadoDevolveProblemDetailsComCorrelacao()
    {
        using var cliente = _fabrica.CreateClient();

        using var resposta = await cliente.GetAsync(
            new Uri(FiltroDeEndpointsDeTeste.RotaDeErroInesperado, UriKind.Relative), TestContext.Current.CancellationToken);
        var corpo = await resposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using var json = JsonDocument.Parse(corpo);

        Assert.Equal("erro_inesperado", json.RootElement.GetProperty("codigo").GetString());
        Assert.True(json.RootElement.TryGetProperty("idDeCorrelacao", out var correlacao));
        Assert.False(string.IsNullOrWhiteSpace(correlacao.GetString()));

        var doCabecalho = resposta.Headers.GetValues("X-Correlation-Id").Single();
        Assert.Equal(doCabecalho, correlacao.GetString());
    }

    [Fact]
    public async Task RotaInexistenteDevolveNotFoundSemCorpoDeErroDetalhado()
    {
        using var cliente = _fabrica.CreateClient();

        using var resposta = await cliente.GetAsync(
            new Uri("/rota/que/nao/existe", UriKind.Relative), TestContext.Current.CancellationToken);
        var corpo = await resposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        Assert.DoesNotContain("at TorreLogistica", corpo, StringComparison.Ordinal);
    }
}
