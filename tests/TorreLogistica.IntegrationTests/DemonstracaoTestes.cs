using System.Net;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>Base das três configurações de demonstração que interessam.</summary>
public abstract class TesteDeDemonstracao(ContainerPostgis banco, Perfil perfil, bool habilitada)
    : TesteDeIntegracao(banco)
{
    /// <summary>Slug da organização de demonstração, conhecido antes de a API subir.</summary>
    protected string Slug { get; } = $"demo-{Guid.NewGuid():n}";

    /// <summary>Conta de demonstração.</summary>
    protected string Email { get; } = $"demo.{Guid.NewGuid():n}@teste.test";

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?> ConfiguracaoAdicional =>
        habilitada
            ? new Dictionary<string, string?>
            {
                ["Torre:Demonstracao:Habilitada"] = "true",
                ["Torre:Demonstracao:Organizacao"] = Slug,
                ["Torre:Demonstracao:Email"] = Email,
                ["Torre:Demonstracao:Senha"] = Cenario.SenhaPadrao,
                ["Torre:Demonstracao:Convite"] = "Entre como operador numa transportadora fictícia.",
            }
            : new Dictionary<string, string?>();

    /// <inheritdoc />
    public override async ValueTask InitializeAsync()
    {
        // A conta precisa existir antes da API subir: a configuração aponta para ela pelo nome.
        await Cenario.CriarContaComIdentidadeAsync(Slug, Email, perfil).ConfigureAwait(false);
        await base.InitializeAsync().ConfigureAwait(false);
    }

    /// <summary>Pede a sessão de demonstração, como o botão do console faz.</summary>
    protected static Task<HttpResponseMessage> PedirSessaoAsync(HttpClient http)
    {
        var requisicao = new HttpRequestMessage(HttpMethod.Post, "/api/demonstracao/sessao");
        requisicao.Headers.Add("Origin", FabricaDaApi.OrigemAutorizada);

        return http.SendAsync(requisicao, Cancelamento);
    }
}

/// <summary>
/// A porta da demonstração, ligada e com conta de privilégio mínimo.
/// </summary>
/// <remarks>
/// É o caminho que o visitante do portfólio percorre: chega sem credencial nenhuma, clica um botão e entra
/// numa operação de verdade. O que se prova aqui é que isso funciona <b>e</b> que o que ele recebe é uma
/// sessão comum, com as mesmas limitações de perfil que qualquer operador teria.
/// </remarks>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class DemonstracaoHabilitadaTestes(ContainerPostgis banco)
    : TesteDeDemonstracao(banco, Perfil.Operador, habilitada: true)
{
    /// <summary>Critério de aceite da Fase 24: entrar sem onboarding comercial.</summary>
    [Fact]
    public async Task VisitanteEntraSemCredencialERecebeSessaoDeOperador()
    {
        using var http = Cliente();

        // O console pergunta se há demonstração neste ambiente antes de oferecer o botão.
        using var oferta = await http.GetAsync(new Uri("/api/demonstracao", UriKind.Relative), Cancelamento);
        Assert.Equal(HttpStatusCode.OK, oferta.StatusCode);

        var descricao = await JsonAsync(oferta);
        Assert.True(descricao.GetProperty("habilitada").GetBoolean());
        Assert.Equal("Entre como operador numa transportadora fictícia.", descricao.GetProperty("convite").GetString());

        using var sessao = await PedirSessaoAsync(http);
        Assert.Equal(HttpStatusCode.OK, sessao.StatusCode);

        var corpo = await JsonAsync(sessao);
        var usuario = corpo.GetProperty("usuario");
        Assert.Equal("Operador", usuario.GetProperty("perfil").GetString());
        Assert.Equal(Slug, usuario.GetProperty("organizacaoSlug").GetString());

        // A sessão é comum: mesmo cookie de renovação, mesmo token, mesmas regras.
        Assert.NotNull(ValorDoCookie(sessao, CanalDeAcesso.Operacao));

        var token = corpo.GetProperty("tokenDeAcesso").GetString()!;

        // Ela abre a operação...
        using var entregas = await EnviarAsync(http, HttpMethod.Get, "/api/entregas?tamanhoDaPagina=5", token);
        Assert.Equal(HttpStatusCode.OK, entregas.StatusCode);

        // ...e não abre a administração. Privilégio mínimo não é promessa: é o que a sessão recusa.
        using var integracoes = await EnviarAsync(http, HttpMethod.Get, "/api/integracoes", token);
        Assert.Equal(HttpStatusCode.Forbidden, integracoes.StatusCode);

        using var usuarios = await EnviarAsync(http, HttpMethod.Post, "/api/usuarios", token, new
        {
            nome = "Conta indevida",
            email = "indevida@teste.test",
            senha = "uma-senha-bem-comprida",
            perfil = "Administrador",
        });
        Assert.Equal(HttpStatusCode.Forbidden, usuarios.StatusCode);
    }

    /// <summary>A porta exige origem conhecida, como o login humano.</summary>
    [Fact]
    public async Task SessaoDeDemonstracaoExigeOrigemConhecida()
    {
        using var http = Cliente();

        using var semOrigem = await http.PostAsync(new Uri("/api/demonstracao/sessao", UriKind.Relative), content: null, Cancelamento);
        Assert.Equal(HttpStatusCode.Forbidden, semOrigem.StatusCode);

        using var requisicao = new HttpRequestMessage(HttpMethod.Post, "/api/demonstracao/sessao");
        requisicao.Headers.Add("Origin", FabricaDaApi.OrigemNaoAutorizada);
        using var deOutraOrigem = await http.SendAsync(requisicao, Cancelamento);
        Assert.Equal(HttpStatusCode.Forbidden, deOutraOrigem.StatusCode);
    }
}

/// <summary>Sem configuração, a porta não existe — e é assim que um ambiente comercial fica.</summary>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class DemonstracaoDesligadaTestes(ContainerPostgis banco)
    : TesteDeDemonstracao(banco, Perfil.Operador, habilitada: false)
{
    /// <summary>Desligada, a oferta é negativa e a sessão responde como recurso inexistente.</summary>
    [Fact]
    public async Task SemDemonstracaoNaoHaBotaoNemPorta()
    {
        using var http = Cliente();

        using var oferta = await http.GetAsync(new Uri("/api/demonstracao", UriKind.Relative), Cancelamento);
        Assert.Equal(HttpStatusCode.OK, oferta.StatusCode);

        var descricao = await JsonAsync(oferta);
        Assert.False(descricao.GetProperty("habilitada").GetBoolean());

        // O convite vem nulo: a resposta não conta qual organização seria usada.
        Assert.Equal(System.Text.Json.JsonValueKind.Null, descricao.GetProperty("convite").ValueKind);

        // E 404, não 403: "existe mas você não pode" já é informação sobre o sistema.
        using var sessao = await PedirSessaoAsync(http);
        Assert.Equal(HttpStatusCode.NotFound, sessao.StatusCode);
    }
}

/// <summary>
/// Conta administrativa configurada por engano: a porta se fecha.
/// </summary>
/// <remarks>
/// Uma conta de administrador aberta ao público daria a qualquer visitante o poder de criar credencial de
/// integração e revogar webhook. Recusar transforma um erro de configuração em indisponibilidade, que é o
/// modo de falhar que não custa caro.
/// </remarks>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class DemonstracaoComContaAdministrativaTestes(ContainerPostgis banco)
    : TesteDeDemonstracao(banco, Perfil.Administrador, habilitada: true)
{
    /// <summary>Configuração com conta administrativa não abre sessão.</summary>
    [Fact]
    public async Task ContaAdministrativaNaoAbreDemonstracao()
    {
        using var http = Cliente();

        using var sessao = await PedirSessaoAsync(http);
        Assert.Equal(HttpStatusCode.NotFound, sessao.StatusCode);

        // Nenhum cookie de sessão foi para o visitante.
        Assert.Null(ValorDoCookie(sessao, CanalDeAcesso.Operacao));

        // E o motivo fica registrado para quem hospeda, não para quem visita.
        Assert.Contains("privilégio mínimo", Fabrica.Logs.TextoCompleto(), StringComparison.Ordinal);
    }
}
