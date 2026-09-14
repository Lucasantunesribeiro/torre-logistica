using System.Globalization;
using System.Net;
using System.Text.Json;
using Npgsql;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// Montagem de rotas contra PostgreSQL real: paradas, ordem, atribuição, planejamento e
/// cancelamento, com os efeitos sobre as entregas.
/// </summary>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class RotasTestes(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    private static readonly string[] ConflitosDeInclusao = ["entrega_em_outra_rota", "conflito_de_versao"];

    private static DateOnly Amanha => DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1));

    /// <summary>Critério de aceite da Fase 4.</summary>
    [Fact]
    public async Task SupervisorMontaARotaDoDiaEAAtribui()
    {
        using var http = Cliente();
        var (_, supervisor, operador) = await OrganizacaoAsync(http);
        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);
        var entregas = await CriarEntregasAsync(http, operador, clienteId, destinatarioId, 3);
        var hubId = await CriarAsync(http, supervisor, "/api/hubs", RoteirosDeCadastro.Obter("hub").Corpo());
        var motoristaId = await CriarAsync(http, supervisor, "/api/motoristas", RoteirosDeCadastro.Obter("motorista").Corpo());
        var veiculoId = await CriarAsync(http, supervisor, "/api/veiculos", RoteirosDeCadastro.Obter("veiculo").Corpo());

        using var criacao = await EnviarAsync(http, HttpMethod.Post, "/api/rotas", supervisor, new { data = Amanha, hubId });
        var criada = await JsonAsync(criacao);
        Assert.True(criacao.StatusCode == HttpStatusCode.Created, criada.ToString());
        var id = criada.GetProperty("id").GetGuid();
        Assert.Equal($"/api/rotas/{id}", criacao.Headers.Location?.OriginalString);
        Assert.Matches(@"^ROT-\d{4}-0001\z", criada.GetProperty("codigo").GetString());
        Assert.Equal("EmMontagem", criada.GetProperty("status").GetString());
        Assert.Equal("Hub Centro", criada.GetProperty("hub").GetProperty("nome").GetString());

        var montada = await OkAsync(http, HttpMethod.Post, $"/api/rotas/{id}/paradas", supervisor, new { entregas });
        Assert.Equal(entregas, EntregasDasParadas(montada));
        Assert.Equal(1, montada.GetProperty("versaoDaOrdem").GetInt32());

        Guid[] ordem = [entregas[2], entregas[0], entregas[1]];
        var reordenada = await OkAsync(http, HttpMethod.Put, $"/api/rotas/{id}/ordem", supervisor, new
        {
            versao = montada.GetProperty("versao").GetUInt32(),
            entregas = ordem,
        });
        Assert.Equal(ordem, EntregasDasParadas(reordenada));
        Assert.Equal([1, 2, 3], reordenada.GetProperty("paradas").EnumerateArray().Select(parada => parada.GetProperty("sequencia").GetInt32()));
        Assert.Equal(2, reordenada.GetProperty("versaoDaOrdem").GetInt32());

        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{id}/motorista", supervisor, new { motoristaId });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{id}/veiculo", supervisor, new { veiculoId });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{id}/saida", supervisor, new { saidaPlanejada = RoteirosDeEntrega.Amanha(8) });
        var planejada = await OkAsync(http, HttpMethod.Post, $"/api/rotas/{id}/planejamento", supervisor, null);

        Assert.Equal("Planejada", planejada.GetProperty("status").GetString());
        Assert.Equal(motoristaId, planejada.GetProperty("motorista").GetProperty("id").GetGuid());
        Assert.Equal(veiculoId, planejada.GetProperty("veiculo").GetProperty("id").GetGuid());
        Assert.Equal(RoteirosDeEntrega.Amanha(8), planejada.GetProperty("saidaPlanejada").GetDateTimeOffset());
        Assert.All(planejada.GetProperty("paradas").EnumerateArray(), parada =>
        {
            Assert.Equal("Atribuida", parada.GetProperty("statusDaEntrega").GetString());
            Assert.StartsWith("ENT-", parada.GetProperty("codigoDaEntrega").GetString(), StringComparison.Ordinal);
            Assert.Equal("Carla Nunes", parada.GetProperty("destinatarioNome").GetString());
        });

        // O operador consulta a sequência montada, mas não monta rota.
        using var consulta = await EnviarAsync(http, HttpMethod.Get, $"/api/rotas/{id}", operador);
        Assert.Equal(planejada.GetRawText(), (await JsonAsync(consulta)).GetRawText());

        using var lista = await EnviarAsync(http, HttpMethod.Get, $"/api/rotas?data={Amanha:yyyy-MM-dd}&status=Planejada", operador);
        var item = Assert.Single((await JsonAsync(lista)).GetProperty("itens").EnumerateArray());
        Assert.Equal(3, item.GetProperty("quantidadeDeParadas").GetInt32());

        using var tentativaDoOperador = await EnviarAsync(http, HttpMethod.Post, "/api/rotas", operador, new { data = Amanha });
        Assert.Equal(HttpStatusCode.Forbidden, tentativaDoOperador.StatusCode);

        Assert.Equal(
            ["Criada", "ParadasAdicionadas", "ParadasReordenadas", "MotoristaAtribuido", "VeiculoAtribuido", "SaidaPlanejada", "Planejada"],
            (await EventosAsync(http, supervisor, $"/api/rotas/{id}/eventos")).Select(evento => evento.GetProperty("tipo").GetString()));
        Assert.Equal(
            ["Criada", "Planejada", "Atribuida"],
            (await EventosAsync(http, supervisor, $"/api/entregas/{entregas[0]}/eventos")).Select(evento => evento.GetProperty("tipo").GetString()));
        Assert.Equal(
            "rota_criada,rota_motorista_atribuido,rota_veiculo_atribuido,rota_planejada",
            await Banco.ConsultarEscalarAsync(
                "SELECT string_agg(tipo, ',' ORDER BY ocorrido_em) FROM eventos_de_auditoria WHERE alvo_id = @id", ("id", id)));
    }

    [Fact]
    public async Task EntregaNaoFicaEmDuasRotasAtivas()
    {
        using var http = Cliente();
        var (_, supervisor, operador) = await OrganizacaoAsync(http);
        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);
        var entregas = await CriarEntregasAsync(http, operador, clienteId, destinatarioId, 2);
        var rotaA = await CriarRotaAsync(http, supervisor);
        var rotaB = await CriarRotaAsync(http, supervisor);

        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rotaA}/paradas", supervisor, new { entregas = new[] { entregas[0] } });

        await ConflitoAsync(http, HttpMethod.Post, $"/api/rotas/{rotaB}/paradas", supervisor, new { entregas = new[] { entregas[0] } }, "entrega_em_outra_rota");
        await ConflitoAsync(http, HttpMethod.Post, $"/api/rotas/{rotaA}/paradas", supervisor, new { entregas = new[] { entregas[0] } }, "entrega_ja_na_rota");

        // Tudo ou nada: a entrega livre da lista também não entra.
        await ConflitoAsync(http, HttpMethod.Post, $"/api/rotas/{rotaB}/paradas", supervisor, new { entregas }, "entrega_em_outra_rota");
        Assert.Empty((await RotaAsync(http, supervisor, rotaB)).GetProperty("paradas").EnumerateArray());
        Assert.Equal("Criada", await StatusDaEntregaAsync(http, supervisor, entregas[1]));

        // Rota cancelada libera a entrega para outra rota.
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rotaA}/cancelamento", supervisor, null);
        Assert.Equal("Criada", await StatusDaEntregaAsync(http, supervisor, entregas[0]));
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rotaB}/paradas", supervisor, new { entregas });
        Assert.Equal("Planejada", await StatusDaEntregaAsync(http, supervisor, entregas[0]));

        var entregaCancelada = (await CriarEntregasAsync(http, operador, clienteId, destinatarioId, 1))[0];
        await OkAsync(http, HttpMethod.Post, $"/api/entregas/{entregaCancelada}/cancelamento", operador, new { motivo = "CadastroDuplicado" });
        await ConflitoAsync(
            http, HttpMethod.Post, $"/api/rotas/{rotaB}/paradas", supervisor, new { entregas = new[] { entregaCancelada } }, "entrega_inelegivel_para_rota");
    }

    [Fact]
    public async Task InclusoesSimultaneasDaMesmaEntregaEmRotasDiferentesTemUmVencedor()
    {
        using var http = Cliente();
        var (_, supervisor, operador) = await OrganizacaoAsync(http);
        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);
        var entrega = (await CriarEntregasAsync(http, operador, clienteId, destinatarioId, 1))[0];
        var rotas = new List<Guid>();
        for (var indice = 0; indice < 5; indice++)
        {
            rotas.Add(await CriarRotaAsync(http, supervisor));
        }

        var respostas = await Task.WhenAll(rotas.Select(rota =>
            EnviarAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/paradas", supervisor, new { entregas = new[] { entrega } })));

        var perdedoras = 0;
        foreach (var resposta in respostas)
        {
            using (resposta)
            {
                if (resposta.StatusCode != HttpStatusCode.OK)
                {
                    Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
                    Assert.Contains(await CodigoDoErroAsync(resposta), ConflitosDeInclusao);
                    perdedoras++;
                }
            }
        }

        Assert.Equal(4, perdedoras);
        Assert.Equal("1", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM paradas WHERE entrega_id = @id AND ativa", ("id", entrega)));
        Assert.Equal("1", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM eventos_da_entrega WHERE entrega_id = @id AND tipo = 'Planejada'", ("id", entrega)));
    }

    [Fact]
    public async Task AtribuicoesSimultaneasDoMesmoMotoristaNoMesmoDiaTemUmVencedor()
    {
        using var http = Cliente();
        var (_, supervisor, _) = await OrganizacaoAsync(http);
        var motoristaId = await CriarAsync(http, supervisor, "/api/motoristas", RoteirosDeCadastro.Obter("motorista").Corpo());
        var veiculoId = await CriarAsync(http, supervisor, "/api/veiculos", RoteirosDeCadastro.Obter("veiculo").Corpo());
        var rotas = new List<Guid>();
        for (var indice = 0; indice < 4; indice++)
        {
            rotas.Add(await CriarRotaAsync(http, supervisor));
        }

        var respostas = await Task.WhenAll(rotas.Select(rota =>
            EnviarAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/motorista", supervisor, new { motoristaId })));

        var vencedoras = new List<Guid>();
        for (var indice = 0; indice < respostas.Length; indice++)
        {
            using var resposta = respostas[indice];
            if (resposta.StatusCode == HttpStatusCode.OK)
            {
                vencedoras.Add(rotas[indice]);
            }
            else
            {
                Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
                Assert.Equal("motorista_ja_em_rota", await CodigoDoErroAsync(resposta));
            }
        }

        var vencedora = Assert.Single(vencedoras);
        var outra = rotas.First(rota => rota != vencedora);

        // Outro dia não conflita.
        var rotaDeDepois = await CriarRotaAsync(http, supervisor, Amanha.AddDays(1));
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rotaDeDepois}/motorista", supervisor, new { motoristaId });

        // Veículo segue a mesma regra.
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{vencedora}/veiculo", supervisor, new { veiculoId });
        await ConflitoAsync(http, HttpMethod.Put, $"/api/rotas/{outra}/veiculo", supervisor, new { veiculoId }, "veiculo_ja_em_rota");

        // Rota cancelada libera motorista e veículo no mesmo dia.
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{vencedora}/cancelamento", supervisor, null);
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{outra}/motorista", supervisor, new { motoristaId });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{outra}/veiculo", supervisor, new { veiculoId });
    }

    [Fact]
    public async Task MotoristaEVeiculoInativosNaoSaoAtribuidos()
    {
        using var http = Cliente();
        var (_, supervisor, _) = await OrganizacaoAsync(http);
        var motoristaId = await CriarAsync(http, supervisor, "/api/motoristas", RoteirosDeCadastro.Obter("motorista").Corpo());
        var veiculoId = await CriarAsync(http, supervisor, "/api/veiculos", RoteirosDeCadastro.Obter("veiculo").Corpo());
        var rota = await CriarRotaAsync(http, supervisor);
        await OkAsync(http, HttpMethod.Post, $"/api/motoristas/{motoristaId}/inativacao", supervisor, null);
        await OkAsync(http, HttpMethod.Post, $"/api/veiculos/{veiculoId}/inativacao", supervisor, null);

        await RegraAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/motorista", supervisor, new { motoristaId }, "motorista_inativo");
        await RegraAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/veiculo", supervisor, new { veiculoId }, "veiculo_inativo");

        var atual = await RotaAsync(http, supervisor, rota);
        Assert.True(SemValor(atual, "motorista"));
        Assert.True(SemValor(atual, "veiculo"));
    }

    [Fact]
    public async Task RemocaoDevolveAEntregaERenumeraAsParadas()
    {
        using var http = Cliente();
        var (_, supervisor, operador) = await OrganizacaoAsync(http);
        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);
        var entregas = await CriarEntregasAsync(http, operador, clienteId, destinatarioId, 3);
        var rota = await CriarRotaAsync(http, supervisor);
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/paradas", supervisor, new { entregas });

        var depois = await OkAsync(http, HttpMethod.Delete, $"/api/rotas/{rota}/paradas/{entregas[1]}", supervisor, null);

        Assert.Equal([entregas[0], entregas[2]], EntregasDasParadas(depois));
        Assert.Equal([1, 2], depois.GetProperty("paradas").EnumerateArray().Select(parada => parada.GetProperty("sequencia").GetInt32()));
        Assert.Equal(2, depois.GetProperty("versaoDaOrdem").GetInt32());
        Assert.Equal("Criada", await StatusDaEntregaAsync(http, supervisor, entregas[1]));
        Assert.Equal(
            "RetiradaDaRota",
            (await EventosAsync(http, supervisor, $"/api/entregas/{entregas[1]}/eventos"))[^1].GetProperty("tipo").GetString());

        var remocao = (await EventosAsync(http, supervisor, $"/api/rotas/{rota}/eventos"))[^1];
        Assert.Equal("ParadaRemovida", remocao.GetProperty("tipo").GetString());
        Assert.Equal("DecisaoDoPlanejamento", remocao.GetProperty("dados").GetProperty("motivo").GetString());

        using var repetida = await EnviarAsync(http, HttpMethod.Delete, $"/api/rotas/{rota}/paradas/{entregas[1]}", supervisor);
        Assert.Equal(HttpStatusCode.NotFound, repetida.StatusCode);
        Assert.Equal("parada_nao_encontrada", await CodigoDoErroAsync(repetida));

        // A parada retirada continua no banco, inativa: a rota guarda de onde a entrega saiu.
        Assert.Equal("DecisaoDoPlanejamento", await Banco.ConsultarEscalarAsync(
            "SELECT motivo_da_remocao FROM paradas WHERE entrega_id = @id AND NOT ativa", ("id", entregas[1])));
    }

    [Fact]
    public async Task ReordenacaoExigePermutacaoExataEVersaoERegistraAsDuasOrdens()
    {
        using var http = Cliente();
        var (_, supervisor, operador) = await OrganizacaoAsync(http);
        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);
        var entregas = await CriarEntregasAsync(http, operador, clienteId, destinatarioId, 3);
        var rota = await CriarRotaAsync(http, supervisor);
        var montada = await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/paradas", supervisor, new { entregas });
        var versao = montada.GetProperty("versao").GetUInt32();

        foreach (var invalida in new[]
        {
            new[] { entregas[0], entregas[1] },
            [entregas[0], entregas[1], entregas[1]],
            [entregas[0], entregas[1], Guid.CreateVersion7()],
        })
        {
            await RegraAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/ordem", supervisor, new { versao, entregas = invalida }, "ordem_invalida");
        }

        Guid[] nova = [entregas[1], entregas[2], entregas[0]];
        var reordenada = await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/ordem", supervisor, new { versao, entregas = nova });
        Assert.Equal(nova, EntregasDasParadas(reordenada));

        await ConflitoAsync(
            http, HttpMethod.Put, $"/api/rotas/{rota}/ordem", supervisor, new { versao, entregas = entregas.ToArray() }, "conflito_de_versao");

        var evento = (await EventosAsync(http, supervisor, $"/api/rotas/{rota}/eventos"))[^1];
        Assert.Equal("ParadasReordenadas", evento.GetProperty("tipo").GetString());
        Assert.Equal(entregas, evento.GetProperty("dados").GetProperty("ordemAnterior").EnumerateArray().Select(valor => valor.GetGuid()));
        Assert.Equal(nova, evento.GetProperty("dados").GetProperty("ordemNova").EnumerateArray().Select(valor => valor.GetGuid()));
        Assert.Equal(2, evento.GetProperty("dados").GetProperty("versaoDaOrdem").GetInt32());
    }

    [Fact]
    public async Task PlanejamentoExigeRotaCompletaESaidaValida()
    {
        using var http = Cliente();
        var (_, supervisor, operador) = await OrganizacaoAsync(http);
        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);
        var entrega = (await CriarEntregasAsync(http, operador, clienteId, destinatarioId, 1))[0];
        var motoristaId = await CriarAsync(http, supervisor, "/api/motoristas", RoteirosDeCadastro.Obter("motorista").Corpo());
        var veiculoId = await CriarAsync(http, supervisor, "/api/veiculos", RoteirosDeCadastro.Obter("veiculo").Corpo());
        var rota = await CriarRotaAsync(http, supervisor);
        var planejar = $"/api/rotas/{rota}/planejamento";

        await RegraAsync(http, HttpMethod.Post, planejar, supervisor, null, "rota_sem_paradas");
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/paradas", supervisor, new { entregas = new[] { entrega } });
        await RegraAsync(http, HttpMethod.Post, planejar, supervisor, null, "rota_sem_motorista");
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/motorista", supervisor, new { motoristaId });
        await RegraAsync(http, HttpMethod.Post, planejar, supervisor, null, "rota_sem_veiculo");
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/veiculo", supervisor, new { veiculoId });
        await RegraAsync(http, HttpMethod.Post, planejar, supervisor, null, "rota_sem_saida_planejada");

        await RegraAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/saida", supervisor, new { saidaPlanejada = DateTimeOffset.UtcNow.AddHours(-1) }, "saida_no_passado");
        await RegraAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/saida", supervisor, new { saidaPlanejada = RoteirosDeEntrega.Amanha(8).AddDays(3) }, "saida_fora_da_data");

        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/saida", supervisor, new { saidaPlanejada = RoteirosDeEntrega.Amanha(8) });
        Assert.Equal("Planejada", (await OkAsync(http, HttpMethod.Post, planejar, supervisor, null)).GetProperty("status").GetString());

        // Repetir não gera evento.
        await OkAsync(http, HttpMethod.Post, planejar, supervisor, null);
        Assert.Single(await EventosAsync(http, supervisor, $"/api/rotas/{rota}/eventos"), evento => evento.GetProperty("tipo").GetString() == "Planejada");
    }

    [Fact]
    public async Task RotaCanceladaNaoAceitaAlteracaoEstruturalEDevolveAsEntregas()
    {
        using var http = Cliente();
        var (_, supervisor, operador) = await OrganizacaoAsync(http);
        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);
        var entregas = await CriarEntregasAsync(http, operador, clienteId, destinatarioId, 3);
        var motoristaId = await CriarAsync(http, supervisor, "/api/motoristas", RoteirosDeCadastro.Obter("motorista").Corpo());
        var rota = await CriarRotaAsync(http, supervisor);
        var montada = await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/paradas", supervisor, new { entregas = new[] { entregas[0], entregas[1] } });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/motorista", supervisor, new { motoristaId });

        var cancelada = await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/cancelamento", supervisor, null);
        Assert.Equal("Cancelada", cancelada.GetProperty("status").GetString());
        Assert.Empty(cancelada.GetProperty("paradas").EnumerateArray());
        Assert.Equal("Criada", await StatusDaEntregaAsync(http, supervisor, entregas[0]));
        Assert.Equal(
            ["Criada", "Planejada", "Atribuida", "RetiradaDaRota"],
            (await EventosAsync(http, supervisor, $"/api/entregas/{entregas[1]}/eventos")).Select(evento => evento.GetProperty("tipo").GetString()));

        foreach (var (metodo, sufixo, corpo) in new (HttpMethod, string, object?)[]
        {
            (HttpMethod.Post, "/paradas", new { entregas = new[] { entregas[2] } }),
            (HttpMethod.Delete, $"/paradas/{entregas[0]}", null),
            (HttpMethod.Put, "/ordem", new { versao = cancelada.GetProperty("versao").GetUInt32(), entregas = new[] { entregas[0] } }),
            (HttpMethod.Put, "/motorista", new { motoristaId }),
            (HttpMethod.Put, "/saida", new { saidaPlanejada = RoteirosDeEntrega.Amanha(8) }),
            (HttpMethod.Post, "/planejamento", null),
        })
        {
            await ConflitoAsync(http, metodo, $"/api/rotas/{rota}{sufixo}", supervisor, corpo, "rota_nao_editavel");
        }

        // Repetir o cancelamento é atendido sem novo evento.
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/cancelamento", supervisor, null);
        Assert.Single(await EventosAsync(http, supervisor, $"/api/rotas/{rota}/eventos"), evento => evento.GetProperty("tipo").GetString() == "Cancelada");
        Assert.NotEqual(montada.GetProperty("versao").GetUInt32(), cancelada.GetProperty("versao").GetUInt32());
    }

    [Fact]
    public async Task CancelarEntregaDeRotaPlanejadaRetiraAParadaEAVoltaParaMontagem()
    {
        using var http = Cliente();
        var (_, supervisor, operador) = await OrganizacaoAsync(http);
        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);
        var entrega = (await CriarEntregasAsync(http, operador, clienteId, destinatarioId, 1))[0];
        var motoristaId = await CriarAsync(http, supervisor, "/api/motoristas", RoteirosDeCadastro.Obter("motorista").Corpo());
        var veiculoId = await CriarAsync(http, supervisor, "/api/veiculos", RoteirosDeCadastro.Obter("veiculo").Corpo());
        var rota = await CriarRotaAsync(http, supervisor);
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/paradas", supervisor, new { entregas = new[] { entrega } });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/motorista", supervisor, new { motoristaId });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/veiculo", supervisor, new { veiculoId });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/saida", supervisor, new { saidaPlanejada = RoteirosDeEntrega.Amanha(8) });
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/planejamento", supervisor, null);

        var cancelada = await OkAsync(http, HttpMethod.Post, $"/api/entregas/{entrega}/cancelamento", operador, new { motivo = "SolicitacaoDoCliente" });
        Assert.Equal("Cancelada", cancelada.GetProperty("status").GetString());

        var depois = await RotaAsync(http, supervisor, rota);
        Assert.Equal("EmMontagem", depois.GetProperty("status").GetString());
        Assert.Empty(depois.GetProperty("paradas").EnumerateArray());

        var eventosDaRota = await EventosAsync(http, supervisor, $"/api/rotas/{rota}/eventos");
        Assert.Equal("ParadaRemovida", eventosDaRota[^2].GetProperty("tipo").GetString());
        Assert.Equal("EntregaCancelada", eventosDaRota[^2].GetProperty("dados").GetProperty("motivo").GetString());
        Assert.Equal("RetornouParaMontagem", eventosDaRota[^1].GetProperty("tipo").GetString());
        Assert.Equal(
            ["Criada", "Planejada", "Atribuida", "Cancelada"],
            (await EventosAsync(http, supervisor, $"/api/entregas/{entrega}/eventos")).Select(evento => evento.GetProperty("tipo").GetString()));
    }

    [Fact]
    public async Task RotaDeOutraOrganizacaoRespondeComoInexistente()
    {
        using var http = Cliente();
        var (_, supervisorDeA, operadorDeA) = await OrganizacaoAsync(http);
        var (_, supervisorDeB, _) = await OrganizacaoAsync(http);
        var (clienteDeA, destinatarioDeA) = await CriarClienteEDestinatarioAsync(http, supervisorDeA);
        var entregaDeA = (await CriarEntregasAsync(http, operadorDeA, clienteDeA, destinatarioDeA, 1))[0];
        var motoristaDeA = await CriarAsync(http, supervisorDeA, "/api/motoristas", RoteirosDeCadastro.Obter("motorista").Corpo());
        var veiculoDeA = await CriarAsync(http, supervisorDeA, "/api/veiculos", RoteirosDeCadastro.Obter("veiculo").Corpo());
        var criadaEmB = await RotaAsync(http, supervisorDeB, await CriarRotaAsync(http, supervisorDeB));
        var rotaDeB = criadaEmB.GetProperty("id").GetGuid();
        var inventada = Guid.CreateVersion7();

        foreach (var (metodo, sufixo, corpo) in new (HttpMethod, string, object?)[]
        {
            (HttpMethod.Get, string.Empty, null),
            (HttpMethod.Get, "/eventos", null),
            (HttpMethod.Post, "/paradas", new { entregas = new[] { entregaDeA } }),
            (HttpMethod.Delete, $"/paradas/{entregaDeA}", null),
            (HttpMethod.Put, "/ordem", new { versao = criadaEmB.GetProperty("versao").GetUInt32(), entregas = new[] { entregaDeA } }),
            (HttpMethod.Put, "/motorista", new { motoristaId = motoristaDeA }),
            (HttpMethod.Put, "/veiculo", new { veiculoId = veiculoDeA }),
            (HttpMethod.Put, "/saida", new { saidaPlanejada = RoteirosDeEntrega.Amanha(8) }),
            (HttpMethod.Post, "/planejamento", null),
            (HttpMethod.Post, "/cancelamento", null),
        })
        {
            using var sobreB = await EnviarAsync(http, metodo, $"/api/rotas/{rotaDeB}{sufixo}", supervisorDeA, corpo);
            using var sobreInventada = await EnviarAsync(http, metodo, $"/api/rotas/{inventada}{sufixo}", supervisorDeA, corpo);

            Assert.True(sobreB.StatusCode == HttpStatusCode.NotFound, $"{metodo}{sufixo}: veio {(int)sobreB.StatusCode}");
            Assert.Equal(await AssinaturaAsync(sobreInventada), await AssinaturaAsync(sobreB));
        }

        var intacta = await RotaAsync(http, supervisorDeB, rotaDeB);
        Assert.Equal("EmMontagem", intacta.GetProperty("status").GetString());
        Assert.Equal(criadaEmB.GetProperty("versao").GetUInt32(), intacta.GetProperty("versao").GetUInt32());
        Assert.Equal("Criada", await StatusDaEntregaAsync(http, supervisorDeA, entregaDeA));
    }

    [Fact]
    public async Task CadastrosDeOutraOrganizacaoNaoEntramNaRota()
    {
        using var http = Cliente();
        var (_, supervisorDeA, _) = await OrganizacaoAsync(http);
        var (_, supervisorDeB, operadorDeB) = await OrganizacaoAsync(http);
        var (clienteDeB, destinatarioDeB) = await CriarClienteEDestinatarioAsync(http, supervisorDeB);
        var entregaDeB = (await CriarEntregasAsync(http, operadorDeB, clienteDeB, destinatarioDeB, 1))[0];
        var motoristaDeB = await CriarAsync(http, supervisorDeB, "/api/motoristas", RoteirosDeCadastro.Obter("motorista").Corpo());
        var veiculoDeB = await CriarAsync(http, supervisorDeB, "/api/veiculos", RoteirosDeCadastro.Obter("veiculo").Corpo());
        var hubDeB = await CriarAsync(http, supervisorDeB, "/api/hubs", RoteirosDeCadastro.Obter("hub").Corpo());
        var rotaDeA = await CriarRotaAsync(http, supervisorDeA);

        foreach (var (metodo, rota, comDeB, comInventado, codigo) in new (HttpMethod, string, object, object, string)[]
        {
            (HttpMethod.Post, $"/api/rotas/{rotaDeA}/paradas", new { entregas = new[] { entregaDeB } }, new { entregas = new[] { Guid.CreateVersion7() } }, "entrega_nao_encontrada"),
            (HttpMethod.Put, $"/api/rotas/{rotaDeA}/motorista", new { motoristaId = motoristaDeB }, new { motoristaId = Guid.CreateVersion7() }, "motorista_nao_encontrado"),
            (HttpMethod.Put, $"/api/rotas/{rotaDeA}/veiculo", new { veiculoId = veiculoDeB }, new { veiculoId = Guid.CreateVersion7() }, "veiculo_nao_encontrado"),
            (HttpMethod.Post, "/api/rotas", new { data = Amanha, hubId = hubDeB }, new { data = Amanha, hubId = Guid.CreateVersion7() }, "hub_nao_encontrado"),
        })
        {
            using var respostaDeB = await EnviarAsync(http, metodo, rota, supervisorDeA, comDeB);
            using var respostaInventada = await EnviarAsync(http, metodo, rota, supervisorDeA, comInventado);

            Assert.Equal(HttpStatusCode.NotFound, respostaDeB.StatusCode);
            Assert.Equal(codigo, await CodigoDoErroAsync(respostaDeB));
            Assert.Equal(await AssinaturaAsync(respostaInventada), await AssinaturaAsync(respostaDeB));
        }

        Assert.Equal("Criada", await StatusDaEntregaAsync(http, operadorDeB, entregaDeB));
    }

    public static TheoryData<string, string> CorposMalformados() => new()
    {
        { string.Empty, """{}""" },
        { string.Empty, """{"data":"15/09/2026"}""" },
        { string.Empty, """{"data":"2030-01-01","status":"Planejada"}""" },
        { string.Empty, """{"data":"2030-01-01","organizacaoId":"0198f0e2-0000-7000-8000-000000000000"}""" },
        { "/paradas", """{"entregas":[]}""" },
        { "/paradas", """{"entregas":"todas"}""" },
        { "/ordem", """{"entregas":["0198f0e2-0000-7000-8000-000000000000"]}""" },
        { "/saida", """{"saidaPlanejada":"amanhã cedo"}""" },
    };

    [Theory]
    [MemberData(nameof(CorposMalformados))]
    public async Task CorpoMalformadoOuComCampoProtegidoDevolve400(string sufixo, string corpo)
    {
        using var http = Cliente();
        var (_, supervisor, _) = await OrganizacaoAsync(http);
        var url = sufixo.Length == 0 ? "/api/rotas" : $"/api/rotas/{await CriarRotaAsync(http, supervisor)}{sufixo}";
        var metodo = sufixo is "/ordem" or "/saida" ? HttpMethod.Put : HttpMethod.Post;

        using var resposta = await EnviarAsync(http, metodo, url, supervisor, new StringContent(corpo, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Theory]
    [InlineData("UPDATE eventos_da_rota SET tipo = 'Cancelada' WHERE id = @id")]
    [InlineData("DELETE FROM eventos_da_rota WHERE id = @id")]
    [InlineData("TRUNCATE eventos_da_rota")]
    public async Task TimelineDaRotaRecusaAlteracaoEExclusaoNoBanco(string sql)
    {
        using var http = Cliente();
        var (_, supervisor, _) = await OrganizacaoAsync(http);
        var rota = await CriarRotaAsync(http, supervisor);
        var eventoId = Guid.Parse((await Banco.ConsultarEscalarAsync(
            "SELECT id FROM eventos_da_rota WHERE rota_id = @rota", ("rota", rota)))!);

        var erro = await Assert.ThrowsAsync<PostgresException>(() => Banco.ExecutarAsync(sql, ("id", eventoId)));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, erro.SqlState);
    }

    [Fact]
    public async Task ListaFiltraPorDataStatusEMotorista()
    {
        using var http = Cliente();
        var (_, supervisor, _) = await OrganizacaoAsync(http);
        var motoristaId = await CriarAsync(http, supervisor, "/api/motoristas", RoteirosDeCadastro.Obter("motorista").Corpo());
        var deAmanha = await CriarRotaAsync(http, supervisor);
        var deDepois = await CriarRotaAsync(http, supervisor, Amanha.AddDays(1));
        var cancelada = await CriarRotaAsync(http, supervisor);
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{deDepois}/motorista", supervisor, new { motoristaId });
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{cancelada}/cancelamento", supervisor, null);

        async Task<List<Guid>> IdsAsync(string consulta)
        {
            using var resposta = await EnviarAsync(http, HttpMethod.Get, $"/api/rotas{consulta}", supervisor);
            Assert.True(resposta.StatusCode == HttpStatusCode.OK, $"{consulta}: {(int)resposta.StatusCode}");
            return [.. (await JsonAsync(resposta)).GetProperty("itens").EnumerateArray().Select(item => item.GetProperty("id").GetGuid())];
        }

        var data = Amanha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Assert.Equal(3, (await IdsAsync(string.Empty)).Count);
        Assert.Equal(new[] { deAmanha, cancelada }.Order(), (await IdsAsync($"?data={data}")).Order());
        Assert.Equal([cancelada], await IdsAsync("?status=Cancelada"));
        Assert.Equal([deDepois], await IdsAsync($"?motoristaId={motoristaId}"));

        foreach (var invalida in new[] { "?status=cancelada", "?status=2", "?data=amanha" })
        {
            using var resposta = await EnviarAsync(http, HttpMethod.Get, $"/api/rotas{invalida}", supervisor);
            Assert.True(resposta.StatusCode == HttpStatusCode.BadRequest, $"{invalida}: {(int)resposta.StatusCode}");
        }
    }

    private async Task<(OrganizacaoDeTeste Organizacao, string Supervisor, string Operador)> OrganizacaoAsync(HttpClient http)
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Supervisor, Perfil.Operador);
        var supervisor = (await EntrarAsync(http, organizacao.Com(Perfil.Supervisor))).TokenDeAcesso;
        var operador = (await EntrarAsync(http, organizacao.Com(Perfil.Operador))).TokenDeAcesso;
        return (organizacao, supervisor, operador);
    }

    private static async Task<Guid> CriarAsync(HttpClient http, string token, string url, object corpo)
    {
        using var resposta = await EnviarAsync(http, HttpMethod.Post, url, token, corpo);
        var json = await JsonAsync(resposta);
        Assert.True(resposta.StatusCode == HttpStatusCode.Created, $"{url}: {json}");
        return json.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CriarRotaAsync(HttpClient http, string token, DateOnly? data = null) =>
        await CriarAsync(http, token, "/api/rotas", new { data = data ?? Amanha });

    private static async Task<List<Guid>> CriarEntregasAsync(HttpClient http, string token, Guid clienteId, Guid destinatarioId, int quantidade)
    {
        var ids = new List<Guid>();
        for (var indice = 0; indice < quantidade; indice++)
        {
            ids.Add(await CriarAsync(http, token, "/api/entregas", RoteirosDeEntrega.Corpo(clienteId, destinatarioId)));
        }

        return ids;
    }

    private static async Task<JsonElement> OkAsync(HttpClient http, HttpMethod metodo, string url, string token, object? corpo)
    {
        using var resposta = await EnviarAsync(http, metodo, url, token, corpo);
        var json = await JsonAsync(resposta);
        Assert.True(resposta.StatusCode == HttpStatusCode.OK, $"{metodo} {url}: {(int)resposta.StatusCode} {json}");
        return json;
    }

    private static async Task ConflitoAsync(HttpClient http, HttpMethod metodo, string url, string token, object? corpo, string codigo)
    {
        using var resposta = await EnviarAsync(http, metodo, url, token, corpo);
        Assert.True(resposta.StatusCode == HttpStatusCode.Conflict, $"{metodo} {url}: {(int)resposta.StatusCode}");
        Assert.Equal(codigo, await CodigoDoErroAsync(resposta));
    }

    private static async Task RegraAsync(HttpClient http, HttpMethod metodo, string url, string token, object? corpo, string codigo)
    {
        using var resposta = await EnviarAsync(http, metodo, url, token, corpo);
        Assert.True(resposta.StatusCode == HttpStatusCode.UnprocessableEntity, $"{metodo} {url}: {(int)resposta.StatusCode}");
        Assert.Equal(codigo, await CodigoDoErroAsync(resposta));
    }

    private static Task<JsonElement> RotaAsync(HttpClient http, string token, Guid rota) =>
        OkAsync(http, HttpMethod.Get, $"/api/rotas/{rota}", token, null);

    private static async Task<string?> StatusDaEntregaAsync(HttpClient http, string token, Guid entrega) =>
        (await OkAsync(http, HttpMethod.Get, $"/api/entregas/{entrega}", token, null)).GetProperty("status").GetString();

    private static async Task<List<JsonElement>> EventosAsync(HttpClient http, string token, string url)
    {
        using var resposta = await EnviarAsync(http, HttpMethod.Get, url, token);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return [.. (await JsonAsync(resposta)).EnumerateArray()];
    }

    private static IEnumerable<Guid> EntregasDasParadas(JsonElement rota) =>
        rota.GetProperty("paradas").EnumerateArray().Select(parada => parada.GetProperty("entregaId").GetGuid());

    private static bool SemValor(JsonElement objeto, string propriedade) =>
        !objeto.TryGetProperty(propriedade, out var valor) || valor.ValueKind == JsonValueKind.Null;

    private static async Task<string> AssinaturaAsync(HttpResponseMessage resposta)
    {
        var json = await JsonAsync(resposta);
        var campos = json.EnumerateObject()
            .Where(campo => campo.Name is not ("traceId" or "idDeCorrelacao" or "instance"))
            .Select(campo => $"{campo.Name}={campo.Value.GetRawText()}");
        return $"{(int)resposta.StatusCode}|{string.Join("|", campos)}";
    }
}
