using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.Infrastructure.Persistencia;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// Critério de aceite da Fase 1: tenant A não consegue ler, alterar nem inferir recurso
/// do tenant B.
/// </summary>
/// <remarks>
/// "Inferir" é a parte difícil. Não basta negar: a resposta sobre um recurso de outra
/// organização precisa ser indistinguível da resposta sobre um identificador que nunca
/// existiu. Por isso vários testes comparam as duas respostas campo a campo.
/// </remarks>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class IsolamentoEntreTenantsTestes(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    [Fact]
    public async Task ContaDeOutraOrganizacaoRespondeIgualAIdentificadorInexistente()
    {
        var (a, b) = await DuasOrganizacoesAsync();
        using var cliente = Cliente();
        var tokenDeA = (await EntrarAsync(cliente, a.Com(Perfil.Administrador))).TokenDeAcesso;

        using var deB = await EnviarAsync(cliente, HttpMethod.Get, $"/api/usuarios/{b.Com(Perfil.Operador).Id}", tokenDeA);
        using var inexistente = await EnviarAsync(cliente, HttpMethod.Get, $"/api/usuarios/{Guid.CreateVersion7()}", tokenDeA);

        Assert.Equal(HttpStatusCode.NotFound, deB.StatusCode);
        Assert.Equal(await AssinaturaAsync(inexistente), await AssinaturaAsync(deB));
    }

    [Fact]
    public async Task AdministradorDeANaoAlteraPerfilDeContaDeB()
    {
        var (a, b) = await DuasOrganizacoesAsync();
        var alvo = b.Com(Perfil.Operador);
        using var cliente = Cliente();
        var tokenDeA = (await EntrarAsync(cliente, a.Com(Perfil.Administrador))).TokenDeAcesso;

        using var deB = await EnviarAsync(cliente, HttpMethod.Put, $"/api/usuarios/{alvo.Id}/perfil", tokenDeA, new { perfil = "Administrador" });
        using var inexistente = await EnviarAsync(cliente, HttpMethod.Put, $"/api/usuarios/{Guid.CreateVersion7()}/perfil", tokenDeA, new { perfil = "Administrador" });

        Assert.Equal(HttpStatusCode.NotFound, deB.StatusCode);
        Assert.Equal(await AssinaturaAsync(inexistente), await AssinaturaAsync(deB));
        Assert.Equal("Operador", await Banco.ConsultarEscalarAsync("SELECT perfil FROM usuarios WHERE id = @id", ("id", alvo.Id)));
    }

    [Fact]
    public async Task AdministradorDeANaoDesativaContaDeBNemDerrubaASessaoDela()
    {
        var (a, b) = await DuasOrganizacoesAsync();
        var alvo = b.Com(Perfil.Operador);
        using var cliente = Cliente();
        var tokenDeA = (await EntrarAsync(cliente, a.Com(Perfil.Administrador))).TokenDeAcesso;
        var sessaoDoAlvo = await EntrarAsync(cliente, alvo);

        using var tentativa = await EnviarAsync(cliente, HttpMethod.Post, $"/api/usuarios/{alvo.Id}/desativacao", tokenDeA);

        Assert.Equal(HttpStatusCode.NotFound, tentativa.StatusCode);
        Assert.Equal("True", await Banco.ConsultarEscalarAsync("SELECT ativo FROM usuarios WHERE id = @id", ("id", alvo.Id)));

        using var alvoContinuaLogado = await EnviarAsync(cliente, HttpMethod.Get, "/api/autenticacao/eu", sessaoDoAlvo.TokenDeAcesso);
        Assert.Equal(HttpStatusCode.OK, alvoContinuaLogado.StatusCode);
    }

    [Fact]
    public async Task ListagemSoTrazContasDaPropriaOrganizacao()
    {
        var (a, b) = await DuasOrganizacoesAsync();
        using var cliente = Cliente();
        var tokenDeA = (await EntrarAsync(cliente, a.Com(Perfil.Administrador))).TokenDeAcesso;

        using var resposta = await EnviarAsync(cliente, HttpMethod.Get, "/api/usuarios?tamanhoDaPagina=100", tokenDeA);
        var json = await JsonAsync(resposta);
        var ids = json.GetProperty("itens").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToHashSet();

        Assert.Equal(a.Contas.Select(conta => conta.Id).ToHashSet(), ids);
        Assert.Equal(a.Contas.Count, json.GetProperty("total").GetInt32());
    }

    /// <summary>
    /// A organização vem da sessão. Mandar outra no corpo não é "ignorado": é recusado,
    /// e nada é criado em organização nenhuma.
    /// </summary>
    [Fact]
    public async Task OrganizacaoInformadaNoCorpoEhRecusada()
    {
        var (a, b) = await DuasOrganizacoesAsync();
        using var cliente = Cliente();
        var tokenDeA = (await EntrarAsync(cliente, a.Com(Perfil.Administrador))).TokenDeAcesso;
        var email = $"infiltrado.{Guid.NewGuid():n}@teste.test";

        using var resposta = await EnviarAsync(cliente, HttpMethod.Post, "/api/usuarios", tokenDeA, new
        {
            nome = "Infiltrado",
            email,
            senha = "senha-do-infiltrado",
            perfil = "Administrador",
            organizacaoId = b.Id,
        });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("0", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM usuarios WHERE email_normalizado = @email", ("email", email)));
    }

    /// <summary>
    /// Com unicidade global de e-mail, este cadastro daria 409 — e o administrador de A
    /// descobriria que a pessoa tem conta em outra organização. A unicidade é por
    /// organização justamente para fechar esse canal.
    /// </summary>
    [Fact]
    public async Task EmailUsadoEmOutraOrganizacaoNaoConflita()
    {
        var (a, b) = await DuasOrganizacoesAsync();
        using var cliente = Cliente();
        var tokenDeA = (await EntrarAsync(cliente, a.Com(Perfil.Administrador))).TokenDeAcesso;

        using var resposta = await EnviarAsync(cliente, HttpMethod.Post, "/api/usuarios", tokenDeA, new
        {
            nome = "Mesma Pessoa",
            email = b.Com(Perfil.Operador).Email,
            senha = "senha-da-mesma-pessoa",
            perfil = "Operador",
        });

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    }

    [Fact]
    public async Task OrganizacaoAtualEhADaSessao()
    {
        var (a, _) = await DuasOrganizacoesAsync();
        using var cliente = Cliente();
        var tokenDeA = (await EntrarAsync(cliente, a.Com(Perfil.Operador))).TokenDeAcesso;

        using var resposta = await EnviarAsync(cliente, HttpMethod.Get, "/api/organizacao", tokenDeA);

        Assert.Equal(a.Slug, (await JsonAsync(resposta)).GetProperty("slug").GetString());
    }

    [Fact]
    public async Task ContaDeBNaoAutenticaUsandoOIdentificadorDeA()
    {
        var (a, b) = await DuasOrganizacoesAsync();
        var contaDeB = b.Com(Perfil.Operador);
        using var cliente = Cliente();

        using var resposta = await LoginAsync(cliente, a.Slug, contaDeB.Email, contaDeB.Senha);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    /// <summary>
    /// A garantia de fundo: o filtro global do contexto de persistência. Com tenant A ele
    /// só enxerga A; sem tenant, não enxerga nada — falha fechada, independente de qualquer
    /// cuidado do caso de uso.
    /// </summary>
    [Fact]
    public async Task FiltroGlobalSoEnxergaOTenantDaSessaoEFalhaFechado()
    {
        var (a, _) = await DuasOrganizacoesAsync();

        await using var comTenantA = Banco.CriarContexto(new TenantFixo(a.Id));
        var visiveis = await comTenantA.Usuarios.Select(usuario => usuario.Id).ToListAsync(Cancelamento);

        await using var semTenant = Banco.CriarContexto(new ContextoDeTenantAusente());
        var semTenantVisiveis = await semTenant.Usuarios.CountAsync(Cancelamento);
        var sessoesSemTenant = await semTenant.Sessoes.CountAsync(Cancelamento);

        Assert.Equal(a.Contas.Select(conta => conta.Id).ToHashSet(), visiveis.ToHashSet());
        Assert.Equal(0, semTenantVisiveis);
        Assert.Equal(0, sessoesSemTenant);
    }

    private async Task<(OrganizacaoDeTeste A, OrganizacaoDeTeste B)> DuasOrganizacoesAsync() =>
        (await Cenario.CriarOrganizacaoAsync(Perfil.Administrador, Perfil.Operador),
         await Cenario.CriarOrganizacaoAsync(Perfil.Administrador, Perfil.Operador));

    // Tudo o que distingue uma resposta de erro, exceto o que muda a cada requisição.
    private static async Task<string> AssinaturaAsync(HttpResponseMessage resposta)
    {
        var json = await JsonAsync(resposta);
        var campos = json.EnumerateObject()
            .Where(campo => campo.Name is not ("traceId" or "idDeCorrelacao" or "instance"))
            .Select(campo => $"{campo.Name}={campo.Value.GetRawText()}");

        return $"{(int)resposta.StatusCode}|{string.Join("|", campos)}|{resposta.Content.Headers.ContentType?.MediaType}";
    }
}
