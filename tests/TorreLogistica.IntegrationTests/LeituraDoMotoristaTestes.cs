using System.Net;
using System.Text.Json;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// O que a PWA do motorista lê e executa, pelas rotas do canal do motorista, contra a API e o PostgreSQL reais.
/// </summary>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class LeituraDoMotoristaTestes(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    /// <summary>
    /// Critério de aceite da Fase 11 no lado da API: o fluxo básico inteiro usando só o que a PWA chama.
    /// </summary>
    [Fact]
    public async Task FluxoBasicoDoMotoristaSoComAsRotasDaPwa()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var motorista = cenario.TokenDoMotorista;

        // Rota do dia.
        var rotas = await OkAsync(http, HttpMethod.Get, "/api/motorista/rotas", motorista);
        var item = Assert.Single(rotas.EnumerateArray());
        Assert.Equal(cenario.Rota, item.GetProperty("id").GetGuid());
        Assert.Equal("Planejada", item.GetProperty("status").GetString());
        Assert.Equal(2, item.GetProperty("totalDeParadas").GetInt32());
        Assert.Equal(2, item.GetProperty("paradasPendentes").GetInt32());

        // Lista de paradas, em ordem, sem dado do console.
        var rota = await OkAsync(http, HttpMethod.Get, $"/api/motorista/rotas/{cenario.Rota}", motorista);
        var paradas = rota.GetProperty("paradas").EnumerateArray().ToList();
        Assert.Equal([cenario.Primeira, cenario.Segunda], paradas.Select(parada => parada.GetProperty("entregaId").GetGuid()));
        Assert.Equal("ABC1D23", rota.GetProperty("veiculo").GetProperty("placa").GetString());
        Assert.False(rota.TryGetProperty("versao", out _));
        Assert.False(paradas[0].TryGetProperty("telefone", out _));

        // Iniciar rota.
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{cenario.Rota}/inicio", motorista);

        // Próxima entrega: detalhe com contato e instruções.
        var detalhe = await OkAsync(http, HttpMethod.Get, $"/api/motorista/entregas/{cenario.Primeira}", motorista);
        Assert.Equal("EmRota", detalhe.GetProperty("status").GetString());
        Assert.Equal(cenario.Rota, detalhe.GetProperty("rotaId").GetGuid());
        Assert.Equal(1, detalhe.GetProperty("sequencia").GetInt32());
        Assert.Equal("Portaria 24h, deixar com o zelador.", detalhe.GetProperty("destinatario").GetProperty("instrucoesDeEntrega").GetString());
        Assert.False(detalhe.TryGetProperty("clienteNome", out _));

        // Chegada e conclusão da primeira.
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Primeira}/chegada", motorista);
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Primeira}/conclusao", motorista);

        // Tentativa sem sucesso na segunda.
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Segunda}/tentativa-frustrada", motorista, new { motivo = "LocalFechado" });
        var frustrada = await OkAsync(http, HttpMethod.Get, $"/api/motorista/entregas/{cenario.Segunda}", motorista);
        Assert.Equal(1, frustrada.GetProperty("execucao").GetProperty("tentativasFrustradas").GetInt32());
        Assert.Equal("LocalFechado", frustrada.GetProperty("execucao").GetProperty("motivoDaUltimaTentativa").GetString());

        var emAndamento = Assert.Single((await OkAsync(http, HttpMethod.Get, "/api/motorista/rotas", motorista)).EnumerateArray());
        Assert.Equal("EmAndamento", emAndamento.GetProperty("status").GetString());
        Assert.Equal(0, emAndamento.GetProperty("paradasPendentes").GetInt32());

        // Encerrar a rota: sai da lista do dia.
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{cenario.Rota}/conclusao", motorista);
        Assert.Empty((await OkAsync(http, HttpMethod.Get, "/api/motorista/rotas", motorista)).EnumerateArray());
    }

    [Fact]
    public async Task MotoristaSoLeOQueEhDele()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var outraOrganizacao = await CenarioAsync(http);

        // Outro motorista da mesma organização, sem rota.
        var outraConta = cenario.Organizacao.TodasCom(Perfil.Motorista)[1];
        var outroMotorista = await CriarAsync(http, cenario.Supervisor, "/api/motoristas", RoteirosDeCadastro.Obter("motorista").Corpo());
        await OkAsync(http, HttpMethod.Put, $"/api/motoristas/{outroMotorista}/conta", cenario.Supervisor, new { usuarioId = outraConta.Id });
        var tokenDoOutro = (await EntrarAsync(http, outraConta)).TokenDeAcesso;

        Assert.Empty((await OkAsync(http, HttpMethod.Get, "/api/motorista/rotas", tokenDoOutro)).EnumerateArray());

        foreach (var url in new[] { $"/api/motorista/rotas/{cenario.Rota}", $"/api/motorista/entregas/{cenario.Primeira}" })
        {
            using var deOutroMotorista = await EnviarAsync(http, HttpMethod.Get, url, tokenDoOutro);
            using var deOutraOrganizacao = await EnviarAsync(http, HttpMethod.Get, url, outraOrganizacao.TokenDoMotorista);
            using var doConsole = await EnviarAsync(http, HttpMethod.Get, url, cenario.Supervisor);

            Assert.Equal(HttpStatusCode.NotFound, deOutroMotorista.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, deOutraOrganizacao.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, doConsole.StatusCode);
        }
    }

    /// <summary>A entrega passada a outro motorista explica o que aconteceu, em vez de sumir sem motivo.</summary>
    [Fact]
    public async Task EntregaReatribuidaRespondeConflitoExplicavel()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{cenario.Rota}/inicio", cenario.TokenDoMotorista);

        var outraConta = cenario.Organizacao.TodasCom(Perfil.Motorista)[1];
        var substituto = await CriarAsync(http, cenario.Supervisor, "/api/motoristas", RoteirosDeCadastro.Obter("motorista").Corpo());
        await OkAsync(http, HttpMethod.Put, $"/api/motoristas/{substituto}/conta", cenario.Supervisor, new { usuarioId = outraConta.Id });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{cenario.Rota}/motorista", cenario.Supervisor, new { motoristaId = substituto });

        using var resposta = await EnviarAsync(http, HttpMethod.Get, $"/api/motorista/entregas/{cenario.Primeira}", cenario.TokenDoMotorista);

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("entrega_reatribuida", await CodigoDoErroAsync(resposta));
        Assert.Empty((await OkAsync(http, HttpMethod.Get, "/api/motorista/rotas", cenario.TokenDoMotorista)).EnumerateArray());
    }

    private sealed record CenarioDaPwa(OrganizacaoDeTeste Organizacao, string Supervisor, string TokenDoMotorista, Guid Rota, Guid Primeira, Guid Segunda);

    private async Task<CenarioDaPwa> CenarioAsync(HttpClient http)
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Supervisor, Perfil.Motorista, Perfil.Motorista);
        var supervisor = (await EntrarAsync(http, organizacao.Com(Perfil.Supervisor))).TokenDeAcesso;

        var clienteId = await CriarAsync(http, supervisor, "/api/clientes", RoteirosDeCadastro.Obter("cliente").Corpo());
        var destinatarioId = await CriarAsync(http, supervisor, "/api/destinatarios", new
        {
            nome = "Carla Nunes",
            telefone = "11987654321",
            endereco = RoteirosDeCadastro.Endereco(),
            instrucoesDeEntrega = "Portaria 24h, deixar com o zelador.",
        });

        var primeira = await CriarAsync(http, supervisor, "/api/entregas", RoteirosDeEntrega.Corpo(clienteId, destinatarioId));
        var segunda = await CriarAsync(http, supervisor, "/api/entregas", RoteirosDeEntrega.Corpo(clienteId, destinatarioId));

        var motorista = await CriarAsync(http, supervisor, "/api/motoristas", RoteirosDeCadastro.Obter("motorista").Corpo());
        await OkAsync(http, HttpMethod.Put, $"/api/motoristas/{motorista}/conta", supervisor, new { usuarioId = organizacao.Com(Perfil.Motorista).Id });
        var tokenDoMotorista = (await EntrarAsync(http, organizacao.Com(Perfil.Motorista))).TokenDeAcesso;

        var veiculo = await CriarAsync(http, supervisor, "/api/veiculos", new { placa = "ABC1D23", identificacao = "Fiorino 04", tipo = "Utilitario", capacidadeEmKg = 650 });
        var rota = await CriarAsync(http, supervisor, "/api/rotas", new { data = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1)) });
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/paradas", supervisor, new { entregas = new[] { primeira, segunda } });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/motorista", supervisor, new { motoristaId = motorista });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/veiculo", supervisor, new { veiculoId = veiculo });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/saida", supervisor, new { saidaPlanejada = RoteirosDeEntrega.Amanha(8) });
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/planejamento", supervisor);

        return new CenarioDaPwa(organizacao, supervisor, tokenDoMotorista, rota, primeira, segunda);
    }

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
}
