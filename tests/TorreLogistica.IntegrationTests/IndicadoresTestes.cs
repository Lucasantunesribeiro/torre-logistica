using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// Indicadores operacionais contra a API e o PostgreSQL reais.
/// </summary>
/// <remarks>
/// <para>
/// Nenhum número aqui é escrito direto no banco: toda entrega percorre o fluxo real — criada, posta em
/// rota, atribuída, saída, chegada, conclusão ou tentativa frustrada. O relógio da API fica parado sob
/// controle do teste, então "concluída 60 minutos depois do fim da janela" é exato, não aproximado.
/// </para>
/// <para>
/// A suíte compartilha o banco, então todo indicador é conferido dentro de uma organização criada pelo
/// próprio teste: o que as outras organizações fazem em paralelo não pode aparecer na conta — e um dos
/// testes prova exatamente isso.
/// </para>
/// </remarks>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class IndicadoresTestes(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    private readonly FakeTimeProvider _tempo = new(
        new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-2).AddHours(6), TimeSpan.Zero));

    /// <inheritdoc />
    protected override TimeProvider Relogio => _tempo;

    /// <summary>
    /// Critério de aceite da Fase 19: o supervisor consegue dizer onde a operação está falhando.
    /// </summary>
    /// <remarks>
    /// A operação montada tem uma falha conhecida — duas entregas concluídas depois da janela, uma delas
    /// depois de uma tentativa frustrada e um reagendamento. O teste confere que cada número do painel
    /// aponta para essa falha, e que cada um vem com a definição de como foi calculado.
    /// </remarks>
    [Fact]
    public async Task PainelRespondeOndeAOperacaoEstaFalhando()
    {
        using var http = Cliente();
        var inicio = _tempo.GetUtcNow();
        var operacao = await OperacaoAsync(
            http, quantidadeDeEntregas: 5, janelaDe: inicio.AddHours(1), janelaAte: inicio.AddHours(3), quantidadeForaDeRota: 1);
        var (a, b, c, d, e, f) = (
            operacao.Entregas[0], operacao.Entregas[1], operacao.Entregas[2],
            operacao.Entregas[3], operacao.Entregas[4], operacao.Entregas[5]);

        // Dentro da janela: a primeira com chegada registrada 10 min antes de concluir, a segunda sem chegada.
        _tempo.SetUtcNow(inicio.AddHours(2));
        await ComandoDoMotoristaAsync(http, operacao, $"/api/motorista/entregas/{a}/chegada");
        _tempo.SetUtcNow(inicio.AddHours(2).AddMinutes(10));
        await ComandoDoMotoristaAsync(http, operacao, $"/api/motorista/entregas/{a}/conclusao");
        await ComandoDoMotoristaAsync(http, operacao, $"/api/motorista/entregas/{b}/conclusao");

        // Uma hora depois do fim da janela: uma concluída atrasada, outra com tentativa frustrada.
        _tempo.SetUtcNow(inicio.AddHours(4));
        await ComandoDoMotoristaAsync(http, operacao, $"/api/motorista/entregas/{c}/conclusao");
        var frustrada = await ComandoDoMotoristaAsync(
            http, operacao, $"/api/motorista/entregas/{d}/tentativa-frustrada", new { motivo = "DestinatarioAusente" });
        Assert.Equal("TentativaFrustrada", frustrada.GetProperty("status").GetString());

        // Uma hora e meia depois do fim da janela: a segunda atrasada. E a que nunca saiu é cancelada.
        _tempo.SetUtcNow(inicio.AddHours(4).AddMinutes(30));
        await ComandoDoMotoristaAsync(http, operacao, $"/api/motorista/entregas/{e}/conclusao");
        var supervisor = await SupervisorAsync(http, operacao);
        await OkAsync(http, HttpMethod.Post, $"/api/entregas/{f}/cancelamento", supervisor, new { motivo = "SolicitacaoDoCliente" });

        // A frustrada é reagendada e sai numa segunda rota, que a conclui dentro da nova janela.
        await OkAsync(http, HttpMethod.Post, $"/api/entregas/{d}/reagendamento", supervisor, new
        {
            prometidaDe = inicio.AddHours(6),
            prometidaAte = inicio.AddHours(8),
        });
        _tempo.SetUtcNow(inicio.AddHours(5));
        await SegundaRotaAsync(http, operacao, d);
        _tempo.SetUtcNow(inicio.AddHours(7));
        await ComandoDoMotoristaAsync(http, operacao, $"/api/motorista/entregas/{d}/conclusao");

        // Uma ocorrência registrada pela torre, além da que a tentativa frustrada gerou sozinha.
        supervisor = await SupervisorAsync(http, operacao);
        using var ocorrencia = await EnviarAsync(http, HttpMethod.Post, $"/api/entregas/{c}/ocorrencias", supervisor, new
        {
            tipo = "DificuldadeDeAcesso",
            severidade = "Media",
            observacao = "Portaria sem acesso ao pátio.",
        });
        Assert.Equal(HttpStatusCode.Created, ocorrencia.StatusCode);

        var painel = await IndicadoresAsync(http, supervisor, inicio, inicio.AddHours(9));

        // Cinco concluídas — A, B, C, E e D depois do reagendamento — e uma cancelada, contada à parte.
        Assert.Equal(5, painel.GetProperty("entregasConcluidas").GetInt32());
        Assert.Equal(1, painel.GetProperty("entregasCanceladas").GetInt32());

        // Onde está a falha: 2 das 5 saíram da janela. A cancelada não pesa contra ninguém.
        AssertIndicador(
            painel,
            "pontualidadeEmPercentual",
            valor: 60,
            baseDoCalculo: 5,
            "Entregas concluídas dentro da janela prometida, dividido pelas entregas concluídas no período. "
            + "Canceladas não entram.");

        // Quatro das cinco foram resolvidas na primeira ida; só a reagendada precisou de segunda visita.
        AssertIndicador(
            painel,
            "sucessoNaPrimeiraTentativaEmPercentual",
            valor: 80,
            baseDoCalculo: 5,
            "Entregas concluídas sem nenhuma tentativa frustrada, dividido pelas entregas concluídas no período.");

        // Quanto se atrasou quem se atrasou: 60 min e 90 min. A média não é diluída pelas que chegaram no prazo.
        AssertIndicador(
            painel,
            "atrasoMedioEmMinutos",
            valor: 75,
            baseDoCalculo: 2,
            "Média de minutos entre o fim da janela prometida e a conclusão, contando só as entregas que "
            + "passaram da janela.");

        // Só uma entrega teve chegada registrada: a base do indicador diz isso, em vez de fingir média de cinco.
        AssertIndicador(
            painel,
            "tempoMedioPorParadaEmMinutos",
            valor: 10,
            baseDoCalculo: 1,
            "Média de minutos entre a chegada registrada ao destino e a conclusão da entrega.");

        // Tempo em rota: A e B 130 min, C 240, E 270; D saiu de novo na segunda rota e levou 120.
        AssertIndicador(
            painel,
            "tempoMedioEmRotaEmMinutos",
            valor: 178,
            baseDoCalculo: 5,
            "Média de minutos entre a saída para rota e a conclusão de cada entrega.");

        // Quem entregou, e com que pontualidade.
        var porMotorista = Linhas(painel, "entregasPorMotorista");
        var linhaDoMotorista = Assert.Single(porMotorista);
        Assert.Equal(5, linhaDoMotorista.GetProperty("quantidade").GetInt32());
        Assert.Equal(60, linhaDoMotorista.GetProperty("valor").GetDouble());

        // SLA por cliente: o mesmo recorte, visto pelo lado de quem contratou.
        var linhaDoCliente = Assert.Single(Linhas(painel, "pontualidadePorCliente"));
        Assert.Equal(5, linhaDoCliente.GetProperty("quantidade").GetInt32());
        Assert.Equal(60, linhaDoCliente.GetProperty("valor").GetDouble());

        // Por que falhou: a tentativa frustrada virou ocorrência sozinha, e a torre registrou outra.
        var porMotivo = Linhas(painel, "ocorrenciasPorMotivo")
            .ToDictionary(linha => linha.GetProperty("rotulo").GetString()!, linha => linha.GetProperty("quantidade").GetInt32());
        Assert.Equal(1, porMotivo["TentativaDeEntrega"]);
        Assert.Equal(1, porMotivo["DificuldadeDeAcesso"]);

        // Por rota: a primeira levou quatro das concluídas, a segunda levou a reagendada — e, como a
        // entrega só tem uma parada ativa, ela não aparece nas duas.
        var porRota = Linhas(painel, "entregasPorRota");
        Assert.Equal(2, porRota.Count);
        Assert.Equal(4, porRota[0].GetProperty("quantidade").GetInt32());
        Assert.Equal(50, porRota[0].GetProperty("valor").GetDouble());
        Assert.Equal(1, porRota[1].GetProperty("quantidade").GetInt32());
        Assert.Equal(100, porRota[1].GetProperty("valor").GetDouble());
    }

    /// <summary>O recorte é pelo instante da conclusão, com início inclusivo e fim exclusivo.</summary>
    [Fact]
    public async Task PeriodoRecortaPeloInstanteDaConclusao()
    {
        using var http = Cliente();
        var inicio = _tempo.GetUtcNow();
        var operacao = await OperacaoAsync(http, quantidadeDeEntregas: 2, janelaDe: inicio.AddHours(1), janelaAte: inicio.AddHours(9));

        var primeira = inicio.AddHours(2);
        _tempo.SetUtcNow(primeira);
        await ComandoDoMotoristaAsync(http, operacao, $"/api/motorista/entregas/{operacao.Entregas[0]}/conclusao");

        var segunda = inicio.AddHours(4);
        _tempo.SetUtcNow(segunda);
        await ComandoDoMotoristaAsync(http, operacao, $"/api/motorista/entregas/{operacao.Entregas[1]}/conclusao");

        var supervisor = await SupervisorAsync(http, operacao);

        // Janela que pega só a primeira.
        var soAPrimeira = await IndicadoresAsync(http, supervisor, inicio, segunda);
        Assert.Equal(1, soAPrimeira.GetProperty("entregasConcluidas").GetInt32());

        // O início inclui quem foi concluída no instante exato; o fim exclui.
        var inclusivoNoInicio = await IndicadoresAsync(http, supervisor, primeira, segunda);
        Assert.Equal(1, inclusivoNoInicio.GetProperty("entregasConcluidas").GetInt32());

        var exclusivoNoFim = await IndicadoresAsync(http, supervisor, segunda, segunda.AddHours(1));
        Assert.Equal(1, exclusivoNoFim.GetProperty("entregasConcluidas").GetInt32());

        // Depois de tudo: nenhuma, e o período é devolvido como foi pedido.
        var depois = await IndicadoresAsync(http, supervisor, segunda.AddHours(1), segunda.AddHours(2));
        Assert.Equal(0, depois.GetProperty("entregasConcluidas").GetInt32());
        Assert.Equal(segunda.AddHours(1), depois.GetProperty("de").GetDateTimeOffset());
        Assert.Equal(segunda.AddHours(2), depois.GetProperty("ate").GetDateTimeOffset());
    }

    /// <summary>
    /// O mesmo instante escrito em fusos diferentes é o mesmo recorte.
    /// </summary>
    /// <remarks>
    /// O sistema persiste tudo em UTC; o fuso é assunto de quem apresenta. O que o cliente manda é um
    /// instante, e um instante não muda porque foi escrito com outro deslocamento.
    /// </remarks>
    [Fact]
    public async Task MesmoInstanteEmFusosDiferentesDaOMesmoResultado()
    {
        using var http = Cliente();
        var inicio = _tempo.GetUtcNow();
        var operacao = await OperacaoAsync(http, quantidadeDeEntregas: 1, janelaDe: inicio.AddHours(1), janelaAte: inicio.AddHours(5));

        _tempo.SetUtcNow(inicio.AddHours(2));
        await ComandoDoMotoristaAsync(http, operacao, $"/api/motorista/entregas/{operacao.Entregas[0]}/conclusao");
        var supervisor = await SupervisorAsync(http, operacao);

        var emUtc = await IndicadoresAsync(http, supervisor, inicio, inicio.AddHours(6));
        var emBrasilia = await IndicadoresAsync(
            http, supervisor, inicio.ToOffset(TimeSpan.FromHours(-3)), inicio.AddHours(6).ToOffset(TimeSpan.FromHours(-3)));
        var emLisboa = await IndicadoresAsync(
            http, supervisor, inicio.ToOffset(TimeSpan.FromHours(1)), inicio.AddHours(6).ToOffset(TimeSpan.FromHours(1)));

        Assert.Equal(1, emUtc.GetProperty("entregasConcluidas").GetInt32());
        Assert.Equal(emUtc.GetRawText(), emBrasilia.GetRawText());
        Assert.Equal(emUtc.GetRawText(), emLisboa.GetRawText());

        // A mesma hora local em fuso diferente é outro instante — e aí o recorte muda de verdade.
        var horaLocalDeOutroFuso = new DateTimeOffset(inicio.AddHours(3).DateTime, TimeSpan.FromHours(-3));
        var deslocado = await IndicadoresAsync(http, supervisor, horaLocalDeOutroFuso, horaLocalDeOutroFuso.AddHours(6));
        Assert.Equal(0, deslocado.GetProperty("entregasConcluidas").GetInt32());
    }

    /// <summary>Cancelada não é falha de pontualidade: fica fora do denominador, com contagem própria.</summary>
    [Fact]
    public async Task CanceladaNaoEntraNaContaDePontualidade()
    {
        using var http = Cliente();
        var inicio = _tempo.GetUtcNow();
        var operacao = await OperacaoAsync(
            http, quantidadeDeEntregas: 1, janelaDe: inicio.AddHours(1), janelaAte: inicio.AddHours(3), quantidadeForaDeRota: 1);

        // Uma concluída no prazo; a outra, que nem chegou a sair, cancelada bem depois do fim da janela.
        _tempo.SetUtcNow(inicio.AddHours(2));
        await ComandoDoMotoristaAsync(http, operacao, $"/api/motorista/entregas/{operacao.Entregas[0]}/conclusao");

        _tempo.SetUtcNow(inicio.AddHours(6));
        var supervisor = await SupervisorAsync(http, operacao);
        await OkAsync(
            http, HttpMethod.Post, $"/api/entregas/{operacao.Entregas[1]}/cancelamento", supervisor, new { motivo = "SolicitacaoDoCliente" });

        var painel = await IndicadoresAsync(http, supervisor, inicio, inicio.AddHours(9));

        // Se a cancelada entrasse como atraso, a pontualidade cairia para 50%.
        Assert.Equal(1, painel.GetProperty("entregasConcluidas").GetInt32());
        Assert.Equal(1, painel.GetProperty("entregasCanceladas").GetInt32());
        Assert.Equal(100, painel.GetProperty("pontualidadeEmPercentual").GetProperty("valor").GetDouble());
        Assert.Equal(1, painel.GetProperty("pontualidadeEmPercentual").GetProperty("base").GetInt32());
        Assert.Equal(0, painel.GetProperty("atrasoMedioEmMinutos").GetProperty("base").GetInt32());
    }

    /// <summary>
    /// Sem base para calcular, o valor é vazio — nunca zero.
    /// </summary>
    /// <remarks>
    /// "0% de pontualidade" e "não houve entrega" são fatos opostos. Mostrar zero nos dois casos faria o
    /// supervisor agir sobre um problema que não existe.
    /// </remarks>
    [Fact]
    public async Task SemBaseOValorEVazioENaoZero()
    {
        using var http = Cliente();
        var inicio = _tempo.GetUtcNow();
        var operacao = await OperacaoAsync(http, quantidadeDeEntregas: 1, janelaDe: inicio.AddHours(1), janelaAte: inicio.AddHours(5));
        var supervisor = await SupervisorAsync(http, operacao);

        // Período sem nenhuma entrega concluída: todo indicador vem vazio, com base zero e definição presente.
        var vazio = await IndicadoresAsync(http, supervisor, inicio, inicio.AddHours(1));
        Assert.Equal(0, vazio.GetProperty("entregasConcluidas").GetInt32());

        foreach (var nome in new[]
        {
            "pontualidadeEmPercentual",
            "sucessoNaPrimeiraTentativaEmPercentual",
            "atrasoMedioEmMinutos",
            "tempoMedioPorParadaEmMinutos",
            "tempoMedioEmRotaEmMinutos",
        })
        {
            var indicador = vazio.GetProperty(nome);
            Assert.Equal(JsonValueKind.Null, indicador.GetProperty("valor").ValueKind);
            Assert.Equal(0, indicador.GetProperty("base").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(indicador.GetProperty("definicao").GetString()));
        }

        Assert.Empty(Linhas(vazio, "entregasPorMotorista"));
        Assert.Empty(Linhas(vazio, "entregasPorRota"));

        // Entrega concluída sem chegada registrada: entra na pontualidade, não entra no tempo por parada.
        _tempo.SetUtcNow(inicio.AddHours(2));
        await ComandoDoMotoristaAsync(http, operacao, $"/api/motorista/entregas/{operacao.Entregas[0]}/conclusao");
        supervisor = await SupervisorAsync(http, operacao);

        var comDadoIncompleto = await IndicadoresAsync(http, supervisor, inicio, inicio.AddHours(9));
        Assert.Equal(1, comDadoIncompleto.GetProperty("pontualidadeEmPercentual").GetProperty("base").GetInt32());
        Assert.Equal(JsonValueKind.Null, comDadoIncompleto.GetProperty("tempoMedioPorParadaEmMinutos").GetProperty("valor").ValueKind);
        Assert.Equal(0, comDadoIncompleto.GetProperty("tempoMedioPorParadaEmMinutos").GetProperty("base").GetInt32());
    }

    /// <summary>Indicador de uma organização nunca soma a operação de outra.</summary>
    [Fact]
    public async Task IndicadoresNaoVazamEntreOrganizacoes()
    {
        using var http = Cliente();
        var inicio = _tempo.GetUtcNow();

        var minha = await OperacaoAsync(http, quantidadeDeEntregas: 1, janelaDe: inicio.AddHours(1), janelaAte: inicio.AddHours(3));
        var alheia = await OperacaoAsync(http, quantidadeDeEntregas: 3, janelaDe: inicio.AddHours(1), janelaAte: inicio.AddHours(3));

        // A minha entrega, no prazo. As três da outra organização, todas atrasadas.
        _tempo.SetUtcNow(inicio.AddHours(2));
        await ComandoDoMotoristaAsync(http, minha, $"/api/motorista/entregas/{minha.Entregas[0]}/conclusao");

        _tempo.SetUtcNow(inicio.AddHours(5));
        foreach (var entrega in alheia.Entregas)
        {
            await ComandoDoMotoristaAsync(http, alheia, $"/api/motorista/entregas/{entrega}/conclusao");
        }

        var painel = await IndicadoresAsync(http, await SupervisorAsync(http, minha), inicio, inicio.AddHours(9));
        Assert.Equal(1, painel.GetProperty("entregasConcluidas").GetInt32());
        Assert.Equal(100, painel.GetProperty("pontualidadeEmPercentual").GetProperty("valor").GetDouble());
        Assert.Equal(1, Assert.Single(Linhas(painel, "entregasPorMotorista")).GetProperty("quantidade").GetInt32());

        var painelDaOutra = await IndicadoresAsync(http, await SupervisorAsync(http, alheia), inicio, inicio.AddHours(9));
        Assert.Equal(3, painelDaOutra.GetProperty("entregasConcluidas").GetInt32());
        Assert.Equal(0, painelDaOutra.GetProperty("pontualidadeEmPercentual").GetProperty("valor").GetDouble());
    }

    /// <summary>Período impossível ou largo demais é recusado antes de varrer o banco.</summary>
    [Fact]
    public async Task PeriodoInvertidoOuLongoDemaisERecusado()
    {
        using var http = Cliente();
        var inicio = _tempo.GetUtcNow();
        var operacao = await OperacaoAsync(http, quantidadeDeEntregas: 1, janelaDe: inicio.AddHours(1), janelaAte: inicio.AddHours(3));
        var supervisor = await SupervisorAsync(http, operacao);

        using var invertido = await EnviarAsync(http, HttpMethod.Get, Url(inicio.AddDays(1), inicio), supervisor);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invertido.StatusCode);
        Assert.Equal("periodo_invalido", await CodigoDoErroAsync(invertido));

        using var longo = await EnviarAsync(http, HttpMethod.Get, Url(inicio, inicio.AddDays(200)), supervisor);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, longo.StatusCode);
        Assert.Equal("periodo_grande_demais", await CodigoDoErroAsync(longo));

        // Sem período informado, vale a janela padrão dos últimos 30 dias, fechada no agora.
        var padrao = await OkAsync(http, HttpMethod.Get, "/api/indicadores", supervisor);
        Assert.Equal(_tempo.GetUtcNow(), padrao.GetProperty("ate").GetDateTimeOffset());
        Assert.Equal(_tempo.GetUtcNow().AddDays(-30), padrao.GetProperty("de").GetDateTimeOffset());
    }

    private static void AssertIndicador(JsonElement painel, string nome, double valor, int baseDoCalculo, string definicao)
    {
        var indicador = painel.GetProperty(nome);
        Assert.Equal(valor, indicador.GetProperty("valor").GetDouble());
        Assert.Equal(baseDoCalculo, indicador.GetProperty("base").GetInt32());
        Assert.Equal(definicao, indicador.GetProperty("definicao").GetString());
    }

    private static IReadOnlyList<JsonElement> Linhas(JsonElement painel, string nome) =>
        [.. painel.GetProperty(nome).EnumerateArray()];

    private static string Url(DateTimeOffset de, DateTimeOffset ate) =>
        "/api/indicadores"
        + $"?de={Uri.EscapeDataString(de.ToString("o", CultureInfo.InvariantCulture))}"
        + $"&ate={Uri.EscapeDataString(ate.ToString("o", CultureInfo.InvariantCulture))}";

    private static Task<JsonElement> IndicadoresAsync(HttpClient http, string token, DateTimeOffset de, DateTimeOffset ate) =>
        OkAsync(http, HttpMethod.Get, Url(de, ate), token);

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

    /// <summary>
    /// Entra de novo como supervisor.
    /// </summary>
    /// <remarks>
    /// O teste avança o relógio em horas, e o token de acesso vale 15 minutos. Pegar um token novo a cada
    /// passo é o que um operador de verdade faria — a sessão dele também expira.
    /// </remarks>
    private static async Task<string> SupervisorAsync(HttpClient http, OperacaoDeTeste operacao) =>
        (await EntrarAsync(http, operacao.Organizacao.Com(Perfil.Supervisor))).TokenDeAcesso;

    private static async Task<JsonElement> ComandoDoMotoristaAsync(
        HttpClient http,
        OperacaoDeTeste operacao,
        string url,
        object? corpo = null)
    {
        var token = (await EntrarAsync(http, operacao.Organizacao.Com(Perfil.Motorista))).TokenDeAcesso;
        return await OkAsync(http, HttpMethod.Post, url, token, corpo);
    }

    /// <summary>Organização, rota iniciada e entregas em rota, tudo pelo fluxo real da API.</summary>
    private async Task<OperacaoDeTeste> OperacaoAsync(
        HttpClient http,
        int quantidadeDeEntregas,
        DateTimeOffset janelaDe,
        DateTimeOffset janelaAte,
        int quantidadeForaDeRota = 0)
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Supervisor, Perfil.Motorista);
        var supervisor = (await EntrarAsync(http, organizacao.Com(Perfil.Supervisor))).TokenDeAcesso;

        var clienteId = await CriarAsync(http, supervisor, "/api/clientes", RoteirosDeCadastro.Obter("cliente").Corpo());
        var destinatarioId = await CriarAsync(http, supervisor, "/api/destinatarios", RoteirosDeCadastro.Obter("destinatario").Corpo());

        // As primeiras saem em rota; as últimas ficam criadas e paradas, que é o único estado de onde a
        // entrega ainda pode ser cancelada — quem já saiu para rota não se cancela, se conclui ou frustra.
        var entregas = new List<Guid>();
        for (var indice = 0; indice < quantidadeDeEntregas + quantidadeForaDeRota; indice++)
        {
            entregas.Add(await CriarAsync(
                http, supervisor, "/api/entregas", RoteirosDeEntrega.Corpo(clienteId, destinatarioId, janelaDe, janelaAte)));
        }

        var motorista = await CriarAsync(http, supervisor, "/api/motoristas", RoteirosDeCadastro.Obter("motorista").Corpo());
        await OkAsync(http, HttpMethod.Put, $"/api/motoristas/{motorista}/conta", supervisor, new { usuarioId = organizacao.Com(Perfil.Motorista).Id });
        var veiculo = await CriarAsync(http, supervisor, "/api/veiculos", RoteirosDeCadastro.Obter("veiculo").Corpo());

        var operacao = new OperacaoDeTeste(organizacao, motorista, veiculo, entregas);
        var rota = await IniciarRotaAsync(http, supervisor, operacao, [.. entregas.Take(quantidadeDeEntregas)], diasAFrente: 0);
        return operacao with { PrimeiraRota = rota };
    }

    /// <summary>Segunda rota, noutro dia, para a entrega reagendada.</summary>
    /// <remarks>
    /// A primeira rota é concluída antes: enquanto ela corre, a entrega continua presa à parada dela, e a
    /// base recusa a mesma entrega em duas rotas ativas. É a ordem real — o motorista fecha o dia, e só
    /// então o que sobrou é reprogramado.
    /// </remarks>
    private async Task SegundaRotaAsync(HttpClient http, OperacaoDeTeste operacao, Guid entrega)
    {
        await ComandoDoMotoristaAsync(http, operacao, $"/api/motorista/rotas/{operacao.PrimeiraRota}/conclusao");
        var supervisor = await SupervisorAsync(http, operacao);
        await IniciarRotaAsync(http, supervisor, operacao, [entrega], diasAFrente: 2);
    }

    private async Task<Guid> IniciarRotaAsync(
        HttpClient http,
        string supervisor,
        OperacaoDeTeste operacao,
        IReadOnlyList<Guid> entregas,
        int diasAFrente)
    {
        var dia = _tempo.GetUtcNow().UtcDateTime.Date.AddDays(1 + diasAFrente);
        var rota = await CriarAsync(http, supervisor, "/api/rotas", new { data = DateOnly.FromDateTime(dia) });

        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/paradas", supervisor, new { entregas });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/motorista", supervisor, new { motoristaId = operacao.Motorista });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/veiculo", supervisor, new { veiculoId = operacao.Veiculo });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/saida", supervisor, new
        {
            saidaPlanejada = new DateTimeOffset(dia.AddHours(8), TimeSpan.Zero),
        });
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/planejamento", supervisor);
        await ComandoDoMotoristaAsync(http, operacao, $"/api/motorista/rotas/{rota}/inicio");
        return rota;
    }

    private sealed record OperacaoDeTeste(
        OrganizacaoDeTeste Organizacao,
        Guid Motorista,
        Guid Veiculo,
        IReadOnlyList<Guid> Entregas)
    {
        /// <summary>A rota que levou as entregas na saída.</summary>
        public Guid PrimeiraRota { get; init; }
    }
}
