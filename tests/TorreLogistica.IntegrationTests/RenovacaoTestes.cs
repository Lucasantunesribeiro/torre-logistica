using System.Buffers.Text;
using System.Net;
using System.Text;
using System.Text.Json;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

[Collection(ColecaoDeIntegracao.Nome)]
public sealed class RenovacaoTestes(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    [Fact]
    public async Task RenovarDevolveNovoParDeTokens()
    {
        var conta = (await Cenario.CriarOrganizacaoAsync(Perfil.Operador)).Com(Perfil.Operador);
        using var cliente = Cliente();
        var sessao = await EntrarAsync(cliente, conta);

        using var resposta = await RenovarAsync(cliente, sessao.CookieDeRenovacao);
        var renovada = await LerSessaoAsync(resposta, CanalDeAcesso.Operacao);

        Assert.NotEqual(sessao.CookieDeRenovacao, renovada.CookieDeRenovacao);
        Assert.NotEqual(sessao.TokenDeAcesso, renovada.TokenDeAcesso);
        Assert.True(resposta.Headers.CacheControl?.NoStore);

        using var eu = await EnviarAsync(cliente, HttpMethod.Get, "/api/autenticacao/eu", renovada.TokenDeAcesso);
        Assert.Equal(HttpStatusCode.OK, eu.StatusCode);
    }

    [Fact]
    public async Task RenovarSemCookieDevolve401ERemoveCookie()
    {
        using var cliente = Cliente();

        using var resposta = await RenovarAsync(cliente, cookie: null);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Equal("sessao_invalida", await CodigoDoErroAsync(resposta));
        Assert.Contains("expires=Thu, 01 Jan 1970", CabecalhoDoCookie(resposta, CanalDeAcesso.Operacao), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("valor-que-nao-tem-formato-de-token")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task RenovarComCookieInventadoDevolve401(string cookie)
    {
        using var cliente = Cliente();

        using var resposta = await RenovarAsync(cliente, cookie);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Equal("sessao_invalida", await CodigoDoErroAsync(resposta));
    }

    /// <summary>
    /// Retry legítimo (resposta perdida) é aceito. Mas o sucessor descartado nesse retry
    /// fica marcado: se ele aparecer depois, é sinal de que duas partes têm a cadeia, e a
    /// família inteira cai — inclusive o token emitido no retry.
    /// </summary>
    [Fact]
    public async Task RepeticaoDentroDaJanelaEhAceitaMasSucessorDescartadoDenunciaReuso()
    {
        var conta = (await Cenario.CriarOrganizacaoAsync(Perfil.Operador)).Com(Perfil.Operador);
        using var cliente = Cliente();
        var original = await EntrarAsync(cliente, conta);

        using var primeira = await RenovarAsync(cliente, original.CookieDeRenovacao);
        var perdida = await LerSessaoAsync(primeira, CanalDeAcesso.Operacao);

        // A resposta "se perdeu": o cliente repete com o token original.
        using var repetida = await RenovarAsync(cliente, original.CookieDeRenovacao);
        var atual = await LerSessaoAsync(repetida, CanalDeAcesso.Operacao);

        using var comSucessorDescartado = await RenovarAsync(cliente, perdida.CookieDeRenovacao);
        Assert.Equal(HttpStatusCode.Unauthorized, comSucessorDescartado.StatusCode);

        using var comTokenAtual = await RenovarAsync(cliente, atual.CookieDeRenovacao);
        Assert.Equal(HttpStatusCode.Unauthorized, comTokenAtual.StatusCode);

        using var eu = await EnviarAsync(cliente, HttpMethod.Get, "/api/autenticacao/eu", atual.TokenDeAcesso);
        Assert.Equal(HttpStatusCode.Unauthorized, eu.StatusCode);
    }

    /// <summary>
    /// Sem trava, dez renovações simultâneas do mesmo token enxergariam todas o token
    /// "disponível" e deixariam dez sucessores válidos. Com a linha da sessão travada, elas
    /// passam uma de cada vez e sobra exatamente um token utilizável.
    /// </summary>
    [Fact]
    public async Task RenovacoesSimultaneasDoMesmoTokenDeixamUmUnicoTokenDisponivel()
    {
        var conta = (await Cenario.CriarOrganizacaoAsync(Perfil.Operador)).Com(Perfil.Operador);
        using var cliente = Cliente();
        var sessao = await EntrarAsync(cliente, conta);
        var sessaoId = SessaoIdDoToken(sessao.TokenDeAcesso);

        var respostas = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => RenovarAsync(cliente, sessao.CookieDeRenovacao)));

        try
        {
            Assert.All(respostas, resposta => Assert.Equal(HttpStatusCode.OK, resposta.StatusCode));
        }
        finally
        {
            foreach (var resposta in respostas)
            {
                resposta.Dispose();
            }
        }

        var disponiveis = await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM tokens_de_renovacao WHERE sessao_id = @sessao AND usado_em IS NULL AND invalidado_em IS NULL",
            ("sessao", sessaoId));
        var aberta = await Banco.ConsultarEscalarAsync(
            "SELECT revogada_em IS NULL FROM sessoes WHERE id = @sessao", ("sessao", sessaoId));

        Assert.Equal("1", disponiveis);
        Assert.Equal("True", aberta);
    }

    [Fact]
    public async Task LogoutEncerraASessaoNaHora()
    {
        var conta = (await Cenario.CriarOrganizacaoAsync(Perfil.Operador)).Com(Perfil.Operador);
        using var cliente = Cliente();
        var sessao = await EntrarAsync(cliente, conta);

        using var saida = await SairAsync(cliente, sessao.CookieDeRenovacao);
        Assert.Equal(HttpStatusCode.NoContent, saida.StatusCode);
        Assert.NotNull(CabecalhoDoCookie(saida, CanalDeAcesso.Operacao));

        // O token de acesso ainda estaria dentro da validade: é a conferência da sessão
        // por requisição que o derruba agora, e não daqui a 15 minutos.
        using var eu = await EnviarAsync(cliente, HttpMethod.Get, "/api/autenticacao/eu", sessao.TokenDeAcesso);
        Assert.Equal(HttpStatusCode.Unauthorized, eu.StatusCode);

        using var renovacao = await RenovarAsync(cliente, sessao.CookieDeRenovacao);
        Assert.Equal(HttpStatusCode.Unauthorized, renovacao.StatusCode);
    }

    [Fact]
    public async Task LogoutSemSessaoTambemDevolve204()
    {
        using var cliente = Cliente();

        using var semCookie = await SairAsync(cliente, cookie: null);
        using var inventado = await SairAsync(cliente, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");

        Assert.Equal(HttpStatusCode.NoContent, semCookie.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, inventado.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(FabricaDaApi.OrigemNaoAutorizada)]
    public async Task RenovacaoELogoutExigemOrigemConfiavelSemEfeitoColateral(string? origem)
    {
        var conta = (await Cenario.CriarOrganizacaoAsync(Perfil.Operador)).Com(Perfil.Operador);
        using var cliente = Cliente();
        var sessao = await EntrarAsync(cliente, conta);

        using var renovacao = await RenovarAsync(cliente, sessao.CookieDeRenovacao, origem: origem);
        using var saida = await SairAsync(cliente, sessao.CookieDeRenovacao, origem: origem);

        Assert.Equal(HttpStatusCode.Forbidden, renovacao.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, saida.StatusCode);

        // Recusadas antes de tocar na sessão: o token continua válido.
        using var legitima = await RenovarAsync(cliente, sessao.CookieDeRenovacao);
        Assert.Equal(HttpStatusCode.OK, legitima.StatusCode);
    }

    [Fact]
    public async Task TokenDoConsoleNaoRenovaPeloCanalDoMotorista()
    {
        var conta = (await Cenario.CriarOrganizacaoAsync(Perfil.Operador)).Com(Perfil.Operador);
        using var cliente = Cliente();
        var sessao = await EntrarAsync(cliente, conta);

        using var cruzada = await RenovarAsync(cliente, sessao.CookieDeRenovacao, CanalDeAcesso.Motorista);
        Assert.Equal(HttpStatusCode.Unauthorized, cruzada.StatusCode);

        // A tentativa pelo canal errado é recusada, não tratada como reuso.
        using var noCanalCerto = await RenovarAsync(cliente, sessao.CookieDeRenovacao);
        Assert.Equal(HttpStatusCode.OK, noCanalCerto.StatusCode);
    }

    internal static Guid SessaoIdDoToken(string token)
    {
        var conteudo = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token.Split('.')[1]));
        using var json = JsonDocument.Parse(conteudo);
        return Guid.Parse(json.RootElement.GetProperty("sid").GetString()!);
    }
}

/// <summary>Com janela zerada, qualquer reapresentação de token já trocado é reuso.</summary>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class ReusoDeTokenTestes(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    protected override IReadOnlyDictionary<string, string?> ConfiguracaoAdicional => new Dictionary<string, string?>
    {
        ["Torre:Autenticacao:JanelaDeToleranciaDaRenovacao"] = "00:00:00",
    };

    [Fact]
    public async Task ReusoDeTokenRevogaAFamiliaInteiraERegistraAuditoria()
    {
        var conta = (await Cenario.CriarOrganizacaoAsync(Perfil.Operador)).Com(Perfil.Operador);
        using var cliente = Cliente();
        var original = await EntrarAsync(cliente, conta);
        var sessaoId = RenovacaoTestes.SessaoIdDoToken(original.TokenDeAcesso);

        using var legitima = await RenovarAsync(cliente, original.CookieDeRenovacao);
        var atual = await LerSessaoAsync(legitima, CanalDeAcesso.Operacao);

        // Alguém reapresenta o token já trocado.
        using var reuso = await RenovarAsync(cliente, original.CookieDeRenovacao);
        Assert.Equal(HttpStatusCode.Unauthorized, reuso.StatusCode);

        // A família inteira cai: o token de renovação atual e o token de acesso atual.
        using var renovacaoAtual = await RenovarAsync(cliente, atual.CookieDeRenovacao);
        using var eu = await EnviarAsync(cliente, HttpMethod.Get, "/api/autenticacao/eu", atual.TokenDeAcesso);
        Assert.Equal(HttpStatusCode.Unauthorized, renovacaoAtual.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, eu.StatusCode);

        Assert.Equal("ReusoDeToken", await Banco.ConsultarEscalarAsync(
            "SELECT motivo_da_revogacao FROM sessoes WHERE id = @sessao", ("sessao", sessaoId)));
        Assert.Equal("1", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM eventos_de_auditoria WHERE tipo = 'reuso_de_token_detectado' AND alvo_id = @sessao",
            ("sessao", sessaoId)));
    }
}
