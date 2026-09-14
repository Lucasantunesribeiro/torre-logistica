using System.Net;
using Npgsql;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

[Collection(ColecaoDeIntegracao.Nome)]
public sealed class GestaoDeUsuariosTestes(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    private static readonly int[] StatusDoPerdedor = [401, 422];

    [Fact]
    public async Task CriarContaDevolve201SemDadoSensivelEAContaAutentica()
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Administrador);
        using var cliente = Cliente();
        var token = (await EntrarAsync(cliente, organizacao.Com(Perfil.Administrador))).TokenDeAcesso;
        var email = $"nova.{Guid.NewGuid():n}@teste.test";

        using var resposta = await EnviarAsync(cliente, HttpMethod.Post, "/api/usuarios", token, NovaConta(email, "Supervisor"));
        var corpo = await resposta.Content.ReadAsStringAsync(Cancelamento);

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        Assert.StartsWith("/api/usuarios/", resposta.Headers.Location?.OriginalString, StringComparison.Ordinal);
        foreach (var proibido in new[] { "senha", "hash", "tentativas", "bloqueado", "senha-da-conta-nova" })
        {
            Assert.DoesNotContain(proibido, corpo, StringComparison.OrdinalIgnoreCase);
        }

        using var login = await LoginAsync(cliente, organizacao.Slug, email, "senha-da-conta-nova");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task EmailRepetidoNaMesmaOrganizacaoDevolve409()
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Administrador, Perfil.Operador);
        using var cliente = Cliente();
        var token = (await EntrarAsync(cliente, organizacao.Com(Perfil.Administrador))).TokenDeAcesso;

        using var resposta = await EnviarAsync(
            cliente, HttpMethod.Post, "/api/usuarios", token, NovaConta(organizacao.Com(Perfil.Operador).Email.ToUpperInvariant()));

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("email_ja_cadastrado", await CodigoDoErroAsync(resposta));
    }

    /// <summary>
    /// A checagem "já existe?" não protege contra requisições simultâneas; a restrição do
    /// banco sim. Nenhuma das perdedoras pode virar 500.
    /// </summary>
    [Fact]
    public async Task CriacoesSimultaneasComMesmoEmailGeramUmaUnicaConta()
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Administrador);
        using var cliente = Cliente();
        var token = (await EntrarAsync(cliente, organizacao.Com(Perfil.Administrador))).TokenDeAcesso;
        var email = $"corrida.{Guid.NewGuid():n}@teste.test";

        var respostas = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => EnviarAsync(cliente, HttpMethod.Post, "/api/usuarios", token, NovaConta(email))));
        var status = respostas.Select(resposta => resposta.StatusCode).ToList();
        foreach (var resposta in respostas)
        {
            resposta.Dispose();
        }

        Assert.Equal(1, status.Count(codigo => codigo == HttpStatusCode.Created));
        Assert.All(status.Where(codigo => codigo != HttpStatusCode.Created), codigo => Assert.Equal(HttpStatusCode.Conflict, codigo));
        Assert.Equal("1", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM usuarios WHERE organizacao_id = @org AND email_normalizado = @email",
            ("org", organizacao.Id), ("email", email)));
    }

    [Theory]
    [InlineData("curta")]
    [InlineData(null)]
    public async Task SenhaForaDaPoliticaDevolve400(string? senha)
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Administrador);
        using var cliente = Cliente();
        var token = (await EntrarAsync(cliente, organizacao.Com(Perfil.Administrador))).TokenDeAcesso;

        using var resposta = await EnviarAsync(cliente, HttpMethod.Post, "/api/usuarios", token, new
        {
            nome = "Conta",
            email = $"conta.{Guid.NewGuid():n}@teste.test",
            senha,
            perfil = "Operador",
        });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    /// <summary>Perfil só por nome declarado: nem nome inventado, nem número.</summary>
    [Fact]
    public async Task PerfilInexistenteOuNumericoDevolve400()
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Administrador);
        using var cliente = Cliente();
        var token = (await EntrarAsync(cliente, organizacao.Com(Perfil.Administrador))).TokenDeAcesso;

        foreach (object perfil in new object[] { "Gerente", 1, 99 })
        {
            using var resposta = await EnviarAsync(cliente, HttpMethod.Post, "/api/usuarios", token, new
            {
                nome = "Conta",
                email = $"conta.{Guid.NewGuid():n}@teste.test",
                senha = "senha-bem-comprida-aqui",
                perfil,
            });

            Assert.True(resposta.StatusCode == HttpStatusCode.BadRequest, $"perfil {perfil}: veio {(int)resposta.StatusCode}");
        }
    }

    [Fact]
    public async Task AlterarPerfilEncerraSessoesDoAlvoENovoLoginTrazOPerfilNovo()
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Administrador, Perfil.Operador);
        var alvo = organizacao.Com(Perfil.Operador);
        using var cliente = Cliente();
        var token = (await EntrarAsync(cliente, organizacao.Com(Perfil.Administrador))).TokenDeAcesso;
        var sessaoDoAlvo = await EntrarAsync(cliente, alvo);

        using var alteracao = await EnviarAsync(cliente, HttpMethod.Put, $"/api/usuarios/{alvo.Id}/perfil", token, new { perfil = "Supervisor" });
        Assert.Equal(HttpStatusCode.OK, alteracao.StatusCode);
        Assert.Equal("Supervisor", (await JsonAsync(alteracao)).GetProperty("perfil").GetString());

        using var tokenAntigo = await EnviarAsync(cliente, HttpMethod.Get, "/api/autenticacao/eu", sessaoDoAlvo.TokenDeAcesso);
        Assert.Equal(HttpStatusCode.Unauthorized, tokenAntigo.StatusCode);

        using var novoLogin = await LoginAsync(cliente, organizacao.Slug, alvo.Email, alvo.Senha);
        Assert.Equal("Supervisor", (await JsonAsync(novoLogin)).GetProperty("usuario").GetProperty("perfil").GetString());
    }

    [Fact]
    public async Task ContaDeMotoristaNaoViraContaDoConsole()
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Administrador, Perfil.Motorista);
        using var cliente = Cliente();
        var token = (await EntrarAsync(cliente, organizacao.Com(Perfil.Administrador))).TokenDeAcesso;

        using var resposta = await EnviarAsync(
            cliente, HttpMethod.Put, $"/api/usuarios/{organizacao.Com(Perfil.Motorista).Id}/perfil", token, new { perfil = "Operador" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        Assert.Equal("perfil_incompativel", await CodigoDoErroAsync(resposta));
    }

    [Fact]
    public async Task OrganizacaoNaoFicaSemAdministradorAtivo()
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Administrador, Perfil.Administrador);
        var (primeiro, segundo) = (organizacao.TodasCom(Perfil.Administrador)[0], organizacao.TodasCom(Perfil.Administrador)[1]);
        using var cliente = Cliente();
        var token = (await EntrarAsync(cliente, primeiro)).TokenDeAcesso;

        using var desativacao = await EnviarAsync(cliente, HttpMethod.Post, $"/api/usuarios/{segundo.Id}/desativacao", token);
        Assert.Equal(HttpStatusCode.OK, desativacao.StatusCode);

        using var autoRebaixamento = await EnviarAsync(cliente, HttpMethod.Put, $"/api/usuarios/{primeiro.Id}/perfil", token, new { perfil = "Operador" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, autoRebaixamento.StatusCode);
        Assert.Equal("ultimo_administrador", await CodigoDoErroAsync(autoRebaixamento));

        using var autoDesativacao = await EnviarAsync(cliente, HttpMethod.Post, $"/api/usuarios/{primeiro.Id}/desativacao", token);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, autoDesativacao.StatusCode);
        Assert.Equal("nao_pode_desativar_a_propria_conta", await CodigoDoErroAsync(autoDesativacao));
    }

    /// <summary>
    /// Dois administradores rebaixando um ao outro ao mesmo tempo. Sem a trava na linha da
    /// organização, cada um veria "ainda existe outro administrador" e a organização
    /// terminaria sem nenhum. Repetido algumas vezes para dar chance à corrida acontecer.
    /// </summary>
    [Fact]
    public async Task RebaixamentosCruzadosSimultaneosDeixamExatamenteUmAdministrador()
    {
        for (var rodada = 0; rodada < 5; rodada++)
        {
            var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Administrador, Perfil.Administrador);
            var (primeiro, segundo) = (organizacao.TodasCom(Perfil.Administrador)[0], organizacao.TodasCom(Perfil.Administrador)[1]);
            using var cliente = Cliente();
            var tokenDoPrimeiro = (await EntrarAsync(cliente, primeiro)).TokenDeAcesso;
            var tokenDoSegundo = (await EntrarAsync(cliente, segundo)).TokenDeAcesso;

            var respostas = await Task.WhenAll(
                EnviarAsync(cliente, HttpMethod.Put, $"/api/usuarios/{segundo.Id}/perfil", tokenDoPrimeiro, new { perfil = "Operador" }),
                EnviarAsync(cliente, HttpMethod.Put, $"/api/usuarios/{primeiro.Id}/perfil", tokenDoSegundo, new { perfil = "Operador" }));
            var status = respostas.Select(resposta => (int)resposta.StatusCode).OrderBy(codigo => codigo).ToList();
            foreach (var resposta in respostas)
            {
                resposta.Dispose();
            }

            // O perdedor recebe 422 se chegou à regra, ou 401 se a sessão dele já tinha
            // sido encerrada pelo rebaixamento do vencedor. Nunca dois 200.
            Assert.Equal(200, status[0]);
            Assert.Contains(status[1], StatusDoPerdedor);
            Assert.Equal("1", await Banco.ConsultarEscalarAsync(
                "SELECT count(*) FROM usuarios WHERE organizacao_id = @org AND perfil = 'Administrador' AND ativo",
                ("org", organizacao.Id)));
        }
    }

    [Fact]
    public async Task DesativarEncerraSessoesBloqueiaLoginEEhIdempotente()
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Administrador, Perfil.Operador);
        var alvo = organizacao.Com(Perfil.Operador);
        using var cliente = Cliente();
        var token = (await EntrarAsync(cliente, organizacao.Com(Perfil.Administrador))).TokenDeAcesso;
        var sessaoDoAlvo = await EntrarAsync(cliente, alvo);

        using var primeira = await EnviarAsync(cliente, HttpMethod.Post, $"/api/usuarios/{alvo.Id}/desativacao", token);
        using var segunda = await EnviarAsync(cliente, HttpMethod.Post, $"/api/usuarios/{alvo.Id}/desativacao", token);

        Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        Assert.False((await JsonAsync(segunda)).GetProperty("ativo").GetBoolean());

        using var tokenDoAlvo = await EnviarAsync(cliente, HttpMethod.Get, "/api/autenticacao/eu", sessaoDoAlvo.TokenDeAcesso);
        using var renovacaoDoAlvo = await RenovarAsync(cliente, sessaoDoAlvo.CookieDeRenovacao);
        using var loginDoAlvo = await LoginAsync(cliente, organizacao.Slug, alvo.Email, alvo.Senha);

        Assert.Equal(HttpStatusCode.Unauthorized, tokenDoAlvo.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, renovacaoDoAlvo.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, loginDoAlvo.StatusCode);

        // A segunda desativação não gerou novo evento.
        Assert.Equal("1", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM eventos_de_auditoria WHERE tipo = 'usuario_desativado' AND alvo_id = @alvo", ("alvo", alvo.Id)));
    }

    [Fact]
    public async Task AcoesAdministrativasFicamNaTrilhaDeAuditoriaSemDadoSensivel()
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Administrador);
        var administrador = organizacao.Com(Perfil.Administrador);
        using var cliente = Cliente();
        var token = (await EntrarAsync(cliente, administrador)).TokenDeAcesso;

        using var criacao = await EnviarAsync(cliente, HttpMethod.Post, "/api/usuarios", token, NovaConta($"auditada.{Guid.NewGuid():n}@teste.test"));
        var criadoId = (await JsonAsync(criacao)).GetProperty("id").GetGuid();
        using var alteracao = await EnviarAsync(cliente, HttpMethod.Put, $"/api/usuarios/{criadoId}/perfil", token, new { perfil = "Supervisor" });
        using var desativacao = await EnviarAsync(cliente, HttpMethod.Post, $"/api/usuarios/{criadoId}/desativacao", token);

        var eventos = new List<(string Tipo, Guid? Autor, string Dados)>();
        await using (var conexao = new NpgsqlConnection(Banco.CadeiaDeConexao))
        {
            await conexao.OpenAsync(Cancelamento);
            await using var comando = new NpgsqlCommand(
                "SELECT tipo, autor_usuario_id, dados::text FROM eventos_de_auditoria WHERE alvo_id = @alvo ORDER BY ocorrido_em", conexao);
            comando.Parameters.AddWithValue("alvo", criadoId);
            await using var leitor = await comando.ExecuteReaderAsync(Cancelamento);
            while (await leitor.ReadAsync(Cancelamento))
            {
                eventos.Add((leitor.GetString(0), leitor.IsDBNull(1) ? null : leitor.GetGuid(1), leitor.GetString(2)));
            }
        }

        Assert.Equal(["usuario_criado", "perfil_alterado", "usuario_desativado"], eventos.Select(evento => evento.Tipo));
        Assert.All(eventos, evento => Assert.Equal(administrador.Id, evento.Autor));
        Assert.All(eventos, evento =>
        {
            Assert.DoesNotContain("senha", evento.Dados, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hash", evento.Dados, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// Somente-inserção garantido pelo banco: nem SQL manual consegue reescrever a trilha.
    /// </summary>
    [Theory]
    [InlineData("UPDATE eventos_de_auditoria SET tipo = 'adulterado' WHERE id = @id")]
    [InlineData("DELETE FROM eventos_de_auditoria WHERE id = @id")]
    [InlineData("TRUNCATE eventos_de_auditoria")]
    public async Task TrilhaDeAuditoriaRecusaAlteracaoEExclusao(string sql)
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Administrador);
        using var cliente = Cliente();
        var token = (await EntrarAsync(cliente, organizacao.Com(Perfil.Administrador))).TokenDeAcesso;
        using var criacao = await EnviarAsync(cliente, HttpMethod.Post, "/api/usuarios", token, NovaConta($"trilha.{Guid.NewGuid():n}@teste.test"));
        var criadoId = (await JsonAsync(criacao)).GetProperty("id").GetGuid();
        var eventoId = Guid.Parse((await Banco.ConsultarEscalarAsync(
            "SELECT id FROM eventos_de_auditoria WHERE alvo_id = @alvo", ("alvo", criadoId)))!);

        var erro = await Assert.ThrowsAsync<PostgresException>(() => Banco.ExecutarAsync(sql, ("id", eventoId)));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, erro.SqlState);
        Assert.Equal("usuario_criado", await Banco.ConsultarEscalarAsync(
            "SELECT tipo FROM eventos_de_auditoria WHERE id = @id", ("id", eventoId)));
    }

    [Fact]
    public async Task ListagemRespeitaOsLimitesDePaginacao()
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Administrador, Perfil.Operador, Perfil.Operador, Perfil.Supervisor);
        using var cliente = Cliente();
        var token = (await EntrarAsync(cliente, organizacao.Com(Perfil.Administrador))).TokenDeAcesso;

        using var pagina = await EnviarAsync(cliente, HttpMethod.Get, "/api/usuarios?pagina=2&tamanhoDaPagina=3", token);
        using var grandeDemais = await EnviarAsync(cliente, HttpMethod.Get, "/api/usuarios?tamanhoDaPagina=101", token);
        using var paginaZero = await EnviarAsync(cliente, HttpMethod.Get, "/api/usuarios?pagina=0", token);

        var json = await JsonAsync(pagina);
        Assert.Single(json.GetProperty("itens").EnumerateArray());
        Assert.Equal(4, json.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, grandeDemais.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, paginaZero.StatusCode);
    }

    private static object NovaConta(string email, string perfil = "Operador") => new
    {
        nome = "Conta Nova",
        email,
        senha = "senha-da-conta-nova",
        perfil,
    };
}
