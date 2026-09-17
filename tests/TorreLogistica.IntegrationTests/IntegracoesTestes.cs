using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TorreLogistica.Application.Integracoes;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// API de integração: credencial de máquina, idempotência em duas camadas e importação por arquivo.
/// </summary>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class IntegracoesTestes(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    private sealed record CenarioDeIntegracao(
        OrganizacaoDeTeste Organizacao,
        string Administrador,
        Guid IntegracaoId,
        string Chave,
        Guid ClienteId,
        Guid DestinatarioId);

    [Fact]
    public async Task ErpCriaEntregaSemPassarPeloConsole()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);

        using var resposta = await EnviarComChaveAsync(http, cenario, "pedido-1", Corpo(cenario, "PED-1"));
        var entrega = await JsonAsync(resposta);

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        Assert.StartsWith("ENT-", entrega.GetProperty("codigo").GetString(), StringComparison.Ordinal);
        Assert.Equal("Criada", entrega.GetProperty("status").GetString());

        // O ERP consulta o que criou sem abrir o console.
        using var consulta = await EnviarComChaveAsync(
            http, cenario, chave: null, corpo: null, metodo: HttpMethod.Get, caminho: $"/entregas/{entrega.GetProperty("id").GetGuid()}");

        Assert.Equal(HttpStatusCode.OK, consulta.StatusCode);

        // A entrega nasceu sem autor humano: quem agiu foi um sistema, e é a auditoria que guarda qual.
        Assert.Equal("0", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM eventos_da_entrega WHERE entrega_id = @id AND autor_usuario_id IS NOT NULL",
            ("id", entrega.GetProperty("id").GetGuid())));
    }

    [Fact]
    public async Task ReenvioDaMesmaChaveNaoCriaOutraEntrega()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var corpo = Corpo(cenario, "PED-1");

        using var primeira = await EnviarComChaveAsync(http, cenario, "pedido-1", corpo);
        using var repetida = await EnviarComChaveAsync(http, cenario, "pedido-1", corpo);

        var criada = await JsonAsync(primeira);
        var devolvida = await JsonAsync(repetida);

        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);

        // 200, e não 201: nada nasceu desta vez.
        Assert.Equal(HttpStatusCode.OK, repetida.StatusCode);
        Assert.Equal(criada.GetProperty("id").GetGuid(), devolvida.GetProperty("id").GetGuid());

        Assert.Equal("1", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM requisicoes_de_integracao WHERE integracao_id = @id", ("id", cenario.IntegracaoId)));
    }

    [Fact]
    public async Task MesmaChaveComCorpoDiferenteEhConflito()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);

        using var primeira = await EnviarComChaveAsync(http, cenario, "pedido-1", Corpo(cenario, "PED-1"));
        using var conflitante = await EnviarComChaveAsync(http, cenario, "pedido-1", Corpo(cenario, "PED-2"));

        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflitante.StatusCode);
        Assert.Equal("chave_de_idempotencia_reutilizada", await CodigoDoErroAsync(conflitante));
    }

    /// <summary>Fila do ERP reprocessada: mesmo pedido, chave nova. Não pode virar duas entregas.</summary>
    [Fact]
    public async Task MesmoIdentificadorDeOrigemComChaveNovaDevolveAEntregaQueJaExiste()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);

        using var primeira = await EnviarComChaveAsync(http, cenario, "pedido-1", Corpo(cenario, "PED-1"));
        using var reprocessada = await EnviarComChaveAsync(http, cenario, "pedido-1-again", Corpo(cenario, "PED-1"));

        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reprocessada.StatusCode);
        Assert.Equal(
            (await JsonAsync(primeira)).GetProperty("id").GetGuid(),
            (await JsonAsync(reprocessada)).GetProperty("id").GetGuid());

        Assert.Equal("1", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM referencias_externas_de_entrega WHERE integracao_id = @id", ("id", cenario.IntegracaoId)));
    }

    [Fact]
    public async Task SemChaveDeIdempotenciaARequisicaoEhRecusada()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);

        using var resposta = await EnviarComChaveAsync(http, cenario, chave: null, Corpo(cenario, "PED-1"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        Assert.Equal("chave_de_idempotencia_obrigatoria", await CodigoDoErroAsync(resposta));
    }

    [Fact]
    public async Task CredencialRevogadaEDesconhecidaRespondemIgual()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);

        await OkAsync(http, HttpMethod.Post, $"/api/integracoes/{cenario.IntegracaoId}/revogacao", cenario.Administrador, null);

        using var comRevogada = await EnviarComChaveAsync(http, cenario, "pedido-1", Corpo(cenario, "PED-1"));
        using var comInventada = await EnviarComChaveAsync(
            http, cenario with { Chave = SegredosDeIntegracao.Gerar().Chave }, "pedido-1", Corpo(cenario, "PED-1"));
        using var semNada = await EnviarComChaveAsync(http, cenario with { Chave = "lixo" }, "pedido-1", Corpo(cenario, "PED-1"));

        Assert.Equal(HttpStatusCode.Unauthorized, comRevogada.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, comInventada.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, semNada.StatusCode);
        Assert.Equal(await CodigoDoErroAsync(comInventada), await CodigoDoErroAsync(comRevogada));
    }

    [Fact]
    public async Task TokenDoConsoleNaoValeNaApiExternaENemOContrario()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);

        // Token de pessoa apresentado à API de máquina.
        using var comTokenDeConsole = await EnviarAsync(
            http, HttpMethod.Post, "/api/integracoes/v1/entregas", cenario.Administrador, Corpo(cenario, "PED-1"));

        // Chave de máquina apresentada ao console.
        using var requisicao = new HttpRequestMessage(HttpMethod.Get, "/api/entregas");
        requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cenario.Chave);
        using var comChaveNoConsole = await http.SendAsync(requisicao, Cancelamento);

        Assert.Equal(HttpStatusCode.Unauthorized, comTokenDeConsole.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, comChaveNoConsole.StatusCode);
    }

    [Fact]
    public async Task CredencialDeUmaOrganizacaoNaoAlcancaOutra()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var outra = await CenarioAsync(http);

        // Cliente e destinatário são da outra organização: a credencial não os enxerga. A resposta é a
        // mesma de um identificador inventado — saber que "existe, mas não é seu" já seria informação.
        using var comDeOutra = await EnviarComChaveAsync(
            http, cenario, "pedido-1", Corpo(cenario with { ClienteId = outra.ClienteId, DestinatarioId = outra.DestinatarioId }, "PED-1"));

        using var comInventado = await EnviarComChaveAsync(
            http, cenario, "pedido-2", Corpo(cenario with { ClienteId = Guid.CreateVersion7(), DestinatarioId = Guid.CreateVersion7() }, "PED-2"));

        Assert.Equal(HttpStatusCode.NotFound, comDeOutra.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, comInventado.StatusCode);
        Assert.Equal(await CodigoDoErroAsync(comInventado), await CodigoDoErroAsync(comDeOutra));

        // E nada nasceu: nem entrega, nem vínculo com o pedido de origem.
        Assert.Equal("0", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM referencias_externas_de_entrega WHERE integracao_id = @id", ("id", cenario.IntegracaoId)));
    }

    [Fact]
    public async Task SoOHashDoSegredoEhGuardado()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);

        var segredo = cenario.Chave.Split('.')[2];
        var esperado = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(segredo))).ToLowerInvariant();

        Assert.Equal(esperado, await Banco.ConsultarEscalarAsync(
            "SELECT encode(hash_do_segredo, 'hex') FROM integracoes WHERE id = @id", ("id", cenario.IntegracaoId)));

        Assert.Equal("0", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM integracoes WHERE integracoes::text LIKE @busca", ("busca", $"%{segredo}%")));
    }

    [Fact]
    public async Task RegistroDeIdempotenciaEhSomenteInsercao()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        await EnviarComChaveAsync(http, cenario, "pedido-1", Corpo(cenario, "PED-1"));

        var erro = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => Banco.ExecutarAsync(
            "UPDATE requisicoes_de_integracao SET chave = 'outra' WHERE integracao_id = @id", ("id", cenario.IntegracaoId)));

        Assert.Equal(Npgsql.PostgresErrorCodes.InsufficientPrivilege, erro.SqlState);
    }

    [Fact]
    public async Task ImportacaoConfereOArquivoAntesDeGravarEReenvioNaoDuplica()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);

        var comErro = $"{string.Join(',', ImportacaoDeEntregas.Cabecalho)}\n{LinhaDeCsv(cenario, "PED-1")}\n{LinhaDeCsv(cenario, "PED-1")}";

        using var previa = await ImportarAsync(http, cenario, comErro, "/importacoes/previa");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, previa.StatusCode);
        Assert.Contains("identificador_repetido", (await JsonAsync(previa)).GetProperty("erros").ToString(), StringComparison.Ordinal);

        using var recusada = await ImportarAsync(http, cenario, comErro, "/importacoes");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, recusada.StatusCode);
        Assert.Equal("0", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM referencias_externas_de_entrega WHERE integracao_id = @id", ("id", cenario.IntegracaoId)));

        var valido = $"{string.Join(',', ImportacaoDeEntregas.Cabecalho)}\n{LinhaDeCsv(cenario, "PED-1")}\n{LinhaDeCsv(cenario, "PED-2")}";

        using var importada = await ImportarAsync(http, cenario, valido, "/importacoes");
        Assert.Equal(HttpStatusCode.OK, importada.StatusCode);
        Assert.Equal(2, (await JsonAsync(importada)).GetProperty("importadas").GetArrayLength());

        // Reenviar o mesmo arquivo é seguro: cada linha reencontra a própria chave.
        using var reenviada = await ImportarAsync(http, cenario, valido, "/importacoes");
        Assert.Equal(HttpStatusCode.OK, reenviada.StatusCode);
        Assert.All(
            (await JsonAsync(reenviada)).GetProperty("importadas").EnumerateArray(),
            linha => Assert.True(linha.GetProperty("jaExistia").GetBoolean()));

        Assert.Equal("2", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM referencias_externas_de_entrega WHERE integracao_id = @id", ("id", cenario.IntegracaoId)));
    }

    [Fact]
    public async Task SoAdministradorEmiteERevoga()
    {
        using var http = Cliente();
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Administrador, Perfil.Supervisor);
        var supervisor = (await EntrarAsync(http, organizacao.Com(Perfil.Supervisor))).TokenDeAcesso;

        using var recusada = await EnviarAsync(
            http, HttpMethod.Post, "/api/integracoes", supervisor, new { nome = "ERP" });

        Assert.Equal(HttpStatusCode.Forbidden, recusada.StatusCode);
    }

    private async Task<CenarioDeIntegracao> CenarioAsync(HttpClient http)
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Administrador);
        var administrador = (await EntrarAsync(http, organizacao.Com(Perfil.Administrador))).TokenDeAcesso;
        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, administrador);

        using var emissao = await EnviarAsync(http, HttpMethod.Post, "/api/integracoes", administrador, new { nome = "ERP do cliente" });
        var emitida = await JsonAsync(emissao);
        Assert.True(emissao.StatusCode == HttpStatusCode.Created, $"emissão: {(int)emissao.StatusCode} {emitida}");

        return new CenarioDeIntegracao(
            organizacao,
            administrador,
            emitida.GetProperty("id").GetGuid(),
            emitida.GetProperty("chave").GetString()!,
            clienteId,
            destinatarioId);
    }

    private static object Corpo(CenarioDeIntegracao cenario, string identificadorExterno) => new
    {
        identificadorExterno,
        clienteId = cenario.ClienteId,
        destinatarioId = cenario.DestinatarioId,
        prometidaDe = RoteirosDeEntrega.Amanha(9),
        prometidaAte = RoteirosDeEntrega.Amanha(12),
    };

    private static string LinhaDeCsv(CenarioDeIntegracao cenario, string identificador) =>
        $"{identificador},{cenario.ClienteId},{cenario.DestinatarioId},"
        + $"{RoteirosDeEntrega.Amanha(9):O},{RoteirosDeEntrega.Amanha(12):O},"
        + "Rua das Flores,100,,Centro,Campinas,SP,13010-000,,,";

    /// <summary>Envia e exige 200, devolvendo o corpo — auxiliar próprio, como nas demais classes.</summary>
    private static async Task<JsonElement> OkAsync(
        HttpClient http,
        HttpMethod metodo,
        string caminho,
        string token,
        object? corpo)
    {
        using var resposta = await EnviarAsync(http, metodo, caminho, token, corpo);
        var json = await JsonAsync(resposta);
        Assert.True(resposta.StatusCode == HttpStatusCode.OK, $"{metodo} {caminho}: {(int)resposta.StatusCode} {json}");
        return json;
    }

    private static async Task<HttpResponseMessage> EnviarComChaveAsync(
        HttpClient http,
        CenarioDeIntegracao cenario,
        string? chave,
        object? corpo,
        HttpMethod? metodo = null,
        string caminho = "/entregas")
    {
        using var requisicao = new HttpRequestMessage(metodo ?? HttpMethod.Post, $"/api/integracoes/v1{caminho}");
        requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cenario.Chave);

        if (chave is not null)
        {
            requisicao.Headers.Add("Idempotency-Key", chave);
        }

        if (corpo is not null)
        {
            requisicao.Content = new StringContent(
                JsonSerializer.Serialize(corpo), Encoding.UTF8, "application/json");
        }

        return await http.SendAsync(requisicao, Cancelamento);
    }

    private static async Task<HttpResponseMessage> ImportarAsync(
        HttpClient http,
        CenarioDeIntegracao cenario,
        string conteudo,
        string caminho)
    {
        using var requisicao = new HttpRequestMessage(HttpMethod.Post, $"/api/integracoes/v1{caminho}")
        {
            Content = new StringContent(conteudo, Encoding.UTF8, "text/csv"),
        };

        requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cenario.Chave);
        return await http.SendAsync(requisicao, Cancelamento);
    }
}
