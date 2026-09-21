using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using TorreLogistica.Application.Retencao;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// Limpeza do histórico bruto de localização contra a API e o PostgreSQL reais.
/// </summary>
/// <remarks>
/// <para>
/// O relógio destes testes começa <b>no passado</b>, e isso é essencial: a limpeza varre por idade e
/// atravessa organizações por desenho, então um corte calculado a partir de um relógio adiantado apagaria
/// a telemetria das outras classes que rodam contra o mesmo banco. Começando dez dias atrás, o corte cai
/// numa faixa em que só existe o que este teste inseriu.
/// </para>
/// <para>
/// Nenhuma linha é escrita direto no banco: as posições entram pela API do motorista, como no aparelho.
/// </para>
/// </remarks>
public abstract class TesteDeRetencao(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    /// <summary>Relógio da API, dez dias atrás para não alcançar a telemetria dos outros testes.</summary>
    protected FakeTimeProvider Tempo { get; } = new(
        new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-10).AddHours(9), TimeSpan.Zero));

    /// <inheritdoc />
    protected override TimeProvider Relogio => Tempo;

    /// <summary>Organização com um motorista autenticado em rota iniciada.</summary>
    protected sealed record CenarioDeRetencao(OrganizacaoDeTeste Organizacao, Guid Motorista, string Supervisor);

    /// <summary>Roda uma rodada de limpeza pelo serviço real, como o processador em segundo plano faria.</summary>
    protected async Task<ResultadoDaLimpeza> LimparAsync()
    {
        using var escopo = Fabrica.Services.CreateScope();
        return await escopo.ServiceProvider.GetRequiredService<LimpezaPorRetencao>().ExecutarAsync(Cancelamento);
    }

    /// <summary>Envia posições pelo canal do motorista, entrando de novo para o token acompanhar o relógio.</summary>
    protected async Task EnviarPosicoesAsync(HttpClient http, CenarioDeRetencao cenario, params long[] sequencias)
    {
        var token = (await EntrarAsync(http, cenario.Organizacao.Com(Perfil.Motorista))).TokenDeAcesso;

        foreach (var sequencia in sequencias)
        {
            var lote = await PosicoesTestes.EnviarAsync(
                http,
                token,
                PosicoesTestes.Posicao(sequencia, Tempo.GetUtcNow().AddSeconds(-5), latitude: -22.9000 - (sequencia / 10_000d)));

            Assert.True(lote.GetProperty("aceitas").GetInt32() == 1, $"lote recusado: {lote}");
        }
    }

    /// <summary>Quantas posições o histórico guarda na janela informada.</summary>
    protected static async Task<int> HistoricoAsync(HttpClient http, CenarioDeRetencao cenario, DateTimeOffset de, DateTimeOffset ate)
    {
        var supervisor = (await EntrarAsync(http, cenario.Organizacao.Com(Perfil.Supervisor))).TokenDeAcesso;
        var url = $"/api/motoristas/{cenario.Motorista}/posicoes"
            + $"?de={Uri.EscapeDataString(de.ToString("o"))}&ate={Uri.EscapeDataString(ate.ToString("o"))}";

        using var resposta = await EnviarAsync(http, HttpMethod.Get, url, supervisor);
        var json = await JsonAsync(resposta);
        Assert.True(resposta.StatusCode == HttpStatusCode.OK, $"{(int)resposta.StatusCode}: {json}");

        return json.GetProperty("posicoes").GetArrayLength();
    }

    /// <summary>A posição atual do motorista, ou <see langword="null"/> quando não há.</summary>
    protected static async Task<JsonElement?> PosicaoAtualAsync(HttpClient http, CenarioDeRetencao cenario)
    {
        var supervisor = (await EntrarAsync(http, cenario.Organizacao.Com(Perfil.Supervisor))).TokenDeAcesso;

        using var resposta = await EnviarAsync(http, HttpMethod.Get, $"/api/motoristas/{cenario.Motorista}/posicao-atual", supervisor);

        if (resposta.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var json = await JsonAsync(resposta);
        Assert.True(resposta.StatusCode == HttpStatusCode.OK, $"{(int)resposta.StatusCode}: {json}");
        return json;
    }

    /// <summary>Monta a operação pelo fluxo real da API.</summary>
    protected async Task<CenarioDeRetencao> CenarioAsync(HttpClient http)
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Supervisor, Perfil.Motorista);
        var supervisor = (await EntrarAsync(http, organizacao.Com(Perfil.Supervisor))).TokenDeAcesso;
        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);

        var agora = Tempo.GetUtcNow();
        var entrega = await CriarAsync(
            http, supervisor, "/api/entregas", RoteirosDeEntrega.Corpo(clienteId, destinatarioId, agora.AddHours(1), agora.AddHours(6)));

        var motorista = await CriarAsync(http, supervisor, "/api/motoristas", RoteirosDeCadastro.Obter("motorista").Corpo());
        await OkAsync(http, HttpMethod.Put, $"/api/motoristas/{motorista}/conta", supervisor, new { usuarioId = organizacao.Com(Perfil.Motorista).Id });
        var veiculo = await CriarAsync(http, supervisor, "/api/veiculos", RoteirosDeCadastro.Obter("veiculo").Corpo());

        var dia = agora.UtcDateTime.Date.AddDays(1);
        var rota = await CriarAsync(http, supervisor, "/api/rotas", new { data = DateOnly.FromDateTime(dia) });
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/paradas", supervisor, new { entregas = new[] { entrega } });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/motorista", supervisor, new { motoristaId = motorista });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/veiculo", supervisor, new { veiculoId = veiculo });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/saida", supervisor, new { saidaPlanejada = new DateTimeOffset(dia.AddHours(8), TimeSpan.Zero) });
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/planejamento", supervisor);

        var tokenDoMotorista = (await EntrarAsync(http, organizacao.Com(Perfil.Motorista))).TokenDeAcesso;
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{rota}/inicio", tokenDoMotorista);

        return new CenarioDeRetencao(organizacao, motorista, supervisor);
    }

    /// <summary>JSON de uma resposta 200.</summary>
    protected static async Task<JsonElement> OkAsync(HttpClient http, HttpMethod metodo, string url, string token, object? corpo = null)
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
}

/// <summary>Prazo de um dia, lote padrão.</summary>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class RetencaoTestes(ContainerPostgis banco) : TesteDeRetencao(banco)
{
    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?> ConfiguracaoAdicional => new Dictionary<string, string?>
    {
        ["Torre:Retencao:PosicoesBrutas"] = "1.00:00:00",
        ["Torre:Retencao:LimparEmSegundoPlano"] = "false",
    };

    /// <summary>
    /// Critério da Fase 20: o rastro bruto vencido sai, e o estado operacional continua de pé.
    /// </summary>
    /// <remarks>
    /// O ponto forte do teste é a segunda asserção: apagar o caminho percorrido não pode cegar a torre
    /// sobre onde o motorista está. Rastro e estado atual são coisas diferentes, e só o rastro vence.
    /// </remarks>
    [Fact]
    public async Task ApagaORastroVencidoESemCegarATorre()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var inicio = Tempo.GetUtcNow();

        await EnviarPosicoesAsync(http, cenario, 101, 102, 103);
        Assert.Equal(3, await HistoricoAsync(http, cenario, inicio.AddHours(-1), inicio.AddHours(1)));

        // Nada vencido ainda: a limpeza não tira o que está dentro do prazo.
        Assert.Equal(0, (await LimparAsync()).PosicoesRemovidas);
        Assert.Equal(3, await HistoricoAsync(http, cenario, inicio.AddHours(-1), inicio.AddHours(1)));

        // Dois dias depois, o prazo de um dia venceu para as três.
        Tempo.SetUtcNow(inicio.AddDays(2));
        var limpeza = await LimparAsync();

        Assert.Equal(3, limpeza.PosicoesRemovidas);
        Assert.False(limpeza.RestouTrabalho);
        Assert.Equal(0, await HistoricoAsync(http, cenario, inicio.AddHours(-1), inicio.AddHours(1)));

        // A projeção de estado atual continua: a torre ainda sabe onde o motorista parou.
        var atual = await PosicaoAtualAsync(http, cenario);
        Assert.NotNull(atual);
        Assert.Equal(103, atual.Value.GetProperty("sequencia").GetInt64());

        // E o que chega depois do expurgo fica: a limpeza é por idade, não é um apagar geral.
        await EnviarPosicoesAsync(http, cenario, 104);
        var depois = Tempo.GetUtcNow();
        Assert.Equal(0, (await LimparAsync()).PosicoesRemovidas);
        Assert.Equal(1, await HistoricoAsync(http, cenario, depois.AddHours(-1), depois.AddHours(1)));
    }

    /// <summary>A limpeza não alcança a telemetria de outra organização que ainda está no prazo.</summary>
    [Fact]
    public async Task NaoApagaOQueAindaEstaNoPrazoDeOutraOrganizacao()
    {
        using var http = Cliente();
        var antiga = await CenarioAsync(http);
        var inicio = Tempo.GetUtcNow();
        await EnviarPosicoesAsync(http, antiga, 201, 202);

        // Dois dias à frente, outra organização começa a operar agora.
        Tempo.SetUtcNow(inicio.AddDays(2));
        var recente = await CenarioAsync(http);
        var agora = Tempo.GetUtcNow();
        await EnviarPosicoesAsync(http, recente, 301, 302, 303);

        var limpeza = await LimparAsync();

        Assert.Equal(2, limpeza.PosicoesRemovidas);
        Assert.Equal(0, await HistoricoAsync(http, antiga, inicio.AddHours(-1), inicio.AddHours(1)));
        Assert.Equal(3, await HistoricoAsync(http, recente, agora.AddHours(-1), agora.AddHours(1)));
    }
}

/// <summary>Lote de uma linha e uma só rodada: prova que a limpeza cede o banco em vez de segurá-lo.</summary>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class RetencaoEmLotesTestes(ContainerPostgis banco) : TesteDeRetencao(banco)
{
    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?> ConfiguracaoAdicional => new Dictionary<string, string?>
    {
        ["Torre:Retencao:PosicoesBrutas"] = "1.00:00:00",
        ["Torre:Retencao:LimparEmSegundoPlano"] = "false",
        ["Torre:Retencao:TamanhoDoLote"] = "100",
        ["Torre:Retencao:LotesPorRodada"] = "1",
    };

    /// <summary>Uma rodada apaga no máximo um lote, avisa que restou trabalho e termina na rodada seguinte.</summary>
    [Fact]
    public async Task ParaNoTetoDaRodadaEAvisaQueRestouTrabalho()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var inicio = Tempo.GetUtcNow();

        // Cento e uma posições vencidas, contra um lote de cem por rodada.
        await EnviarPosicoesAsync(http, cenario, [.. Enumerable.Range(401, 101).Select(numero => (long)numero)]);
        Tempo.SetUtcNow(inicio.AddDays(2));

        var primeira = await LimparAsync();
        Assert.Equal(100, primeira.PosicoesRemovidas);
        Assert.True(primeira.RestouTrabalho, "A rodada encheu o lote e precisa avisar que sobrou.");
        Assert.Equal(1, await HistoricoAsync(http, cenario, inicio.AddHours(-1), inicio.AddHours(1)));

        var segunda = await LimparAsync();
        Assert.Equal(1, segunda.PosicoesRemovidas);
        Assert.False(segunda.RestouTrabalho);
        Assert.Equal(0, await HistoricoAsync(http, cenario, inicio.AddHours(-1), inicio.AddHours(1)));
    }
}

/// <summary>Prazo zerado na configuração: o piso protege o que ainda está em uso.</summary>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class RetencaoComPrazoInvalidoTestes(ContainerPostgis banco) : TesteDeRetencao(banco)
{
    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?> ConfiguracaoAdicional => new Dictionary<string, string?>
    {
        ["Torre:Retencao:PosicoesBrutas"] = "00:00:00",
        ["Torre:Retencao:LimparEmSegundoPlano"] = "false",
    };

    /// <summary>
    /// Prazo abaixo do piso não apaga a operação do dia.
    /// </summary>
    /// <remarks>
    /// Retenção zerada apagaria a posição no instante seguinte ao envio, e a torre perderia o rastro da
    /// operação em curso por causa de uma linha de configuração. O piso de um dia transforma o erro de
    /// digitação em comportamento previsível.
    /// </remarks>
    [Fact]
    public async Task PrazoAbaixoDoPisoNaoApagaAOperacaoDoDia()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var inicio = Tempo.GetUtcNow();

        await EnviarPosicoesAsync(http, cenario, 501, 502);

        // Duas horas depois: com prazo zero valendo, teriam sumido; com o piso, ficam.
        Tempo.SetUtcNow(inicio.AddHours(2));
        Assert.Equal(0, (await LimparAsync()).PosicoesRemovidas);
        Assert.Equal(2, await HistoricoAsync(http, cenario, inicio.AddHours(-1), inicio.AddHours(1)));

        // Passado o piso, saem.
        Tempo.SetUtcNow(inicio.AddDays(2));
        Assert.Equal(2, (await LimparAsync()).PosicoesRemovidas);
        Assert.Equal(0, await HistoricoAsync(http, cenario, inicio.AddHours(-1), inicio.AddHours(1)));
    }
}
