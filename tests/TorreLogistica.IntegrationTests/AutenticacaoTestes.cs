using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

[Collection(ColecaoDeIntegracao.Nome)]
public sealed class AutenticacaoTestes(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    [Fact]
    public async Task LoginValidoDevolveTokenECookieProtegido()
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Operador);
        var conta = organizacao.Com(Perfil.Operador);
        using var cliente = Cliente();

        using var resposta = await LoginAsync(cliente, conta.OrganizacaoSlug, conta.Email, conta.Senha);
        var corpo = await resposta.Content.ReadAsStringAsync(Cancelamento);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);

        var json = await JsonAsync(resposta);
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("tokenDeAcesso").GetString()));
        Assert.Equal("Bearer", json.GetProperty("tipoDoToken").GetString());
        Assert.Equal("Operador", json.GetProperty("usuario").GetProperty("perfil").GetString());
        Assert.Equal(organizacao.Slug, json.GetProperty("usuario").GetProperty("organizacaoSlug").GetString());

        var cookie = CabecalhoDoCookie(resposta, CanalDeAcesso.Operacao)!;
        Assert.NotNull(cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/autenticacao", cookie, StringComparison.OrdinalIgnoreCase);

        // O token de renovação só existe no cookie HttpOnly: se aparecesse no corpo, um
        // script da página conseguiria lê-lo.
        Assert.DoesNotContain(ValorDoCookie(resposta, CanalDeAcesso.Operacao)!, corpo, StringComparison.Ordinal);
        Assert.True(resposta.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task TokenDeAcessoAbreAContaAtual()
    {
        var conta = (await Cenario.CriarOrganizacaoAsync(Perfil.Supervisor)).Com(Perfil.Supervisor);
        using var cliente = Cliente();
        var sessao = await EntrarAsync(cliente, conta);

        using var resposta = await EnviarAsync(cliente, HttpMethod.Get, "/api/autenticacao/eu", sessao.TokenDeAcesso);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var json = await JsonAsync(resposta);
        Assert.Equal(conta.Id, json.GetProperty("id").GetGuid());
        Assert.Equal(conta.Email, json.GetProperty("email").GetString());
    }

    [Fact]
    public async Task CaixaDoEmailEDaOrganizacaoNaoImporta()
    {
        var conta = (await Cenario.CriarOrganizacaoAsync(Perfil.Operador)).Com(Perfil.Operador);
        using var cliente = Cliente();

        using var resposta = await LoginAsync(
            cliente, conta.OrganizacaoSlug.ToUpperInvariant(), $"  {conta.Email.ToUpperInvariant()} ", conta.Senha);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    /// <summary>
    /// Enumeração de contas: nenhuma causa de falha pode ser distinguível pela resposta.
    /// Status, código, título e detalhe são idênticos, e nenhuma delas grava cookie.
    /// </summary>
    [Fact]
    public async Task TodasAsFalhasDeLoginSaoIndistinguiveis()
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(
            Perfil.Administrador, Perfil.Operador, Perfil.Motorista, Perfil.Supervisor);
        var outra = await Cenario.CriarOrganizacaoAsync(Perfil.Operador);
        var operador = organizacao.Com(Perfil.Operador);
        var inativa = organizacao.Com(Perfil.Supervisor);
        await Cenario.DesativarDiretamenteAsync(inativa.Id);
        using var cliente = Cliente();

        var tentativas = new (string Caso, Func<Task<HttpResponseMessage>> Tentativa)[]
        {
            ("senha errada", () => LoginAsync(cliente, organizacao.Slug, operador.Email, "senha-errada-mas-comprida")),
            ("e-mail inexistente", () => LoginAsync(cliente, organizacao.Slug, "ninguem@teste.test", operador.Senha)),
            ("organização inexistente", () => LoginAsync(cliente, "organizacao-que-nao-existe", operador.Email, operador.Senha)),
            ("conta de outra organização", () => LoginAsync(cliente, organizacao.Slug, outra.Com(Perfil.Operador).Email, operador.Senha)),
            ("conta inativa", () => LoginAsync(cliente, organizacao.Slug, inativa.Email, inativa.Senha)),
            ("motorista no console", () => LoginAsync(cliente, organizacao.Slug, organizacao.Com(Perfil.Motorista).Email, operador.Senha)),
            ("administrador no aplicativo do motorista", () => LoginAsync(
                cliente, organizacao.Slug, organizacao.Com(Perfil.Administrador).Email, operador.Senha, CanalDeAcesso.Motorista)),
        };

        var assinaturas = new List<string>();

        foreach (var (caso, tentativa) in tentativas)
        {
            using var resposta = await tentativa();
            var json = await JsonAsync(resposta);

            Assert.True(resposta.StatusCode == HttpStatusCode.Unauthorized, $"{caso}: veio {(int)resposta.StatusCode}.");
            Assert.False(resposta.Headers.Contains("Set-Cookie"), $"{caso}: gravou cookie.");

            assinaturas.Add(string.Join(
                "|",
                (int)resposta.StatusCode,
                json.GetProperty("codigo").GetString(),
                json.GetProperty("title").GetString(),
                json.GetProperty("detail").GetString()));
        }

        Assert.Single(assinaturas.Distinct(StringComparer.Ordinal));
        Assert.Contains("credenciais_invalidas", assinaturas[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task CorpoIncompletoDevolve400ComOsCampos()
    {
        using var cliente = Cliente();

        using var resposta = await LoginAsync(cliente, "alguma-organizacao", "a@teste.test", senha: null);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var json = await JsonAsync(resposta);
        Assert.Equal("requisicao_invalida", json.GetProperty("codigo").GetString());
        Assert.True(json.GetProperty("errors").TryGetProperty("senha", out _));
    }

    /// <summary>Mass assignment: campo que o contrato não declara é recusado, não ignorado.</summary>
    [Fact]
    public async Task CampoDesconhecidoNoCorpoDevolve400()
    {
        using var cliente = Cliente();
        using var requisicao = new HttpRequestMessage(HttpMethod.Post, "/api/autenticacao/login")
        {
            Content = JsonContent.Create(new
            {
                organizacao = "alguma-organizacao",
                email = "a@teste.test",
                senha = "senha-qualquer-comprida",
                organizacaoId = Guid.CreateVersion7(),
            }),
        };
        requisicao.Headers.Add("Origin", FabricaDaApi.OrigemAutorizada);

        using var resposta = await cliente.SendAsync(requisicao, Cancelamento);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(FabricaDaApi.OrigemNaoAutorizada)]
    public async Task LoginExigeOrigemConfiavel(string? origem)
    {
        var conta = (await Cenario.CriarOrganizacaoAsync(Perfil.Operador)).Com(Perfil.Operador);
        using var cliente = Cliente();

        using var resposta = await LoginAsync(cliente, conta.OrganizacaoSlug, conta.Email, conta.Senha, origem: origem);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("origem_nao_autorizada", await CodigoDoErroAsync(resposta));
        Assert.False(resposta.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task SemTokenDevolve401ComProblemDetails()
    {
        using var cliente = Cliente();

        using var resposta = await EnviarAsync(cliente, HttpMethod.Get, "/api/autenticacao/eu", tokenDeAcesso: null);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Equal("nao_autenticado", await CodigoDoErroAsync(resposta));
        Assert.Equal("Bearer", resposta.Headers.WwwAuthenticate.Single().Scheme);
    }

    [Fact]
    public async Task TokenAdulteradoDevolve401()
    {
        var conta = (await Cenario.CriarOrganizacaoAsync(Perfil.Operador)).Com(Perfil.Operador);
        using var cliente = Cliente();
        var sessao = await EntrarAsync(cliente, conta);
        var partes = sessao.TokenDeAcesso.Split('.');

        // Perfil trocado para Administrador no conteúdo, assinatura original mantida.
        var conteudo = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(partes[1]))
            .Replace("\"Operador\"", "\"Administrador\"", StringComparison.Ordinal);
        var adulterado = $"{partes[0]}.{Base64Url.EncodeToString(Encoding.UTF8.GetBytes(conteudo))}.{partes[2]}";

        using var resposta = await EnviarAsync(cliente, HttpMethod.Get, "/api/usuarios", adulterado);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task TokenAssinadoComOutraChaveDevolve401()
    {
        var conta = (await Cenario.CriarOrganizacaoAsync(Perfil.Operador)).Com(Perfil.Operador);
        using var cliente = Cliente();
        var sessao = await EntrarAsync(cliente, conta);
        var partes = sessao.TokenDeAcesso.Split('.');
        var assinatura = HMACSHA256.HashData(RandomNumberGenerator.GetBytes(64), Encoding.ASCII.GetBytes($"{partes[0]}.{partes[1]}"));
        var forjado = $"{partes[0]}.{partes[1]}.{Base64Url.EncodeToString(assinatura)}";

        using var resposta = await EnviarAsync(cliente, HttpMethod.Get, "/api/autenticacao/eu", forjado);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    /// <summary>O ataque clássico ao JWT: declarar <c>alg: none</c> e mandar sem assinatura.</summary>
    [Fact]
    public async Task TokenSemAssinaturaDevolve401()
    {
        var conta = (await Cenario.CriarOrganizacaoAsync(Perfil.Operador)).Com(Perfil.Operador);
        using var cliente = Cliente();
        var sessao = await EntrarAsync(cliente, conta);
        var cabecalho = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("{\"alg\":\"none\",\"typ\":\"JWT\"}"));
        var semAssinatura = $"{cabecalho}.{sessao.TokenDeAcesso.Split('.')[1]}.";

        using var resposta = await EnviarAsync(cliente, HttpMethod.Get, "/api/autenticacao/eu", semAssinatura);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task LoginBemSucedidoZeraAsFalhasAcumuladas()
    {
        var conta = (await Cenario.CriarOrganizacaoAsync(Perfil.Operador)).Com(Perfil.Operador);
        using var cliente = Cliente();

        for (var rodada = 0; rodada < 2; rodada++)
        {
            for (var falha = 0; falha < 4; falha++)
            {
                using var errada = await LoginAsync(cliente, conta.OrganizacaoSlug, conta.Email, "senha-errada-mas-comprida");
                Assert.Equal(HttpStatusCode.Unauthorized, errada.StatusCode);
            }

            // Com o limite em 5, duas rodadas de 4 falhas só não bloqueiam se o acerto no
            // meio zerar o contador.
            using var certa = await LoginAsync(cliente, conta.OrganizacaoSlug, conta.Email, conta.Senha);
            Assert.Equal(HttpStatusCode.OK, certa.StatusCode);
        }
    }
}
