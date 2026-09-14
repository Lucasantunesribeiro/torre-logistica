using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using TorreLogistica.Application.Rastreamento;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// Ingestão de localização contra PostgreSQL + PostGIS real: histórico somente-inserção, posição
/// atual que nunca regride, duplicatas, lote parcial, relógio do aparelho e contexto do motorista.
/// </summary>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class PosicoesTestes(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    /// <summary>Teste obrigatório do ROADMAP e regressão permanente (CLAUDE.md, seção 67).</summary>
    [Fact]
    public async Task EventoForaDeOrdemEntraNoHistoricoMasNaoRegressaAPosicaoAtual()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var base_ = DateTimeOffset.UtcNow.AddMinutes(-5);

        var r101 = await EnviarAsync(http, cenario.TokenDoMotoristaUm, Posicao(101, base_.AddMinutes(1), latitude: -22.9010));
        var r103 = await EnviarAsync(http, cenario.TokenDoMotoristaUm, Posicao(103, base_.AddMinutes(3), latitude: -22.9030));
        var r102 = await EnviarAsync(http, cenario.TokenDoMotoristaUm, Posicao(102, base_.AddMinutes(2), latitude: -22.9020));

        Assert.True(Unico(r101).GetProperty("atualizouPosicaoAtual").GetBoolean());
        Assert.True(Unico(r103).GetProperty("atualizouPosicaoAtual").GetBoolean());
        var foraDeOrdem = Unico(r102);
        Assert.Equal("Aceita", foraDeOrdem.GetProperty("resultado").GetString());
        Assert.True(foraDeOrdem.GetProperty("foraDeOrdem").GetBoolean());
        Assert.False(foraDeOrdem.GetProperty("atualizouPosicaoAtual").GetBoolean());

        await AssertHistoricoEAtualAsync(http, cenario, base_, [101, 102, 103], 103, -22.9030);
    }

    [Fact]
    public async Task MesmoCenarioNumLoteUnicoTemOMesmoResultado()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var base_ = DateTimeOffset.UtcNow.AddMinutes(-5);

        var lote = await EnviarAsync(
            http,
            cenario.TokenDoMotoristaUm,
            Posicao(101, base_.AddMinutes(1), latitude: -22.9010),
            Posicao(103, base_.AddMinutes(3), latitude: -22.9030),
            Posicao(102, base_.AddMinutes(2), latitude: -22.9020));

        Assert.Equal(3, lote.GetProperty("aceitas").GetInt32());
        Assert.Equal([false, false, true], Resultados(lote).Select(item => item.GetProperty("foraDeOrdem").GetBoolean()));

        await AssertHistoricoEAtualAsync(http, cenario, base_, [101, 102, 103], 103, -22.9030);
    }

    [Fact]
    public async Task DuplicataNaoDuplicaHistorico()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var base_ = DateTimeOffset.UtcNow.AddMinutes(-5);
        var primeira = Posicao(1, base_.AddMinutes(1));
        var segunda = Posicao(2, base_.AddMinutes(2));

        await EnviarAsync(http, cenario.TokenDoMotoristaUm, primeira);
        var reenvio = await EnviarAsync(http, cenario.TokenDoMotoristaUm, primeira);
        Assert.Equal("Duplicada", Unico(reenvio).GetProperty("resultado").GetString());

        var repetidaNoLote = await EnviarAsync(http, cenario.TokenDoMotoristaUm, segunda, segunda);
        Assert.Equal(["Aceita", "Duplicada"], Resultados(repetidaNoLote).Select(item => item.GetProperty("resultado").GetString()));

        // A resposta se perdeu e o aplicativo reenvia o lote inteiro.
        var loteReenviado = await EnviarAsync(http, cenario.TokenDoMotoristaUm, primeira, segunda);
        Assert.Equal(2, loteReenviado.GetProperty("duplicadas").GetInt32());
        Assert.Equal(0, loteReenviado.GetProperty("aceitas").GetInt32());

        Assert.Equal("2", await ContarPosicoesAsync(cenario.MotoristaUm));
    }

    [Fact]
    public async Task LoteParcialGravaOsValidosERelataCadaRecusa()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var agora = DateTimeOffset.UtcNow;
        var base_ = agora.AddMinutes(-5);

        var lote = await EnviarAsync(
            http,
            cenario.TokenDoMotoristaUm,
            Posicao(1, base_.AddMinutes(1), latitude: -22.9011),
            Posicao(2, base_.AddMinutes(2), latitude: -22.9022, precisao: 500),
            Posicao(3, base_.AddMinutes(3), latitude: 91),
            Posicao(4, base_.AddMinutes(3), latitude: 0, longitude: 0),
            Posicao(5, base_.AddMinutes(3), precisao: 0),
            Posicao(6, base_.AddMinutes(3), precisao: 5_000),
            Posicao(7, agora.AddMinutes(10)),
            Posicao(8, agora.AddDays(-8)),
            Posicao(9, agora.AddHours(-2)),
            new { eventoDeLocalizacaoId = Guid.CreateVersion7(), latitude = -22.9, longitude = -47.06, precisaoEmMetros = 8, capturadaEm = base_ },
            Posicao(11, base_.AddMinutes(3), velocidade: -1),
            Posicao(12, base_.AddMinutes(3), direcao: 360));

        Assert.Equal(12, lote.GetProperty("recebidas").GetInt32());
        Assert.Equal(2, lote.GetProperty("aceitas").GetInt32());
        Assert.Equal(10, lote.GetProperty("rejeitadas").GetInt32());
        Assert.Equal(
            [null, null, "coordenada_invalida", "coordenada_invalida", "precisao_invalida", "precisao_invalida", "capturada_no_futuro",
                "capturada_antiga_demais", "fora_de_rota", "campo_obrigatorio", "velocidade_invalida", "direcao_invalida"],
            Resultados(lote).Select(item => item.TryGetProperty("motivo", out var motivo) && motivo.ValueKind == JsonValueKind.String ? motivo.GetString() : null));

        var imprecisa = Resultados(lote)[1];
        Assert.Equal("Imprecisa", imprecisa.GetProperty("qualidade").GetString());
        Assert.False(imprecisa.GetProperty("atualizouPosicaoAtual").GetBoolean());

        Assert.Equal("2", await ContarPosicoesAsync(cenario.MotoristaUm));

        // Imprecisa é mais recente, mas não move a posição atual.
        var atual = await OkAsync(http, HttpMethod.Get, $"/api/motoristas/{cenario.MotoristaUm}/posicao-atual", cenario.Operador);
        Assert.Equal(1, atual.GetProperty("sequencia").GetInt64());
    }

    [Fact]
    public async Task PosicaoAntigaNaoRegridePosicaoAtualEACapturaDecideMesmoComSequenciaReiniciada()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var base_ = DateTimeOffset.UtcNow.AddMinutes(-10);

        await EnviarAsync(http, cenario.TokenDoMotoristaUm, Posicao(500, base_.AddMinutes(4)));

        // Aplicativo reinstalado: a sequência recomeça, mas a captura é mais recente.
        var reinicio = await EnviarAsync(http, cenario.TokenDoMotoristaUm, Posicao(3, base_.AddMinutes(5), latitude: -22.9555));
        Assert.True(Unico(reinicio).GetProperty("atualizouPosicaoAtual").GetBoolean());

        // Posição antiga chegando tarde, com sequência alta: histórico sim, posição atual não.
        var antiga = await EnviarAsync(http, cenario.TokenDoMotoristaUm, Posicao(900, base_.AddMinutes(1)));
        Assert.True(Unico(antiga).GetProperty("foraDeOrdem").GetBoolean());

        var atual = await OkAsync(http, HttpMethod.Get, $"/api/motoristas/{cenario.MotoristaUm}/posicao-atual", cenario.Supervisor);
        Assert.Equal(3, atual.GetProperty("sequencia").GetInt64());
        Assert.Equal(-22.9555, atual.GetProperty("latitude").GetDouble(), 6);
        Assert.Equal("3", await ContarPosicoesAsync(cenario.MotoristaUm));
    }

    [Fact]
    public async Task TimestampFuturoSoEhAceitoDentroDaToleranciaDoRelogio()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var agora = DateTimeOffset.UtcNow;

        var lote = await EnviarAsync(http, cenario.TokenDoMotoristaUm, Posicao(1, agora.AddMinutes(1)), Posicao(2, agora.AddMinutes(5)));

        Assert.Equal(["Aceita", "Rejeitada"], Resultados(lote).Select(item => item.GetProperty("resultado").GetString()));
        Assert.Equal("capturada_no_futuro", Resultados(lote)[1].GetProperty("motivo").GetString());
    }

    [Fact]
    public async Task MotoristaSoEnviaParaOProprioContexto()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var base_ = DateTimeOffset.UtcNow.AddMinutes(-5);

        // Motorista no corpo não é aceito: o motorista vem da sessão.
        using var comMotorista = await EnviarAsync(http, HttpMethod.Post, "/api/motorista/posicoes", cenario.TokenDoMotoristaUm, new StringContent(
            $$"""{"posicoes":[{"eventoDeLocalizacaoId":"{{Guid.CreateVersion7()}}","motoristaId":"{{cenario.MotoristaDois}}","latitude":-22.9,"longitude":-47.06,"precisaoEmMetros":8,"capturadaEm":"{{base_.ToString("O", CultureInfo.InvariantCulture)}}","sequencia":1}]}""",
            System.Text.Encoding.UTF8,
            "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, comMotorista.StatusCode);

        // Motorista dois tem rota planejada, não iniciada: nada é coletado.
        var semRotaIniciada = await EnviarAsync(http, cenario.TokenDoMotoristaDois, Posicao(1, base_.AddMinutes(1)));
        Assert.Equal("fora_de_rota", Unico(semRotaIniciada).GetProperty("motivo").GetString());

        await OkAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{cenario.RotaDoMotoristaDois}/inicio", cenario.TokenDoMotoristaDois);
        var agoraSim = await EnviarAsync(http, cenario.TokenDoMotoristaDois, Posicao(2, DateTimeOffset.UtcNow));
        Assert.Equal("Aceita", Unico(agoraSim).GetProperty("resultado").GetString());

        // A posição do motorista dois não aparece no motorista um.
        using var atualDoUm = await EnviarAsync(http, HttpMethod.Get, $"/api/motoristas/{cenario.MotoristaUm}/posicao-atual", cenario.Supervisor);
        Assert.Equal(HttpStatusCode.NotFound, atualDoUm.StatusCode);
        Assert.Equal("posicao_nao_encontrada", await CodigoDoErroAsync(atualDoUm));
        Assert.Equal("0", await ContarPosicoesAsync(cenario.MotoristaUm));

        // Conta de motorista sem cadastro.
        using var semCadastro = await EnviarAsync(http, HttpMethod.Post, "/api/motorista/posicoes", cenario.TokenSemCadastro, Corpo(Posicao(1, base_)));
        Assert.Equal(HttpStatusCode.NotFound, semCadastro.StatusCode);
        Assert.Equal("motorista_nao_associado", await CodigoDoErroAsync(semCadastro));

        // Canais separados.
        using var consoleEnviando = await EnviarAsync(http, HttpMethod.Post, "/api/motorista/posicoes", cenario.Supervisor, Corpo(Posicao(1, base_)));
        using var motoristaConsultando = await EnviarAsync(http, HttpMethod.Get, $"/api/motoristas/{cenario.MotoristaDois}/posicao-atual", cenario.TokenDoMotoristaDois);
        Assert.Equal(HttpStatusCode.Unauthorized, consoleEnviando.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, motoristaConsultando.StatusCode);

        // Outra organização não enxerga a posição.
        var outra = await CenarioAsync(http);
        using var deOutra = await EnviarAsync(http, HttpMethod.Get, $"/api/motoristas/{cenario.MotoristaDois}/posicao-atual", outra.Supervisor);
        using var inventado = await EnviarAsync(http, HttpMethod.Get, $"/api/motoristas/{Guid.CreateVersion7()}/posicao-atual", outra.Supervisor);
        Assert.Equal(HttpStatusCode.NotFound, deOutra.StatusCode);
        Assert.Equal(await AssinaturaAsync(inventado), await AssinaturaAsync(deOutra));
    }

    [Fact]
    public async Task LotesSimultaneosDoMesmoMotoristaNaoRegridemAPosicaoAtualNemDuplicam()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var base_ = DateTimeOffset.UtcNow.AddMinutes(-5);
        var aleatorio = new Random(42);
        var todas = Enumerable.Range(1, 100)
            .Select(sequencia => (Sequencia: sequencia, Corpo: Posicao(sequencia, base_.AddSeconds(sequencia * 2), latitude: -22.9 - (sequencia / 10_000.0))))
            .OrderBy(_ => aleatorio.Next())
            .ToList();

        var lotes = todas.Chunk(20).Select(lote => lote.Select(item => item.Corpo).ToArray()).ToList();
        lotes.Add([.. todas.OrderBy(_ => aleatorio.Next()).Take(20).Select(item => item.Corpo)]);

        var respostas = await Task.WhenAll(lotes.Select(lote => EnviarAsync(http, cenario.TokenDoMotoristaUm, lote)));

        Assert.Equal(100, respostas.Sum(resposta => resposta.GetProperty("aceitas").GetInt32()));
        Assert.Equal(20, respostas.Sum(resposta => resposta.GetProperty("duplicadas").GetInt32()));
        Assert.Equal("100", await ContarPosicoesAsync(cenario.MotoristaUm));

        var atual = await OkAsync(http, HttpMethod.Get, $"/api/motoristas/{cenario.MotoristaUm}/posicao-atual", cenario.Supervisor);
        Assert.Equal(100, atual.GetProperty("sequencia").GetInt64());
    }

    /// <summary>
    /// Critério de aceite: telemetria realista, sem mapa. Envio a cada 15 segundos, perda de sinal,
    /// lote de recuperação embaralhado com reenvios e posições imprecisas — e as métricas contando.
    /// </summary>
    [Fact]
    public async Task RecebeTelemetriaRealistaComRecuperacaoOfflineEMetricas()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        using var medicao = new MedicaoDeRastreamento();
        var base_ = DateTimeOffset.UtcNow.AddMinutes(-14);
        var imprecisas = new HashSet<int> { 5, 20, 33, 47 };

        object Leitura(int indice) => Posicao(
            indice,
            base_.AddSeconds(indice * 15),
            latitude: -22.9056 - (indice * 0.0011),
            longitude: -47.0608 + (indice * 0.0004),
            precisao: imprecisas.Contains(indice) ? 150 : 6 + (indice % 5),
            velocidade: 8.3,
            direcao: 160);

        var leituras = Enumerable.Range(0, 52).Select(Leitura).ToList();

        // Online: 12 envios, um a cada 15 s.
        foreach (var leitura in leituras.Take(12))
        {
            await EnviarAsync(http, cenario.TokenDoMotoristaUm, leitura);
        }

        // Sem sinal por 10 minutos; ao reconectar, o aplicativo manda o acumulado fora de ordem e reenvia
        // cinco que não teve certeza de ter enviado.
        var recuperacao = leituras.Skip(12).OrderBy(_ => Random.Shared.Next()).Concat(leituras.Take(5)).ToArray();
        var lote = await EnviarAsync(http, cenario.TokenDoMotoristaUm, recuperacao);

        Assert.Equal(40, lote.GetProperty("aceitas").GetInt32());
        Assert.Equal(5, lote.GetProperty("duplicadas").GetInt32());
        Assert.Equal(0, lote.GetProperty("rejeitadas").GetInt32());
        var foraDeOrdem = Resultados(lote).Count(item => item.GetProperty("foraDeOrdem").GetBoolean());

        var atual = await OkAsync(http, HttpMethod.Get, $"/api/motoristas/{cenario.MotoristaUm}/posicao-atual", cenario.Operador);
        Assert.Equal(51, atual.GetProperty("sequencia").GetInt64());
        Assert.Equal(cenario.RotaDoMotoristaUm, atual.GetProperty("rotaId").GetGuid());

        var historico = await HistoricoAsync(http, cenario, base_.AddMinutes(-1), base_.AddMinutes(15));
        Assert.False(historico.GetProperty("truncado").GetBoolean());
        Assert.Equal(Enumerable.Range(0, 52), historico.GetProperty("posicoes").EnumerateArray().Select(item => (int)item.GetProperty("sequencia").GetInt64()));
        Assert.Equal(4, historico.GetProperty("posicoes").EnumerateArray().Count(item => item.GetProperty("qualidade").GetString() == "Imprecisa"));

        Assert.Equal(57, medicao.Soma("tracking.positions.received"));
        Assert.Equal(5, medicao.Soma("tracking.positions.duplicate"));
        Assert.Equal(4, medicao.Soma("tracking.positions.inaccurate"));
        Assert.Equal(foraDeOrdem, medicao.Soma("tracking.positions.out_of_order"));
        Assert.Equal(52, medicao.Soma("tracking.ingestion.lag"));

        // Nenhuma coordenada vai para log.
        var logs = Fabrica.Logs.TextoCompleto();
        Assert.Contains("posições do motorista", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("-22.9", logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HistoricoExigePeriodoLimitadoEPerfilDeGestao()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var agora = DateTimeOffset.UtcNow;
        string Instante(DateTimeOffset instante) => Uri.EscapeDataString(instante.ToString("O", CultureInfo.InvariantCulture));
        var url = $"/api/motoristas/{cenario.MotoristaUm}/posicoes";

        foreach (var invalida in new[]
        {
            string.Empty,
            $"?de={Instante(agora)}",
            $"?de={Instante(agora)}&ate={Instante(agora)}",
            $"?de={Instante(agora.AddHours(-25))}&ate={Instante(agora)}",
        })
        {
            using var resposta = await EnviarAsync(http, HttpMethod.Get, url + invalida, cenario.Supervisor);
            Assert.True(resposta.StatusCode == HttpStatusCode.BadRequest, $"{invalida}: {(int)resposta.StatusCode}");
        }

        var valida = $"?de={Instante(agora.AddHours(-1))}&ate={Instante(agora)}";
        using var operador = await EnviarAsync(http, HttpMethod.Get, url + valida, cenario.Operador);
        using var supervisor = await EnviarAsync(http, HttpMethod.Get, url + valida, cenario.Supervisor);
        Assert.Equal(HttpStatusCode.Forbidden, operador.StatusCode);
        Assert.Equal(HttpStatusCode.OK, supervisor.StatusCode);
    }

    [Fact]
    public async Task LoteVazioOuGrandeDemaisEhRecusado()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var agora = DateTimeOffset.UtcNow;

        using var vazio = await EnviarAsync(http, HttpMethod.Post, "/api/motorista/posicoes", cenario.TokenDoMotoristaUm, new { posicoes = Array.Empty<object>() });
        using var grande = await EnviarAsync(
            http, HttpMethod.Post, "/api/motorista/posicoes", cenario.TokenDoMotoristaUm, Corpo([.. Enumerable.Range(0, 501).Select(indice => Posicao(indice, agora))]));

        Assert.Equal(HttpStatusCode.BadRequest, vazio.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, grande.StatusCode);
        Assert.Equal("0", await ContarPosicoesAsync(cenario.MotoristaUm));
    }

    /// <summary>
    /// O que foi capturado não é reescrito: <c>UPDATE</c> é recusado pelo banco. <c>DELETE</c> segue
    /// permitido de propósito, para a limpeza por retenção.
    /// </summary>
    [Fact]
    public async Task HistoricoDePosicoesRecusaAlteracaoMasPermiteLimpeza()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        await EnviarAsync(http, cenario.TokenDoMotoristaUm, Posicao(1, DateTimeOffset.UtcNow.AddMinutes(-1)));

        var erro = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => Banco.ExecutarAsync(
            "UPDATE posicoes SET precisao_em_metros = 1 WHERE motorista_id = @id", ("id", cenario.MotoristaUm)));
        Assert.Equal(Npgsql.PostgresErrorCodes.InsufficientPrivilege, erro.SqlState);

        Assert.Equal(1, await Banco.ExecutarAsync("DELETE FROM posicoes WHERE motorista_id = @id", ("id", cenario.MotoristaUm)));
    }

    internal sealed record CenarioDeRastreamento(
        string Supervisor,
        string Operador,
        Guid MotoristaUm,
        string TokenDoMotoristaUm,
        Guid RotaDoMotoristaUm,
        Guid MotoristaDois,
        string TokenDoMotoristaDois,
        Guid RotaDoMotoristaDois,
        string TokenSemCadastro);

    /// <summary>
    /// Organização com motorista um em rota iniciada, motorista dois com rota só planejada e uma
    /// conta de motorista sem cadastro.
    /// </summary>
    internal async Task<CenarioDeRastreamento> CenarioAsync(HttpClient http)
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Supervisor, Perfil.Operador, Perfil.Motorista, Perfil.Motorista, Perfil.Motorista);
        var supervisor = (await EntrarAsync(http, organizacao.Com(Perfil.Supervisor))).TokenDeAcesso;
        var operador = (await EntrarAsync(http, organizacao.Com(Perfil.Operador))).TokenDeAcesso;
        var contas = organizacao.TodasCom(Perfil.Motorista);
        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);

        var motoristaUm = await MotoristaComContaAsync(http, supervisor, contas[0]);
        var motoristaDois = await MotoristaComContaAsync(http, supervisor, contas[1]);
        var tokenUm = (await EntrarAsync(http, contas[0])).TokenDeAcesso;
        var tokenDois = (await EntrarAsync(http, contas[1])).TokenDeAcesso;

        var rotaUm = await RotaPlanejadaAsync(http, supervisor, operador, clienteId, destinatarioId, motoristaUm);
        var rotaDois = await RotaPlanejadaAsync(http, supervisor, operador, clienteId, destinatarioId, motoristaDois);
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{rotaUm}/inicio", tokenUm);

        return new CenarioDeRastreamento(
            supervisor, operador, motoristaUm, tokenUm, rotaUm, motoristaDois, tokenDois, rotaDois,
            (await EntrarAsync(http, contas[2])).TokenDeAcesso);
    }

    internal static object Posicao(
        long sequencia,
        DateTimeOffset capturadaEm,
        double latitude = -22.9056,
        double longitude = -47.0608,
        double precisao = 8,
        double? velocidade = null,
        double? direcao = null) => new
        {
            eventoDeLocalizacaoId = Guid.CreateVersion7(),
            latitude,
            longitude,
            precisaoEmMetros = precisao,
            capturadaEm,
            sequencia,
            velocidadeEmMetrosPorSegundo = velocidade,
            direcaoEmGraus = direcao,
        };

    internal static object Corpo(params object[] posicoes) => new { posicoes };

    internal static async Task<JsonElement> EnviarAsync(HttpClient http, string token, params object[] posicoes)
    {
        using var resposta = await EnviarAsync(http, HttpMethod.Post, "/api/motorista/posicoes", token, Corpo(posicoes));
        var json = await JsonAsync(resposta);
        Assert.True(resposta.StatusCode == HttpStatusCode.OK, $"{(int)resposta.StatusCode}: {json}");
        return json;
    }

    private async Task AssertHistoricoEAtualAsync(
        HttpClient http,
        CenarioDeRastreamento cenario,
        DateTimeOffset base_,
        long[] sequenciasEsperadas,
        long sequenciaAtual,
        double latitudeAtual)
    {
        var historico = await HistoricoAsync(http, cenario, base_, base_.AddMinutes(10));
        Assert.Equal(sequenciasEsperadas, historico.GetProperty("posicoes").EnumerateArray().Select(item => item.GetProperty("sequencia").GetInt64()));

        var atual = await OkAsync(http, HttpMethod.Get, $"/api/motoristas/{cenario.MotoristaUm}/posicao-atual", cenario.Operador);
        Assert.Equal(sequenciaAtual, atual.GetProperty("sequencia").GetInt64());
        Assert.Equal(latitudeAtual, atual.GetProperty("latitude").GetDouble(), 6);

        // Ponto real no PostGIS, não número solto.
        Assert.Equal("ST_Point|4326", await Banco.ConsultarEscalarAsync(
            "SELECT ST_GeometryType(localizacao::geometry) || '|' || ST_SRID(localizacao::geometry) FROM posicoes_atuais WHERE motorista_id = @id",
            ("id", cenario.MotoristaUm)));
    }

    private static async Task<JsonElement> HistoricoAsync(HttpClient http, CenarioDeRastreamento cenario, DateTimeOffset de, DateTimeOffset ate) =>
        await OkAsync(
            http,
            HttpMethod.Get,
            $"/api/motoristas/{cenario.MotoristaUm}/posicoes?de={Uri.EscapeDataString(de.ToString("O", CultureInfo.InvariantCulture))}&ate={Uri.EscapeDataString(ate.ToString("O", CultureInfo.InvariantCulture))}",
            cenario.Supervisor);

    private Task<string?> ContarPosicoesAsync(Guid motoristaId) =>
        Banco.ConsultarEscalarAsync("SELECT count(*) FROM posicoes WHERE motorista_id = @id", ("id", motoristaId));

    private static List<JsonElement> Resultados(JsonElement lote) => [.. lote.GetProperty("resultados").EnumerateArray()];

    private static JsonElement Unico(JsonElement lote) => Assert.Single(Resultados(lote));

    private static async Task<Guid> MotoristaComContaAsync(HttpClient http, string supervisor, ContaDeTeste conta)
    {
        var id = await CriarAsync(http, supervisor, "/api/motoristas", RoteirosDeCadastro.Obter("motorista").Corpo());
        await OkAsync(http, HttpMethod.Put, $"/api/motoristas/{id}/conta", supervisor, new { usuarioId = conta.Id });
        return id;
    }

    private static async Task<Guid> RotaPlanejadaAsync(HttpClient http, string supervisor, string operador, Guid clienteId, Guid destinatarioId, Guid motoristaId)
    {
        var data = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1));
        var entrega = await CriarAsync(http, operador, "/api/entregas", RoteirosDeEntrega.Corpo(clienteId, destinatarioId));
        var veiculo = await CriarAsync(http, supervisor, "/api/veiculos", RoteirosDeCadastro.Obter("veiculo").Corpo());
        var rota = await CriarAsync(http, supervisor, "/api/rotas", new { data });
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/paradas", supervisor, new { entregas = new[] { entrega } });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/motorista", supervisor, new { motoristaId });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/veiculo", supervisor, new { veiculoId = veiculo });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/saida", supervisor, new { saidaPlanejada = RoteirosDeEntrega.Amanha(8) });
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/planejamento", supervisor);
        return rota;
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

    private static async Task<string> AssinaturaAsync(HttpResponseMessage resposta)
    {
        var json = await JsonAsync(resposta);
        var campos = json.EnumerateObject()
            .Where(campo => campo.Name is not ("traceId" or "idDeCorrelacao" or "instance"))
            .Select(campo => $"{campo.Name}={campo.Value.GetRawText()}");
        return $"{(int)resposta.StatusCode}|{string.Join("|", campos)}";
    }

    /// <summary>Ouve o medidor de rastreamento enquanto existir. Histogramas contam medições.</summary>
    private sealed class MedicaoDeRastreamento : IDisposable
    {
        private readonly MeterListener _ouvinte = new();
        private readonly ConcurrentDictionary<string, long> _somas = new(StringComparer.Ordinal);

        public MedicaoDeRastreamento()
        {
            _ouvinte.InstrumentPublished = (instrumento, ouvinte) =>
            {
                if (instrumento.Meter.Name == MetricasDeRastreamento.NomeDoMedidor)
                {
                    ouvinte.EnableMeasurementEvents(instrumento);
                }
            };
            _ouvinte.SetMeasurementEventCallback<long>((instrumento, valor, _, _) =>
                _somas.AddOrUpdate(instrumento.Name, valor, (_, atual) => atual + valor));
            _ouvinte.SetMeasurementEventCallback<double>((instrumento, _, _, _) =>
                _somas.AddOrUpdate(instrumento.Name, 1, (_, atual) => atual + 1));
            _ouvinte.Start();
        }

        public long Soma(string instrumento) => _somas.GetValueOrDefault(instrumento);

        public void Dispose() => _ouvinte.Dispose();
    }
}

/// <summary>Limite de envio de posições por motorista, e não por endereço.</summary>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class LimiteDeTelemetriaTestes(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    protected override IReadOnlyDictionary<string, string?> ConfiguracaoAdicional => new Dictionary<string, string?>
    {
        ["Torre:LimiteDeRequisicoes:EnviosDePosicaoPorMinuto"] = "2",
    };

    [Fact]
    public async Task EnvioAlemDoLimiteDevolve429SemBloquearOutroMotoristaDoMesmoEndereco()
    {
        using var http = Cliente();
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Motorista, Perfil.Motorista);
        var contas = organizacao.TodasCom(Perfil.Motorista);
        var tokenUm = (await EntrarAsync(http, contas[0])).TokenDeAcesso;
        var tokenDois = (await EntrarAsync(http, contas[1])).TokenDeAcesso;
        var corpo = PosicoesTestes.Corpo(PosicoesTestes.Posicao(1, DateTimeOffset.UtcNow));

        var status = new List<HttpStatusCode>();
        for (var envio = 0; envio < 3; envio++)
        {
            using var resposta = await EnviarAsync(http, HttpMethod.Post, "/api/motorista/posicoes", tokenUm, corpo);
            status.Add(resposta.StatusCode);
        }

        // As contas não têm cadastro de motorista: os dois primeiros envios chegam ao caso de uso (404).
        Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests], status);

        using var outroMotorista = await EnviarAsync(http, HttpMethod.Post, "/api/motorista/posicoes", tokenDois, corpo);
        Assert.Equal(HttpStatusCode.NotFound, outroMotorista.StatusCode);
    }
}
