using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using TorreLogistica.Application.Webhooks;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.Domain.Webhooks;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// Outbox, entrega de webhook, retentativa, desistência visível e reenvio manual.
/// </summary>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class WebhooksTestes(ContainerPostgis banco) : TesteDeWebhook(banco)
{
    [Fact]
    public async Task OEventoNasceNoMesmoCommitDoFatoMesmoSemAssinante()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);

        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/conclusao", cenario.TokenDoMotorista, null);

        // Sem assinatura cadastrada o evento existe do mesmo jeito: o registro do fato não depende de
        // haver alguém interessado hoje.
        Assert.Equal("1", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM outbox WHERE entrega_id = @id AND tipo = 'delivery.completed'", ("id", cenario.Entrega)));

        Assert.Equal("1", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM outbox WHERE entrega_id = @id AND tipo = 'delivery.started'", ("id", cenario.Entrega)));
    }

    [Fact]
    public async Task OEventoChegaAoAssinanteAssinadoEComIdentificadorParaDeduplicar()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var assinatura = await AssinarAsync(http, cenario);

        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/conclusao", cenario.TokenDoMotorista, null);

        await DespacharAsync();
        await EntregarAsync();

        var recebidos = Assinante.Recebidos;
        var concluida = Assert.Single(recebidos, recebido => recebido.Tipo == TiposDeEventoDeWebhook.EntregaConcluida);

        Assert.True(Guid.TryParse(concluida.EventoId, out _), "O cabeçalho de identificação do evento não veio.");
        Assert.Contains(cenario.Entrega.ToString(), concluida.Corpo, StringComparison.OrdinalIgnoreCase);

        // A conferência é a mesma que documentamos para o assinante.
        Assert.True(
            AssinaturaHmacDeWebhook.Confere(
                concluida.Assinatura, assinatura.Segredo, concluida.Corpo, Tempo.GetUtcNow(), PoliticaDeWebhook.ToleranciaDoCarimbo),
            "A assinatura recebida não confere com o segredo emitido.");

        // Segredo errado precisa falhar, senão a conferência acima não prova nada.
        Assert.False(AssinaturaHmacDeWebhook.Confere(
            concluida.Assinatura, "outro-segredo", concluida.Corpo, Tempo.GetUtcNow(), PoliticaDeWebhook.ToleranciaDoCarimbo));

        Assert.Equal("Entregue", await Banco.ConsultarEscalarAsync(
            "SELECT estado FROM entregas_de_webhook WHERE mensagem_id = (SELECT id FROM outbox WHERE entrega_id = @id AND tipo = 'delivery.completed')",
            ("id", cenario.Entrega)));
    }

    [Fact]
    public async Task DespacharDuasVezesNaoGeraEntregaRepetida()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        await AssinarAsync(http, cenario);

        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/conclusao", cenario.TokenDoMotorista, null);

        await DespacharAsync();
        await DespacharAsync();

        Assert.Equal("2", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM entregas_de_webhook WHERE organizacao_id = @id", ("id", cenario.Organizacao.Id)));

        Assert.Equal("0", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM outbox WHERE organizacao_id = @id AND despachada_em IS NULL", ("id", cenario.Organizacao.Id)));
    }

    [Fact]
    public async Task AssinanteQuebradoEhRetentadoEDepoisDesisteDeFormaVisivel()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        await AssinarAsync(http, cenario);
        Assinante.Modo = ModoDoAssinante.Quebra;

        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/conclusao", cenario.TokenDoMotorista, null);
        await DespacharAsync();

        var entregaId = await EntregarAteDesistirAsync(cenario);

        // Horas se passaram no relógio do teste para vencer o backoff, e o token de acesso é curto: o
        // operador que vai olhar a fila depois disso entra de novo, como qualquer pessoa faria.
        var administrador = await EntrarDeNovoAsync(http, cenario);
        var detalhe = await OkAsync(http, HttpMethod.Get, $"/api/webhooks/entregas/{entregaId}", administrador, null);

        var tentativas = detalhe.GetProperty("entrega").GetProperty("tentativas").GetInt32();

        Assert.Equal("Falhada", detalhe.GetProperty("entrega").GetProperty("estado").GetString());
        Assert.Equal(500, detalhe.GetProperty("entrega").GetProperty("ultimoStatus").GetInt32());

        // "No mínimo", e não "exatamente": a entrega é no mínimo-uma-vez, e uma tentativa a mais é
        // comportamento previsto quando um arrendamento vence. O que a política garante é que não se
        // desiste antes do limite.
        Assert.True(
            tentativas >= PoliticaDeWebhook.MaximoDeTentativas,
            $"Desistiu com {tentativas} tentativas, antes do limite de {PoliticaDeWebhook.MaximoDeTentativas}.");

        // Nada se perdeu em silêncio: o histórico tem uma linha para cada tentativa contada.
        Assert.Equal(tentativas, detalhe.GetProperty("tentativas").GetArrayLength());
    }

    [Fact]
    public async Task ReenvioManualColocaDeVoltaNaFilaEEntrega()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        await AssinarAsync(http, cenario);
        Assinante.Modo = ModoDoAssinante.Quebra;

        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/conclusao", cenario.TokenDoMotorista, null);
        await DespacharAsync();

        var entregaId = await EntregarAteDesistirAsync(cenario);

        // O assinante voltou do ar; o operador entra de novo e manda reenviar.
        Assinante.Modo = ModoDoAssinante.Confirma;
        Assinante.Limpar();

        var administrador = await EntrarDeNovoAsync(http, cenario);

        var reenviada = await OkAsync(
            http, HttpMethod.Post, $"/api/webhooks/entregas/{entregaId}/reenvio", administrador, null);

        Assert.Equal("Pendente", reenviada.GetProperty("estado").GetString());
        Assert.Equal(0, reenviada.GetProperty("tentativas").GetInt32());

        await EntregarAsync();

        Assert.Equal("Entregue", await Banco.ConsultarEscalarAsync(
            "SELECT estado FROM entregas_de_webhook WHERE id = @id", ("id", entregaId)));

        Assert.NotEmpty(Assinante.Recebidos);
    }

    [Fact]
    public async Task ReenviarEntregaQueNaoFalhouEhRecusado()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        await AssinarAsync(http, cenario);

        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/conclusao", cenario.TokenDoMotorista, null);
        await DespacharAsync();

        var pendente = await Banco.ConsultarEscalarAsync(
            "SELECT id FROM entregas_de_webhook WHERE organizacao_id = @id LIMIT 1", ("id", cenario.Organizacao.Id));

        using var resposta = await EnviarAsync(
            http, HttpMethod.Post, $"/api/webhooks/entregas/{pendente}/reenvio", cenario.Administrador);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        Assert.Equal("entrega_nao_reenviavel", await CodigoDoErroAsync(resposta));
    }

    [Fact]
    public async Task AssinaturaRecebeSoOsEventosQueEscolheuENadaDepoisDeRevogada()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var assinatura = await AssinarAsync(http, cenario, [TiposDeEventoDeWebhook.EntregaConcluida]);

        // A saída para rota já aconteceu no cenário: ela não é assinada, e não pode virar entrega.
        await DespacharAsync();

        Assert.Equal("0", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM entregas_de_webhook WHERE organizacao_id = @id", ("id", cenario.Organizacao.Id)));

        await OkAsync(http, HttpMethod.Post, $"/api/webhooks/assinaturas/{assinatura.Id}/revogacao", cenario.Administrador, null);
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/conclusao", cenario.TokenDoMotorista, null);
        await DespacharAsync();

        // Revogada, nem o evento que ela assinava gera entrega.
        Assert.Equal("0", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM entregas_de_webhook WHERE organizacao_id = @id", ("id", cenario.Organizacao.Id)));
    }

    [Fact]
    public async Task OSegredoNaoApareceEmConsultaNemNaAuditoria()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var assinatura = await AssinarAsync(http, cenario);

        using var listagem = await EnviarAsync(http, HttpMethod.Get, "/api/webhooks/assinaturas", cenario.Administrador);
        var bruto = await listagem.Content.ReadAsStringAsync(Cancelamento);

        Assert.DoesNotContain(assinatura.Segredo, bruto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("segredo", bruto, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("0", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM eventos_de_auditoria WHERE dados::text LIKE @busca", ("busca", $"%{assinatura.Segredo}%")));

        // No banco o segredo está cifrado: procurar o valor emitido não acha nada.
        Assert.Equal("0", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM assinaturas_de_webhook WHERE encode(segredo_cifrado, 'escape') LIKE @busca",
            ("busca", $"%{assinatura.Segredo}%")));
    }

    [Fact]
    public async Task HistoricoDeTentativaEhSomenteInsercao()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        await AssinarAsync(http, cenario);

        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/conclusao", cenario.TokenDoMotorista, null);
        await DespacharAsync();
        await EntregarAsync();

        var erro = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => Banco.ExecutarAsync(
            "UPDATE tentativas_de_webhook SET status = 999 WHERE organizacao_id = @id", ("id", cenario.Organizacao.Id)));

        Assert.Equal(Npgsql.PostgresErrorCodes.InsufficientPrivilege, erro.SqlState);
    }

    [Fact]
    public async Task OutraOrganizacaoNaoEnxergaAssinaturaNemEntrega()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var outra = await CenarioAsync(http);
        var assinatura = await AssinarAsync(http, cenario);

        using var deOutra = await EnviarAsync(
            http, HttpMethod.Get, $"/api/webhooks/assinaturas/{assinatura.Id}", outra.Administrador);
        using var inventada = await EnviarAsync(
            http, HttpMethod.Get, $"/api/webhooks/assinaturas/{Guid.CreateVersion7()}", outra.Administrador);

        Assert.Equal(HttpStatusCode.NotFound, deOutra.StatusCode);
        Assert.Equal(await CodigoDoErroAsync(inventada), await CodigoDoErroAsync(deOutra));

        var lista = await OkAsync(http, HttpMethod.Get, "/api/webhooks/assinaturas", outra.Administrador, null);
        Assert.Equal(0, lista.GetProperty("total").GetInt32());
    }

    /// <summary>Autentica o administrador outra vez, depois de o relógio do teste andar horas.</summary>
    private static async Task<string> EntrarDeNovoAsync(HttpClient http, CenarioDeWebhook cenario) =>
        (await EntrarAsync(http, cenario.Organizacao.Com(Perfil.Administrador))).TokenDeAcesso;

    /// <summary>Tenta até a entrega desistir, avançando o relógio para vencer cada espera do backoff.</summary>
    private async Task<Guid> EntregarAteDesistirAsync(CenarioDeWebhook cenario)
    {
        for (var rodada = 0; rodada < PoliticaDeWebhook.MaximoDeTentativas * 2; rodada++)
        {
            await EntregarAsync();
            Tempo.Advance(TimeSpan.FromHours(3));

            var falhada = await Banco.ConsultarEscalarAsync(
                "SELECT id FROM entregas_de_webhook WHERE organizacao_id = @id AND estado = 'Falhada' LIMIT 1",
                ("id", cenario.Organizacao.Id));

            if (falhada is not null)
            {
                return Guid.Parse(falhada);
            }
        }

        var situacao = await Banco.ConsultarEscalarAsync(
            "SELECT string_agg(estado || ' com ' || tentativas || ' tentativa(s)', '; ') FROM entregas_de_webhook WHERE organizacao_id = @id",
            ("id", cenario.Organizacao.Id));

        Assert.Fail($"Nenhuma entrega desistiu depois de {PoliticaDeWebhook.MaximoDeTentativas * 2} rodadas. Situação: {situacao ?? "nenhuma entrega criada"}.");
        return Guid.Empty;
    }
}

/// <summary>Com tempo limite curto, assinante lento conta como falha.</summary>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class WebhookComTempoLimiteCurtoTestes(ContainerPostgis banco) : TesteDeWebhook(banco)
{
    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?> ConfiguracaoAdicional =>
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Torre:Webhooks:TempoLimite"] = "00:00:01",
        };

    [Fact]
    public async Task AssinanteQueNaoRespondeNoPrazoContaComoFalhaERetenta()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        await AssinarAsync(http, cenario);

        Assinante.Modo = ModoDoAssinante.Demora;
        Assinante.Demora = TimeSpan.FromSeconds(4);

        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/conclusao", cenario.TokenDoMotorista, null);
        await DespacharAsync();
        await EntregarAsync();

        var entregaId = await Banco.ConsultarEscalarAsync(
            "SELECT id FROM entregas_de_webhook WHERE organizacao_id = @id LIMIT 1", ("id", cenario.Organizacao.Id));

        var detalhe = await OkAsync(http, HttpMethod.Get, $"/api/webhooks/entregas/{entregaId}", cenario.Administrador, null);
        var tentativa = detalhe.GetProperty("tentativas")[0];

        Assert.Equal("Pendente", detalhe.GetProperty("entrega").GetProperty("estado").GetString());
        Assert.Equal(1, detalhe.GetProperty("entrega").GetProperty("tentativas").GetInt32());
        Assert.Equal(JsonValueKind.Null, tentativa.GetProperty("status").ValueKind);
        Assert.Contains("tempo limite", tentativa.GetProperty("erro").GetString()!, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Cenário comum: organização com administrador, rota iniciada e assinante de webhook no ar.</summary>
public abstract class TesteDeWebhook(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    private AssinanteDeWebhookDeTeste? _assinante;

    /// <summary>Relógio da API.</summary>
    protected FakeTimeProvider Tempo { get; } = new(DateTimeOffset.UtcNow);

    /// <inheritdoc />
    protected override TimeProvider Relogio => Tempo;

    /// <summary>Assinante HTTP de verdade, em porta efêmera.</summary>
    protected AssinanteDeWebhookDeTeste Assinante =>
        _assinante ?? throw new InvalidOperationException("Assinante não iniciado.");

    /// <summary>Organização, contas e a entrega em execução.</summary>
    protected sealed record CenarioDeWebhook(
        OrganizacaoDeTeste Organizacao,
        string Administrador,
        string TokenDoMotorista,
        Guid Entrega);

    /// <inheritdoc />
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        _assinante = await AssinanteDeWebhookDeTeste.IniciarAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (_assinante is not null)
        {
            await _assinante.DisposeAsync().ConfigureAwait(false);
        }

        await base.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>Cria organização, motorista, rota planejada e inicia a rota.</summary>
    protected async Task<CenarioDeWebhook> CenarioAsync(HttpClient http)
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Administrador, Perfil.Motorista);
        var contaDoMotorista = organizacao.Com(Perfil.Motorista);
        var administrador = (await EntrarAsync(http, organizacao.Com(Perfil.Administrador))).TokenDeAcesso;
        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, administrador);

        var entrega = await CriarAsync(http, administrador, "/api/entregas", RoteirosDeEntrega.Corpo(clienteId, destinatarioId));
        var motorista = await CriarAsync(http, administrador, "/api/motoristas", RoteirosDeCadastro.Obter("motorista").Corpo());
        await OkAsync(http, HttpMethod.Put, $"/api/motoristas/{motorista}/conta", administrador, new { usuarioId = contaDoMotorista.Id });
        var tokenDoMotorista = (await EntrarAsync(http, contaDoMotorista)).TokenDeAcesso;

        var veiculo = await CriarAsync(http, administrador, "/api/veiculos", RoteirosDeCadastro.Obter("veiculo").Corpo());
        var rota = await CriarAsync(http, administrador, "/api/rotas", new { data = DateOnly.FromDateTime(Tempo.GetUtcNow().UtcDateTime.Date.AddDays(1)) });
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/paradas", administrador, new { entregas = new[] { entrega } });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/motorista", administrador, new { motoristaId = motorista });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/veiculo", administrador, new { veiculoId = veiculo });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/saida", administrador, new
        {
            saidaPlanejada = new DateTimeOffset(Tempo.GetUtcNow().UtcDateTime.Date.AddDays(1).AddHours(8), TimeSpan.Zero),
        });
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/planejamento", administrador, null);
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{rota}/inicio", tokenDoMotorista, null);
        await IsolarFilaAsync(organizacao.Id);

        return new CenarioDeWebhook(organizacao, administrador, tokenDoMotorista, entrega);
    }

    /// <summary>Cria a assinatura apontando para o assinante de teste.</summary>
    protected async Task<AssinaturaCriada> AssinarAsync(
        HttpClient http,
        CenarioDeWebhook cenario,
        IReadOnlyList<string>? eventos = null)
    {
        using var resposta = await EnviarAsync(http, HttpMethod.Post, "/api/webhooks/assinaturas", cenario.Administrador, new
        {
            nome = "ERP do cliente",
            url = Assinante.Url,
            eventos,
        });

        var json = await JsonAsync(resposta);
        Assert.True(resposta.StatusCode == HttpStatusCode.Created, $"assinatura: {(int)resposta.StatusCode} {json}");

        return new AssinaturaCriada(
            json.GetProperty("id").GetGuid(),
            json.GetProperty("nome").GetString()!,
            json.GetProperty("url").GetString()!,
            json.GetProperty("segredo").GetString()!,
            [.. json.GetProperty("eventos").EnumerateArray().Select(evento => evento.GetString()!)]);
    }

    /// <summary>
    /// Tira da fila o que pertence a outras organizações, para esta rodada ver só o próprio cenário.
    /// </summary>
    /// <remarks>
    /// O despachante e o entregador varrem o banco inteiro — é isso que se quer em produção, onde o
    /// processo atende todas as organizações. Como a coleção de testes compartilha o mesmo banco, sem
    /// isolar aqui um teste tentaria entregar o webhook que outro deixou pendente, para uma porta que já
    /// foi fechada.
    /// </remarks>
    private async Task IsolarFilaAsync(Guid organizacaoId)
    {
        await Banco.ExecutarAsync(
            "UPDATE outbox SET despachada_em = now() WHERE organizacao_id <> @id AND despachada_em IS NULL",
            ("id", organizacaoId));

        // Trinta dias, e não um: o teste de desistência avança o relógio horas a fio para vencer o
        // backoff, e uma margem curta faria as entregas de outros testes voltarem à fila no meio da
        // rodada e disputarem o lote com a que está sendo observada.
        await Banco.ExecutarAsync(
            "UPDATE entregas_de_webhook SET disponivel_em = now() + interval '30 days' WHERE organizacao_id <> @id AND estado = 'Pendente'",
            ("id", organizacaoId));
    }

    /// <summary>Roda uma volta do despachante do outbox.</summary>
    protected async Task DespacharAsync()
    {
        using var escopo = Fabrica.Services.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<DespachoDeWebhooks>().DespacharLoteAsync(Cancelamento);
    }

    /// <summary>Roda uma volta do entregador.</summary>
    protected async Task EntregarAsync()
    {
        using var escopo = Fabrica.Services.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<EntregaDeWebhooks>().EntregarLoteAsync(Cancelamento);
    }

    /// <summary>Envia e exige 200, devolvendo o corpo.</summary>
    protected static async Task<JsonElement> OkAsync(
        HttpClient http,
        HttpMethod metodo,
        string caminho,
        string token,
        object? corpo)
    {
        using var resposta = await EnviarAsync(http, metodo, caminho, token, corpo);
        var json = await JsonAsync(resposta);
        Assert.True(resposta.StatusCode == HttpStatusCode.OK, $"{metodo} {caminho}: {(int)resposta.StatusCode} {json}");
        return json;
    }

    /// <summary>Cria um recurso e devolve o identificador.</summary>
    protected static async Task<Guid> CriarAsync(HttpClient http, string token, string caminho, object corpo)
    {
        using var resposta = await EnviarAsync(http, HttpMethod.Post, caminho, token, corpo);
        var json = await JsonAsync(resposta);
        Assert.True(resposta.StatusCode == HttpStatusCode.Created, $"POST {caminho}: {(int)resposta.StatusCode} {json}");
        return json.GetProperty("id").GetGuid();
    }
}
