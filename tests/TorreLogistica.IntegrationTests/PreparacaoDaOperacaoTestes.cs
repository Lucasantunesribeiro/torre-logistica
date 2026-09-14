using System.Globalization;
using System.Net;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// Critério de aceite da Fase 2, conta do motorista e PostGIS real.
/// </summary>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class PreparacaoDaOperacaoTestes(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    /// <summary>
    /// "Supervisor consegue preparar uma organização operacional sem criar entrega ainda."
    /// </summary>
    [Fact]
    public async Task SupervisorPreparaAOrganizacaoOperacionalSemCriarEntrega()
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Supervisor, Perfil.Operador, Perfil.Motorista);
        using var cliente = Cliente();
        var supervisor = (await EntrarAsync(cliente, organizacao.Com(Perfil.Supervisor))).TokenDeAcesso;
        var operador = (await EntrarAsync(cliente, organizacao.Com(Perfil.Operador))).TokenDeAcesso;

        foreach (var recurso in new[] { "hub", "cliente", "destinatario", "veiculo", "motorista" })
        {
            var roteiro = RoteirosDeCadastro.Obter(recurso);
            using var criacao = await EnviarAsync(cliente, HttpMethod.Post, roteiro.Rota, supervisor, roteiro.Corpo());
            Assert.True(criacao.StatusCode == HttpStatusCode.Created, $"{recurso}: {(int)criacao.StatusCode}");
        }

        using var motoristas = await EnviarAsync(cliente, HttpMethod.Get, "/api/motoristas", supervisor);
        var motoristaId = (await JsonAsync(motoristas)).GetProperty("itens")[0].GetProperty("id").GetGuid();
        var contaDoMotorista = organizacao.Com(Perfil.Motorista).Id;

        using var associacao = await EnviarAsync(cliente, HttpMethod.Put, $"/api/motoristas/{motoristaId}/conta", supervisor, new { usuarioId = contaDoMotorista });
        Assert.Equal(HttpStatusCode.OK, associacao.StatusCode);
        Assert.Equal(contaDoMotorista, (await JsonAsync(associacao)).GetProperty("usuarioId").GetGuid());

        // O operador enxerga a operação preparada, mas não a altera.
        foreach (var rota in new[] { "/api/hubs", "/api/clientes", "/api/destinatarios", "/api/veiculos", "/api/motoristas" })
        {
            using var leitura = await EnviarAsync(cliente, HttpMethod.Get, rota, operador);
            Assert.Equal(1, (await JsonAsync(leitura)).GetProperty("total").GetInt32());
        }

        using var tentativaDoOperador = await EnviarAsync(cliente, HttpMethod.Post, "/api/hubs", operador, RoteirosDeCadastro.Obter("hub").Corpo());
        Assert.Equal(HttpStatusCode.Forbidden, tentativaDoOperador.StatusCode);
    }

    [Fact]
    public async Task ContaDoMotoristaSegueAsRegrasDeAssociacao()
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(
            Perfil.Supervisor, Perfil.Operador, Perfil.Motorista, Perfil.Motorista, Perfil.Motorista);
        var outra = await Cenario.CriarOrganizacaoAsync(Perfil.Motorista);
        var contas = organizacao.TodasCom(Perfil.Motorista);
        await Cenario.DesativarDiretamenteAsync(contas[2].Id);
        using var cliente = Cliente();
        var token = (await EntrarAsync(cliente, organizacao.Com(Perfil.Supervisor))).TokenDeAcesso;
        var primeiro = await CriarMotoristaAsync(cliente, token, "Rafael Mendes");
        var segundo = await CriarMotoristaAsync(cliente, token, "Bruno Tavares");

        async Task<HttpResponseMessage> Associar(Guid motorista, Guid usuario) =>
            await EnviarAsync(cliente, HttpMethod.Put, $"/api/motoristas/{motorista}/conta", token, new { usuarioId = usuario });

        using (var associacao = await Associar(primeiro, contas[0].Id))
        using (var repetida = await Associar(primeiro, contas[0].Id))
        {
            Assert.Equal(HttpStatusCode.OK, associacao.StatusCode);
            Assert.Equal(HttpStatusCode.OK, repetida.StatusCode);
        }

        await AssertCodigoAsync(Associar(segundo, organizacao.Com(Perfil.Operador).Id), HttpStatusCode.UnprocessableEntity, "conta_nao_e_de_motorista");
        await AssertCodigoAsync(Associar(segundo, contas[2].Id), HttpStatusCode.UnprocessableEntity, "conta_inativa");
        await AssertCodigoAsync(Associar(segundo, contas[0].Id), HttpStatusCode.Conflict, "conta_ja_associada");
        await AssertCodigoAsync(Associar(primeiro, contas[1].Id), HttpStatusCode.Conflict, "motorista_ja_possui_conta");

        // Conta de outra organização: mesma resposta de conta inexistente.
        using (var deOutra = await Associar(segundo, outra.Com(Perfil.Motorista).Id))
        using (var inexistente = await Associar(segundo, Guid.CreateVersion7()))
        {
            Assert.Equal(HttpStatusCode.NotFound, deOutra.StatusCode);
            Assert.Equal(await CodigoDoErroAsync(inexistente), await CodigoDoErroAsync(deOutra));
        }

        using (var desassociacao = await EnviarAsync(cliente, HttpMethod.Delete, $"/api/motoristas/{primeiro}/conta", token))
        using (var novaConta = await Associar(primeiro, contas[1].Id))
        using (var contaLiberada = await Associar(segundo, contas[0].Id))
        {
            Assert.True((await JsonAsync(desassociacao)).GetProperty("usuarioId").ValueKind == System.Text.Json.JsonValueKind.Null);
            Assert.Equal(HttpStatusCode.OK, novaConta.StatusCode);
            Assert.Equal(HttpStatusCode.OK, contaLiberada.StatusCode);
        }
    }

    /// <summary>
    /// Duas associações simultâneas da mesma conta a motoristas diferentes passam juntas pela
    /// checagem prévia; o índice único parcial decide, e a perdedora recebe 409, nunca 500.
    /// </summary>
    [Fact]
    public async Task AssociacoesSimultaneasDaMesmaContaTemUmUnicoVencedor()
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Supervisor, Perfil.Motorista);
        using var cliente = Cliente();
        var token = (await EntrarAsync(cliente, organizacao.Com(Perfil.Supervisor))).TokenDeAcesso;
        var motoristas = new List<Guid>();
        for (var indice = 0; indice < 6; indice++)
        {
            motoristas.Add(await CriarMotoristaAsync(cliente, token, $"Motorista {indice}"));
        }

        var conta = organizacao.Com(Perfil.Motorista).Id;
        var respostas = await Task.WhenAll(motoristas.Select(motorista =>
            EnviarAsync(cliente, HttpMethod.Put, $"/api/motoristas/{motorista}/conta", token, new { usuarioId = conta })));
        var status = respostas.Select(resposta => resposta.StatusCode).ToList();
        foreach (var resposta in respostas)
        {
            resposta.Dispose();
        }

        Assert.Equal(1, status.Count(codigo => codigo == HttpStatusCode.OK));
        Assert.All(status.Where(codigo => codigo != HttpStatusCode.OK), codigo => Assert.Equal(HttpStatusCode.Conflict, codigo));
        Assert.Equal("1", await Banco.ConsultarEscalarAsync("SELECT count(*) FROM motoristas WHERE usuario_id = @conta", ("conta", conta)));
    }

    /// <summary>
    /// A coordenada chega ao banco como <c>geography(Point,4326)</c> de verdade, com longitude
    /// em X e latitude em Y — a inversão das duas é o erro geoespacial mais comum.
    /// </summary>
    [Fact]
    public async Task LocalizacaoDoHubEhPontoGeograficoRealNoPostGis()
    {
        using var cliente = Cliente();
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Supervisor);
        var token = (await EntrarAsync(cliente, organizacao.Com(Perfil.Supervisor))).TokenDeAcesso;

        using var hub = await EnviarAsync(cliente, HttpMethod.Post, "/api/hubs", token, new
        {
            nome = "Hub Sé",
            endereco = RoteirosDeCadastro.Endereco(),
            latitude = -23.5505,
            longitude = -46.6339,
        });
        using var destinatario = await EnviarAsync(cliente, HttpMethod.Post, "/api/destinatarios", token, new
        {
            nome = "Parque",
            endereco = RoteirosDeCadastro.Endereco(),
            latitude = -23.5874,
            longitude = -46.6570,
        });
        var hubId = (await JsonAsync(hub)).GetProperty("id").GetGuid();
        var destinatarioId = (await JsonAsync(destinatario)).GetProperty("id").GetGuid();

        var descricao = await Banco.ConsultarEscalarAsync("""
            SELECT concat_ws('|', GeometryType(localizacao::geometry), ST_SRID(localizacao::geometry),
                             ST_X(localizacao::geometry), ST_Y(localizacao::geometry), pg_typeof(localizacao))
            FROM hubs WHERE id = @id
            """, ("id", hubId));
        Assert.Equal("POINT|4326|-46.6339|-23.5505|geography", descricao);

        var distancia = await Banco.ConsultarEscalarAsync("""
            SELECT round(ST_Distance(h.localizacao, d.localizacao)::numeric)
            FROM hubs h, destinatarios d WHERE h.id = @hub AND d.id = @destinatario
            """, ("hub", hubId), ("destinatario", destinatarioId));
        Assert.InRange(double.Parse(distancia!, CultureInfo.InvariantCulture), 4_000, 6_000);

        var indices = await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM pg_indexes WHERE tablename IN ('hubs', 'destinatarios') AND indexdef ILIKE '%USING gist%'");
        Assert.Equal("2", indices);

        using var leitura = await EnviarAsync(cliente, HttpMethod.Get, $"/api/hubs/{hubId}", token);
        var localizacao = (await JsonAsync(leitura)).GetProperty("localizacao");
        Assert.Equal(-23.5505, localizacao.GetProperty("latitude").GetDouble());
        Assert.Equal(-46.6339, localizacao.GetProperty("longitude").GetDouble());
    }

    private static async Task<Guid> CriarMotoristaAsync(HttpClient cliente, string token, string nome)
    {
        using var resposta = await EnviarAsync(cliente, HttpMethod.Post, "/api/motoristas", token, new { nome });
        return (await JsonAsync(resposta)).GetProperty("id").GetGuid();
    }

    private static async Task AssertCodigoAsync(Task<HttpResponseMessage> chamada, HttpStatusCode status, string codigo)
    {
        using var resposta = await chamada;
        Assert.True(resposta.StatusCode == status, $"{codigo}: esperava {(int)status}, veio {(int)resposta.StatusCode}");
        Assert.Equal(codigo, await CodigoDoErroAsync(resposta));
    }
}
