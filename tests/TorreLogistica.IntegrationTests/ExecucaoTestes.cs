using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// Máquina de estados e concorrência contra PostgreSQL real: execução da rota pelo motorista,
/// comandos repetidos, transições proibidas e o cenário obrigatório "operador reatribui enquanto
/// motorista conclui".
/// </summary>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class ExecucaoTestes(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    private static readonly string[] PerdasDaConclusao = ["conflito_de_versao", "entrega_reatribuida"];

    private static DateOnly Amanha => DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1));

    /// <summary>Critério de aceite: nenhum endpoint genérico de status; cada mudança tem nome.</summary>
    [Fact]
    public void NaoExisteEndpointGenericoDeStatus()
    {
        var endpoints = Fabrica.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(metodo => $"{metodo} {endpoint.RoutePattern.RawText}"))
            .ToList();

        Assert.DoesNotContain(endpoints, endpoint => endpoint.StartsWith("PATCH ", StringComparison.Ordinal));
        Assert.DoesNotContain(endpoints, endpoint => endpoint.Contains("status", StringComparison.OrdinalIgnoreCase));

        string[] prefixos = ["/api/entregas", "/api/rotas", "/api/motorista/rotas", "/api/motorista/entregas"];
        var comandos = endpoints
            .Where(endpoint => !endpoint.StartsWith("GET ", StringComparison.Ordinal))
            .Where(endpoint => prefixos.Any(prefixo => endpoint.Split(' ')[1].StartsWith(prefixo, StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToList();

        string[] esperados =
        [
            "DELETE /api/rotas/{id:guid}/paradas/{entregaId:guid}",
            "POST /api/entregas/",
            "POST /api/entregas/{id:guid}/cancelamento",
            "POST /api/entregas/{id:guid}/reagendamento",
            "POST /api/motorista/entregas/{id:guid}/chegada",
            "POST /api/motorista/entregas/{id:guid}/conclusao",
            "POST /api/motorista/entregas/{id:guid}/tentativa-frustrada",
            "POST /api/motorista/rotas/{id:guid}/conclusao",
            "POST /api/motorista/rotas/{id:guid}/inicio",
            "POST /api/rotas/",
            "POST /api/rotas/{id:guid}/cancelamento",
            "POST /api/rotas/{id:guid}/paradas",
            "POST /api/rotas/{id:guid}/planejamento",
            "PUT /api/entregas/{id:guid}",
            "PUT /api/rotas/{id:guid}/motorista",
            "PUT /api/rotas/{id:guid}/ordem",
            "PUT /api/rotas/{id:guid}/saida",
            "PUT /api/rotas/{id:guid}/veiculo",
        ];

        Assert.Equal(esperados.Order(StringComparer.Ordinal), comandos);
    }

    /// <summary>Todas as transições permitidas da entrega e da rota, pela API, com timeline coerente.</summary>
    [Fact]
    public async Task MotoristaExecutaARotaDoInicioAoFimComTimelineCoerente()
    {
        using var http = Cliente();
        var operacao = await OperacaoAsync(http);
        var (rota, entregas) = await RotaPlanejadaAsync(http, operacao, 3, Amanha, operacao.MotoristaUm);

        var iniciada = await OkAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{rota}/inicio", operacao.TokenDoMotoristaUm);
        Assert.Equal("EmAndamento", iniciada.GetProperty("status").GetString());
        foreach (var entrega in entregas)
        {
            Assert.Equal("EmRota", await StatusAsync(http, operacao.Supervisor, entrega));
        }

        var chegada = await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{entregas[0]}/chegada", operacao.TokenDoMotoristaUm);
        Assert.Equal("ProximaDoDestino", chegada.GetProperty("status").GetString());

        var entregue = await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{entregas[0]}/conclusao", operacao.TokenDoMotoristaUm);
        Assert.Equal("Entregue", entregue.GetProperty("status").GetString());
        Assert.Equal(operacao.MotoristaUm, entregue.GetProperty("execucao").GetProperty("motoristaId").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, entregue.GetProperty("execucao").GetProperty("entregueEm").ValueKind);

        var frustrada = await OkAsync(
            http, HttpMethod.Post, $"/api/motorista/entregas/{entregas[1]}/tentativa-frustrada", operacao.TokenDoMotoristaUm, new { motivo = "DestinatarioAusente" });
        Assert.Equal("TentativaFrustrada", frustrada.GetProperty("status").GetString());
        Assert.Equal(1, frustrada.GetProperty("execucao").GetProperty("tentativasFrustradas").GetInt32());

        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{entregas[2]}/conclusao", operacao.TokenDoMotoristaUm);

        var reagendada = await OkAsync(http, HttpMethod.Post, $"/api/entregas/{entregas[1]}/reagendamento", operacao.Operador, new
        {
            prometidaDe = RoteirosDeEntrega.Amanha(9).AddDays(2),
            prometidaAte = RoteirosDeEntrega.Amanha(12).AddDays(2),
        });
        Assert.Equal("Reagendada", reagendada.GetProperty("status").GetString());

        var concluida = await OkAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{rota}/conclusao", operacao.TokenDoMotoristaUm);
        Assert.Equal("Concluida", concluida.GetProperty("status").GetString());

        Assert.Equal(
            ["Criada", "Planejada", "Atribuida", "SaiuParaRota", "ChegadaRegistrada", "Entregue"],
            await TiposAsync(http, operacao.Supervisor, $"/api/entregas/{entregas[0]}/eventos"));
        Assert.Equal(
            ["Criada", "Planejada", "Atribuida", "SaiuParaRota", "TentativaFrustrada", "Reagendada"],
            await TiposAsync(http, operacao.Supervisor, $"/api/entregas/{entregas[1]}/eventos"));
        Assert.Equal(
            ["Criada", "Planejada", "Atribuida", "SaiuParaRota", "Entregue"],
            await TiposAsync(http, operacao.Supervisor, $"/api/entregas/{entregas[2]}/eventos"));
        Assert.Equal(["Iniciada", "Concluida"], (await TiposAsync(http, operacao.Supervisor, $"/api/rotas/{rota}/eventos"))[^2..]);

        foreach (var entrega in entregas)
        {
            await AssertTimelineCoerenteAsync(http, operacao.Supervisor, entrega);
        }

        // A entrega reagendada, com a rota concluída, entra em outra rota.
        var outraRota = await CriarAsync(http, operacao.Supervisor, "/api/rotas", new { data = Amanha.AddDays(2) });
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{outraRota}/paradas", operacao.Supervisor, new { entregas = new[] { entregas[1] } });
        Assert.Equal("Planejada", await StatusAsync(http, operacao.Supervisor, entregas[1]));
    }

    /// <summary>
    /// O aplicativo repete o envio quando a resposta se perde. Repetir um comando já aplicado responde
    /// 200 com o estado atual e não duplica efeito.
    /// </summary>
    [Fact]
    public async Task RepeticaoDeComandoPeloAplicativoNaoDuplicaEfeito()
    {
        using var http = Cliente();
        var operacao = await OperacaoAsync(http);
        var (rota, entregas) = await RotaPlanejadaAsync(http, operacao, 2, Amanha, operacao.MotoristaUm);
        var token = operacao.TokenDoMotoristaUm;

        foreach (var url in new[]
        {
            $"/api/motorista/rotas/{rota}/inicio",
            $"/api/motorista/rotas/{rota}/inicio",
            $"/api/motorista/entregas/{entregas[0]}/chegada",
            $"/api/motorista/entregas/{entregas[0]}/chegada",
            $"/api/motorista/entregas/{entregas[0]}/conclusao",
            $"/api/motorista/entregas/{entregas[0]}/conclusao",
        })
        {
            await OkAsync(http, HttpMethod.Post, url, token);
        }

        // Toque duplo: dois envios simultâneos do mesmo comando. Um só efeito.
        var duplos = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            EnviarAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{entregas[1]}/conclusao", token)));
        foreach (var resposta in duplos)
        {
            using (resposta)
            {
                Assert.True(resposta.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, $"veio {(int)resposta.StatusCode}");
            }
        }

        await OkAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{rota}/conclusao", token);
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{rota}/conclusao", token);

        Assert.Equal("1", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM eventos_da_rota WHERE rota_id = @id AND tipo = 'Iniciada'", ("id", rota)));
        Assert.Equal("1", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM eventos_da_rota WHERE rota_id = @id AND tipo = 'Concluida'", ("id", rota)));
        foreach (var entrega in entregas)
        {
            Assert.Equal("1", await Banco.ConsultarEscalarAsync(
                "SELECT count(*) FROM eventos_da_entrega WHERE entrega_id = @id AND tipo = 'Entregue'", ("id", entrega)));
            await AssertTimelineCoerenteAsync(http, operacao.Supervisor, entrega);
        }

        Assert.Equal("1", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM eventos_da_entrega WHERE entrega_id = @id AND tipo = 'ChegadaRegistrada'", ("id", entregas[0])));
    }

    [Fact]
    public async Task TransicoesProibidasImportantesRespondem409SemMudarNada()
    {
        using var http = Cliente();
        var operacao = await OperacaoAsync(http);
        var token = operacao.TokenDoMotoristaUm;
        var (rota, entregas) = await RotaPlanejadaAsync(http, operacao, 2, Amanha, operacao.MotoristaUm);

        // Antes de sair.
        await ConflitoAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{entregas[0]}/conclusao", token, null, "transicao_invalida");
        await ConflitoAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{entregas[0]}/chegada", token, null, "transicao_invalida");
        await ConflitoAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{rota}/conclusao", token, null, "transicao_de_rota_invalida");

        var emMontagem = await CriarAsync(http, operacao.Supervisor, "/api/rotas", new { data = Amanha });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{emMontagem}/motorista", operacao.Supervisor, new { motoristaId = operacao.MotoristaDois });
        await ConflitoAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{emMontagem}/inicio", operacao.TokenDoMotoristaDois, null, "transicao_de_rota_invalida");

        // Em rota.
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{rota}/inicio", token);
        var emRota = await OkAsync(http, HttpMethod.Get, $"/api/entregas/{entregas[0]}", operacao.Operador);

        await ConflitoAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{rota}/conclusao", token, null, "rota_com_entregas_pendentes");
        await ConflitoAsync(http, HttpMethod.Post, $"/api/entregas/{entregas[0]}/cancelamento", operacao.Operador, new { motivo = "SolicitacaoDoCliente" }, "cancelamento_nao_permitido");
        await ConflitoAsync(http, HttpMethod.Post, $"/api/entregas/{entregas[0]}/reagendamento", operacao.Operador, new
        {
            prometidaDe = RoteirosDeEntrega.Amanha(9).AddDays(2),
            prometidaAte = RoteirosDeEntrega.Amanha(12).AddDays(2),
        }, "transicao_invalida");
        await ConflitoAsync(http, HttpMethod.Put, $"/api/entregas/{entregas[0]}", operacao.Operador, new
        {
            versao = emRota.GetProperty("versao").GetUInt32(),
            clienteId = operacao.ClienteId,
            destinatarioId = operacao.DestinatarioId,
            endereco = RoteirosDeCadastro.Endereco(cep: "13015-904"),
            latitude = (double?)null,
            longitude = (double?)null,
            prometidaDe = emRota.GetProperty("janelaPrometida").GetProperty("de").GetDateTimeOffset(),
            prometidaAte = emRota.GetProperty("janelaPrometida").GetProperty("ate").GetDateTimeOffset(),
            observacoes = (string?)null,
        }, "campo_nao_editavel");
        var outraEntrega = await CriarAsync(http, operacao.Operador, "/api/entregas", RoteirosDeEntrega.Corpo(operacao.ClienteId, operacao.DestinatarioId));
        await ConflitoAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/paradas", operacao.Supervisor, new { entregas = new[] { outraEntrega } }, "rota_nao_editavel");
        await ConflitoAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/cancelamento", operacao.Supervisor, null, "cancelamento_de_rota_nao_permitido");

        // Depois de entregue.
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{entregas[0]}/conclusao", token);
        await ConflitoAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{entregas[0]}/chegada", token, null, "transicao_invalida");
        await ConflitoAsync(
            http, HttpMethod.Post, $"/api/motorista/entregas/{entregas[0]}/tentativa-frustrada", token, new { motivo = "LocalFechado" }, "transicao_invalida");

        var depois = await OkAsync(http, HttpMethod.Get, $"/api/entregas/{entregas[0]}", operacao.Operador);
        Assert.Equal("Entregue", depois.GetProperty("status").GetString());
        await AssertTimelineCoerenteAsync(http, operacao.Supervisor, entregas[0]);
        Assert.Equal("EmRota", await StatusAsync(http, operacao.Supervisor, entregas[1]));
    }

    /// <summary>
    /// Cenário obrigatório da Fase 5, sob corrida real: o operador troca o motorista da rota em
    /// andamento enquanto o motorista conclui a entrega. Só uma sequência prevalece, e quem perde
    /// recebe 409.
    /// </summary>
    [Fact]
    public async Task OperadorReatribuiEnquantoMotoristaConcluiApenasUmaSequenciaPrevalece()
    {
        using var http = Cliente();
        var operacao = await OperacaoAsync(http);

        for (var rodada = 0; rodada < 6; rodada++)
        {
            var (rota, entregas) = await RotaPlanejadaAsync(http, operacao, 1, Amanha.AddDays(rodada), operacao.MotoristaUm);
            await OkAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{rota}/inicio", operacao.TokenDoMotoristaUm);
            var entrega = entregas[0];

            var reatribuicao = EnviarAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/motorista", operacao.Supervisor, new { motoristaId = operacao.MotoristaDois });
            var conclusao = EnviarAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{entrega}/conclusao", operacao.TokenDoMotoristaUm);
            using var respostaDaReatribuicao = await reatribuicao;
            using var respostaDaConclusao = await conclusao;

            var concluiu = respostaDaConclusao.StatusCode == HttpStatusCode.OK;
            var reatribuiu = respostaDaReatribuicao.StatusCode == HttpStatusCode.OK;
            var atual = await OkAsync(http, HttpMethod.Get, $"/api/entregas/{entrega}", operacao.Supervisor);
            var tipos = await TiposAsync(http, operacao.Supervisor, $"/api/entregas/{entrega}/eventos");

            Assert.True(concluiu || reatribuiu, $"rodada {rodada}: as duas operações perderam");

            if (concluiu)
            {
                Assert.Equal("Entregue", atual.GetProperty("status").GetString());
                Assert.Equal(operacao.MotoristaUm, atual.GetProperty("execucao").GetProperty("motoristaId").GetGuid());
                Assert.DoesNotContain("Reatribuida", tipos);
            }
            else
            {
                Assert.Equal(HttpStatusCode.Conflict, respostaDaConclusao.StatusCode);
                Assert.Contains(await CodigoDoErroAsync(respostaDaConclusao), PerdasDaConclusao);
                Assert.Equal("EmRota", atual.GetProperty("status").GetString());
                Assert.Equal(operacao.MotoristaDois, atual.GetProperty("execucao").GetProperty("motoristaId").GetGuid());
                Assert.Contains("Reatribuida", tipos);

                // A sequência que prevaleceu continua válida: o novo motorista conclui.
                await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{entrega}/conclusao", operacao.TokenDoMotoristaDois);
            }

            if (!reatribuiu)
            {
                Assert.Equal(HttpStatusCode.Conflict, respostaDaReatribuicao.StatusCode);
                Assert.Equal("conflito_de_versao", await CodigoDoErroAsync(respostaDaReatribuicao));
            }

            // No momento da leitura: uma conclusão se o motorista venceu; nenhuma se perdeu.
            Assert.Equal(concluiu ? 1 : 0, tipos.Count(tipo => tipo == "Entregue"));
            await AssertTimelineCoerenteAsync(http, operacao.Supervisor, entrega);
        }
    }

    /// <summary>As duas ordens possíveis do cenário obrigatório, sem depender de sorte na corrida.</summary>
    [Fact]
    public async Task CenarioObrigatorioEmCadaOrdemTemContratoExplicito()
    {
        using var http = Cliente();
        var operacao = await OperacaoAsync(http);

        // Reatribuição primeiro: o motorista antigo recebe 409 entrega_reatribuida; o novo conclui.
        var (rotaUm, entregasUm) = await RotaPlanejadaAsync(http, operacao, 1, Amanha, operacao.MotoristaUm);
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{rotaUm}/inicio", operacao.TokenDoMotoristaUm);
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rotaUm}/motorista", operacao.Supervisor, new { motoristaId = operacao.MotoristaDois });
        await ConflitoAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{entregasUm[0]}/conclusao", operacao.TokenDoMotoristaUm, null, "entrega_reatribuida");
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{entregasUm[0]}/conclusao", operacao.TokenDoMotoristaDois);
        Assert.Equal(
            ["Criada", "Planejada", "Atribuida", "SaiuParaRota", "Reatribuida", "Entregue"],
            await TiposAsync(http, operacao.Supervisor, $"/api/entregas/{entregasUm[0]}/eventos"));

        // Conclusão primeiro: a reatribuição prossegue, mas a entrega concluída não é tocada.
        var (rotaDois, entregasDois) = await RotaPlanejadaAsync(http, operacao, 2, Amanha.AddDays(1), operacao.MotoristaUm);
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{rotaDois}/inicio", operacao.TokenDoMotoristaUm);
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{entregasDois[0]}/conclusao", operacao.TokenDoMotoristaUm);
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rotaDois}/motorista", operacao.Supervisor, new { motoristaId = operacao.MotoristaDois });

        var concluida = await OkAsync(http, HttpMethod.Get, $"/api/entregas/{entregasDois[0]}", operacao.Supervisor);
        Assert.Equal(operacao.MotoristaUm, concluida.GetProperty("execucao").GetProperty("motoristaId").GetGuid());
        Assert.DoesNotContain("Reatribuida", await TiposAsync(http, operacao.Supervisor, $"/api/entregas/{entregasDois[0]}/eventos"));
        var pendente = await OkAsync(http, HttpMethod.Get, $"/api/entregas/{entregasDois[1]}", operacao.Supervisor);
        Assert.Equal(operacao.MotoristaDois, pendente.GetProperty("execucao").GetProperty("motoristaId").GetGuid());

        // O motorista antigo repete a própria conclusão: continua 200, porque o resultado é dele.
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{entregasDois[0]}/conclusao", operacao.TokenDoMotoristaUm);
    }

    [Fact]
    public async Task MotoristaSoComandaAsPropriasEntregasERotas()
    {
        using var http = Cliente();
        var operacao = await OperacaoAsync(http);
        var (rota, entregas) = await RotaPlanejadaAsync(http, operacao, 1, Amanha, operacao.MotoristaUm);
        var inventada = Guid.CreateVersion7();

        // Outro motorista da mesma organização, que nunca teve a entrega: como inexistente.
        foreach (var (sufixo, alvo) in new[] { ($"rotas/{rota}/inicio", $"rotas/{inventada}/inicio"), ($"entregas/{entregas[0]}/chegada", $"entregas/{inventada}/chegada") })
        {
            using var sobreAlheia = await EnviarAsync(http, HttpMethod.Post, $"/api/motorista/{sufixo}", operacao.TokenDoMotoristaDois);
            using var sobreInventada = await EnviarAsync(http, HttpMethod.Post, $"/api/motorista/{alvo}", operacao.TokenDoMotoristaDois);
            Assert.Equal(HttpStatusCode.NotFound, sobreAlheia.StatusCode);
            Assert.Equal(await AssinaturaAsync(sobreInventada), await AssinaturaAsync(sobreAlheia));
        }

        // Motorista de outra organização.
        var outra = await OperacaoAsync(http);
        using var deOutraOrganizacao = await EnviarAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{entregas[0]}/chegada", outra.TokenDoMotoristaUm);
        using var inventadaNaOutra = await EnviarAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{inventada}/chegada", outra.TokenDoMotoristaUm);
        Assert.Equal(HttpStatusCode.NotFound, deOutraOrganizacao.StatusCode);
        Assert.Equal(await AssinaturaAsync(inventadaNaOutra), await AssinaturaAsync(deOutraOrganizacao));

        // Conta de motorista sem cadastro associado.
        using var semCadastro = await EnviarAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{rota}/inicio", operacao.TokenSemCadastro);
        Assert.Equal(HttpStatusCode.NotFound, semCadastro.StatusCode);
        Assert.Equal("motorista_nao_associado", await CodigoDoErroAsync(semCadastro));

        // Canais separados: token do console não comanda execução, token do motorista não reatribui.
        using var consoleNoCanalDoMotorista = await EnviarAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{rota}/inicio", operacao.Supervisor);
        using var motoristaNoConsole = await EnviarAsync(
            http, HttpMethod.Put, $"/api/rotas/{rota}/motorista", operacao.TokenDoMotoristaUm, new { motoristaId = operacao.MotoristaUm });
        Assert.Equal(HttpStatusCode.Unauthorized, consoleNoCanalDoMotorista.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, motoristaNoConsole.StatusCode);

        Assert.Equal("Planejada", (await OkAsync(http, HttpMethod.Get, $"/api/rotas/{rota}", operacao.Supervisor)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task ReagendamentoValidaJanelaEIsolamento()
    {
        using var http = Cliente();
        var operacao = await OperacaoAsync(http);
        var (rota, entregas) = await RotaPlanejadaAsync(http, operacao, 1, Amanha, operacao.MotoristaUm);
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{rota}/inicio", operacao.TokenDoMotoristaUm);
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{entregas[0]}/tentativa-frustrada", operacao.TokenDoMotoristaUm, new { motivo = "AcessoImpedido" });

        using var noPassado = await EnviarAsync(http, HttpMethod.Post, $"/api/entregas/{entregas[0]}/reagendamento", operacao.Operador, new
        {
            prometidaDe = DateTimeOffset.UtcNow.AddHours(-3),
            prometidaAte = DateTimeOffset.UtcNow.AddHours(-1),
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noPassado.StatusCode);
        Assert.Equal("janela_no_passado", await CodigoDoErroAsync(noPassado));

        using var motivoInvalido = await EnviarAsync(
            http, HttpMethod.Post, $"/api/motorista/entregas/{entregas[0]}/tentativa-frustrada", operacao.TokenDoMotoristaUm, new { motivo = "Chuva" });
        Assert.Equal(HttpStatusCode.BadRequest, motivoInvalido.StatusCode);

        var outra = await OperacaoAsync(http);
        var corpo = new { prometidaDe = RoteirosDeEntrega.Amanha(9).AddDays(2), prometidaAte = RoteirosDeEntrega.Amanha(12).AddDays(2) };
        using var deOutra = await EnviarAsync(http, HttpMethod.Post, $"/api/entregas/{entregas[0]}/reagendamento", outra.Operador, corpo);
        using var inventada = await EnviarAsync(http, HttpMethod.Post, $"/api/entregas/{Guid.CreateVersion7()}/reagendamento", outra.Operador, corpo);
        Assert.Equal(HttpStatusCode.NotFound, deOutra.StatusCode);
        Assert.Equal(await AssinaturaAsync(inventada), await AssinaturaAsync(deOutra));
        Assert.Equal("TentativaFrustrada", await StatusAsync(http, operacao.Supervisor, entregas[0]));
    }

    private sealed record Operacao(
        string Supervisor,
        string Operador,
        Guid MotoristaUm,
        string TokenDoMotoristaUm,
        Guid MotoristaDois,
        string TokenDoMotoristaDois,
        string TokenSemCadastro,
        Guid ClienteId,
        Guid DestinatarioId);

    private async Task<Operacao> OperacaoAsync(HttpClient http)
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Supervisor, Perfil.Operador, Perfil.Motorista, Perfil.Motorista, Perfil.Motorista);
        var supervisor = (await EntrarAsync(http, organizacao.Com(Perfil.Supervisor))).TokenDeAcesso;
        var operador = (await EntrarAsync(http, organizacao.Com(Perfil.Operador))).TokenDeAcesso;
        var contas = organizacao.TodasCom(Perfil.Motorista);
        var motoristaUm = await MotoristaComContaAsync(http, supervisor, contas[0]);
        var motoristaDois = await MotoristaComContaAsync(http, supervisor, contas[1]);
        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);

        return new Operacao(
            supervisor,
            operador,
            motoristaUm,
            (await EntrarAsync(http, contas[0])).TokenDeAcesso,
            motoristaDois,
            (await EntrarAsync(http, contas[1])).TokenDeAcesso,
            (await EntrarAsync(http, contas[2])).TokenDeAcesso,
            clienteId,
            destinatarioId);
    }

    private static async Task<Guid> MotoristaComContaAsync(HttpClient http, string supervisor, ContaDeTeste conta)
    {
        var id = await CriarAsync(http, supervisor, "/api/motoristas", RoteirosDeCadastro.Obter("motorista").Corpo());
        await OkAsync(http, HttpMethod.Put, $"/api/motoristas/{id}/conta", supervisor, new { usuarioId = conta.Id });
        return id;
    }

    private static async Task<(Guid Rota, List<Guid> Entregas)> RotaPlanejadaAsync(
        HttpClient http,
        Operacao operacao,
        int quantidade,
        DateOnly data,
        Guid motoristaId)
    {
        var entregas = new List<Guid>();
        for (var indice = 0; indice < quantidade; indice++)
        {
            entregas.Add(await CriarAsync(http, operacao.Operador, "/api/entregas", RoteirosDeEntrega.Corpo(operacao.ClienteId, operacao.DestinatarioId)));
        }

        var veiculoId = await CriarAsync(http, operacao.Supervisor, "/api/veiculos", RoteirosDeCadastro.Obter("veiculo").Corpo());
        var rota = await CriarAsync(http, operacao.Supervisor, "/api/rotas", new { data });
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/paradas", operacao.Supervisor, new { entregas });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/motorista", operacao.Supervisor, new { motoristaId });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/veiculo", operacao.Supervisor, new { veiculoId });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/saida", operacao.Supervisor, new
        {
            saidaPlanejada = new DateTimeOffset(data.ToDateTime(new TimeOnly(8, 0)), TimeSpan.Zero),
        });
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/planejamento", operacao.Supervisor);

        return (rota, entregas);
    }

    private async Task AssertTimelineCoerenteAsync(HttpClient http, string token, Guid entrega)
    {
        var atual = await OkAsync(http, HttpMethod.Get, $"/api/entregas/{entrega}", token);
        var eventos = await EventosAsync(http, token, $"/api/entregas/{entrega}/eventos");

        Assert.Equal(Enumerable.Range(1, eventos.Count), eventos.Select(evento => evento.GetProperty("sequencia").GetInt32()));
        Assert.Equal(atual.GetProperty("status").GetString(), eventos[^1].GetProperty("statusResultante").GetString());
        Assert.Equal(eventos.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), await Banco.ConsultarEscalarAsync(
            "SELECT ultima_sequencia_de_evento::text FROM entregas WHERE id = @id", ("id", entrega)));
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

    private static async Task ConflitoAsync(HttpClient http, HttpMethod metodo, string url, string token, object? corpo, string codigo)
    {
        using var resposta = await EnviarAsync(http, metodo, url, token, corpo);
        var json = await JsonAsync(resposta);
        Assert.True(resposta.StatusCode == HttpStatusCode.Conflict, $"{metodo} {url}: {(int)resposta.StatusCode} {json}");
        Assert.Equal(codigo, json.GetProperty("codigo").GetString());
    }

    private static async Task<string?> StatusAsync(HttpClient http, string token, Guid entrega) =>
        (await OkAsync(http, HttpMethod.Get, $"/api/entregas/{entrega}", token)).GetProperty("status").GetString();

    private static async Task<List<JsonElement>> EventosAsync(HttpClient http, string token, string url)
    {
        using var resposta = await EnviarAsync(http, HttpMethod.Get, url, token);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return [.. (await JsonAsync(resposta)).EnumerateArray()];
    }

    private static async Task<List<string?>> TiposAsync(HttpClient http, string token, string url) =>
        [.. (await EventosAsync(http, token, url)).Select(evento => evento.GetProperty("tipo").GetString())];

    private static async Task<string> AssinaturaAsync(HttpResponseMessage resposta)
    {
        var json = await JsonAsync(resposta);
        var campos = json.EnumerateObject()
            .Where(campo => campo.Name is not ("traceId" or "idDeCorrelacao" or "instance"))
            .Select(campo => $"{campo.Name}={campo.Value.GetRawText()}");
        return $"{(int)resposta.StatusCode}|{string.Join("|", campos)}";
    }
}
