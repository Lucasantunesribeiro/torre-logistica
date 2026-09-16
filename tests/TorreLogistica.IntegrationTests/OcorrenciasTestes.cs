using System.Net;
using System.Text.Json;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// Ocorrências e tentativas de entrega contra a API e o PostgreSQL reais: motivos tipados, severidade,
/// timeline, autoria, isolamento e registro somente-inserção.
/// </summary>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class OcorrenciasTestes(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    /// <summary>Roadmap, Fase 13: cada motivo tipado produz tentativa, ocorrência e timeline.</summary>
    [Theory]
    [InlineData("DestinatarioAusente", "Media")]
    [InlineData("EnderecoNaoLocalizado", "Media")]
    [InlineData("RecusadaPeloDestinatario", "Media")]
    [InlineData("LocalFechado", "Media")]
    [InlineData("AcessoImpedido", "Media")]
    [InlineData("ProblemaComVeiculo", "Alta")]
    [InlineData("ProblemaComMercadoria", "Critica")]
    public async Task CadaMotivoDaTentativaRegistraOcorrenciaComSeveridadeETimeline(string motivo, string severidade)
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);

        var entrega = await OkAsync(
            http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Primeira}/tentativa-frustrada", cenario.TokenDoMotorista, new { motivo });

        Assert.Equal("TentativaFrustrada", entrega.GetProperty("status").GetString());
        Assert.Equal(1, entrega.GetProperty("execucao").GetProperty("tentativasFrustradas").GetInt32());

        var ocorrencia = Assert.Single((await OkAsync(
            http, HttpMethod.Get, $"/api/entregas/{cenario.Primeira}/ocorrencias", cenario.Operador)).EnumerateArray());

        Assert.Equal("TentativaDeEntrega", ocorrencia.GetProperty("tipo").GetString());
        Assert.Equal(motivo, ocorrencia.GetProperty("motivoDaTentativa").GetString());
        Assert.Equal(severidade, ocorrencia.GetProperty("severidade").GetString());
        Assert.Equal("Motorista", ocorrencia.GetProperty("origem").GetString());
        Assert.Equal(cenario.ContaDoMotorista, ocorrencia.GetProperty("autorUsuarioId").GetGuid());
        Assert.Equal(cenario.Rota, ocorrencia.GetProperty("rotaId").GetGuid());
        Assert.Equal(cenario.Motorista, ocorrencia.GetProperty("motoristaId").GetGuid());
        Assert.Equal(JsonValueKind.Null, ocorrencia.GetProperty("observacao").ValueKind);

        var tipos = (await OkAsync(http, HttpMethod.Get, $"/api/entregas/{cenario.Primeira}/eventos", cenario.Operador))
            .EnumerateArray()
            .Select(evento => evento.GetProperty("tipo").GetString());
        Assert.Contains("TentativaFrustrada", tipos);
    }

    /// <summary>Texto livre nunca é a única estrutura: o motivo é tipado, e só "Outro" exige descrição.</summary>
    [Fact]
    public async Task MotivoOutroExigeDescricaoEAGuardaNaOcorrencia()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);

        using var semDescricao = await EnviarAsync(
            http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Primeira}/tentativa-frustrada", cenario.TokenDoMotorista, new { motivo = "Outro" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, semDescricao.StatusCode);
        Assert.Equal("motivo_exige_descricao", await CodigoDoErroAsync(semDescricao));
        Assert.Equal("EmRota", await StatusAsync(http, cenario, cenario.Primeira));

        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Primeira}/tentativa-frustrada", cenario.TokenDoMotorista, new
        {
            motivo = "Outro",
            observacao = "  Rua interditada por obra da prefeitura.  ",
        });

        var ocorrencia = Assert.Single((await OkAsync(
            http, HttpMethod.Get, $"/api/entregas/{cenario.Primeira}/ocorrencias", cenario.Operador)).EnumerateArray());
        Assert.Equal("Rua interditada por obra da prefeitura.", ocorrencia.GetProperty("observacao").GetString());

        // A timeline registra que houve descrição, mas não guarda o texto: ele vive na ocorrência.
        var dados = await Banco.ConsultarEscalarAsync(
            "SELECT dados::text FROM eventos_da_entrega WHERE entrega_id = @id AND tipo = 'TentativaFrustrada'", ("id", cenario.Primeira));
        Assert.Contains("\"comDescricao\": true", dados!.Replace("\"comDescricao\":true", "\"comDescricao\": true", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("prefeitura", dados, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MotoristaRegistraOcorrenciaQueNaoMudaOStatusDaEntrega()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);

        using var criacao = await EnviarAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Primeira}/ocorrencia", cenario.TokenDoMotorista, new
        {
            tipo = "ProblemaComVeiculo",
            observacao = "Pneu furado na avenida.",
            latitude = -22.91,
            longitude = -47.06,
            ocorridaEm = DateTimeOffset.UtcNow.AddMinutes(-10),
        });
        var registrada = await JsonAsync(criacao);
        Assert.True(criacao.StatusCode == HttpStatusCode.Created, registrada.ToString());

        Assert.Equal("ProblemaComVeiculo", registrada.GetProperty("tipo").GetString());
        Assert.Equal("Alta", registrada.GetProperty("severidade").GetString());
        Assert.Equal(JsonValueKind.Null, registrada.GetProperty("motivoDaTentativa").ValueKind);
        Assert.Equal(-22.91, registrada.GetProperty("localizacao").GetProperty("latitude").GetDouble(), 2);

        // O status não mudou: ocorrência é fato, não comando de máquina de estados.
        Assert.Equal("EmRota", await StatusAsync(http, cenario, cenario.Primeira));

        // A tentativa tem rota própria, porque muda o status.
        using var tentativaPelaRotaErrada = await EnviarAsync(
            http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Primeira}/ocorrencia", cenario.TokenDoMotorista, new { tipo = "TentativaDeEntrega" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tentativaPelaRotaErrada.StatusCode);
        Assert.Equal("tentativa_tem_rota_propria", await CodigoDoErroAsync(tentativaPelaRotaErrada));
    }

    [Fact]
    public async Task OperacaoRegistraOcorrenciaPeloConsoleEListaComFiltro()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);

        using var criacao = await EnviarAsync(http, HttpMethod.Post, $"/api/entregas/{cenario.Primeira}/ocorrencias", cenario.Operador, new
        {
            tipo = "ProblemaComMercadoria",
            observacao = "Caixa violada na conferência.",
        });
        var registrada = await JsonAsync(criacao);
        Assert.True(criacao.StatusCode == HttpStatusCode.Created, registrada.ToString());
        Assert.Equal("Critica", registrada.GetProperty("severidade").GetString());
        Assert.Equal("Operacao", registrada.GetProperty("origem").GetString());
        Assert.Equal(cenario.ContaDoOperador, registrada.GetProperty("autorUsuarioId").GetGuid());

        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Segunda}/tentativa-frustrada", cenario.TokenDoMotorista, new
        {
            motivo = "DestinatarioAusente",
        });

        var todas = await OkAsync(http, HttpMethod.Get, "/api/ocorrencias", cenario.Operador);
        Assert.Equal(2, todas.GetProperty("total").GetInt32());

        var criticas = await OkAsync(http, HttpMethod.Get, "/api/ocorrencias?severidade=Critica", cenario.Operador);
        Assert.Equal(registrada.GetProperty("id").GetGuid(), Assert.Single(criticas.GetProperty("itens").EnumerateArray()).GetProperty("id").GetGuid());

        var porTipo = await OkAsync(http, HttpMethod.Get, "/api/ocorrencias?tipo=TentativaDeEntrega", cenario.Operador);
        Assert.Equal(cenario.Segunda, Assert.Single(porTipo.GetProperty("itens").EnumerateArray()).GetProperty("entregaId").GetGuid());

        using var filtroInvalido = await EnviarAsync(http, HttpMethod.Get, "/api/ocorrencias?severidade=Urgentissima", cenario.Operador);
        Assert.Equal(HttpStatusCode.BadRequest, filtroInvalido.StatusCode);

        // A tentativa é do motorista: o console não a registra por aqui.
        using var tentativaPeloConsole = await EnviarAsync(
            http, HttpMethod.Post, $"/api/entregas/{cenario.Primeira}/ocorrencias", cenario.Operador, new { tipo = "TentativaDeEntrega" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tentativaPeloConsole.StatusCode);
        Assert.Equal("tentativa_pelo_motorista", await CodigoDoErroAsync(tentativaPeloConsole));
    }

    [Fact]
    public async Task OcorrenciaDeOutraOrganizacaoNaoApareceNemPodeSerRegistrada()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var outra = await CenarioAsync(http);

        var criada = await OkAsync(http, HttpMethod.Get, $"/api/entregas/{cenario.Primeira}/ocorrencias", cenario.Operador);
        Assert.Empty(criada.EnumerateArray());

        using var registro = await EnviarAsync(http, HttpMethod.Post, $"/api/entregas/{cenario.Primeira}/ocorrencias", outra.Operador, new
        {
            tipo = "AcidenteOuIncidente",
            observacao = "Não é minha entrega.",
        });
        using var inventada = await EnviarAsync(http, HttpMethod.Post, $"/api/entregas/{Guid.CreateVersion7()}/ocorrencias", outra.Operador, new
        {
            tipo = "AcidenteOuIncidente",
        });

        Assert.Equal(HttpStatusCode.NotFound, registro.StatusCode);
        Assert.Equal(await AssinaturaAsync(inventada), await AssinaturaAsync(registro));
        Assert.Equal("0", await Banco.ConsultarEscalarAsync("SELECT count(*) FROM ocorrencias WHERE entrega_id = @id", ("id", cenario.Primeira)));
    }

    [Theory]
    [InlineData("UPDATE ocorrencias SET severidade = 'Baixa' WHERE entrega_id = @id")]
    [InlineData("DELETE FROM ocorrencias WHERE entrega_id = @id")]
    [InlineData("TRUNCATE ocorrencias")]
    public async Task RegistroDeOcorrenciasEhSomenteInsercao(string sql)
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Primeira}/tentativa-frustrada", cenario.TokenDoMotorista, new
        {
            motivo = "LocalFechado",
        });

        var erro = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => Banco.ExecutarAsync(sql, ("id", cenario.Primeira)));

        Assert.Equal(Npgsql.PostgresErrorCodes.InsufficientPrivilege, erro.SqlState);
    }

    /// <summary>Critério de aceite: a falha deixa rastro completo — o que, quando, onde, quem e por quê.</summary>
    [Fact]
    public async Task FalhaDeEntregaDeixaRastreabilidadeOperacionalCompleta()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);

        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Primeira}/tentativa-frustrada", cenario.TokenDoMotorista, new
        {
            motivo = "ProblemaComMercadoria",
            observacao = "Duas caixas molhadas.",
        });
        await OkAsync(http, HttpMethod.Post, $"/api/entregas/{cenario.Primeira}/reagendamento", cenario.Operador, new
        {
            prometidaDe = RoteirosDeEntrega.Amanha(9).AddDays(2),
            prometidaAte = RoteirosDeEntrega.Amanha(12).AddDays(2),
        });

        var entrega = await OkAsync(http, HttpMethod.Get, $"/api/entregas/{cenario.Primeira}", cenario.Operador);
        Assert.Equal("Reagendada", entrega.GetProperty("status").GetString());
        Assert.Equal("ProblemaComMercadoria", entrega.GetProperty("execucao").GetProperty("motivoDaUltimaTentativa").GetString());

        var ocorrencia = Assert.Single((await OkAsync(
            http, HttpMethod.Get, $"/api/entregas/{cenario.Primeira}/ocorrencias", cenario.Operador)).EnumerateArray());
        Assert.Equal("Critica", ocorrencia.GetProperty("severidade").GetString());
        Assert.Equal("Duas caixas molhadas.", ocorrencia.GetProperty("observacao").GetString());
        Assert.Equal(cenario.ContaDoMotorista, ocorrencia.GetProperty("autorUsuarioId").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, ocorrencia.GetProperty("ocorridaEm").ValueKind);

        Assert.Equal(
            ["Criada", "Planejada", "Atribuida", "SaiuParaRota", "TentativaFrustrada", "Reagendada"],
            (await OkAsync(http, HttpMethod.Get, $"/api/entregas/{cenario.Primeira}/eventos", cenario.Operador))
                .EnumerateArray()
                .Select(evento => evento.GetProperty("tipo").GetString()));
    }

    private sealed record CenarioDeOcorrencias(
        string Supervisor,
        string Operador,
        Guid ContaDoOperador,
        string TokenDoMotorista,
        Guid ContaDoMotorista,
        Guid Motorista,
        Guid Rota,
        Guid Primeira,
        Guid Segunda);

    private async Task<CenarioDeOcorrencias> CenarioAsync(HttpClient http)
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Supervisor, Perfil.Operador, Perfil.Motorista);
        var contaDoOperador = organizacao.Com(Perfil.Operador);
        var contaDoMotorista = organizacao.Com(Perfil.Motorista);
        var supervisor = (await EntrarAsync(http, organizacao.Com(Perfil.Supervisor))).TokenDeAcesso;
        var operador = (await EntrarAsync(http, contaDoOperador)).TokenDeAcesso;
        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);

        var primeira = await CriarAsync(http, supervisor, "/api/entregas", RoteirosDeEntrega.Corpo(clienteId, destinatarioId));
        var segunda = await CriarAsync(http, supervisor, "/api/entregas", RoteirosDeEntrega.Corpo(clienteId, destinatarioId));

        var motorista = await CriarAsync(http, supervisor, "/api/motoristas", RoteirosDeCadastro.Obter("motorista").Corpo());
        await OkAsync(http, HttpMethod.Put, $"/api/motoristas/{motorista}/conta", supervisor, new { usuarioId = contaDoMotorista.Id });
        var tokenDoMotorista = (await EntrarAsync(http, contaDoMotorista)).TokenDeAcesso;

        var veiculo = await CriarAsync(http, supervisor, "/api/veiculos", RoteirosDeCadastro.Obter("veiculo").Corpo());
        var rota = await CriarAsync(http, supervisor, "/api/rotas", new { data = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1)) });
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/paradas", supervisor, new { entregas = new[] { primeira, segunda } });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/motorista", supervisor, new { motoristaId = motorista });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/veiculo", supervisor, new { veiculoId = veiculo });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/saida", supervisor, new { saidaPlanejada = RoteirosDeEntrega.Amanha(8) });
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/planejamento", supervisor);
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{rota}/inicio", tokenDoMotorista);

        return new CenarioDeOcorrencias(
            supervisor, operador, contaDoOperador.Id, tokenDoMotorista, contaDoMotorista.Id, motorista, rota, primeira, segunda);
    }

    private static async Task<string?> StatusAsync(HttpClient http, CenarioDeOcorrencias cenario, Guid entrega) =>
        (await OkAsync(http, HttpMethod.Get, $"/api/entregas/{entrega}", cenario.Operador)).GetProperty("status").GetString();

    private static async Task<Guid> CriarAsync(HttpClient http, string token, string url, object corpo)
    {
        using var resposta = await EnviarAsync(http, HttpMethod.Post, url, token, corpo);
        var json = await JsonAsync(resposta);
        Assert.True(resposta.StatusCode == HttpStatusCode.Created, $"{url}: {json}");
        return json.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> OkAsync(HttpClient http, HttpMethod metodo, string url, string token, object? corpo = null)
    {
        using var resposta = await EnviarAsync(http, metodo, url, token, corpo);
        var json = await JsonAsync(resposta);
        Assert.True(resposta.StatusCode == HttpStatusCode.OK, $"{metodo} {url}: {(int)resposta.StatusCode} {json}");
        return json;
    }

    private static async Task<string> AssinaturaAsync(HttpResponseMessage resposta)
    {
        var json = await JsonAsync(resposta);
        var campos = json.EnumerateObject()
            .Where(campo => campo.Name is not ("traceId" or "idDeCorrelacao" or "instance"))
            .Select(campo => $"{campo.Name}={campo.Value.GetRawText()}");
        return $"{(int)resposta.StatusCode}|{string.Join("|", campos)}";
    }
}
