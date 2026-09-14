using System.Net;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>Limite de requisições no login.</summary>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class LimiteDeRequisicoesTestes(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    protected override IReadOnlyDictionary<string, string?> ConfiguracaoAdicional => new Dictionary<string, string?>
    {
        ["Torre:LimiteDeRequisicoes:TentativasDeLoginPorMinuto"] = "3",
    };

    [Fact]
    public async Task TentativaAlemDoLimiteDevolve429AntesDeConferirSenha()
    {
        var conta = (await Cenario.CriarOrganizacaoAsync(Perfil.Operador)).Com(Perfil.Operador);
        using var cliente = Cliente();

        for (var tentativa = 0; tentativa < 3; tentativa++)
        {
            using var dentroDoLimite = await LoginAsync(cliente, conta.OrganizacaoSlug, conta.Email, "senha-errada-mas-comprida");
            Assert.Equal(HttpStatusCode.Unauthorized, dentroDoLimite.StatusCode);
        }

        using var alemDoLimite = await LoginAsync(cliente, conta.OrganizacaoSlug, conta.Email, "senha-errada-mas-comprida");
        Assert.Equal(HttpStatusCode.TooManyRequests, alemDoLimite.StatusCode);
        Assert.Equal("limite_de_requisicoes", await CodigoDoErroAsync(alemDoLimite));
        Assert.True(alemDoLimite.Headers.RetryAfter is not null);

        // Nem a senha certa passa: o limite vem antes da autenticação.
        using var senhaCerta = await LoginAsync(cliente, conta.OrganizacaoSlug, conta.Email, conta.Senha);
        Assert.Equal(HttpStatusCode.TooManyRequests, senhaCerta.StatusCode);
    }
}

/// <summary>Nada de token, senha ou e-mail nos logs.</summary>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class LogsSemSegredoTestes(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    [Fact]
    public async Task FluxoCompletoNaoRegistraTokenSenhaNemEmail()
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Administrador, Perfil.Operador);
        var administrador = organizacao.Com(Perfil.Administrador);
        var operador = organizacao.Com(Perfil.Operador);
        const string SenhaErrada = "senha-errada-que-nao-pode-aparecer";
        const string SenhaDaNovaConta = "senha-da-nova-conta-no-log";
        var emailDaNovaConta = $"logs.{Guid.NewGuid():n}@teste.test";
        using var cliente = Cliente();

        var sessao = await EntrarAsync(cliente, administrador);
        using var renovacao = await RenovarAsync(cliente, sessao.CookieDeRenovacao);
        var renovada = await LerSessaoAsync(renovacao, CanalDeAcesso.Operacao);
        using var criacao = await EnviarAsync(cliente, HttpMethod.Post, "/api/usuarios", renovada.TokenDeAcesso, new
        {
            nome = "Conta do Log",
            email = emailDaNovaConta,
            senha = SenhaDaNovaConta,
            perfil = "Operador",
        });
        using var falha = await LoginAsync(cliente, organizacao.Slug, operador.Email, SenhaErrada);
        using var saida = await SairAsync(cliente, renovada.CookieDeRenovacao);

        var logs = Fabrica.Logs.TextoCompleto();

        // Sem isto, o teste passaria mesmo com o sink desligado: primeiro, prova de que o
        // log foi capturado.
        Assert.Contains("Login concluído", logs, StringComparison.Ordinal);
        Assert.Contains("Login recusado", logs, StringComparison.Ordinal);
        Assert.Contains("Logout", logs, StringComparison.Ordinal);

        foreach (var segredo in new[]
        {
            sessao.TokenDeAcesso, sessao.CookieDeRenovacao, renovada.TokenDeAcesso, renovada.CookieDeRenovacao,
            Cenario.SenhaPadrao, SenhaErrada, SenhaDaNovaConta,
            administrador.Email, operador.Email, emailDaNovaConta,
        })
        {
            Assert.DoesNotContain(segredo, logs, StringComparison.OrdinalIgnoreCase);
        }
    }
}
