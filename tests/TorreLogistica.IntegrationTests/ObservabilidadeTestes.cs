using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using TorreLogistica.Application.Observabilidade;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// Diagnóstico ponta a ponta: o que aconteceu com uma entrega, contado por rastros e logs.
/// </summary>
/// <remarks>
/// O critério de aceite da Fase 21 é operacional, não técnico: alguém precisa explicar uma entrega
/// problemática <b>sem abrir o banco</b>. O teste reproduz o caso que mais dói — o aviso ao cliente não
/// saiu — e prova que, partindo do rastro da requisição que concluiu a entrega, chega-se ao webhook que
/// falhou, atravessando a fila que separa um do outro.
/// </remarks>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class ObservabilidadeTestes(ContainerPostgis banco) : TesteDeWebhook(banco)
{
    /// <summary>Critério de aceite da Fase 21.</summary>
    [Fact]
    public async Task EntregaProblematicaEhRastreavelDoInicioAoWebhookQueFalhou()
    {
        using var coletor = new ColetorDeRastros();
        using var http = Cliente();

        var cenario = await CenarioAsync(http);
        await AssinarAsync(http, cenario);
        Assinante.Modo = ModoDoAssinante.Quebra;

        // O console propaga o contexto de rastro do W3C. Fixá-lo aqui é o que torna a prova
        // determinística: o identificador que o cliente carrega é o mesmo que deve reaparecer no fim.
        var rastroDoCliente = ActivityTraceId.CreateRandom();
        var spanDoCliente = ActivitySpanId.CreateRandom();

        using (var requisicao = new HttpRequestMessage(HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/conclusao"))
        {
            requisicao.Headers.Add("traceparent", $"00-{rastroDoCliente}-{spanDoCliente}-01");
            requisicao.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cenario.TokenDoMotorista);

            using var resposta = await http.SendAsync(requisicao, Cancelamento);
            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        }

        await DespacharAsync(cenario.Organizacao.Id);
        await EntregarAsync();

        // O assinante recebeu a chamada e recusou com 500 — que é o caso difícil: do lado de fora, o
        // cliente diz que não foi avisado, e do lado de dentro "a mensagem saiu". Quem decide é o rastro.
        Assert.Contains(Assinante.Recebidos, recebido => recebido.Tipo == "delivery.completed");

        // E o rastro conta essa história inteira, sob o identificador que o cliente já tinha em mãos.
        var doMesmoRastro = coletor.Por(rastroDoCliente);

        var conclusao = Assert.Single(
            doMesmoRastro,
            atividade => atividade.DisplayName.Contains("conclusao", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(rastroDoCliente, conclusao.TraceId);

        var entregaDoWebhook = Assert.Single(
            doMesmoRastro,
            atividade => atividade.OperationName == "webhook.entrega");
        Assert.Equal(rastroDoCliente, entregaDoWebhook.TraceId);
        Assert.Equal(500, entregaDoWebhook.GetTagItem("webhook.status"));
        Assert.Equal(ActivityStatusCode.Error, entregaDoWebhook.Status);
        Assert.Equal("delivery.completed", entregaDoWebhook.GetTagItem("webhook.tipo"));

        // O comando SQL da conclusão entra no mesmo rastro, sem instrumentação escrita à mão.
        Assert.Contains(doMesmoRastro, atividade => atividade.Source.Name == "Npgsql");

        // E o log dá a ponte de volta: quem tem a linha de log chega ao rastro, e vice-versa.
        var registrado = Fabrica.Logs.TextoCompleto();
        Assert.Contains(rastroDoCliente.ToHexString(), registrado, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// O despacho do outbox tem rastro próprio, porque junta eventos de origens diferentes.
    /// </summary>
    /// <remarks>
    /// Pendurar o lote inteiro num dos rastros de origem seria mentira: as outras mensagens apareceriam
    /// dentro de uma operação com a qual não têm relação.
    /// </remarks>
    [Fact]
    public async Task DespachoDoOutboxTemRastroProprioSeparadoDaOrigem()
    {
        using var coletor = new ColetorDeRastros();
        using var http = Cliente();

        var cenario = await CenarioAsync(http);
        await AssinarAsync(http, cenario);
        Assinante.Modo = ModoDoAssinante.Confirma;

        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/conclusao", cenario.TokenDoMotorista, null);
        await DespacharAsync(cenario.Organizacao.Id);
        await EntregarAsync();

        var despachos = coletor.Todos.Where(atividade => atividade.OperationName == "outbox.despacho").ToList();
        Assert.NotEmpty(despachos);
        Assert.Contains(despachos, despacho => Convert.ToInt32(despacho.GetTagItem("outbox.entregas_criadas")!, provider: null) > 0);

        // A entrega do webhook, essa sim, herda o rastro da conclusão — e não o do despachante. A rota
        // iniciada no cenário também gerou aviso, por isso o recorte pelo tipo.
        var entrega = Assert.Single(
            coletor.Todos,
            atividade => atividade.OperationName == "webhook.entrega"
                && (string?)atividade.GetTagItem("webhook.tipo") == "delivery.completed");
        Assert.DoesNotContain(despachos, despacho => despacho.TraceId == entrega.TraceId);
    }

    /// <summary>As medidas de estado respondem "quantos, agora" a partir do banco real.</summary>
    [Fact]
    public async Task MedidasDeEstadoContamAOperacaoEmAndamento()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);

        var retrato = await LerEstadoAsync();

        // A entrega do cenário está em rota, e o motorista dela está em rota sem ter mandado posição:
        // offline é exatamente o que ele é, e é isso que a torre precisa ver.
        Assert.True(retrato.EntregasEmRota >= 1, $"Esperava ao menos uma entrega em rota, veio {retrato.EntregasEmRota}.");
        Assert.True(retrato.MotoristasOffline >= 1, $"Esperava ao menos um motorista offline, veio {retrato.MotoristasOffline}.");

        // Concluir a entrega tira ela da contagem e põe uma mensagem no outbox.
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/conclusao", cenario.TokenDoMotorista, null);

        var depois = await LerEstadoAsync();
        Assert.Equal(retrato.EntregasEmRota - 1, depois.EntregasEmRota);
        Assert.True(depois.OutboxPendente >= 1, "A conclusão devia ter deixado evento pendente no outbox.");
    }

    private async Task<RetratoDaOperacao> LerEstadoAsync()
    {
        using var escopo = Fabrica.Services.CreateScope();
        return await escopo.ServiceProvider.GetRequiredService<LeituraDoEstadoDaOperacao>().LerAsync(Cancelamento);
    }
}

/// <summary>
/// Escuta os rastros produzidos durante o teste.
/// </summary>
/// <remarks>
/// Sem um ouvinte registrado, o .NET nem cria as atividades — a instrumentação é inerte quando ninguém
/// coleta, que é justamente o que mantém o custo perto de zero em produção sem coletor.
/// </remarks>
internal sealed class ColetorDeRastros : IDisposable
{
    private readonly ConcurrentBag<Activity> _atividades = [];
    private readonly ActivityListener _ouvinte;

    public ColetorDeRastros()
    {
        _ouvinte = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = atividade => _atividades.Add(atividade),
        };

        ActivitySource.AddActivityListener(_ouvinte);
    }

    public IReadOnlyList<Activity> Todos => [.. _atividades];

    public IReadOnlyList<Activity> Por(ActivityTraceId rastro) =>
        [.. _atividades.Where(atividade => atividade.TraceId == rastro)];

    public void Dispose() => _ouvinte.Dispose();
}
