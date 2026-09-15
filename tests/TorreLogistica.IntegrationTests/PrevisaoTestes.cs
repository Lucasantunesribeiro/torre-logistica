using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using TorreLogistica.Api.TempoReal;
using TorreLogistica.Application.Abstracoes.Roteamento;
using TorreLogistica.Domain.Comum;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.Domain.Previsao;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// Previsão de chegada e SLA contra a API, o processador em segundo plano e o PostgreSQL + PostGIS reais.
/// </summary>
/// <remarks>
/// O relógio da API fica parado sob controle do teste: a chegada prevista é exata ao segundo, e o tempo só
/// passa quando o teste manda. Os destinos e a posição do motorista ficam sobre o mesmo meridiano, a
/// distâncias exatas calculadas pelo PostGIS — o deslocamento esperado é conta simples.
/// </remarks>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class PrevisaoTestes(ContainerPostgis banco) : TesteDePrevisao(banco)
{
    /// <summary>Critério de aceite da Fase 9.</summary>
    [Fact]
    public async Task ExplicaPorQueAEntregaPassouDeNormalParaRisco()
    {
        using var http = Cliente();
        var inicio = Tempo.GetUtcNow();
        var cenario = await CenarioAsync(http, inicio.AddMinutes(10), inicio.AddMinutes(120), 0);
        var entrega = cenario.Entregas[0];

        // Saída sem posição: há previsão, sem chegada prevista.
        var semPosicao = await EsperarPrevisaoAsync(http, cenario.Supervisor, entrega, previsao => previsao.GetProperty("disponivel").GetBoolean());
        Assert.Equal("SemPosicaoDoMotorista", semPosicao.GetProperty("motivoSemChegadaPrevista").GetString());
        Assert.Equal("Normal", semPosicao.GetProperty("situacao").GetString());

        // Motorista a 10 km: 1.000 s a 10 m/s, folga de 103 min.
        await EnviarPosicaoAsync(http, cenario, distanciaAoSulEmMetros: 10_000, sequencia: 1);
        var normal = await EsperarPrevisaoAsync(http, cenario.Supervisor, entrega, TemChegadaPrevista);
        Assert.Equal("Normal", normal.GetProperty("situacao").GetString());
        Assert.Equal(Truncado(inicio).AddSeconds(1_000), normal.GetProperty("chegadaPrevistaEm").GetDateTimeOffset());
        Assert.Equal(1_000, normal.GetProperty("composicao").GetProperty("deslocamentoEmSegundos").GetInt32());
        Assert.Equal(10_000, normal.GetProperty("composicao").GetProperty("distanciaEmMetros").GetDouble(), 0);
        Assert.Equal("Provedor", normal.GetProperty("composicao").GetProperty("fonte").GetString());
        Assert.Equal("simulado", normal.GetProperty("composicao").GetProperty("provedor").GetString());
        var historicoAntes = normal.GetProperty("historico").EnumerateArray().Select(registro => registro.GetRawText()).ToList();

        // Cem minutos sem posição nova: a chegada prevista escorrega com o relógio, e a folga cai para 200 s.
        // Quem percebe é a reavaliação periódica — nenhum evento aconteceu.
        Tempo.Advance(TimeSpan.FromMinutes(100));
        var supervisor = (await EntrarAsync(http, cenario.Organizacao.Com(Perfil.Supervisor))).TokenDeAcesso;
        var risco = await EsperarPrevisaoAsync(http, supervisor, entrega, previsao => previsao.GetProperty("situacao").GetString() == "Risco", EsperaDaReavaliacao);

        Assert.Equal("FolgaAbaixoDoLimiarDeRisco", risco.GetProperty("motivo").GetString());
        Assert.Equal(200, risco.GetProperty("folgaEmSegundos").GetInt32());

        var mudanca = risco.GetProperty("historico").EnumerateArray().Last(registro => registro.GetProperty("tipo").GetString() == "SituacaoAlterada");
        Assert.Equal("Normal", mudanca.GetProperty("situacaoAnterior").GetString());
        Assert.Equal("Risco", mudanca.GetProperty("situacao").GetString());
        Assert.Equal(300, mudanca.GetProperty("criterios").GetProperty("limiarDeRiscoEmSegundos").GetInt32());
        Assert.Equal(
            "Passou de Normal para Risco porque a folga até o fim da janela prometida caiu para 3 min, abaixo do limiar de risco de 5 min. "
            + "A chegada prevista soma 17 min de deslocamento (10,0 km, pelo provedor de rotas simulado) e nenhuma parada antes desta. "
            + "A posição do motorista usada foi capturada 1 h 40 min antes do cálculo.",
            mudanca.GetProperty("explicacao").GetString());

        // O histórico anterior continua exatamente como era: a mudança acrescentou, não reescreveu.
        var historicoDepois = risco.GetProperty("historico").EnumerateArray().Select(registro => registro.GetRawText()).ToList();
        Assert.Equal(historicoAntes, historicoDepois.Take(historicoAntes.Count));

        // Fim da janela sem entrega: Atrasada.
        Tempo.Advance(TimeSpan.FromMinutes(25));
        supervisor = (await EntrarAsync(http, cenario.Organizacao.Com(Perfil.Supervisor))).TokenDeAcesso;
        var atrasada = await EsperarPrevisaoAsync(http, supervisor, entrega, previsao => previsao.GetProperty("situacao").GetString() == "Atrasada", EsperaDaReavaliacao);
        Assert.Equal("JanelaEncerrada", atrasada.GetProperty("motivo").GetString());
        Assert.StartsWith(
            "Passou de Risco para Atrasada porque a janela prometida terminou há 5 min",
            atrasada.GetProperty("historico").EnumerateArray().Last().GetProperty("explicacao").GetString(),
            StringComparison.Ordinal);
    }

    /// <summary>Duração restante, paradas anteriores, tempo por parada e estado operacional, compostos na chegada.</summary>
    [Fact]
    public async Task ChegadaPrevistaSomaDeslocamentoParadasAnterioresETempoPorParada()
    {
        using var http = Cliente();
        var inicio = Truncado(Tempo.GetUtcNow());
        var cenario = await CenarioAsync(http, inicio.AddMinutes(30), inicio.AddHours(4), 0, 6_000);
        var (primeira, segunda) = (cenario.Entregas[0], cenario.Entregas[1]);

        // Motorista a 3 km da primeira; a segunda fica 6 km depois dela.
        await EnviarPosicaoAsync(http, cenario, distanciaAoSulEmMetros: 3_000, sequencia: 1);

        var previsaoDaSegunda = await EsperarPrevisaoAsync(http, cenario.Supervisor, segunda, TemChegadaPrevista);
        var previsaoDaPrimeira = await EsperarPrevisaoAsync(http, cenario.Supervisor, primeira, TemChegadaPrevista);
        Assert.Equal(inicio.AddSeconds(300), previsaoDaPrimeira.GetProperty("chegadaPrevistaEm").GetDateTimeOffset());
        Assert.Equal(inicio.AddSeconds(300 + 300 + 600), previsaoDaSegunda.GetProperty("chegadaPrevistaEm").GetDateTimeOffset());
        AssertComposicao(previsaoDaSegunda, deslocamento: 900, paradasAntes: 1, tempoDasParadasAntes: 300);

        // O motorista chega à primeira: ela passa a "já no destino", e o atendimento dela ainda conta para a segunda.
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{primeira}/chegada", cenario.TokenDoMotorista);
        var noDestino = await EsperarPrevisaoAsync(
            http, cenario.Supervisor, primeira, previsao => previsao.GetProperty("composicao").GetProperty("jaNoDestino").GetBoolean());
        Assert.Equal(inicio, noDestino.GetProperty("chegadaPrevistaEm").GetDateTimeOffset());
        AssertComposicao(await PrevisaoAsync(http, cenario.Supervisor, segunda), deslocamento: 900, paradasAntes: 1, tempoDasParadasAntes: 300);

        // Primeira concluída: a previsão dela se encerra, e a segunda deixa de esperar o atendimento dela.
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{primeira}/conclusao", cenario.TokenDoMotorista);
        var encerrada = await EsperarPrevisaoAsync(http, cenario.Supervisor, primeira, previsao => !previsao.GetProperty("ativa").GetBoolean());
        var ultimo = encerrada.GetProperty("historico").EnumerateArray().Last();
        Assert.Equal("Encerrada", ultimo.GetProperty("tipo").GetString());
        Assert.Equal("Entregue", ultimo.GetProperty("statusDaEntrega").GetString());

        var adiantada = await EsperarPrevisaoAsync(
            http, cenario.Supervisor, segunda, previsao => previsao.GetProperty("composicao").GetProperty("paradasAntes").GetInt32() == 0);
        Assert.Equal(inicio.AddSeconds(900), adiantada.GetProperty("chegadaPrevistaEm").GetDateTimeOffset());
        Assert.Equal("ChegadaPrevistaAlterada", adiantada.GetProperty("historico").EnumerateArray().Last().GetProperty("tipo").GetString());
    }

    [Fact]
    public async Task MudancaDeRiscoChegaAoConsoleEmTempoReal()
    {
        using var http = Cliente();
        var inicio = Tempo.GetUtcNow();
        var cenario = await CenarioAsync(http, inicio.AddMinutes(1), inicio.AddMinutes(30), 0);
        var entrega = cenario.Entregas[0];
        await EsperarPrevisaoAsync(http, cenario.Supervisor, entrega, previsao => previsao.GetProperty("disponivel").GetBoolean());
        await using var console = await ConectarAoConsoleAsync(cenario.Supervisor, EventosDeTempoReal.RiscoDaEntregaAlterado);

        // A 30 km, a chegada prevista cai 20 min depois do fim da janela.
        await EnviarPosicaoAsync(http, cenario, distanciaAoSulEmMetros: 30_000, sequencia: 1);

        var aviso = await console.EsperarAsync(EventosDeTempoReal.RiscoDaEntregaAlterado, carga => carga.GetProperty("entregaId").GetGuid() == entrega);
        Assert.Equal("Normal", aviso.GetProperty("situacaoAnterior").GetString());
        Assert.Equal("Risco", aviso.GetProperty("situacao").GetString());
        Assert.Equal("ChegadaPrevistaDepoisDaJanela", aviso.GetProperty("motivo").GetString());
        Assert.Equal(-1_200, aviso.GetProperty("folgaEmSegundos").GetInt32());
        Assert.Equal(cenario.Organizacao.Id, aviso.GetProperty("organizacaoId").GetGuid());

        // A primeira previsão, Normal, não é mudança de risco.
        await Task.Delay(TimeSpan.FromSeconds(1), Cancelamento);
        Assert.Equal(1, console.Contar(EventosDeTempoReal.RiscoDaEntregaAlterado, carga => carga.GetProperty("entregaId").GetGuid() == entrega));
    }

    [Fact]
    public async Task PrevisaoDeOutraOrganizacaoNaoEhVisivel()
    {
        using var http = Cliente();
        var inicio = Tempo.GetUtcNow();
        var organizacaoA = await CenarioAsync(http, inicio.AddMinutes(10), inicio.AddHours(3), 0);
        var organizacaoB = await CenarioAsync(http, inicio.AddMinutes(10), inicio.AddHours(3), 0);

        await EsperarPrevisaoAsync(http, organizacaoA.Supervisor, organizacaoA.Entregas[0], previsao => previsao.GetProperty("disponivel").GetBoolean());
        await EsperarPrevisaoAsync(http, organizacaoB.Supervisor, organizacaoB.Entregas[0], previsao => previsao.GetProperty("disponivel").GetBoolean());

        using var deOutra = await EnviarAsync(http, HttpMethod.Get, $"/api/entregas/{organizacaoA.Entregas[0]}/previsao", organizacaoB.Supervisor);
        using var inventada = await EnviarAsync(http, HttpMethod.Get, $"/api/entregas/{Guid.CreateVersion7()}/previsao", organizacaoB.Supervisor);
        Assert.Equal(HttpStatusCode.NotFound, deOutra.StatusCode);
        Assert.Equal(await AssinaturaAsync(inventada), await AssinaturaAsync(deOutra));

        // Cada organização grava só a própria previsão, mesmo com recálculos da mesma instância em série.
        Assert.Equal(organizacaoA.Organizacao.Id.ToString(), await Banco.ConsultarEscalarAsync(
            "SELECT organizacao_id::text FROM previsoes_da_entrega WHERE entrega_id = @id", ("id", organizacaoA.Entregas[0])));
        Assert.Equal("0", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM registros_de_previsao WHERE entrega_id = @id AND organizacao_id <> @organizacao",
            ("id", organizacaoA.Entregas[0]),
            ("organizacao", organizacaoA.Organizacao.Id)));
    }

    [Theory]
    [InlineData("UPDATE registros_de_previsao SET situacao = 'Normal' WHERE entrega_id = @id")]
    [InlineData("DELETE FROM registros_de_previsao WHERE entrega_id = @id")]
    [InlineData("TRUNCATE registros_de_previsao")]
    public async Task HistoricoDePrevisoesEhSomenteInsercao(string sql)
    {
        using var http = Cliente();
        var inicio = Tempo.GetUtcNow();
        var cenario = await CenarioAsync(http, inicio.AddMinutes(10), inicio.AddHours(3), 0);
        await EsperarPrevisaoAsync(http, cenario.Supervisor, cenario.Entregas[0], previsao => previsao.GetProperty("historico").GetArrayLength() > 0);

        var erro = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => Banco.ExecutarAsync(sql, ("id", cenario.Entregas[0])));

        Assert.Equal(Npgsql.PostgresErrorCodes.InsufficientPrivilege, erro.SqlState);
    }

    private static void AssertComposicao(JsonElement previsao, int deslocamento, int paradasAntes, int tempoDasParadasAntes)
    {
        var composicao = previsao.GetProperty("composicao");
        Assert.Equal(deslocamento, composicao.GetProperty("deslocamentoEmSegundos").GetInt32());
        Assert.Equal(paradasAntes, composicao.GetProperty("paradasAntes").GetInt32());
        Assert.Equal(tempoDasParadasAntes, composicao.GetProperty("tempoDasParadasAntesEmSegundos").GetInt32());
        Assert.Equal(300, composicao.GetProperty("tempoPorParadaEmSegundos").GetInt32());
    }
}

/// <summary>Provedor de rotas que falha ou não responde: a previsão continua, pela contingência.</summary>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class ProvedorDeRotasIndisponivelTestes(ContainerPostgis banco) : TesteDePrevisao(banco)
{
    private readonly ProvedorDeRotasDeTeste _provedor = new();

    protected override IReadOnlyDictionary<string, string?> ConfiguracaoAdicional => new Dictionary<string, string?>(base.ConfiguracaoAdicional)
    {
        ["Torre:Previsao:Provedor"] = ProvedorDeRotasDeTeste.NomeDoProvedor,
        ["Torre:Previsao:TempoLimiteDoProvedor"] = "00:00:02",
    };

    protected override Action<IServiceCollection> ServicosDeTeste => servicos => servicos.AddScoped<IProvedorDeRotas>(_ => _provedor);

    [Fact]
    public async Task FalhaDoProvedorCaiNaContingenciaEmLinhaReta()
    {
        _provedor.Modo = ModoDoProvedor.Falhar;
        using var http = Cliente();
        var inicio = Tempo.GetUtcNow();
        var cenario = await CenarioAsync(http, inicio.AddMinutes(10), inicio.AddHours(3), 0);

        await EnviarPosicaoAsync(http, cenario, distanciaAoSulEmMetros: 10_000, sequencia: 1);

        var previsao = await EsperarPrevisaoAsync(http, cenario.Supervisor, cenario.Entregas[0], TemChegadaPrevista);
        AssertContingencia(previsao, "FalhaDoProvedor", "o provedor de rotas falhou");
        Assert.True(_provedor.Chamadas > 0);
    }

    /// <summary>
    /// O provedor trava: a ingestão de GPS continua respondendo enquanto ele espera, e a previsão sai pela
    /// contingência no tempo limite.
    /// </summary>
    [Fact]
    public async Task ProvedorQueNaoRespondeNaoSeguraAIngestaoECaiNaContingencia()
    {
        _provedor.Modo = ModoDoProvedor.NaoResponder;
        using var http = Cliente();
        var inicio = Tempo.GetUtcNow();
        var cenario = await CenarioAsync(http, inicio.AddMinutes(10), inicio.AddHours(3), 0);

        await EnviarPosicaoAsync(http, cenario, distanciaAoSulEmMetros: 10_000, sequencia: 1);
        await _provedor.EmEspera.Task.WaitAsync(EsperaDoProcessamento, Cancelamento);

        // Com o provedor preso, a ingestão responde normalmente.
        await EnviarPosicaoAsync(http, cenario, distanciaAoSulEmMetros: 10_000, sequencia: 2);
        Assert.False(_provedor.Liberado.Task.IsCompleted, "O provedor já tinha sido liberado: a ingestão pode ter esperado por ele.");

        var previsao = await EsperarPrevisaoAsync(http, cenario.Supervisor, cenario.Entregas[0], TemChegadaPrevista);
        AssertContingencia(previsao, "TempoLimite", "o provedor de rotas não respondeu no tempo limite");
        Assert.True(_provedor.Liberado.Task.IsCompleted);
    }

    private static void AssertContingencia(JsonElement previsao, string motivo, string trecho)
    {
        var composicao = previsao.GetProperty("composicao");
        Assert.Equal("Contingencia", composicao.GetProperty("fonte").GetString());
        Assert.Equal(motivo, composicao.GetProperty("motivoDaContingencia").GetString());
        Assert.Equal(JsonValueKind.Null, composicao.GetProperty("provedor").ValueKind);

        // Contingência do teste: linha reta a 5 m/s, sem sinuosidade.
        Assert.Equal(2_000, composicao.GetProperty("deslocamentoEmSegundos").GetInt32());
        Assert.Contains($"estimado em linha reta porque {trecho}", previsao.GetProperty("explicacao").GetString(), StringComparison.Ordinal);
    }

    private enum ModoDoProvedor
    {
        Falhar,
        NaoResponder,
    }

    private sealed class ProvedorDeRotasDeTeste : IProvedorDeRotas
    {
        public const string NomeDoProvedor = "teste";

        private int _chamadas;

        public ModoDoProvedor Modo { get; set; }

        public int Chamadas => _chamadas;

        public TaskCompletionSource EmEspera { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Liberado { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Nome => NomeDoProvedor;

        public async Task<IReadOnlyList<TrechoDeTrajeto>> EstimarTrajetoAsync(IReadOnlyList<CoordenadaGeografica> pontos, CancellationToken cancelamento)
        {
            Interlocked.Increment(ref _chamadas);

            if (Modo == ModoDoProvedor.Falhar)
            {
                throw new HttpRequestException("Provedor de rotas indisponível.");
            }

            EmEspera.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancelamento);
                throw new InvalidOperationException("Inalcançável.");
            }
            finally
            {
                Liberado.TrySetResult();
            }
        }
    }
}

/// <summary>Sem provedor de rotas configurado.</summary>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class SemProvedorDeRotasTestes(ContainerPostgis banco) : TesteDePrevisao(banco)
{
    protected override IReadOnlyDictionary<string, string?> ConfiguracaoAdicional => new Dictionary<string, string?>(base.ConfiguracaoAdicional)
    {
        ["Torre:Previsao:Provedor"] = "Nenhum",
    };

    [Fact]
    public async Task SemProvedorAPrevisaoUsaAContingenciaEDizPorque()
    {
        using var http = Cliente();
        var inicio = Tempo.GetUtcNow();
        var cenario = await CenarioAsync(http, inicio.AddMinutes(10), inicio.AddHours(3), 0);

        await EnviarPosicaoAsync(http, cenario, distanciaAoSulEmMetros: 10_000, sequencia: 1);

        var previsao = await EsperarPrevisaoAsync(http, cenario.Supervisor, cenario.Entregas[0], TemChegadaPrevista);
        var composicao = previsao.GetProperty("composicao");
        Assert.Equal("Contingencia", composicao.GetProperty("fonte").GetString());
        Assert.Equal("ProvedorAusente", composicao.GetProperty("motivoDaContingencia").GetString());
        Assert.Equal(2_000, composicao.GetProperty("deslocamentoEmSegundos").GetInt32());
        Assert.Equal(Truncado(inicio).AddSeconds(2_000), previsao.GetProperty("chegadaPrevistaEm").GetDateTimeOffset());
        Assert.Contains("não há provedor de rotas configurado", previsao.GetProperty("explicacao").GetString(), StringComparison.Ordinal);
    }
}

/// <summary>Uma entrega do cenário: destino ao norte da base, a esta distância, com esta janela.</summary>
public sealed record DestinoDeTeste(double DistanciaAoNorteEmMetros, DateTimeOffset JanelaDe, DateTimeOffset JanelaAte);

/// <summary>
/// Base dos testes de previsão e alertas: relógio parado, parâmetros exatos, cenário com rota em andamento e
/// console de tempo real.
/// </summary>
public abstract class TesteDePrevisao(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    /// <summary>Espera por recálculo disparado por evento.</summary>
    protected static readonly TimeSpan EsperaDoProcessamento = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Espera pela reavaliação periódica: ela percorre as rotas em andamento de toda a suíte, que compartilha
    /// o banco, antes de chegar à deste teste.
    /// </summary>
    protected static readonly TimeSpan EsperaDaReavaliacao = TimeSpan.FromSeconds(60);

    private const double LatitudeDaBase = -22.9100;
    private const double LongitudeDaBase = -47.0650;

    /// <summary>Relógio da API.</summary>
    protected FakeTimeProvider Tempo { get; } = new(DateTimeOffset.UtcNow);

    /// <inheritdoc />
    protected override TimeProvider Relogio => Tempo;

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?> ConfiguracaoAdicional => new Dictionary<string, string?>
    {
        ["Torre:Previsao:Provedor"] = "simulado",
        ["Torre:Previsao:ProvedorSimulado:VelocidadeMediaEmMetrosPorSegundo"] = "10",
        ["Torre:Previsao:ProvedorSimulado:FatorDeSinuosidade"] = "1",
        ["Torre:Previsao:VelocidadeDeContingenciaEmMetrosPorSegundo"] = "5",
        ["Torre:Previsao:FatorDeSinuosidadeDeContingencia"] = "1",
        ["Torre:Previsao:TempoMedioPorParada"] = "00:05:00",
        ["Torre:Previsao:FolgaParaAtencao"] = "00:20:00",
        ["Torre:Previsao:FolgaParaRisco"] = "00:05:00",
        ["Torre:Previsao:IntervaloDeReavaliacao"] = "00:01:00",
    };

    /// <summary>Organização, supervisor, operador, motorista autenticado e rota iniciada com as entregas em ordem.</summary>
    protected sealed record CenarioDePrevisao(
        OrganizacaoDeTeste Organizacao,
        string Supervisor,
        string Operador,
        string TokenDoMotorista,
        Guid Motorista,
        Guid Rota,
        IReadOnlyList<Guid> Entregas);

    /// <summary>A previsão já tem chegada prevista.</summary>
    protected static bool TemChegadaPrevista(JsonElement previsao) =>
        previsao.GetProperty("chegadaPrevistaEm").ValueKind != JsonValueKind.Null;

    /// <summary>Instante truncado ao segundo, como a previsão grava.</summary>
    protected static DateTimeOffset Truncado(DateTimeOffset instante) =>
        new(instante.UtcTicks - (instante.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero);

    /// <summary>Cenário com a mesma janela para todas as entregas.</summary>
    protected Task<CenarioDePrevisao> CenarioAsync(
        HttpClient http,
        DateTimeOffset janelaDe,
        DateTimeOffset janelaAte,
        params double[] distanciasDosDestinosAoNorte) =>
        CenarioAsync(http, [.. distanciasDosDestinosAoNorte.Select(distancia => new DestinoDeTeste(distancia, janelaDe, janelaAte))]);

    /// <summary>
    /// Cria o cenário. Cada entrega tem destino ao norte da base; a rota as visita nessa ordem e é iniciada
    /// pelo motorista.
    /// </summary>
    protected async Task<CenarioDePrevisao> CenarioAsync(HttpClient http, IReadOnlyList<DestinoDeTeste> destinos)
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Supervisor, Perfil.Operador, Perfil.Motorista);
        var supervisor = (await EntrarAsync(http, organizacao.Com(Perfil.Supervisor))).TokenDeAcesso;
        var operador = (await EntrarAsync(http, organizacao.Com(Perfil.Operador))).TokenDeAcesso;
        var clienteId = await CriarAsync(http, supervisor, "/api/clientes", RoteirosDeCadastro.Obter("cliente").Corpo());

        var entregas = new List<Guid>();
        foreach (var (destino, indice) in destinos.Select((destino, indice) => (destino, indice)))
        {
            var (latitude, longitude) = await ProjetarAsync(destino.DistanciaAoNorteEmMetros, azimute: 0);
            var destinatarioId = await CriarAsync(http, supervisor, "/api/destinatarios", new
            {
                nome = $"Destinatária {indice + 1}",
                endereco = RoteirosDeCadastro.Endereco(),
                latitude,
                longitude,
            });

            entregas.Add(await CriarAsync(http, supervisor, "/api/entregas", RoteirosDeEntrega.Corpo(clienteId, destinatarioId, destino.JanelaDe, destino.JanelaAte)));
        }

        var motorista = await CriarAsync(http, supervisor, "/api/motoristas", RoteirosDeCadastro.Obter("motorista").Corpo());
        await OkAsync(http, HttpMethod.Put, $"/api/motoristas/{motorista}/conta", supervisor, new { usuarioId = organizacao.Com(Perfil.Motorista).Id });
        var tokenDoMotorista = (await EntrarAsync(http, organizacao.Com(Perfil.Motorista))).TokenDeAcesso;

        var veiculo = await CriarAsync(http, supervisor, "/api/veiculos", RoteirosDeCadastro.Obter("veiculo").Corpo());
        var rota = await CriarAsync(http, supervisor, "/api/rotas", new { data = DateOnly.FromDateTime(Tempo.GetUtcNow().UtcDateTime.Date.AddDays(1)) });
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/paradas", supervisor, new { entregas });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/motorista", supervisor, new { motoristaId = motorista });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/veiculo", supervisor, new { veiculoId = veiculo });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/saida", supervisor, new
        {
            saidaPlanejada = new DateTimeOffset(Tempo.GetUtcNow().UtcDateTime.Date.AddDays(1).AddHours(8), TimeSpan.Zero),
        });
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/planejamento", supervisor);
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{rota}/inicio", tokenDoMotorista);

        return new CenarioDePrevisao(organizacao, supervisor, operador, tokenDoMotorista, motorista, rota, entregas);
    }

    /// <summary>
    /// Envia uma posição do motorista ao sul da base, capturada há 5 segundos. Entra de novo a cada envio: o
    /// teste avança o relógio além dos 15 minutos do token de acesso.
    /// </summary>
    protected async Task EnviarPosicaoAsync(HttpClient http, CenarioDePrevisao cenario, double distanciaAoSulEmMetros, long sequencia)
    {
        var (latitude, longitude) = await ProjetarAsync(distanciaAoSulEmMetros, azimute: Math.PI);
        var tokenDoMotorista = (await EntrarAsync(http, cenario.Organizacao.Com(Perfil.Motorista))).TokenDeAcesso;
        var lote = await PosicoesTestes.EnviarAsync(
            http,
            tokenDoMotorista,
            PosicoesTestes.Posicao(sequencia, Tempo.GetUtcNow().AddSeconds(-5), latitude, longitude, precisao: 5));

        Assert.Equal(1, lote.GetProperty("aceitas").GetInt32());
    }

    /// <summary>Consulta a previsão.</summary>
    protected static Task<JsonElement> PrevisaoAsync(HttpClient http, string token, Guid entrega) =>
        OkAsync(http, HttpMethod.Get, $"/api/entregas/{entrega}/previsao", token);

    /// <summary>Consulta a previsão até a condição valer: o recálculo roda em segundo plano.</summary>
    protected static Task<JsonElement> EsperarPrevisaoAsync(
        HttpClient http,
        string token,
        Guid entrega,
        Func<JsonElement, bool> condicao,
        TimeSpan? espera = null) =>
        EsperarAsync(http, token, $"/api/entregas/{entrega}/previsao", condicao, espera);

    /// <summary>Consulta a URL até a condição valer.</summary>
    protected static async Task<JsonElement> EsperarAsync(
        HttpClient http,
        string token,
        string url,
        Func<JsonElement, bool> condicao,
        TimeSpan? espera = null)
    {
        var limite = espera ?? EsperaDoProcessamento;
        var relogio = Stopwatch.StartNew();
        JsonElement ultima;

        do
        {
            ultima = await OkAsync(http, HttpMethod.Get, url, token);
            if (condicao(ultima))
            {
                return ultima;
            }

            await Task.Delay(100, Cancelamento);
        }
        while (relogio.Elapsed < limite);

        throw new TimeoutException($"{url} não chegou ao estado esperado em {limite.TotalSeconds} s. Última resposta: {ultima}");
    }

    /// <summary>JSON de uma resposta 200.</summary>
    protected static async Task<JsonElement> OkAsync(HttpClient http, HttpMethod metodo, string url, string token, object? corpo = null)
    {
        using var resposta = await EnviarAsync(http, metodo, url, token, corpo);
        var json = await JsonAsync(resposta);
        Assert.True(resposta.StatusCode == HttpStatusCode.OK, $"{metodo} {url}: {(int)resposta.StatusCode} {json}");
        return json;
    }

    /// <summary>Status e corpo sem campos de rastreio, para comparar respostas de erro.</summary>
    protected static async Task<string> AssinaturaAsync(HttpResponseMessage resposta)
    {
        var json = await JsonAsync(resposta);
        var campos = json.EnumerateObject()
            .Where(campo => campo.Name is not ("traceId" or "idDeCorrelacao" or "instance"))
            .Select(campo => $"{campo.Name}={campo.Value.GetRawText()}");
        return $"{(int)resposta.StatusCode}|{string.Join("|", campos)}";
    }

    /// <summary>Conecta ao hub do console por WebSocket e guarda os avisos pedidos.</summary>
    protected async Task<ClienteDoConsole> ConectarAoConsoleAsync(string token, params string[] eventos)
    {
        var conexao = new HubConnectionBuilder()
            .WithUrl(new Uri(Fabrica.Server.BaseAddress, HubDaOperacao.Caminho), opcoes =>
            {
                opcoes.Transports = HttpTransportType.WebSockets;
                opcoes.SkipNegotiation = true;
                opcoes.WebSocketFactory = async (contexto, cancelamento) =>
                {
                    var fabricaDeSocket = Fabrica.Server.CreateWebSocketClient();
                    fabricaDeSocket.ConfigureRequest = requisicao => requisicao.Headers.Authorization = $"Bearer {token}";
                    return await fabricaDeSocket.ConnectAsync(contexto.Uri, cancelamento);
                };
            })
            .AddJsonProtocol(json => json.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();

        var cliente = new ClienteDoConsole(conexao, eventos);
        await conexao.StartAsync(Cancelamento);
        return cliente;
    }

    private static async Task<Guid> CriarAsync(HttpClient http, string token, string url, object corpo)
    {
        using var resposta = await EnviarAsync(http, HttpMethod.Post, url, token, corpo);
        var json = await JsonAsync(resposta);
        Assert.True(resposta.StatusCode == HttpStatusCode.Created, $"{url}: {json}");
        return json.GetProperty("id").GetGuid();
    }

    /// <summary>Ponto a uma distância exata da base, na direção informada, calculado pelo PostGIS.</summary>
    private async Task<(double Latitude, double Longitude)> ProjetarAsync(double distanciaEmMetros, double azimute)
    {
        var ponto = await Banco.ConsultarEscalarAsync(
            """
            SELECT ST_Y(p::geometry)::text || ';' || ST_X(p::geometry)::text
            FROM (SELECT ST_Project(ST_SetSRID(ST_MakePoint(@lon, @lat), 4326)::geography, @distancia, @azimute) AS p) projetado
            """,
            ("lat", LatitudeDaBase),
            ("lon", LongitudeDaBase),
            ("distancia", distanciaEmMetros),
            ("azimute", azimute));

        var partes = ponto!.Split(';');
        return (double.Parse(partes[0], CultureInfo.InvariantCulture), double.Parse(partes[1], CultureInfo.InvariantCulture));
    }

    /// <summary>Cliente do console que guarda os avisos recebidos.</summary>
    protected sealed class ClienteDoConsole : IAsyncDisposable
    {
        private readonly ConcurrentQueue<(string Evento, JsonElement Carga)> _avisos = new();
        private readonly HubConnection _conexao;

        public ClienteDoConsole(HubConnection conexao, IEnumerable<string> eventos)
        {
            _conexao = conexao;
            foreach (var evento in eventos)
            {
                conexao.On<JsonElement>(evento, carga => _avisos.Enqueue((evento, carga.Clone())));
            }
        }

        public async Task<JsonElement> EsperarAsync(string evento, Func<JsonElement, bool> filtro)
        {
            var relogio = Stopwatch.StartNew();
            while (relogio.Elapsed < EsperaDoProcessamento)
            {
                foreach (var (nome, carga) in _avisos)
                {
                    if (nome == evento && filtro(carga))
                    {
                        return carga;
                    }
                }

                await Task.Delay(50, Cancelamento);
            }

            throw new TimeoutException($"O aviso {evento} não chegou.");
        }

        public int Contar(string evento, Func<JsonElement, bool> filtro) =>
            _avisos.Count(aviso => aviso.Evento == evento && filtro(aviso.Carga));

        public ValueTask DisposeAsync() => _conexao.DisposeAsync();
    }
}
