using System.Net;
using System.Text.Json;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// Contrato de erro da API.
/// </summary>
/// <remarks>
/// As rotas <c>/__teste/*</c> atravessam o pipeline real, inclusive a autorização. Como
/// endpoint sem política exige sessão do console (política de fallback), os testes
/// chamam essas rotas autenticados.
/// </remarks>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class ContratoDeErrosHttpTestes(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    [Theory]
    [InlineData(FiltroDeEndpointsDeTeste.RotaDeRegraViolada, 422, "regra_de_teste")]
    [InlineData(FiltroDeEndpointsDeTeste.RotaDeConflito, 409, "conflito_de_teste")]
    [InlineData(FiltroDeEndpointsDeTeste.RotaDeNaoEncontrado, 404, "recurso_de_teste")]
    public async Task ErroDeDominioViraProblemDetailsComCodigoEstavel(
        string rota,
        int statusEsperado,
        string codigoEsperado)
    {
        using var cliente = Cliente();
        var token = await TokenDeOperadorAsync(cliente);

        using var resposta = await EnviarAsync(cliente, HttpMethod.Get, rota, token);
        var corpo = await resposta.Content.ReadAsStringAsync(Cancelamento);

        Assert.Equal(statusEsperado, (int)resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);

        using var json = JsonDocument.Parse(corpo);
        Assert.Equal(codigoEsperado, json.RootElement.GetProperty("codigo").GetString());
        Assert.Equal($"urn:torre-logistica:erro:{codigoEsperado}", json.RootElement.GetProperty("type").GetString());
        Assert.Equal(statusEsperado, json.RootElement.GetProperty("status").GetInt32());
    }

    /// <summary>
    /// Item do Security Gate 0: fora de desenvolvimento, nada da exceção original
    /// pode chegar ao cliente — nem mensagem, nem tipo, nem pilha de chamada.
    /// </summary>
    [Fact]
    public async Task ErroInesperadoNaoVazaMensagemNemPilhaDeChamada()
    {
        using var cliente = Cliente();
        var token = await TokenDeOperadorAsync(cliente);

        using var resposta = await EnviarAsync(cliente, HttpMethod.Get, FiltroDeEndpointsDeTeste.RotaDeErroInesperado, token);
        var corpo = await resposta.Content.ReadAsStringAsync(Cancelamento);

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
        using var cliente = Cliente();
        var token = await TokenDeOperadorAsync(cliente);

        using var resposta = await EnviarAsync(cliente, HttpMethod.Get, FiltroDeEndpointsDeTeste.RotaDeErroInesperado, token);
        var json = await JsonAsync(resposta);

        Assert.Equal("erro_inesperado", json.GetProperty("codigo").GetString());
        Assert.True(json.TryGetProperty("idDeCorrelacao", out var correlacao));
        Assert.Equal(resposta.Headers.GetValues("X-Correlation-Id").Single(), correlacao.GetString());
    }

    /// <summary>
    /// Sem sessão, rota inexistente responde 401 — a mesma resposta de uma rota que
    /// existe. A política de fallback impede que o mapa de rotas da API seja levantado
    /// por quem não está autenticado. Com sessão, a resposta é o 404 esperado.
    /// </summary>
    [Fact]
    public async Task RotaInexistenteNaoSeRevelaParaQuemNaoTemSessao()
    {
        using var cliente = Cliente();
        var token = await TokenDeOperadorAsync(cliente);

        using var anonima = await EnviarAsync(cliente, HttpMethod.Get, "/rota/que/nao/existe", tokenDeAcesso: null);
        using var existenteAnonima = await EnviarAsync(cliente, HttpMethod.Get, "/api/usuarios", tokenDeAcesso: null);
        using var autenticada = await EnviarAsync(cliente, HttpMethod.Get, "/rota/que/nao/existe", token);
        var corpo = await autenticada.Content.ReadAsStringAsync(Cancelamento);

        Assert.Equal(HttpStatusCode.Unauthorized, anonima.StatusCode);
        Assert.Equal(existenteAnonima.StatusCode, anonima.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, autenticada.StatusCode);
        Assert.DoesNotContain("at TorreLogistica", corpo, StringComparison.Ordinal);
    }

    /// <summary>
    /// Corpo que o cliente mandou errado é erro do cliente: 400, nunca 500.
    /// </summary>
    /// <remarks>
    /// O caminho exercitado é o de produção — vínculo de parâmetro tipado de Minimal API num endpoint
    /// real, não uma rota sintética. É ali que o JSON é lido, e era ali que a falha virava 500.
    /// </remarks>
    [Theory]
    [InlineData("sintaxe quebrada", "{\"nome\": ")]
    [InlineData("chave sem valor", "{\"nome\":}")]
    [InlineData("tipo incompatível", "{\"nome\": 123}")]
    [InlineData("corpo vazio", "")]
    public async Task CorpoJsonMalformadoResponde400ComProblemDetails(string caso, string corpo)
    {
        using var cliente = Cliente();
        var token = await TokenDeSupervisorAsync(cliente);

        using var resposta = await EnviarAsync(cliente, HttpMethod.Post, "/api/clientes", token, Json(corpo));
        var texto = await resposta.Content.ReadAsStringAsync(Cancelamento);

        Assert.True(
            resposta.StatusCode == HttpStatusCode.BadRequest,
            $"{caso}: esperado 400, veio {(int)resposta.StatusCode}. Corpo: {texto}");
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);

        using var json = JsonDocument.Parse(texto);
        var raiz = json.RootElement;
        Assert.Equal("corpo_invalido", raiz.GetProperty("codigo").GetString());
        Assert.Equal("urn:torre-logistica:erro:corpo_invalido", raiz.GetProperty("type").GetString());
        Assert.Equal(400, raiz.GetProperty("status").GetInt32());

        // Correlação preservada: é ela que liga esta resposta à linha de log.
        Assert.False(string.IsNullOrWhiteSpace(raiz.GetProperty("idDeCorrelacao").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(raiz.GetProperty("traceId").GetString()));

        // Nada de implementação interna no corpo.
        foreach (var vazamento in new[] { "at TorreLogistica", "stackTrace", "JsonException", "BadHttpRequest", "System.Text.Json", "LineNumber" })
        {
            Assert.DoesNotContain(vazamento, texto, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>O contrário da correção: JSON bem formado segue o fluxo normal, sem virar 400.</summary>
    [Fact]
    public async Task CorpoJsonValidoContinuaSendoProcessado()
    {
        using var cliente = Cliente();
        var token = await TokenDeSupervisorAsync(cliente);

        using var criado = await EnviarAsync(
            cliente, HttpMethod.Post, "/api/clientes", token, Json("{\"nome\":\"Cliente do contrato\"}"));

        // E a validação de modelo continua respondendo 400 por conta própria, com o seu código.
        using var semNome = await EnviarAsync(cliente, HttpMethod.Post, "/api/clientes", token, Json("{\"cnpj\":\"00\"}"));

        Assert.Equal(HttpStatusCode.Created, criado.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, semNome.StatusCode);
        Assert.NotEqual("corpo_invalido", await CodigoDoErroAsync(semNome));
    }

    /// <summary>
    /// A guarda que impede a correção de esconder defeito nosso: exceção interna continua 500.
    /// </summary>
    [Fact]
    public async Task FalhaInternaNaoViraErroDeCliente()
    {
        using var cliente = Cliente();
        var token = await TokenDeOperadorAsync(cliente);

        using var resposta = await EnviarAsync(cliente, HttpMethod.Get, FiltroDeEndpointsDeTeste.RotaDeErroInesperado, token);

        Assert.Equal(HttpStatusCode.InternalServerError, resposta.StatusCode);
        Assert.Equal("erro_inesperado", await CodigoDoErroAsync(resposta));
    }

    private static StringContent Json(string corpo) => new(corpo, System.Text.Encoding.UTF8, "application/json");

    private async Task<string> TokenDeOperadorAsync(HttpClient cliente)
    {
        var conta = (await Cenario.CriarOrganizacaoAsync(Perfil.Operador)).Com(Perfil.Operador);
        return (await EntrarAsync(cliente, conta)).TokenDeAcesso;
    }

    private async Task<string> TokenDeSupervisorAsync(HttpClient cliente)
    {
        var conta = (await Cenario.CriarOrganizacaoAsync(Perfil.Supervisor)).Com(Perfil.Supervisor);
        return (await EntrarAsync(cliente, conta)).TokenDeAcesso;
    }
}
