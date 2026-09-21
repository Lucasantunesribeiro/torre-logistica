using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// Carga de engenharia: a ingestão de GPS sob a taxa que o ROADMAP fixou.
/// </summary>
/// <remarks>
/// <para>
/// <b>Desligado por padrão.</b> Benchmark dentro de suíte de regressão mede errado — compete por CPU com
/// o que roda antes e depois, e vira teste instável que alguém acaba desabilitando por outro motivo.
/// Roda sob demanda, com <c>TORRE_CARGA=1</c>, e o relatório sai em
/// <c>TORRE_CARGA_RELATORIO</c> quando definido.
/// </para>
/// <para>
/// A meta é <b>33 posições por segundo</b> — 500 motoristas a uma posição a cada 15 segundos. Aqui a mesma
/// taxa é produzida por menos motoristas enviando com mais frequência, e isso é deliberadamente
/// conservador: concentrar a taxa em menos chaves aumenta a disputa na tabela de posição atual, que é
/// justamente onde o <c>UPSERT</c> condicional pode engasgar. Se aguenta assim, aguenta espalhado.
/// </para>
/// </remarks>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class CargaDeIngestaoTestes(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    private const int Motoristas = 60;
    private const int PosicoesPorSegundo = 33;
    private const int SegundosDeCarga = 30;

    /// <summary>Um dia de operação na meta: 33 posições por segundo durante 24 horas.</summary>
    private const int PosicoesDeUmDia = PosicoesPorSegundo * 60 * 60 * 24;

    /// <summary>Quantas linhas cada comando de semeadura insere.</summary>
    private const int TamanhoDoLoteDeSemeadura = 200_000;

    /// <summary>O benchmark só roda quando pedido.</summary>
    public static bool CargaHabilitada =>
        string.Equals(Environment.GetEnvironmentVariable("TORRE_CARGA"), "1", StringComparison.Ordinal);

    /// <summary>Ingestão sob a carga de engenharia, com latência por percentil.</summary>
    [Fact(Skip = "Benchmark sob demanda: defina TORRE_CARGA=1.", SkipUnless = nameof(CargaHabilitada))]
    public async Task IngestaoSustentaATaxaDeEngenharia()
    {
        using var http = Cliente();
        var relatorio = new StringBuilder();
        var preparo = Stopwatch.StartNew();

        var frota = await PrepararFrotaAsync(http);
        preparo.Stop();
        relatorio.AppendLine(CultureInfo.InvariantCulture, $"Preparo: {frota.Count} motoristas em rota em {preparo.Elapsed.TotalSeconds:F1} s.");

        var latencias = new List<double>(PosicoesPorSegundo * SegundosDeCarga);
        var recusadas = 0;
        var sequencia = 0L;
        var carga = Stopwatch.StartNew();

        // Uma "rodada" por segundo, com as posições do segundo distribuídas entre os motoristas.
        for (var segundo = 0; segundo < SegundosDeCarga; segundo++)
        {
            var alvoDoSegundo = carga.Elapsed + TimeSpan.FromSeconds(1);
            var envios = new List<Task<(double Milissegundos, bool Aceita)>>(PosicoesPorSegundo);

            for (var posicao = 0; posicao < PosicoesPorSegundo; posicao++)
            {
                var motorista = frota[(segundo * PosicoesPorSegundo + posicao) % frota.Count];
                envios.Add(EnviarCronometradoAsync(http, motorista, Interlocked.Increment(ref sequencia)));
            }

            foreach (var (milissegundos, aceita) in await Task.WhenAll(envios))
            {
                latencias.Add(milissegundos);

                if (!aceita)
                {
                    recusadas++;
                }
            }

            var sobra = alvoDoSegundo - carga.Elapsed;

            if (sobra > TimeSpan.Zero)
            {
                await Task.Delay(sobra, Cancelamento);
            }
        }

        carga.Stop();
        latencias.Sort();

        var vazao = latencias.Count / carga.Elapsed.TotalSeconds;
        relatorio.AppendLine(CultureInfo.InvariantCulture, $"Carga: {latencias.Count} posições em {carga.Elapsed.TotalSeconds:F1} s → {vazao:F1}/s (meta {PosicoesPorSegundo}/s).");
        relatorio.AppendLine(CultureInfo.InvariantCulture, $"Latência da ingestão: p50 {Percentil(latencias, 50):F1} ms · p95 {Percentil(latencias, 95):F1} ms · p99 {Percentil(latencias, 99):F1} ms · máx {latencias[^1]:F1} ms.");
        relatorio.AppendLine(CultureInfo.InvariantCulture, $"Recusadas pela política: {recusadas}.");

        await MedirLeiturasAsync(http, frota, relatorio);
        await MedirBancoAsync(relatorio);

        Publicar(relatorio.ToString());

        // O benchmark também é teste: a carga tem de ter sido sustentada de verdade.
        Assert.True(recusadas == 0, $"A política recusou {recusadas} posição(ões) — a medição não vale.");
        Assert.True(
            vazao >= PosicoesPorSegundo * 0.95,
            $"Vazão sustentada de {vazao:F1}/s, abaixo da meta de {PosicoesPorSegundo}/s.");
    }

    /// <summary>
    /// Planos de consulta sob o volume de um dia de operação na meta.
    /// </summary>
    /// <remarks>
    /// Medir plano com mil linhas não diz nada: o planejador escolhe varredura sequencial porque a tabela
    /// cabe em poucas páginas, e o resultado engana nos dois sentidos — parece rápido e parece sem índice.
    /// Aqui o histórico é semeado direto em SQL até o volume de um dia (2,8 milhões de posições, que é o
    /// que 500 motoristas produzem a 33/s). Semear por SQL é legítimo porque o que se mede é a decisão do
    /// banco, não o caminho da aplicação — esse já foi medido no teste de ingestão.
    /// </remarks>
    [Fact(Skip = "Benchmark sob demanda: defina TORRE_CARGA=1.", SkipUnless = nameof(CargaHabilitada))]
    public async Task PlanosDeConsultaSobVolumeDeUmDia()
    {
        using var http = Cliente();
        var relatorio = new StringBuilder();
        // A frota inteira, e não um motorista só: com todas as posições concentradas numa chave, a
        // consulta de histórico não teria seletividade nenhuma, e o plano medido enganaria.
        var frota = await PrepararFrotaAsync(http);
        var motorista = frota[0];

        // Em lotes: um INSERT único de milhões de linhas estoura o tempo limite do comando, e o que
        // se quer aqui é o volume no fim, não a proeza de inserir tudo numa transação só.
        var semeadura = Stopwatch.StartNew();

        for (var inserido = 0; inserido < PosicoesDeUmDia; inserido += TamanhoDoLoteDeSemeadura)
        {
            await SemearHistoricoAsync(motorista.Id, Math.Min(TamanhoDoLoteDeSemeadura, PosicoesDeUmDia - inserido), inserido);
        }

        semeadura.Stop();

        var linhas = await Banco.ConsultarEscalarAsync("SELECT count(*)::text FROM posicoes");
        var tamanho = await Banco.ConsultarEscalarAsync("SELECT pg_size_pretty(pg_total_relation_size('posicoes'))");
        var indices = await Banco.ConsultarEscalarAsync("SELECT pg_size_pretty(pg_indexes_size('posicoes'))");

        relatorio.AppendLine(CultureInfo.InvariantCulture, $"Volume: {linhas} posições semeadas em {semeadura.Elapsed.TotalSeconds:F1} s.");
        relatorio.AppendLine(CultureInfo.InvariantCulture, $"Tabela: {tamanho} no total, {indices} só de índice.");

        await MedirBancoAsync(relatorio);

        var limpeza = Stopwatch.StartNew();
        var apagadas = await Banco.ExecutarAsync(
            """
            DELETE FROM posicoes
            WHERE ctid IN (
                SELECT ctid FROM posicoes
                WHERE recebida_em < now() - interval '30 days'
                ORDER BY recebida_em
                LIMIT 5000
            )
            """);
        limpeza.Stop();

        relatorio.AppendLine(CultureInfo.InvariantCulture, $"Limpeza: lote de {apagadas} linha(s) em {limpeza.Elapsed.TotalMilliseconds:F0} ms.");

        // Ingestão pelo caminho real, agora com a tabela cheia: é a comparação que interessa contra os
        // 31 ms medidos com a tabela vazia.
        var envios = new List<double>(20);

        for (var vez = 0; vez < 20; vez++)
        {
            var (milissegundos, aceita) = await EnviarCronometradoAsync(http, motorista, 9_000_000 + vez);
            Assert.True(aceita, "A posição precisa ser aceita para a medição valer.");
            envios.Add(milissegundos);
        }

        envios.Sort();
        relatorio.AppendLine(CultureInfo.InvariantCulture, $"Ingestão com a tabela cheia: p50 {Percentil(envios, 50):F1} ms · p95 {Percentil(envios, 95):F1} ms.");

        var leitura = Stopwatch.StartNew();
        using (var resposta = await EnviarAsync(
            http, HttpMethod.Get, $"/api/motoristas/{motorista.Id}/posicao-atual", motorista.Supervisor))
        {
            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        }

        leitura.Stop();
        relatorio.AppendLine(CultureInfo.InvariantCulture, $"Posição atual com a tabela cheia: {leitura.Elapsed.TotalMilliseconds:F1} ms.");

        Publicar(relatorio.ToString());
    }

    /// <summary>
    /// Semeia o histórico direto no banco, com datas espalhadas por 45 dias.
    /// </summary>
    /// <remarks>
    /// Quarenta e cinco dias, e não trinta: parte do volume precisa estar vencida para a limpeza por
    /// retenção ter o que apagar na medição.
    /// </remarks>
    private Task<int> SemearHistoricoAsync(Guid motoristaId, int quantidade, int deslocamento) =>
        Banco.ExecutarAsync(
            """
            WITH frota AS (
                SELECT array_agg(id ORDER BY id) AS ids
                FROM motoristas
                WHERE organizacao_id = (SELECT organizacao_id FROM motoristas WHERE id = @motorista)
            )
            INSERT INTO posicoes (
                id, organizacao_id, motorista_id, rota_id, evento_de_localizacao_id, sequencia,
                localizacao, precisao_em_metros, velocidade_em_metros_por_segundo, direcao_em_graus,
                capturada_em, recebida_em, qualidade)
            SELECT
                gen_random_uuid(),
                (SELECT organizacao_id FROM motoristas WHERE id = @motorista),
                f.ids[((s.n + @deslocamento) % array_length(f.ids, 1)) + 1],
                NULL,
                gen_random_uuid(),
                s.n + @deslocamento + 1000000,
                ST_SetSRID(ST_MakePoint(-47.065 + (s.n % 1000) * 0.0001, -22.91 + (s.n % 1000) * 0.0001), 4326)::geography,
                8,
                NULL,
                NULL,
                now() - ((s.n + @deslocamento) % 45) * interval '1 day' - (s.n % 86400) * interval '1 second',
                now() - ((s.n + @deslocamento) % 45) * interval '1 day' - (s.n % 86400) * interval '1 second',
                'Confiavel'
            FROM generate_series(1, @quantidade) AS s(n)
            CROSS JOIN frota f
            """,
            ("quantidade", quantidade),
            ("deslocamento", deslocamento),
            ("motorista", motoristaId));

    private static async Task<(double Milissegundos, bool Aceita)> EnviarCronometradoAsync(
        HttpClient http,
        MotoristaDeCarga motorista,
        long sequencia)
    {
        var corpo = PosicoesTestes.Corpo(PosicoesTestes.Posicao(
            sequencia,
            DateTimeOffset.UtcNow.AddSeconds(-1),
            latitude: -22.9100 + (sequencia % 200 * 0.0001),
            longitude: -47.0650 + (sequencia % 200 * 0.0001)));

        var relogio = Stopwatch.StartNew();
        using var resposta = await EnviarAsync(http, HttpMethod.Post, "/api/motorista/posicoes", motorista.Token, corpo)
            .ConfigureAwait(false);
        relogio.Stop();

        var aceita = false;

        if (resposta.StatusCode == HttpStatusCode.OK)
        {
            var json = await JsonAsync(resposta).ConfigureAwait(false);
            aceita = json.GetProperty("aceitas").GetInt32() == 1;
        }

        return (relogio.Elapsed.TotalMilliseconds, aceita);
    }

    private static async Task MedirLeiturasAsync(HttpClient http, IReadOnlyList<MotoristaDeCarga> frota, StringBuilder relatorio)
    {
        var supervisor = frota[0].Supervisor;

        var doMapa = await CronometrarAsync(20, async () =>
        {
            using var resposta = await EnviarAsync(http, HttpMethod.Get, "/api/entregas?status=EmRota&tamanhoDaPagina=100", supervisor)
                .ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        }).ConfigureAwait(false);

        var daPosicao = await CronometrarAsync(20, async () =>
        {
            using var resposta = await EnviarAsync(
                http, HttpMethod.Get, $"/api/motoristas/{frota[0].Id}/posicao-atual", supervisor).ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        }).ConfigureAwait(false);

        relatorio.AppendLine(CultureInfo.InvariantCulture, $"Lista do mapa (entregas em rota): p50 {Percentil(doMapa, 50):F1} ms · p95 {Percentil(doMapa, 95):F1} ms.");
        relatorio.AppendLine(CultureInfo.InvariantCulture, $"Posição atual de um motorista: p50 {Percentil(daPosicao, 50):F1} ms · p95 {Percentil(daPosicao, 95):F1} ms.");
    }

    private async Task MedirBancoAsync(StringBuilder relatorio)
    {
        var posicoes = await Banco.ConsultarEscalarAsync("SELECT count(*)::text FROM posicoes").ConfigureAwait(false);
        var atuais = await Banco.ConsultarEscalarAsync("SELECT count(*)::text FROM posicoes_atuais").ConfigureAwait(false);
        var tamanho = await Banco.ConsultarEscalarAsync(
            "SELECT pg_size_pretty(pg_total_relation_size('posicoes'))").ConfigureAwait(false);

        relatorio.AppendLine(CultureInfo.InvariantCulture, $"Banco: {posicoes} posições no histórico, {atuais} projeções, tabela com {tamanho}.");

        // Sem estatísticas atualizadas o planejador decide no escuro, e o plano medido não seria o que
        // o banco usaria de verdade depois de um dia de operação.
        await Banco.ExecutarAsync("ANALYZE posicoes, posicoes_atuais, outbox");

        foreach (var (nome, sql) in Consultas())
        {
            // Duas vezes, e o que vale é a segunda: a primeira paga a leitura do disco, e o número
            // frio diria mais sobre o cache da máquina do que sobre a consulta.
            await Banco.ConsultarEscalarAsync($"EXPLAIN (ANALYZE, FORMAT JSON) {sql}").ConfigureAwait(false);
            var plano = await Banco.ConsultarEscalarAsync($"EXPLAIN (ANALYZE, FORMAT JSON) {sql}").ConfigureAwait(false);
            relatorio.AppendLine(CultureInfo.InvariantCulture, $"Plano — {nome}: {ResumirPlano(plano)}");
        }
    }

    private static IEnumerable<(string Nome, string Sql)> Consultas() =>
    [
        ("posição atual por motorista",
            "SELECT * FROM posicoes_atuais WHERE motorista_id = (SELECT id FROM motoristas ORDER BY id LIMIT 1)"),
        ("histórico de um motorista por período",
            "SELECT * FROM posicoes WHERE motorista_id = (SELECT id FROM motoristas ORDER BY id LIMIT 1) "
            + "AND capturada_em >= now() - interval '24 hours' ORDER BY capturada_em LIMIT 500"),
        ("limpeza por retenção",
            "SELECT ctid FROM posicoes WHERE recebida_em < now() - interval '30 days' ORDER BY recebida_em LIMIT 5000"),
        ("outbox pendente",
            "SELECT id FROM outbox WHERE despachada_em IS NULL AND disponivel_em <= now() ORDER BY disponivel_em, criada_em LIMIT 100"),
    ];

    private static string ResumirPlano(string? planoEmJson)
    {
        if (string.IsNullOrWhiteSpace(planoEmJson))
        {
            return "(sem plano)";
        }

        using var documento = JsonDocument.Parse(planoEmJson);
        var plano = documento.RootElement[0].GetProperty("Plan");
        var tempo = documento.RootElement[0].GetProperty("Execution Time").GetDouble();

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{DescreverNo(plano)} · {tempo:F2} ms");
    }

    /// <summary>
    /// Resume o plano pelos tipos de nó e pelos índices realmente usados, em toda a árvore.
    /// </summary>
    /// <remarks>
    /// Descer só pelo primeiro filho engana: numa consulta com subconsulta, o primeiro ramo costuma ser a
    /// subconsulta, e o índice que interessa fica escondido no irmão.
    /// </remarks>
    private static string DescreverNo(JsonElement no)
    {
        var tipos = new List<string>();
        var indices = new List<string>();
        Percorrer(no, tipos, indices);

        var usados = indices.Count == 0 ? "nenhum índice" : string.Join(", ", indices.Distinct());
        return $"{string.Join(" + ", tipos.Distinct())} · usa {usados}";
    }

    private static void Percorrer(JsonElement no, List<string> tipos, List<string> indices)
    {
        if (no.GetProperty("Node Type").GetString() is { } tipo && tipo is not ("Limit" or "Result"))
        {
            tipos.Add(tipo);
        }

        if (no.TryGetProperty("Index Name", out var nome) && nome.GetString() is { } indice)
        {
            indices.Add(indice);
        }

        if (no.TryGetProperty("Plans", out var filhos))
        {
            foreach (var filho in filhos.EnumerateArray())
            {
                Percorrer(filho, tipos, indices);
            }
        }
    }

    private static async Task<List<double>> CronometrarAsync(int repeticoes, Func<Task> acao)
    {
        var medidas = new List<double>(repeticoes);

        for (var vez = 0; vez < repeticoes; vez++)
        {
            var relogio = Stopwatch.StartNew();
            await acao().ConfigureAwait(false);
            relogio.Stop();
            medidas.Add(relogio.Elapsed.TotalMilliseconds);
        }

        medidas.Sort();
        return medidas;
    }

    private static double Percentil(List<double> ordenadas, int percentil) =>
        ordenadas.Count == 0
            ? 0
            : ordenadas[Math.Clamp((int)Math.Ceiling(percentil / 100d * ordenadas.Count) - 1, 0, ordenadas.Count - 1)];

    private static void Publicar(string relatorio)
    {
        TestContext.Current.TestOutputHelper?.WriteLine(relatorio);

        if (Environment.GetEnvironmentVariable("TORRE_CARGA_RELATORIO") is { Length: > 0 } caminho)
        {
            File.WriteAllText(caminho, relatorio);
        }
    }

    /// <summary>Cria a frota: cada motorista com conta, cadastro, veículo e rota iniciada.</summary>
    private async Task<IReadOnlyList<MotoristaDeCarga>> PrepararFrotaAsync(HttpClient http, int? quantidade = null)
    {
        var total = quantidade ?? Motoristas;
        var perfis = new List<Perfil> { Perfil.Supervisor };
        perfis.AddRange(Enumerable.Repeat(Perfil.Motorista, total));

        var organizacao = await Cenario.CriarOrganizacaoAsync([.. perfis]);
        var supervisor = (await EntrarAsync(http, organizacao.Com(Perfil.Supervisor))).TokenDeAcesso;
        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);
        var contas = organizacao.TodasCom(Perfil.Motorista);
        var dia = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1));

        var frota = new List<MotoristaDeCarga>(total);

        foreach (var conta in contas)
        {
            var entrega = await CriarAsync(http, supervisor, "/api/entregas", RoteirosDeEntrega.Corpo(clienteId, destinatarioId));
            var motorista = await CriarAsync(http, supervisor, "/api/motoristas", RoteirosDeCadastro.Obter("motorista").Corpo());
            await OkAsync(http, HttpMethod.Put, $"/api/motoristas/{motorista}/conta", supervisor, new { usuarioId = conta.Id });

            var veiculo = await CriarAsync(http, supervisor, "/api/veiculos", RoteirosDeCadastro.Obter("veiculo").Corpo());
            var rota = await CriarAsync(http, supervisor, "/api/rotas", new { data = dia });
            await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/paradas", supervisor, new { entregas = new[] { entrega } });
            await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/motorista", supervisor, new { motoristaId = motorista });
            await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/veiculo", supervisor, new { veiculoId = veiculo });
            await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/saida", supervisor, new
            {
                saidaPlanejada = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(1).AddHours(8), TimeSpan.Zero),
            });
            await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/planejamento", supervisor);

            var token = (await EntrarAsync(http, conta)).TokenDeAcesso;
            await OkAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{rota}/inicio", token);

            frota.Add(new MotoristaDeCarga(motorista, token, supervisor));
        }

        return frota;
    }

    private static async Task<JsonElement> OkAsync(HttpClient http, HttpMethod metodo, string url, string token, object? corpo = null)
    {
        using var resposta = await EnviarAsync(http, metodo, url, token, corpo);
        var json = await JsonAsync(resposta);
        Assert.True(resposta.StatusCode == HttpStatusCode.OK, $"{metodo} {url}: {(int)resposta.StatusCode} {json}");
        return json;
    }

    private static async Task<Guid> CriarAsync(HttpClient http, string token, string url, object corpo)
    {
        using var resposta = await EnviarAsync(http, HttpMethod.Post, url, token, corpo);
        var json = await JsonAsync(resposta);
        Assert.True(resposta.StatusCode == HttpStatusCode.Created, $"{url}: {(int)resposta.StatusCode} {json}");
        return json.GetProperty("id").GetGuid();
    }

    private sealed record MotoristaDeCarga(Guid Id, string Token, string Supervisor);
}
