using System.Net;
using System.Text.Json;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// Operações feitas no aparelho, sincronizadas exatamente uma vez, contra a API e o PostgreSQL reais.
/// </summary>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class SincronizacaoTestes(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    /// <summary>Caso obrigatório: backend conclui, resposta cai, PWA repete — mesmo resultado, sem efeito duplicado.</summary>
    [Fact]
    public async Task RespostaPerdidaERepetidaNaoDuplicaEfeito()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http, iniciarRota: true);
        var conclusao = Operacao("ConcluirEntrega", cenario.Primeira);

        var primeira = Unico(await SincronizarAsync(http, cenario, conclusao));
        var repetida = Unico(await SincronizarAsync(http, cenario, conclusao));

        Assert.Equal("Aplicada", primeira.GetProperty("desfecho").GetString());
        Assert.False(primeira.GetProperty("repetida").GetBoolean());
        Assert.Equal("Aplicada", repetida.GetProperty("desfecho").GetString());
        Assert.True(repetida.GetProperty("repetida").GetBoolean());

        Assert.Equal("1", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM eventos_da_entrega WHERE entrega_id = @id AND tipo = 'Entregue'", ("id", cenario.Primeira)));
        Assert.Equal("1", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM operacoes_do_cliente WHERE alvo_id = @id", ("id", cenario.Primeira)));
    }

    /// <summary>A mesma operação chegando várias vezes ao mesmo tempo — rede instável reenviando — tem um efeito.</summary>
    [Fact]
    public async Task RepeticoesSimultaneasTemUmUnicoEfeito()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http, iniciarRota: true);
        var tentativa = Operacao("RegistrarTentativaFrustrada", cenario.Primeira, motivo: "DestinatarioAusente");

        var respostas = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => SincronizarAsync(http, cenario, tentativa)));
        var resultados = respostas.Select(Unico).ToList();

        Assert.All(resultados, resultado => Assert.Equal("Aplicada", resultado.GetProperty("desfecho").GetString()));
        Assert.Single(resultados, resultado => !resultado.GetProperty("repetida").GetBoolean());
        Assert.Equal("1", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM eventos_da_entrega WHERE entrega_id = @id AND tipo = 'TentativaFrustrada'", ("id", cenario.Primeira)));
        Assert.Equal("1", await Banco.ConsultarEscalarAsync(
            "SELECT tentativas_frustradas::text FROM entregas WHERE id = @id", ("id", cenario.Primeira)));
    }

    /// <summary>Um dia inteiro feito offline, enviado num lote: aplicado na ordem, com desfecho por operação.</summary>
    [Fact]
    public async Task LoteAplicaNaOrdemEDevolveDesfechoPorOperacao()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http, iniciarRota: false);
        var inicio = DateTimeOffset.UtcNow.AddHours(-2);

        var lote = await SincronizarAsync(http, cenario, cenario.LoteDoDia(inicio));

        var resultados = lote.GetProperty("resultados").EnumerateArray().ToList();
        Assert.Equal(5, resultados.Count);
        Assert.All(resultados, resultado => Assert.Equal("Aplicada", resultado.GetProperty("desfecho").GetString()));
        Assert.Equal("Entregue", await StatusAsync(cenario.Primeira));
        Assert.Equal("TentativaFrustrada", await StatusAsync(cenario.Segunda));
        Assert.Equal("Concluida", await Banco.ConsultarEscalarAsync("SELECT status FROM rotas WHERE id = @id", ("id", cenario.Rota)));
        var eventos = await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM eventos_da_entrega WHERE entrega_id = ANY(@ids)", ("ids", new[] { cenario.Primeira, cenario.Segunda }));

        // Reenviar o lote inteiro — aplicativo que não soube se o envio chegou — não muda nada.
        var reenvio = await SincronizarAsync(http, cenario, cenario.LoteDoDia(inicio));

        Assert.All(reenvio.GetProperty("resultados").EnumerateArray(), resultado =>
        {
            Assert.Equal("Aplicada", resultado.GetProperty("desfecho").GetString());
            Assert.True(resultado.GetProperty("repetida").GetBoolean());
        });
        Assert.Equal(eventos, await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM eventos_da_entrega WHERE entrega_id = ANY(@ids)", ("ids", new[] { cenario.Primeira, cenario.Segunda })));
    }

    /// <summary>
    /// Caso obrigatório de conflito real: o operador cancela enquanto o aparelho está desatualizado, e a
    /// conclusão atrasada não sobrescreve o cancelamento.
    /// </summary>
    [Fact]
    public async Task ConclusaoAtrasadaNaoSobrescreveCancelamento()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http, iniciarRota: false);

        await OkAsync(http, HttpMethod.Post, $"/api/entregas/{cenario.Primeira}/cancelamento", cenario.Supervisor, new { motivo = "SolicitacaoDoCliente" });

        var conclusao = Operacao("ConcluirEntrega", cenario.Primeira);
        var resultado = Unico(await SincronizarAsync(http, cenario, conclusao));

        Assert.Equal("Conflito", resultado.GetProperty("desfecho").GetString());
        Assert.Equal("transicao_invalida", resultado.GetProperty("codigo").GetString());
        Assert.Equal("Cancelada", await StatusAsync(cenario.Primeira));

        // O conflito é definitivo: repetir devolve o mesmo, sem nova tentativa contra o estado atual.
        var repetida = Unico(await SincronizarAsync(http, cenario, conclusao));
        Assert.Equal("Conflito", repetida.GetProperty("desfecho").GetString());
        Assert.True(repetida.GetProperty("repetida").GetBoolean());
    }

    [Fact]
    public async Task EntregaPassadaAOutroMotoristaEnquantoOfflineEhConflito()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http, iniciarRota: true);

        var outraConta = cenario.Organizacao.TodasCom(Perfil.Motorista)[1];
        var substituto = await CriarAsync(http, cenario.Supervisor, "/api/motoristas", RoteirosDeCadastro.Obter("motorista").Corpo());
        await OkAsync(http, HttpMethod.Put, $"/api/motoristas/{substituto}/conta", cenario.Supervisor, new { usuarioId = outraConta.Id });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{cenario.Rota}/motorista", cenario.Supervisor, new { motoristaId = substituto });

        var resultado = Unico(await SincronizarAsync(http, cenario, Operacao("ConcluirEntrega", cenario.Primeira)));

        Assert.Equal("Conflito", resultado.GetProperty("desfecho").GetString());
        Assert.Equal("entrega_reatribuida", resultado.GetProperty("codigo").GetString());
        Assert.Equal("EmRota", await StatusAsync(cenario.Primeira));
    }

    [Fact]
    public async Task OperacaoInvalidaDivergenteOuAlheiaEhRecusadaSemDerrubarOLote()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http, iniciarRota: true);
        var outra = await CenarioAsync(http, iniciarRota: true);

        var idDaChegada = Guid.CreateVersion7();
        var chegada = Operacao("RegistrarChegada", cenario.Primeira, id: idDaChegada);
        var lote = await SincronizarAsync(
            http,
            cenario,
            chegada,
            Operacao("ConcluirEntrega", cenario.Primeira, id: Guid.NewGuid()),
            Operacao("ConcluirEntrega", cenario.Primeira, criadaEm: DateTimeOffset.UtcNow.AddDays(-8)),
            Operacao("ConcluirEntrega", outra.Primeira),
            new { tipo = "ConcluirEntrega", alvoId = cenario.Primeira, criadaEm = DateTimeOffset.UtcNow });

        var codigos = lote.GetProperty("resultados").EnumerateArray()
            .Select(resultado => $"{resultado.GetProperty("desfecho").GetString()}:{Texto(resultado, "codigo")}")
            .ToList();
        Assert.Equal(
            ["Aplicada:", "Recusada:identificador_de_operacao_invalido", "Recusada:operacao_antiga", "Recusada:entrega_nao_encontrada", "Recusada:campo_obrigatorio"],
            codigos);
        Assert.Equal("EmRota", await StatusAsync(outra.Primeira));

        // Mesmo identificador, outro pedido: não herda o desfecho nem executa.
        var divergente = Unico(await SincronizarAsync(http, cenario, Operacao("ConcluirEntrega", cenario.Primeira, id: idDaChegada)));
        Assert.Equal("Recusada", divergente.GetProperty("desfecho").GetString());
        Assert.Equal("operacao_divergente", divergente.GetProperty("codigo").GetString());
        Assert.Equal("ProximaDoDestino", await StatusAsync(cenario.Primeira));
    }

    [Fact]
    public async Task LoteVazioGrandeDemaisOuComCampoDesconhecidoEhRecusadoInteiro()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http, iniciarRota: true);

        using var vazio = await EnviarAsync(http, HttpMethod.Post, "/api/motorista/sincronizacao", cenario.TokenDoMotorista, new { operacoes = Array.Empty<object>() });
        using var grande = await EnviarAsync(http, HttpMethod.Post, "/api/motorista/sincronizacao", cenario.TokenDoMotorista, new
        {
            operacoes = Enumerable.Range(0, 101).Select(_ => Operacao("RegistrarChegada", cenario.Primeira)).ToArray(),
        });
        using var desconhecido = await EnviarAsync(http, HttpMethod.Post, "/api/motorista/sincronizacao", cenario.TokenDoMotorista, new
        {
            operacoes = new[] { new { operacaoDoClienteId = Guid.CreateVersion7(), tipo = "ConcluirEntrega", alvoId = cenario.Primeira, criadaEm = DateTimeOffset.UtcNow, motoristaId = Guid.CreateVersion7() } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, vazio.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, grande.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, desconhecido.StatusCode);
        Assert.Equal("EmRota", await StatusAsync(cenario.Primeira));
    }

    [Theory]
    [InlineData("UPDATE operacoes_do_cliente SET resultado = 'Recusada' WHERE alvo_id = @id")]
    [InlineData("DELETE FROM operacoes_do_cliente WHERE alvo_id = @id")]
    [InlineData("TRUNCATE operacoes_do_cliente")]
    public async Task RegistroDeOperacoesEhSomenteInsercao(string sql)
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http, iniciarRota: true);
        await SincronizarAsync(http, cenario, Operacao("RegistrarChegada", cenario.Primeira));

        var erro = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => Banco.ExecutarAsync(sql, ("id", cenario.Primeira)));

        Assert.Equal(Npgsql.PostgresErrorCodes.InsufficientPrivilege, erro.SqlState);
    }

    private sealed record CenarioDaSincronizacao(OrganizacaoDeTeste Organizacao, string Supervisor, string TokenDoMotorista, Guid Rota, Guid Primeira, Guid Segunda)
    {
        private readonly Dictionary<string, Guid> _identificadores = [];

        /// <summary>O mesmo lote, com os mesmos identificadores, para o reenvio.</summary>
        public object[] LoteDoDia(DateTimeOffset inicio) =>
        [
            Operacao("IniciarRota", Rota, criadaEm: inicio, id: Id("inicio")),
            Operacao("RegistrarChegada", Primeira, criadaEm: inicio.AddMinutes(20), id: Id("chegada")),
            Operacao("ConcluirEntrega", Primeira, criadaEm: inicio.AddMinutes(25), id: Id("conclusao")),
            Operacao("RegistrarTentativaFrustrada", Segunda, motivo: "LocalFechado", criadaEm: inicio.AddMinutes(50), id: Id("tentativa")),
            Operacao("ConcluirRota", Rota, criadaEm: inicio.AddMinutes(55), id: Id("encerramento")),
        ];

        private Guid Id(string nome)
        {
            if (!_identificadores.TryGetValue(nome, out var id))
            {
                id = Guid.CreateVersion7();
                _identificadores[nome] = id;
            }

            return id;
        }
    }

    private static object Operacao(string tipo, Guid alvoId, string? motivo = null, DateTimeOffset? criadaEm = null, Guid? id = null) => new
    {
        operacaoDoClienteId = id ?? Guid.CreateVersion7(),
        tipo,
        alvoId,
        motivo,
        criadaEm = criadaEm ?? DateTimeOffset.UtcNow.AddMinutes(-1),
    };

    private static async Task<JsonElement> SincronizarAsync(HttpClient http, CenarioDaSincronizacao cenario, params object[] operacoes)
    {
        using var resposta = await EnviarAsync(http, HttpMethod.Post, "/api/motorista/sincronizacao", cenario.TokenDoMotorista, new { operacoes });
        var json = await JsonAsync(resposta);
        Assert.True(resposta.StatusCode == HttpStatusCode.OK, $"{(int)resposta.StatusCode}: {json}");
        return json;
    }

    private static JsonElement Unico(JsonElement lote) => Assert.Single(lote.GetProperty("resultados").EnumerateArray());

    private static string? Texto(JsonElement elemento, string nome) =>
        elemento.TryGetProperty(nome, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() : string.Empty;

    private Task<string?> StatusAsync(Guid entrega) =>
        Banco.ConsultarEscalarAsync("SELECT status FROM entregas WHERE id = @id", ("id", entrega));

    private async Task<CenarioDaSincronizacao> CenarioAsync(HttpClient http, bool iniciarRota)
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Supervisor, Perfil.Motorista, Perfil.Motorista);
        var supervisor = (await EntrarAsync(http, organizacao.Com(Perfil.Supervisor))).TokenDeAcesso;
        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);

        var primeira = await CriarAsync(http, supervisor, "/api/entregas", RoteirosDeEntrega.Corpo(clienteId, destinatarioId));
        var segunda = await CriarAsync(http, supervisor, "/api/entregas", RoteirosDeEntrega.Corpo(clienteId, destinatarioId));

        var motorista = await CriarAsync(http, supervisor, "/api/motoristas", RoteirosDeCadastro.Obter("motorista").Corpo());
        await OkAsync(http, HttpMethod.Put, $"/api/motoristas/{motorista}/conta", supervisor, new { usuarioId = organizacao.Com(Perfil.Motorista).Id });
        var tokenDoMotorista = (await EntrarAsync(http, organizacao.Com(Perfil.Motorista))).TokenDeAcesso;

        var veiculo = await CriarAsync(http, supervisor, "/api/veiculos", RoteirosDeCadastro.Obter("veiculo").Corpo());
        var rota = await CriarAsync(http, supervisor, "/api/rotas", new { data = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1)) });
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/paradas", supervisor, new { entregas = new[] { primeira, segunda } });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/motorista", supervisor, new { motoristaId = motorista });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/veiculo", supervisor, new { veiculoId = veiculo });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/saida", supervisor, new { saidaPlanejada = RoteirosDeEntrega.Amanha(8) });
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/planejamento", supervisor);

        if (iniciarRota)
        {
            await OkAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{rota}/inicio", tokenDoMotorista);
        }

        return new CenarioDaSincronizacao(organizacao, supervisor, tokenDoMotorista, rota, primeira, segunda);
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
