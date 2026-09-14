using System.Globalization;
using System.Net;
using System.Text.Json;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// Geofence do destino contra PostgreSQL + PostGIS real. Os pontos de teste são calculados pelo
/// próprio PostGIS (<c>ST_Project</c>) a distâncias exatas do destino — nada de aproximação em graus.
/// </summary>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class GeofenceTestes(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    private const double LatitudeDoDestino = -22.9100;
    private const double LongitudeDoDestino = -47.0650;

    /// <summary>Critério de aceite: dentro, fora e borda com geometria real (CLAUDE.md, seção 66).</summary>
    [Fact]
    public async Task ForaDentroEBordaComGeometriaReal()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var inicio = DateTimeOffset.UtcNow.AddMinutes(-5);

        await EnviarAsync(http, cenario, await PontoAsync(2_000, 1, inicio.AddSeconds(10)));
        var aTrezentosEUm = await EnviarAsync(http, cenario, await PontoAsync(301, 2, inicio.AddSeconds(20)));
        Assert.True(Unico(aTrezentosEUm).GetProperty("atualizouPosicaoAtual").GetBoolean());

        var fora = await GeofenceAsync(http, cenario.Supervisor, cenario.Entrega);
        Assert.False(fora.GetProperty("dentro").GetBoolean());
        Assert.Equal(301, fora.GetProperty("distanciaEmMetros").GetDouble(), 0);
        Assert.Empty(fora.GetProperty("eventos").EnumerateArray());
        Assert.Equal("EmRota", await StatusAsync(http, cenario.Supervisor, cenario.Entrega));

        await EnviarAsync(http, cenario, await PontoAsync(299, 3, inicio.AddSeconds(30)));

        var dentro = await GeofenceAsync(http, cenario.Supervisor, cenario.Entrega);
        Assert.True(dentro.GetProperty("disponivel").GetBoolean());
        Assert.True(dentro.GetProperty("dentro").GetBoolean());
        Assert.Equal(299, dentro.GetProperty("distanciaEmMetros").GetDouble(), 0);
        Assert.Equal(300, dentro.GetProperty("raioEmMetros").GetDouble());
        var entrada = Assert.Single(dentro.GetProperty("eventos").EnumerateArray());
        Assert.Equal("Entrada", entrada.GetProperty("tipo").GetString());

        Assert.Equal("ProximaDoDestino", await StatusAsync(http, cenario.Supervisor, cenario.Entrega));
        var tipos = await TiposDaTimelineAsync(http, cenario.Supervisor, cenario.Entrega);
        Assert.Equal("ProximidadeDetectada", tipos[^1]);

        // O motorista toca "cheguei" depois: repetição sem efeito, sem segunda transição.
        using var chegada = await EnviarAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/chegada", cenario.TokenDoMotorista);
        Assert.Equal(HttpStatusCode.OK, chegada.StatusCode);
        Assert.Equal(tipos.Count, (await TiposDaTimelineAsync(http, cenario.Supervisor, cenario.Entrega)).Count);

        // A distância veio do PostGIS: conferir contra o próprio banco.
        Assert.Equal("t", await Banco.ConsultarEscalarAsync(
            """
            SELECT (ST_DWithin(e.localizacao, p.localizacao, 300) AND NOT ST_DWithin(e.localizacao, ST_Project(e.localizacao, 301, 0), 300))::text
            FROM entregas e JOIN posicoes_atuais p ON p.motorista_id = e.motorista_id
            WHERE e.id = @id
            """,
            ("id", cenario.Entrega)) is "true" or "t" ? "t" : "f");
    }

    [Fact]
    public async Task PermanecerDentroEPosicaoDuplicadaNaoRepetemEntrada()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var inicio = DateTimeOffset.UtcNow.AddMinutes(-5);

        await EnviarAsync(http, cenario, await PontoAsync(900, 1, inicio.AddSeconds(5)));
        var primeiraDentro = await PontoAsync(200, 2, inicio.AddSeconds(10));
        await EnviarAsync(http, cenario, primeiraDentro);

        // Trinta posições dentro, uma delas reenviada duas vezes.
        var dentro = new List<object>();
        for (var indice = 0; indice < 30; indice++)
        {
            dentro.Add(await PontoAsync(150 - indice, 3 + indice, inicio.AddSeconds(15 + indice)));
        }

        await EnviarAsync(http, cenario, [.. dentro]);
        await EnviarAsync(http, cenario, primeiraDentro, dentro[5]);

        var geofence = await GeofenceAsync(http, cenario.Supervisor, cenario.Entrega);
        Assert.Single(geofence.GetProperty("eventos").EnumerateArray());
        Assert.Equal(1, geofence.GetProperty("entradas").GetInt32());
        Assert.Equal("1", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM eventos_da_entrega WHERE entrega_id = @id AND tipo = 'ProximidadeDetectada'", ("id", cenario.Entrega)));
    }

    [Fact]
    public async Task SaidaComMargemEReentrada()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var inicio = DateTimeOffset.UtcNow.AddMinutes(-5);

        await EnviarAsync(
            http,
            cenario,
            await PontoAsync(100, 1, inicio.AddSeconds(10)),
            await PontoAsync(330, 2, inicio.AddSeconds(20)),   // entre o raio e a margem: continua dentro
            await PontoAsync(420, 3, inicio.AddSeconds(30)),   // além da margem: saída
            await PontoAsync(800, 4, inicio.AddSeconds(40)),
            await PontoAsync(250, 5, inicio.AddSeconds(50)));  // reentrada

        var geofence = await GeofenceAsync(http, cenario.Supervisor, cenario.Entrega);
        Assert.Equal(
            ["Entrada", "Saida", "Entrada"],
            geofence.GetProperty("eventos").EnumerateArray().Select(evento => evento.GetProperty("tipo").GetString()));
        Assert.Equal(2, geofence.GetProperty("entradas").GetInt32());
        Assert.True(geofence.GetProperty("dentro").GetBoolean());

        // A entrega transiciona uma vez só; a reentrada não gera nova proximidade.
        Assert.Equal("ProximaDoDestino", await StatusAsync(http, cenario.Supervisor, cenario.Entrega));
        Assert.Equal("1", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM eventos_da_entrega WHERE entrega_id = @id AND tipo = 'ProximidadeDetectada'", ("id", cenario.Entrega)));
    }

    /// <summary>GPS antigo não produz transição retroativa — em envios separados e dentro do mesmo lote.</summary>
    [Fact]
    public async Task PosicaoForaDeOrdemNaoProduzTransicaoRetroativa()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var inicio = DateTimeOffset.UtcNow.AddMinutes(-5);

        await EnviarAsync(http, cenario, await PontoAsync(1_500, 30, inicio.AddSeconds(30)));

        // Chega depois uma leitura antiga, dentro do raio: histórico sim, transição não.
        var antiga = await EnviarAsync(http, cenario, await PontoAsync(50, 10, inicio.AddSeconds(10)));
        Assert.True(Unico(antiga).GetProperty("foraDeOrdem").GetBoolean());

        // No mesmo lote: a mais recente fora vem antes, a antiga dentro vem depois.
        await EnviarAsync(
            http,
            cenario,
            await PontoAsync(1_200, 40, inicio.AddSeconds(40)),
            await PontoAsync(80, 35, inicio.AddSeconds(35)));

        var geofence = await GeofenceAsync(http, cenario.Supervisor, cenario.Entrega);
        Assert.False(geofence.GetProperty("dentro").GetBoolean());
        Assert.Empty(geofence.GetProperty("eventos").EnumerateArray());
        Assert.Equal("EmRota", await StatusAsync(http, cenario.Supervisor, cenario.Entrega));
        Assert.Equal("4", await Banco.ConsultarEscalarAsync("SELECT count(*) FROM posicoes WHERE motorista_id = @id", ("id", cenario.Motorista)));
    }

    [Fact]
    public async Task GeofenceDeOutraOrganizacaoNaoEhAfetadaNemVisivel()
    {
        using var http = Cliente();
        var organizacaoA = await CenarioAsync(http);
        var organizacaoB = await CenarioAsync(http);
        var inicio = DateTimeOffset.UtcNow.AddMinutes(-5);

        // Os dois destinos estão no mesmo ponto. Só o motorista de A envia posição, dentro do raio.
        await EnviarAsync(http, organizacaoA, await PontoAsync(900, 1, inicio.AddSeconds(5)), await PontoAsync(40, 2, inicio.AddSeconds(10)));

        Assert.Equal("ProximaDoDestino", await StatusAsync(http, organizacaoA.Supervisor, organizacaoA.Entrega));
        Assert.Equal("EmRota", await StatusAsync(http, organizacaoB.Supervisor, organizacaoB.Entrega));
        Assert.Equal("0", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM estados_de_geofence WHERE entrega_id = @id", ("id", organizacaoB.Entrega)));

        using var deOutra = await EnviarAsync(http, HttpMethod.Get, $"/api/entregas/{organizacaoA.Entrega}/geofence", organizacaoB.Supervisor);
        using var inventada = await EnviarAsync(http, HttpMethod.Get, $"/api/entregas/{Guid.CreateVersion7()}/geofence", organizacaoB.Supervisor);
        Assert.Equal(HttpStatusCode.NotFound, deOutra.StatusCode);
        Assert.Equal(await AssinaturaAsync(inventada), await AssinaturaAsync(deOutra));
    }

    [Fact]
    public async Task EntregaConcluidaOuSemCoordenadaNaoEhAvaliada()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http, destinoComCoordenada: false);
        var inicio = DateTimeOffset.UtcNow.AddMinutes(-5);

        var semCoordenada = await GeofenceAsync(http, cenario.Supervisor, cenario.Entrega);
        Assert.False(semCoordenada.GetProperty("disponivel").GetBoolean());

        await EnviarAsync(http, cenario, await PontoAsync(10, 1, inicio.AddSeconds(5)));
        Assert.Equal("EmRota", await StatusAsync(http, cenario.Supervisor, cenario.Entrega));

        var comCoordenada = await CenarioAsync(http);
        using var conclusao = await EnviarAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{comCoordenada.Entrega}/conclusao", comCoordenada.TokenDoMotorista);
        Assert.Equal(HttpStatusCode.OK, conclusao.StatusCode);
        await EnviarAsync(http, comCoordenada, await PontoAsync(900, 1, inicio.AddSeconds(5)), await PontoAsync(10, 2, inicio.AddSeconds(10)));

        var concluida = await GeofenceAsync(http, comCoordenada.Supervisor, comCoordenada.Entrega);
        Assert.Empty(concluida.GetProperty("eventos").EnumerateArray());
        Assert.Equal("Entregue", await StatusAsync(http, comCoordenada.Supervisor, comCoordenada.Entrega));
    }

    /// <summary>
    /// Chegada tocada pelo motorista e entrada detectada pelo GPS ao mesmo tempo: as duas requisições
    /// terminam bem, e a entrega transiciona uma única vez.
    /// </summary>
    [Fact]
    public async Task ChegadaManualEEntradaNaGeofenceSimultaneasTransicionamUmaVez()
    {
        using var http = Cliente();

        for (var rodada = 0; rodada < 4; rodada++)
        {
            var cenario = await CenarioAsync(http);
            var inicio = DateTimeOffset.UtcNow.AddMinutes(-5);
            await EnviarAsync(http, cenario, await PontoAsync(1_000, 1, inicio.AddSeconds(5)));
            var dentro = await PontoAsync(60, 2, inicio.AddSeconds(10));

            var chegada = EnviarAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/chegada", cenario.TokenDoMotorista);
            var posicao = EnviarAsync(http, HttpMethod.Post, "/api/motorista/posicoes", cenario.TokenDoMotorista, new { posicoes = new[] { dentro } });
            using var respostaDaChegada = await chegada;
            using var respostaDaPosicao = await posicao;

            Assert.Equal(HttpStatusCode.OK, respostaDaPosicao.StatusCode);
            Assert.True(
                respostaDaChegada.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
                $"rodada {rodada}: chegada {(int)respostaDaChegada.StatusCode}");

            Assert.Equal("ProximaDoDestino", await StatusAsync(http, cenario.Supervisor, cenario.Entrega));
            Assert.Equal("1", await Banco.ConsultarEscalarAsync(
                "SELECT count(*) FROM eventos_da_entrega WHERE entrega_id = @id AND status_resultante = 'ProximaDoDestino'",
                ("id", cenario.Entrega)));
            Assert.Equal("1", await Banco.ConsultarEscalarAsync(
                "SELECT count(*) FROM eventos_de_geofence WHERE entrega_id = @id AND tipo = 'Entrada'", ("id", cenario.Entrega)));
        }
    }

    [Theory]
    [InlineData("UPDATE eventos_de_geofence SET distancia_em_metros = 0 WHERE entrega_id = @id")]
    [InlineData("DELETE FROM eventos_de_geofence WHERE entrega_id = @id")]
    [InlineData("TRUNCATE eventos_de_geofence")]
    public async Task EventosDeGeofenceSaoSomenteInsercao(string sql)
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var inicio = DateTimeOffset.UtcNow.AddMinutes(-5);
        await EnviarAsync(http, cenario, await PontoAsync(900, 1, inicio.AddSeconds(5)), await PontoAsync(40, 2, inicio.AddSeconds(10)));

        var erro = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => Banco.ExecutarAsync(sql, ("id", cenario.Entrega)));

        Assert.Equal(Npgsql.PostgresErrorCodes.InsufficientPrivilege, erro.SqlState);
    }

    private sealed record CenarioDeGeofence(string Supervisor, Guid Motorista, string TokenDoMotorista, Guid Entrega);

    /// <summary>Organização com uma entrega em rota, destino fixo, e o motorista dela autenticado.</summary>
    private async Task<CenarioDeGeofence> CenarioAsync(HttpClient http, bool destinoComCoordenada = true)
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Supervisor, Perfil.Motorista);
        var supervisor = (await EntrarAsync(http, organizacao.Com(Perfil.Supervisor))).TokenDeAcesso;

        var clienteId = await CriarAsync(http, supervisor, "/api/clientes", RoteirosDeCadastro.Obter("cliente").Corpo());
        var destinatarioId = await CriarAsync(http, supervisor, "/api/destinatarios", new
        {
            nome = "Carla Nunes",
            endereco = RoteirosDeCadastro.Endereco(),
            latitude = destinoComCoordenada ? LatitudeDoDestino : (double?)null,
            longitude = destinoComCoordenada ? LongitudeDoDestino : (double?)null,
        });

        var motorista = await CriarAsync(http, supervisor, "/api/motoristas", RoteirosDeCadastro.Obter("motorista").Corpo());
        await OkAsync(http, HttpMethod.Put, $"/api/motoristas/{motorista}/conta", supervisor, new { usuarioId = organizacao.Com(Perfil.Motorista).Id });
        var tokenDoMotorista = (await EntrarAsync(http, organizacao.Com(Perfil.Motorista))).TokenDeAcesso;

        var entrega = await CriarAsync(http, supervisor, "/api/entregas", RoteirosDeEntrega.Corpo(clienteId, destinatarioId));
        var veiculo = await CriarAsync(http, supervisor, "/api/veiculos", RoteirosDeCadastro.Obter("veiculo").Corpo());
        var rota = await CriarAsync(http, supervisor, "/api/rotas", new { data = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1)) });
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/paradas", supervisor, new { entregas = new[] { entrega } });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/motorista", supervisor, new { motoristaId = motorista });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/veiculo", supervisor, new { veiculoId = veiculo });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/saida", supervisor, new { saidaPlanejada = RoteirosDeEntrega.Amanha(8) });
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/planejamento", supervisor);
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{rota}/inicio", tokenDoMotorista);

        return new CenarioDeGeofence(supervisor, motorista, tokenDoMotorista, entrega);
    }

    /// <summary>Posição a uma distância exata do destino, ao norte, calculada pelo PostGIS.</summary>
    private async Task<object> PontoAsync(double distanciaEmMetros, long sequencia, DateTimeOffset capturadaEm)
    {
        var ponto = await Banco.ConsultarEscalarAsync(
            """
            SELECT ST_Y(p::geometry)::text || ';' || ST_X(p::geometry)::text
            FROM (SELECT ST_Project(ST_SetSRID(ST_MakePoint(@lon, @lat), 4326)::geography, @distancia, 0) AS p) projetado
            """,
            ("lat", LatitudeDoDestino),
            ("lon", LongitudeDoDestino),
            ("distancia", distanciaEmMetros));

        var partes = ponto!.Split(';');
        return new
        {
            eventoDeLocalizacaoId = Guid.CreateVersion7(),
            latitude = double.Parse(partes[0], CultureInfo.InvariantCulture),
            longitude = double.Parse(partes[1], CultureInfo.InvariantCulture),
            precisaoEmMetros = 5,
            capturadaEm,
            sequencia,
        };
    }

    private static async Task<JsonElement> EnviarAsync(HttpClient http, CenarioDeGeofence cenario, params object[] posicoes)
    {
        using var resposta = await EnviarAsync(http, HttpMethod.Post, "/api/motorista/posicoes", cenario.TokenDoMotorista, new { posicoes });
        var json = await JsonAsync(resposta);
        Assert.True(resposta.StatusCode == HttpStatusCode.OK, $"{(int)resposta.StatusCode}: {json}");
        Assert.Equal(0, json.GetProperty("rejeitadas").GetInt32());
        return json;
    }

    private static JsonElement Unico(JsonElement lote) => Assert.Single(lote.GetProperty("resultados").EnumerateArray());

    private static Task<JsonElement> GeofenceAsync(HttpClient http, string token, Guid entrega) =>
        OkAsync(http, HttpMethod.Get, $"/api/entregas/{entrega}/geofence", token);

    private static async Task<string?> StatusAsync(HttpClient http, string token, Guid entrega) =>
        (await OkAsync(http, HttpMethod.Get, $"/api/entregas/{entrega}", token)).GetProperty("status").GetString();

    private static async Task<List<string?>> TiposDaTimelineAsync(HttpClient http, string token, Guid entrega)
    {
        using var resposta = await EnviarAsync(http, HttpMethod.Get, $"/api/entregas/{entrega}/eventos", token);
        return [.. (await JsonAsync(resposta)).EnumerateArray().Select(evento => evento.GetProperty("tipo").GetString())];
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
}
