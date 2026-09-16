using System.Net;
using System.Text.Json;
using TorreLogistica.Api.TempoReal;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// Motor de alertas contra a API, o processador em segundo plano e o PostgreSQL + PostGIS reais, com o
/// relógio da API sob controle do teste.
/// </summary>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class AlertasTestes(ContainerPostgis banco) : TesteDePrevisao(banco)
{
    protected override IReadOnlyDictionary<string, string?> ConfiguracaoAdicional => new Dictionary<string, string?>(base.ConfiguracaoAdicional)
    {
        ["Torre:Alertas:TempoSemPosicaoParaOffline"] = "00:10:00",
        ["Torre:Alertas:TempoParadoParaAlerta"] = "00:15:00",
        ["Torre:Alertas:TempoParadoAtendendoParadaParaAlerta"] = "00:30:00",
        ["Torre:Alertas:RaioDeImobilidadeEmMetros"] = "100",
        ["Torre:Alertas:LimiteDeTentativas"] = "1",
        ["Torre:Alertas:JanelaDeReabertura"] = "00:30:00",
    };

    /// <summary>Critério de aceite da Fase 10.</summary>
    [Fact]
    public async Task OperadorIdentificaEntregaProblematicaSemAnalisarOsGps()
    {
        using var http = Cliente();
        var inicio = Tempo.GetUtcNow();

        // Duas entregas na mesma rota; só a primeira tem janela curta demais para o trajeto.
        var cenario = await CenarioAsync(http,
        [
            new DestinoDeTeste(0, inicio.AddMinutes(10), inicio.AddMinutes(30)),
            new DestinoDeTeste(1_000, inicio.AddMinutes(10), inicio.AddHours(4)),
        ]);
        var (problematica, tranquila) = (cenario.Entregas[0], cenario.Entregas[1]);
        await using var console = await ConectarAoConsoleAsync(cenario.Operador, EventosDeTempoReal.AlertaCriado);

        // Motorista a 20 km: chega à primeira 3 min depois do fim da janela.
        await EnviarPosicaoAsync(http, cenario, distanciaAoSulEmMetros: 20_000, sequencia: 1);

        var abertos = await EsperarAsync(http, cenario.Operador, "/api/alertas?estado=Aberto", pagina => pagina.GetProperty("total").GetInt32() > 0);
        var alerta = Assert.Single(abertos.GetProperty("itens").EnumerateArray());

        Assert.Equal("RiscoDeAtraso", alerta.GetProperty("tipo").GetString());
        Assert.Equal("Media", alerta.GetProperty("severidade").GetString());
        Assert.Equal(problematica, alerta.GetProperty("entregaId").GetGuid());
        Assert.Equal(
            (await OkAsync(http, HttpMethod.Get, $"/api/entregas/{problematica}", cenario.Operador)).GetProperty("codigo").GetString(),
            alerta.GetProperty("codigoDaEntrega").GetString());
        Assert.StartsWith(
            "Entrega em risco de atraso. Risco porque a chegada prevista fica 3 min depois do fim da janela prometida. "
            + "A chegada prevista soma 33 min de deslocamento (20,0 km, pelo provedor de rotas simulado) e nenhuma parada antes desta.",
            alerta.GetProperty("descricao").GetString(),
            StringComparison.Ordinal);

        var evidencia = alerta.GetProperty("evidencia");
        Assert.Equal(-200, evidencia.GetProperty("folgaEmSegundos").GetInt32());
        Assert.Equal("ChegadaPrevistaDepoisDaJanela", evidencia.GetProperty("motivo").GetString());

        // A evidência é suficiente sem expor o GPS: nenhuma coordenada no alerta.
        Assert.DoesNotContain("latitude", alerta.GetRawText(), StringComparison.OrdinalIgnoreCase);

        using (var daTranquila = await EnviarAsync(http, HttpMethod.Get, $"/api/alertas?estado=Aberto&entregaId={tranquila}", cenario.Operador))
        {
            Assert.Equal(0, (await JsonAsync(daTranquila)).GetProperty("total").GetInt32());
        }

        var aviso = await console.EsperarAsync(EventosDeTempoReal.AlertaCriado, carga => carga.GetProperty("entregaId").GetGuid() == problematica);
        Assert.Equal("RiscoDeAtraso", aviso.GetProperty("tipo").GetString());
        Assert.Equal(alerta.GetProperty("id").GetGuid(), aviso.GetProperty("alertaId").GetGuid());
        Assert.False(aviso.GetProperty("reaberto").GetBoolean());
    }

    /// <summary>R11: o mesmo risco não cria alerta nem evento novo a cada avaliação.</summary>
    [Fact]
    public async Task MesmaCondicaoNaoDuplicaAlertaACadaAvaliacao()
    {
        using var http = Cliente();
        var inicio = Tempo.GetUtcNow();
        var cenario = await CenarioAsync(http, inicio.AddMinutes(10), inicio.AddMinutes(30), 0);
        var entrega = cenario.Entregas[0];

        await EnviarPosicaoAsync(http, cenario, distanciaAoSulEmMetros: 20_000, sequencia: 1);
        var alerta = await EsperarAlertaAbertoAsync(http, cenario.Operador, "RiscoDeAtraso", entrega);
        var id = alerta.GetProperty("id").GetGuid();

        for (var rodada = 1; rodada <= 3; rodada++)
        {
            Tempo.Advance(TimeSpan.FromMinutes(1));
            var agora = Tempo.GetUtcNow();
            await EnviarPosicaoAsync(http, cenario, distanciaAoSulEmMetros: 20_000, sequencia: 1 + rodada);

            await EsperarAsync(
                http,
                cenario.Operador,
                $"/api/alertas/{id}",
                detalhe => detalhe.GetProperty("alerta").GetProperty("ultimaConstatacaoEm").GetDateTimeOffset() >= agora.AddMilliseconds(-1),
                EsperaDaReavaliacao);
        }

        Assert.Equal("1", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM alertas_operacionais WHERE entrega_id = @id AND tipo = 'RiscoDeAtraso'", ("id", entrega)));
        Assert.Equal("1", await Banco.ConsultarEscalarAsync("SELECT count(*) FROM eventos_de_alerta WHERE alerta_id = @id", ("id", id)));
    }

    /// <summary>Abre, resolve quando a posição volta, reabre dentro da janela e vira alerta novo depois dela.</summary>
    [Fact]
    public async Task MotoristaOfflineAbreResolveReabreEViraAlertaNovoDepoisDaJanela()
    {
        using var http = Cliente();
        var inicio = Tempo.GetUtcNow();
        var cenario = await CenarioAsync(http, inicio.AddMinutes(10), inicio.AddHours(6), 0);
        await EnviarPosicaoAsync(http, cenario, distanciaAoSulEmMetros: 10_000, sequencia: 1);

        // Onze minutos sem posição: offline.
        Tempo.Advance(TimeSpan.FromMinutes(11));
        var operador = await EntrarComoOperadorAsync(http, cenario);
        var offline = await EsperarAlertaAbertoAsync(http, operador, "MotoristaOffline", motorista: cenario.Motorista, espera: EsperaDaReavaliacao);
        var id = offline.GetProperty("id").GetGuid();
        Assert.Equal("Alta", offline.GetProperty("severidade").GetString());
        Assert.Equal(11, offline.GetProperty("evidencia").GetProperty("minutosSemPosicao").GetInt32());
        Assert.Equal("Motorista sem enviar posição há 11 min (limite 10 min), com 1 entrega(s) pendente(s).", offline.GetProperty("descricao").GetString());

        // A posição volta: resolve sozinho.
        await EnviarPosicaoAsync(http, cenario, distanciaAoSulEmMetros: 9_000, sequencia: 2);
        await EsperarEstadoAsync(http, operador, id, "Resolvido");

        // Some de novo dentro da janela de reabertura: o mesmo alerta reabre.
        Tempo.Advance(TimeSpan.FromMinutes(11));
        operador = await EntrarComoOperadorAsync(http, cenario);
        var reaberto = await EsperarEstadoAsync(http, operador, id, "Aberto", EsperaDaReavaliacao);
        Assert.Equal(1, reaberto.GetProperty("alerta").GetProperty("reaberturas").GetInt32());

        await EnviarPosicaoAsync(http, cenario, distanciaAoSulEmMetros: 8_000, sequencia: 3);
        var resolvido = await EsperarEstadoAsync(http, operador, id, "Resolvido");
        Assert.Equal("Automatica", resolvido.GetProperty("alerta").GetProperty("formaDeResolucao").GetString());
        Assert.Equal(
            ["Aberto", "Resolvido", "Reaberto", "Resolvido"],
            resolvido.GetProperty("eventos").EnumerateArray().Select(evento => evento.GetProperty("tipo").GetString()));

        // Depois da janela de reabertura, o mesmo problema é um alerta novo.
        Tempo.Advance(TimeSpan.FromMinutes(45));
        operador = await EntrarComoOperadorAsync(http, cenario);
        var novo = await EsperarAlertaAbertoAsync(http, operador, "MotoristaOffline", motorista: cenario.Motorista, espera: EsperaDaReavaliacao);
        Assert.NotEqual(id, novo.GetProperty("id").GetGuid());
        Assert.Equal(0, novo.GetProperty("reaberturas").GetInt32());
    }

    [Fact]
    public async Task OperadorResolveEAlertaNaoReabreEnquantoACondicaoPersiste()
    {
        using var http = Cliente();
        var inicio = Tempo.GetUtcNow();
        var cenario = await CenarioAsync(http, inicio.AddMinutes(10), inicio.AddMinutes(30), 0);
        var entrega = cenario.Entregas[0];

        await EnviarPosicaoAsync(http, cenario, distanciaAoSulEmMetros: 20_000, sequencia: 1);
        var id = (await EsperarAlertaAbertoAsync(http, cenario.Operador, "RiscoDeAtraso", entrega)).GetProperty("id").GetGuid();

        var resolvido = await OkAsync(http, HttpMethod.Post, $"/api/alertas/{id}/resolucao", cenario.Operador, new { observacao = "Cliente avisado do atraso." });
        Assert.Equal("Resolvido", resolvido.GetProperty("alerta").GetProperty("estado").GetString());
        Assert.Equal("PeloOperador", resolvido.GetProperty("alerta").GetProperty("formaDeResolucao").GetString());
        Assert.Equal(cenario.Organizacao.Com(Perfil.Operador).Id, resolvido.GetProperty("alerta").GetProperty("resolvidoPorUsuarioId").GetGuid());
        var ultimoEvento = resolvido.GetProperty("eventos").EnumerateArray().Last();
        Assert.Equal("ResolvidoPeloOperador", ultimoEvento.GetProperty("tipo").GetString());
        Assert.Equal("Cliente avisado do atraso.", ultimoEvento.GetProperty("observacao").GetString());

        // A condição continua valendo na avaliação seguinte: a decisão do operador não é desfeita.
        Tempo.Advance(TimeSpan.FromMinutes(1));
        var agora = Tempo.GetUtcNow();
        await EnviarPosicaoAsync(http, cenario, distanciaAoSulEmMetros: 20_000, sequencia: 2);
        var depois = await EsperarAsync(
            http,
            cenario.Operador,
            $"/api/alertas/{id}",
            detalhe => detalhe.GetProperty("alerta").GetProperty("ultimaConstatacaoEm").GetDateTimeOffset() >= agora.AddMilliseconds(-1));
        Assert.Equal("Resolvido", depois.GetProperty("alerta").GetProperty("estado").GetString());
        Assert.True(depois.GetProperty("alerta").GetProperty("condicaoAtiva").GetBoolean());

        // Repetir a resolução não gera evento: continuam só a abertura e a resolução pelo operador.
        var repetido = await OkAsync(http, HttpMethod.Post, $"/api/alertas/{id}/resolucao", cenario.Operador, new { observacao = "De novo." });
        Assert.Equal(2, repetido.GetProperty("eventos").GetArrayLength());
        Assert.Equal("1", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM alertas_operacionais WHERE entrega_id = @id AND tipo = 'RiscoDeAtraso'", ("id", entrega)));
    }

    [Fact]
    public async Task TentativasExcedidasAbreEResolveQuandoAEntregaEhCancelada()
    {
        using var http = Cliente();
        var inicio = Tempo.GetUtcNow();
        var cenario = await CenarioAsync(http, inicio.AddMinutes(10), inicio.AddHours(6), 0);
        var entrega = cenario.Entregas[0];

        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{entrega}/tentativa-frustrada", cenario.TokenDoMotorista, new { motivo = "DestinatarioAusente" });

        var alerta = await EsperarAlertaAbertoAsync(http, cenario.Operador, "TentativasExcedidas", entrega);
        Assert.Equal("Alta", alerta.GetProperty("severidade").GetString());
        Assert.Equal("1 tentativa(s) sem sucesso (limite 1); último motivo: DestinatarioAusente.", alerta.GetProperty("descricao").GetString());

        // Cancelar não dispara recálculo; a reavaliação periódica percebe que a entrega saiu da operação.
        await OkAsync(http, HttpMethod.Post, $"/api/entregas/{entrega}/cancelamento", cenario.Operador, new { motivo = "SolicitacaoDoCliente" });
        Tempo.Advance(TimeSpan.FromMinutes(1));
        var operador = await EntrarComoOperadorAsync(http, cenario);

        var resolvido = await EsperarEstadoAsync(http, operador, alerta.GetProperty("id").GetGuid(), "Resolvido", EsperaDaReavaliacao);
        Assert.Equal("Automatica", resolvido.GetProperty("alerta").GetProperty("formaDeResolucao").GetString());
        Assert.Equal("Cancelada", resolvido.GetProperty("alerta").GetProperty("evidencia").GetProperty("statusDaEntrega").GetString());
    }

    /// <summary>Fase 13: a ocorrência crítica do motorista vira alerta crítico na torre.</summary>
    [Fact]
    public async Task OcorrenciaCriticaAbreAlertaEResolveQuandoAEntregaSaiDaOperacao()
    {
        using var http = Cliente();
        var inicio = Tempo.GetUtcNow();
        var cenario = await CenarioAsync(http, inicio.AddMinutes(10), inicio.AddHours(6), 0);
        var entrega = cenario.Entregas[0];

        using (var registro = await EnviarAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{entrega}/ocorrencia", cenario.TokenDoMotorista, new
        {
            tipo = "ProblemaComMercadoria",
            observacao = "Caixa violada, produto exposto.",
        }))
        {
            Assert.Equal(HttpStatusCode.Created, registro.StatusCode);
        }

        // Ocorrência não dispara recálculo: quem percebe é a reavaliação periódica.
        Tempo.Advance(TimeSpan.FromMinutes(1));
        var operador = await EntrarComoOperadorAsync(http, cenario);
        var alerta = await EsperarAlertaAbertoAsync(http, operador, "OcorrenciaCritica", entrega, espera: EsperaDaReavaliacao);

        Assert.Equal("Critica", alerta.GetProperty("severidade").GetString());
        Assert.Equal(
            "Ocorrência crítica registrada: problema com a mercadoria. Há descrição registrada na ocorrência.",
            alerta.GetProperty("descricao").GetString());
        Assert.DoesNotContain("violada", alerta.GetRawText(), StringComparison.OrdinalIgnoreCase);

        // A entrega sai da operação pelo caminho que a máquina de estados permite a partir de EmRota:
        // a conclusão. O alerta resolve sozinho, porque a condição deixou de valer.
        var tokenDoMotorista = (await EntrarAsync(http, cenario.Organizacao.Com(Perfil.Motorista))).TokenDeAcesso;
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{entrega}/conclusao", tokenDoMotorista);
        Tempo.Advance(TimeSpan.FromMinutes(1));
        operador = await EntrarComoOperadorAsync(http, cenario);

        var resolvido = await EsperarEstadoAsync(http, operador, alerta.GetProperty("id").GetGuid(), "Resolvido", EsperaDaReavaliacao);
        Assert.Equal("Automatica", resolvido.GetProperty("alerta").GetProperty("formaDeResolucao").GetString());
        Assert.Equal("Entregue", resolvido.GetProperty("alerta").GetProperty("evidencia").GetProperty("statusDaEntrega").GetString());
    }

    /// <summary>Parado fora do destino: a permanência sai do histórico de posições, pelo PostGIS.</summary>
    [Fact]
    public async Task MotoristaParadoAbreAlertaPelaPermanenciaCalculadaNoPostgis()
    {
        using var http = Cliente();
        var inicio = Tempo.GetUtcNow();
        var cenario = await CenarioAsync(http, inicio.AddMinutes(10), inicio.AddHours(6), 0);

        await EnviarPosicaoAsync(http, cenario, distanciaAoSulEmMetros: 10_000, sequencia: 1);
        Tempo.Advance(TimeSpan.FromMinutes(8));
        await EnviarPosicaoAsync(http, cenario, distanciaAoSulEmMetros: 10_000, sequencia: 2);
        Tempo.Advance(TimeSpan.FromMinutes(8));
        var operador = await EntrarComoOperadorAsync(http, cenario);

        // Cinquenta metros adiante: ainda dentro do raio de 100 m das posições anteriores.
        await EnviarPosicaoAsync(http, cenario, distanciaAoSulEmMetros: 10_050, sequencia: 3);

        var parado = await EsperarAlertaAbertoAsync(http, operador, "ParadoTempoExcessivo", motorista: cenario.Motorista);
        var evidencia = parado.GetProperty("evidencia");
        Assert.Equal(16, evidencia.GetProperty("minutosParado").GetInt32());
        Assert.Equal(3, evidencia.GetProperty("posicoesNoLocal").GetInt32());
        Assert.False(evidencia.GetProperty("atendendoParada").GetBoolean());
        Assert.Equal("Motorista parado há 16 min num raio de 100 m (limite 15 min).", parado.GetProperty("descricao").GetString());

        // Andou 600 m: a permanência recomeça, e o alerta resolve.
        Tempo.Advance(TimeSpan.FromMinutes(1));
        await EnviarPosicaoAsync(http, cenario, distanciaAoSulEmMetros: 9_400, sequencia: 4);
        await EsperarEstadoAsync(http, operador, parado.GetProperty("id").GetGuid(), "Resolvido");
    }

    [Fact]
    public async Task AlertaDeOutraOrganizacaoNaoApareceNemPodeSerResolvido()
    {
        using var http = Cliente();
        var inicio = Tempo.GetUtcNow();
        var organizacaoA = await CenarioAsync(http, inicio.AddMinutes(10), inicio.AddMinutes(30), 0);
        var organizacaoB = await CenarioAsync(http, inicio.AddMinutes(10), inicio.AddHours(6), 0);

        await EnviarPosicaoAsync(http, organizacaoA, distanciaAoSulEmMetros: 20_000, sequencia: 1);
        var id = (await EsperarAlertaAbertoAsync(http, organizacaoA.Operador, "RiscoDeAtraso", organizacaoA.Entregas[0])).GetProperty("id").GetGuid();

        var listaDeB = await OkAsync(http, HttpMethod.Get, "/api/alertas", organizacaoB.Operador);
        Assert.DoesNotContain(listaDeB.GetProperty("itens").EnumerateArray(), item => item.GetProperty("id").GetGuid() == id);

        using var deOutra = await EnviarAsync(http, HttpMethod.Get, $"/api/alertas/{id}", organizacaoB.Operador);
        using var inventado = await EnviarAsync(http, HttpMethod.Get, $"/api/alertas/{Guid.CreateVersion7()}", organizacaoB.Operador);
        Assert.Equal(HttpStatusCode.NotFound, deOutra.StatusCode);
        Assert.Equal(await AssinaturaAsync(inventado), await AssinaturaAsync(deOutra));

        using var resolucao = await EnviarAsync(http, HttpMethod.Post, $"/api/alertas/{id}/resolucao", organizacaoB.Operador, new { observacao = "Não é meu." });
        Assert.Equal(HttpStatusCode.NotFound, resolucao.StatusCode);
        Assert.Equal("Aberto", (await OkAsync(http, HttpMethod.Get, $"/api/alertas/{id}", organizacaoA.Operador)).GetProperty("alerta").GetProperty("estado").GetString());
    }

    [Theory]
    [InlineData("UPDATE eventos_de_alerta SET tipo = 'Resolvido' WHERE alerta_id IN (SELECT id FROM alertas_operacionais WHERE entrega_id = @id)")]
    [InlineData("DELETE FROM eventos_de_alerta WHERE alerta_id IN (SELECT id FROM alertas_operacionais WHERE entrega_id = @id)")]
    [InlineData("TRUNCATE eventos_de_alerta")]
    public async Task CicloDeVidaDosAlertasEhSomenteInsercao(string sql)
    {
        using var http = Cliente();
        var inicio = Tempo.GetUtcNow();
        var cenario = await CenarioAsync(http, inicio.AddMinutes(10), inicio.AddMinutes(30), 0);
        await EnviarPosicaoAsync(http, cenario, distanciaAoSulEmMetros: 20_000, sequencia: 1);
        await EsperarAlertaAbertoAsync(http, cenario.Operador, "RiscoDeAtraso", cenario.Entregas[0]);

        var erro = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => Banco.ExecutarAsync(sql, ("id", cenario.Entregas[0])));

        Assert.Equal(Npgsql.PostgresErrorCodes.InsufficientPrivilege, erro.SqlState);
    }

    private static async Task<string> EntrarComoOperadorAsync(HttpClient http, CenarioDePrevisao cenario) =>
        (await EntrarAsync(http, cenario.Organizacao.Com(Perfil.Operador))).TokenDeAcesso;

    private static async Task<JsonElement> EsperarAlertaAbertoAsync(
        HttpClient http,
        string token,
        string tipo,
        Guid? entrega = null,
        Guid? motorista = null,
        TimeSpan? espera = null)
    {
        var filtro = entrega is { } entregaId ? $"&entregaId={entregaId}" : $"&motoristaId={motorista}";
        var pagina = await EsperarAsync(
            http,
            token,
            $"/api/alertas?estado=Aberto&tipo={tipo}{filtro}",
            resultado => resultado.GetProperty("total").GetInt32() > 0,
            espera);

        return Assert.Single(pagina.GetProperty("itens").EnumerateArray());
    }

    private static Task<JsonElement> EsperarEstadoAsync(HttpClient http, string token, Guid alerta, string estado, TimeSpan? espera = null) =>
        EsperarAsync(
            http,
            token,
            $"/api/alertas/{alerta}",
            detalhe => detalhe.GetProperty("alerta").GetProperty("estado").GetString() == estado,
            espera);
}
