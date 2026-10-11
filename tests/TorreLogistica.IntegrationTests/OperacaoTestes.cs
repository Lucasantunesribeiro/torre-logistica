using System.Net;
using System.Text.Json;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// Pulso da operação contra PostgreSQL + PostGIS real: a idade do dado mais recente, separada da conexão,
/// e recortada pelo tenant.
/// </summary>
/// <remarks>
/// É a prova da correção da Onda 2 (TL-02): o console precisa distinguir "a API responde" de "há operação
/// agora". Sem posição, o pulso diz que não há dado; com posição, devolve o instante de recebimento; e uma
/// organização nunca vê o pulso da outra.
/// </remarks>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class OperacaoTestes(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    [Fact]
    public async Task PulsoSemPosicaoDizQueNaoHaDado()
    {
        using var http = Cliente();
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Operador);
        var operador = (await EntrarAsync(http, organizacao.Com(Perfil.Operador))).TokenDeAcesso;

        using var resposta = await EnviarAsync(http, HttpMethod.Get, "/api/operacao/pulso", operador);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);

        var pulso = await JsonAsync(resposta);
        Assert.Equal(JsonValueKind.Null, pulso.GetProperty("ultimaPosicaoRecebidaEm").ValueKind);
        Assert.Equal(0, pulso.GetProperty("motoristasComPosicao").GetInt32());
        Assert.Equal(0, pulso.GetProperty("entregasEmAndamento").GetInt32());

        // O agora vem do servidor, não do cliente — é o que permite medir idade sem o relógio do navegador.
        Assert.True(pulso.GetProperty("agora").GetDateTimeOffset() <= DateTimeOffset.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task PulsoRefleteAUltimaPosicaoRecebidaESoDaPropriaOrganizacao()
    {
        using var http = Cliente();

        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Supervisor, Perfil.Operador, Perfil.Motorista);
        var supervisor = (await EntrarAsync(http, organizacao.Com(Perfil.Supervisor))).TokenDeAcesso;
        var operador = (await EntrarAsync(http, organizacao.Com(Perfil.Operador))).TokenDeAcesso;
        var contaDoMotorista = organizacao.Com(Perfil.Motorista);

        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);
        var motoristaId = await MotoristaComContaAsync(http, supervisor, contaDoMotorista);
        var tokenDoMotorista = (await EntrarAsync(http, contaDoMotorista)).TokenDeAcesso;
        await RotaIniciadaAsync(http, supervisor, operador, clienteId, destinatarioId, motoristaId, tokenDoMotorista);

        var antes = DateTimeOffset.UtcNow.AddSeconds(-5);
        await PosicoesTestes.EnviarAsync(
            http, tokenDoMotorista, PosicoesTestes.Posicao(1, DateTimeOffset.UtcNow.AddMinutes(-1)));

        using var resposta = await EnviarAsync(http, HttpMethod.Get, "/api/operacao/pulso", operador);
        var pulso = await JsonAsync(resposta);

        Assert.Equal(1, pulso.GetProperty("motoristasComPosicao").GetInt32());
        Assert.Equal(1, pulso.GetProperty("entregasEmAndamento").GetInt32());
        var recebida = pulso.GetProperty("ultimaPosicaoRecebidaEm").GetDateTimeOffset();
        Assert.True(recebida >= antes, $"recebidaEm {recebida:O} deveria ser recente, após {antes:O}");

        // Outra organização: a mesma pergunta, e o pulso dela não enxerga a posição da primeira.
        var outra = await Cenario.CriarOrganizacaoAsync(Perfil.Operador);
        var operadorDaOutra = (await EntrarAsync(http, outra.Com(Perfil.Operador))).TokenDeAcesso;
        using var respostaDaOutra = await EnviarAsync(http, HttpMethod.Get, "/api/operacao/pulso", operadorDaOutra);
        var pulsoDaOutra = await JsonAsync(respostaDaOutra);

        Assert.Equal(0, pulsoDaOutra.GetProperty("motoristasComPosicao").GetInt32());
        Assert.Equal(JsonValueKind.Null, pulsoDaOutra.GetProperty("ultimaPosicaoRecebidaEm").ValueKind);
    }

    [Fact]
    public async Task PulsoExigeSessaoComLeituraDaOperacao()
    {
        using var http = Cliente();
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Motorista);
        var motorista = (await EntrarAsync(http, organizacao.Com(Perfil.Motorista))).TokenDeAcesso;

        // Sem token: a porta não se abre.
        using var anonimo = await EnviarAsync(http, HttpMethod.Get, "/api/operacao/pulso", tokenDeAcesso: null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);

        // Token de motorista é outra autoridade: não lê o console.
        using var comMotorista = await EnviarAsync(http, HttpMethod.Get, "/api/operacao/pulso", motorista);
        Assert.Equal(HttpStatusCode.Unauthorized, comMotorista.StatusCode);
    }

    private static async Task<Guid> MotoristaComContaAsync(HttpClient http, string supervisor, ContaDeTeste conta)
    {
        var id = await CriarAsync(http, supervisor, "/api/motoristas", RoteirosDeCadastro.Obter("motorista").Corpo());
        using var _ = await EnviarAsync(http, HttpMethod.Put, $"/api/motoristas/{id}/conta", supervisor, new { usuarioId = conta.Id });
        return id;
    }

    private static async Task RotaIniciadaAsync(
        HttpClient http,
        string supervisor,
        string operador,
        Guid clienteId,
        Guid destinatarioId,
        Guid motoristaId,
        string tokenDoMotorista)
    {
        var data = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1));
        var entrega = await CriarAsync(http, operador, "/api/entregas", RoteirosDeEntrega.Corpo(clienteId, destinatarioId));
        var veiculo = await CriarAsync(http, supervisor, "/api/veiculos", RoteirosDeCadastro.Obter("veiculo").Corpo());
        var rota = await CriarAsync(http, supervisor, "/api/rotas", new { data });
        using var _ = await EnviarAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/paradas", supervisor, new { entregas = new[] { entrega } });
        using var __ = await EnviarAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/motorista", supervisor, new { motoristaId });
        using var ___ = await EnviarAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/veiculo", supervisor, new { veiculoId = veiculo });
        using var ____ = await EnviarAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/saida", supervisor, new { saidaPlanejada = RoteirosDeEntrega.Amanha(8) });
        using var _____ = await EnviarAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/planejamento", supervisor);
        using var ______ = await EnviarAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{rota}/inicio", tokenDoMotorista);
    }

    private static async Task<Guid> CriarAsync(HttpClient http, string token, string url, object corpo)
    {
        using var resposta = await EnviarAsync(http, HttpMethod.Post, url, token, corpo);
        var json = await JsonAsync(resposta);
        Assert.True(resposta.StatusCode == HttpStatusCode.Created, $"{url}: {(int)resposta.StatusCode} {json}");
        return json.GetProperty("id").GetGuid();
    }
}
