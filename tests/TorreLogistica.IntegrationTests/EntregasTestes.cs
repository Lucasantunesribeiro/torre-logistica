using System.Globalization;
using System.Net;
using System.Text.Json;
using Npgsql;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// Entrega como agregado central, contra PostgreSQL real — sem rota e sem GPS.
/// </summary>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class EntregasTestes(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    private static readonly string[] ConflitosPossiveisNaAlteracao = ["conflito_de_versao", "entrega_nao_editavel"];

    /// <summary>Critério de aceite da Fase 3.</summary>
    [Fact]
    public async Task ApiCriaEConsultaEntregaComHistoricoConsistente()
    {
        using var http = Cliente();
        var (organizacao, supervisor, operador) = await OrganizacaoAsync(http);
        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);
        var de = RoteirosDeEntrega.Amanha(9);
        var ate = RoteirosDeEntrega.Amanha(12);

        using var criacao = await EnviarAsync(http, HttpMethod.Post, "/api/entregas", operador, RoteirosDeEntrega.Corpo(clienteId, destinatarioId, de, ate));
        var criada = await JsonAsync(criacao);
        Assert.True(criacao.StatusCode == HttpStatusCode.Created, criada.ToString());

        var id = criada.GetProperty("id").GetGuid();
        Assert.Equal($"/api/entregas/{id}", criacao.Headers.Location?.OriginalString);
        Assert.Matches(@"^ENT-\d{4}-000001\z", criada.GetProperty("codigo").GetString());
        Assert.Equal("Criada", criada.GetProperty("status").GetString());
        Assert.Equal("Mercado Horizonte", criada.GetProperty("clienteNome").GetString());
        Assert.Equal("Carla Nunes", criada.GetProperty("destinatarioNome").GetString());
        Assert.Equal(de, criada.GetProperty("janelaPrometida").GetProperty("de").GetDateTimeOffset());
        Assert.Equal(ate, criada.GetProperty("janelaPrometida").GetProperty("ate").GetDateTimeOffset());
        Assert.True(SemValor(criada, "cancelamento"));

        // Sem endereço no pedido, a entrega copia o do destinatário, com a coordenada.
        Assert.Equal("Rua das Palmeiras", criada.GetProperty("endereco").GetProperty("logradouro").GetString());
        Assert.Equal("13010000", criada.GetProperty("endereco").GetProperty("cep").GetString());
        Assert.Equal(-22.91, criada.GetProperty("localizacao").GetProperty("latitude").GetDouble(), 6);

        using var leitura = await EnviarAsync(http, HttpMethod.Get, $"/api/entregas/{id}", operador);
        Assert.Equal(HttpStatusCode.OK, leitura.StatusCode);
        Assert.Equal(criada.GetRawText(), (await JsonAsync(leitura)).GetRawText());

        using var lista = await EnviarAsync(http, HttpMethod.Get, "/api/entregas", operador);
        var itens = (await JsonAsync(lista)).GetProperty("itens");
        Assert.Equal(id, Assert.Single(itens.EnumerateArray()).GetProperty("id").GetGuid());

        var evento = Assert.Single(await EventosAsync(http, operador, id));
        Assert.Equal(1, evento.GetProperty("sequencia").GetInt32());
        Assert.Equal("Criada", evento.GetProperty("tipo").GetString());
        Assert.Equal("Criada", evento.GetProperty("statusResultante").GetString());
        Assert.Equal(organizacao.Com(Perfil.Operador).Id, evento.GetProperty("autorUsuarioId").GetGuid());

        Assert.Equal("entrega_criada", await TiposDeAuditoriaAsync(id));
    }

    [Fact]
    public async Task CodigoHumanoEhSequencialSemRepeticaoEPorOrganizacao()
    {
        using var http = Cliente();
        var (_, supervisor, operador) = await OrganizacaoAsync(http);
        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);

        var respostas = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ =>
            EnviarAsync(http, HttpMethod.Post, "/api/entregas", operador, RoteirosDeEntrega.Corpo(clienteId, destinatarioId))));

        var numeros = new List<int>();
        foreach (var resposta in respostas)
        {
            using (resposta)
            {
                Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
                var codigo = (await JsonAsync(resposta)).GetProperty("codigo").GetString()!;
                numeros.Add(int.Parse(codigo[^6..], CultureInfo.InvariantCulture));
            }
        }

        // Doze criações simultâneas: doze números, de 1 a 12, sem repetição e sem lacuna.
        Assert.Equal(Enumerable.Range(1, 12), numeros.Order());

        var (_, supervisorDeB, operadorDeB) = await OrganizacaoAsync(http);
        var (clienteDeB, destinatarioDeB) = await CriarClienteEDestinatarioAsync(http, supervisorDeB);
        using var primeiraDeB = await EnviarAsync(http, HttpMethod.Post, "/api/entregas", operadorDeB, RoteirosDeEntrega.Corpo(clienteDeB, destinatarioDeB));
        Assert.EndsWith("-000001", (await JsonAsync(primeiraDeB)).GetProperty("codigo").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AlteracaoPermitidaRegistraOsCamposNaTimelineEExigeVersao()
    {
        using var http = Cliente();
        var (_, supervisor, operador) = await OrganizacaoAsync(http);
        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);
        var criada = await CriarEntregaAsync(http, operador, clienteId, destinatarioId);
        var id = criada.GetProperty("id").GetGuid();
        var versao = criada.GetProperty("versao").GetUInt32();

        using var alteracao = await EnviarAsync(http, HttpMethod.Put, $"/api/entregas/{id}", operador, Alteracao(
            versao, clienteId, destinatarioId, cep: "13015-904", observacoes: "Ligar antes de subir"));
        var alterada = await JsonAsync(alteracao);

        Assert.True(alteracao.StatusCode == HttpStatusCode.OK, alterada.ToString());
        Assert.Equal("13015904", alterada.GetProperty("endereco").GetProperty("cep").GetString());
        Assert.Equal(RoteirosDeEntrega.Amanha(10), alterada.GetProperty("janelaPrometida").GetProperty("de").GetDateTimeOffset());
        Assert.NotEqual(versao, alterada.GetProperty("versao").GetUInt32());

        var eventos = await EventosAsync(http, operador, id);
        Assert.Equal(2, eventos.Count);
        Assert.Equal("DadosAlterados", eventos[1].GetProperty("tipo").GetString());
        Assert.Equal(
            ["endereco", "localizacao", "janelaPrometida", "observacoes"],
            eventos[1].GetProperty("dados").GetProperty("campos").EnumerateArray().Select(campo => campo.GetString()));

        using var comVersaoVelha = await EnviarAsync(http, HttpMethod.Put, $"/api/entregas/{id}", operador, Alteracao(
            versao, clienteId, destinatarioId, observacoes: "Sobrescrita às cegas"));
        Assert.Equal(HttpStatusCode.Conflict, comVersaoVelha.StatusCode);
        Assert.Equal("conflito_de_versao", await CodigoDoErroAsync(comVersaoVelha));

        Assert.Equal("entrega_criada,entrega_alterada", await TiposDeAuditoriaAsync(id));
    }

    /// <summary>
    /// Histórico não é reescrito por tabela: o endereço da entrega é o do momento em que ela foi
    /// criada, e alterar o destinatário depois não muda a entrega nem a timeline.
    /// </summary>
    [Fact]
    public async Task EnderecoDaEntregaNaoMudaQuandoODestinatarioMuda()
    {
        using var http = Cliente();
        var (_, supervisor, operador) = await OrganizacaoAsync(http);
        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);
        var id = (await CriarEntregaAsync(http, operador, clienteId, destinatarioId)).GetProperty("id").GetGuid();

        using var destinatario = await EnviarAsync(http, HttpMethod.Get, $"/api/destinatarios/{destinatarioId}", supervisor);
        var versaoDoDestinatario = (await JsonAsync(destinatario)).GetProperty("versao").GetUInt32();
        using var mudanca = await EnviarAsync(http, HttpMethod.Put, $"/api/destinatarios/{destinatarioId}", supervisor, new
        {
            versao = versaoDoDestinatario,
            nome = "Carla Nunes",
            telefone = "11 3456-7890",
            endereco = RoteirosDeCadastro.Endereco(cep: "13015-904"),
            latitude = (double?)null,
            longitude = (double?)null,
            instrucoesDeEntrega = (string?)null,
        });
        Assert.Equal(HttpStatusCode.OK, mudanca.StatusCode);

        using var releitura = await EnviarAsync(http, HttpMethod.Get, $"/api/entregas/{id}", operador);
        var entrega = await JsonAsync(releitura);
        Assert.Equal("13010000", entrega.GetProperty("endereco").GetProperty("cep").GetString());
        Assert.Equal(-22.91, entrega.GetProperty("localizacao").GetProperty("latitude").GetDouble(), 6);
        Assert.Single(await EventosAsync(http, operador, id));
    }

    [Fact]
    public async Task CancelamentoEncerraAEntregaEBloqueiaAlteracao()
    {
        using var http = Cliente();
        var (_, supervisor, operador) = await OrganizacaoAsync(http);
        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);
        var id = (await CriarEntregaAsync(http, operador, clienteId, destinatarioId)).GetProperty("id").GetGuid();

        using var cancelamento = await EnviarAsync(http, HttpMethod.Post, $"/api/entregas/{id}/cancelamento", operador, new { motivo = "SolicitacaoDoCliente" });
        var cancelada = await JsonAsync(cancelamento);
        Assert.True(cancelamento.StatusCode == HttpStatusCode.OK, cancelada.ToString());
        Assert.Equal("Cancelada", cancelada.GetProperty("status").GetString());
        Assert.Equal("SolicitacaoDoCliente", cancelada.GetProperty("cancelamento").GetProperty("motivo").GetString());

        // Repetir é atendido sem efeito: o motivo registrado continua o primeiro.
        using var repeticao = await EnviarAsync(http, HttpMethod.Post, $"/api/entregas/{id}/cancelamento", operador, new { motivo = "EnderecoIncorreto" });
        var repetida = await JsonAsync(repeticao);
        Assert.Equal(HttpStatusCode.OK, repeticao.StatusCode);
        Assert.Equal("SolicitacaoDoCliente", repetida.GetProperty("cancelamento").GetProperty("motivo").GetString());

        using var alteracao = await EnviarAsync(http, HttpMethod.Put, $"/api/entregas/{id}", operador, Alteracao(
            repetida.GetProperty("versao").GetUInt32(), clienteId, destinatarioId, observacoes: "Depois do cancelamento"));
        Assert.Equal(HttpStatusCode.Conflict, alteracao.StatusCode);
        Assert.Equal("entrega_nao_editavel", await CodigoDoErroAsync(alteracao));

        var eventos = await EventosAsync(http, operador, id);
        Assert.Equal(["Criada", "Cancelada"], eventos.Select(evento => evento.GetProperty("tipo").GetString()));
        Assert.Equal("Cancelada", eventos[^1].GetProperty("statusResultante").GetString());
        Assert.Equal("entrega_criada,entrega_cancelada", await TiposDeAuditoriaAsync(id));
    }

    [Fact]
    public async Task MotivoOutroSemDescricaoEhRecusadoSemCancelar()
    {
        using var http = Cliente();
        var (_, supervisor, operador) = await OrganizacaoAsync(http);
        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);
        var id = (await CriarEntregaAsync(http, operador, clienteId, destinatarioId)).GetProperty("id").GetGuid();

        using var resposta = await EnviarAsync(http, HttpMethod.Post, $"/api/entregas/{id}/cancelamento", operador, new { motivo = "Outro" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        Assert.Equal("motivo_exige_descricao", await CodigoDoErroAsync(resposta));
        using var releitura = await EnviarAsync(http, HttpMethod.Get, $"/api/entregas/{id}", operador);
        Assert.Equal("Criada", (await JsonAsync(releitura)).GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("""{"motivo":"Desistiu"}""")]
    [InlineData("""{"motivo":4}""")]
    [InlineData("""{}""")]
    [InlineData("""{"motivo":"Outro","descricao":"x","status":"Criada"}""")]
    public async Task CorpoDeCancelamentoMalformadoDevolve400(string corpo)
    {
        using var http = Cliente();
        var (_, supervisor, operador) = await OrganizacaoAsync(http);
        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);
        var id = (await CriarEntregaAsync(http, operador, clienteId, destinatarioId)).GetProperty("id").GetGuid();

        using var resposta = await EnviarAsync(http, HttpMethod.Post, $"/api/entregas/{id}/cancelamento", operador, Json(corpo));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    /// <summary>Somente-inserção garantido pelo banco: nem SQL manual reescreve a timeline.</summary>
    [Theory]
    [InlineData("UPDATE eventos_da_entrega SET tipo = 'Cancelada' WHERE id = @id")]
    [InlineData("DELETE FROM eventos_da_entrega WHERE id = @id")]
    [InlineData("TRUNCATE eventos_da_entrega")]
    public async Task TimelineRecusaAlteracaoEExclusaoNoBanco(string sql)
    {
        using var http = Cliente();
        var (_, supervisor, operador) = await OrganizacaoAsync(http);
        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);
        var id = (await CriarEntregaAsync(http, operador, clienteId, destinatarioId)).GetProperty("id").GetGuid();
        var eventoId = Guid.Parse((await Banco.ConsultarEscalarAsync(
            "SELECT id FROM eventos_da_entrega WHERE entrega_id = @entrega", ("entrega", id)))!);

        var erro = await Assert.ThrowsAsync<PostgresException>(() => Banco.ExecutarAsync(sql, ("id", eventoId)));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, erro.SqlState);
        Assert.Equal("Criada", await Banco.ConsultarEscalarAsync(
            "SELECT tipo FROM eventos_da_entrega WHERE id = @id", ("id", eventoId)));
    }

    [Fact]
    public async Task EntregaDeOutraOrganizacaoRespondeComoInexistente()
    {
        using var http = Cliente();
        var (_, supervisorDeA, operadorDeA) = await OrganizacaoAsync(http);
        var (_, supervisorDeB, operadorDeB) = await OrganizacaoAsync(http);
        var (clienteDeA, destinatarioDeA) = await CriarClienteEDestinatarioAsync(http, supervisorDeA);
        var (clienteDeB, destinatarioDeB) = await CriarClienteEDestinatarioAsync(http, supervisorDeB);
        var deB = await CriarEntregaAsync(http, operadorDeB, clienteDeB, destinatarioDeB);
        var idDeB = deB.GetProperty("id").GetGuid();
        var versaoDeB = deB.GetProperty("versao").GetUInt32();
        var inventado = Guid.CreateVersion7();

        foreach (var (metodo, sufixo, corpo) in new (HttpMethod, string, object?)[]
        {
            (HttpMethod.Get, string.Empty, null),
            (HttpMethod.Get, "/eventos", null),
            (HttpMethod.Put, string.Empty, Alteracao(versaoDeB, clienteDeA, destinatarioDeA, observacoes: "invasão")),
            (HttpMethod.Post, "/cancelamento", new { motivo = "SolicitacaoDoCliente" }),
        })
        {
            using var sobreB = await EnviarAsync(http, metodo, $"/api/entregas/{idDeB}{sufixo}", operadorDeA, corpo);
            using var sobreInventado = await EnviarAsync(http, metodo, $"/api/entregas/{inventado}{sufixo}", operadorDeA, corpo);

            Assert.True(sobreB.StatusCode == HttpStatusCode.NotFound, $"{metodo}{sufixo}: veio {(int)sobreB.StatusCode}");
            Assert.Equal(await AssinaturaAsync(sobreInventado), await AssinaturaAsync(sobreB));
        }

        using var listaDeA = await EnviarAsync(http, HttpMethod.Get, "/api/entregas", operadorDeA);
        Assert.Equal(0, (await JsonAsync(listaDeA)).GetProperty("total").GetInt32());

        using var releituraEmB = await EnviarAsync(http, HttpMethod.Get, $"/api/entregas/{idDeB}", operadorDeB);
        var intacta = await JsonAsync(releituraEmB);
        Assert.Equal("Criada", intacta.GetProperty("status").GetString());
        Assert.Equal(versaoDeB, intacta.GetProperty("versao").GetUInt32());
        Assert.Single(await EventosAsync(http, operadorDeB, idDeB));
    }

    [Fact]
    public async Task CadastroDeOutraOrganizacaoNaoServeParaEntrega()
    {
        using var http = Cliente();
        var (_, supervisorDeA, operadorDeA) = await OrganizacaoAsync(http);
        var (_, supervisorDeB, _) = await OrganizacaoAsync(http);
        var (clienteDeA, destinatarioDeA) = await CriarClienteEDestinatarioAsync(http, supervisorDeA);
        var (clienteDeB, destinatarioDeB) = await CriarClienteEDestinatarioAsync(http, supervisorDeB);

        foreach (var (comDeB, comInventado, codigo) in new[]
        {
            (RoteirosDeEntrega.Corpo(clienteDeB, destinatarioDeA), RoteirosDeEntrega.Corpo(Guid.CreateVersion7(), destinatarioDeA), "cliente_nao_encontrado"),
            (RoteirosDeEntrega.Corpo(clienteDeA, destinatarioDeB), RoteirosDeEntrega.Corpo(clienteDeA, Guid.CreateVersion7()), "destinatario_nao_encontrado"),
        })
        {
            using var respostaDeB = await EnviarAsync(http, HttpMethod.Post, "/api/entregas", operadorDeA, comDeB);
            using var respostaInventada = await EnviarAsync(http, HttpMethod.Post, "/api/entregas", operadorDeA, comInventado);

            Assert.Equal(HttpStatusCode.NotFound, respostaDeB.StatusCode);
            Assert.Equal(codigo, await CodigoDoErroAsync(respostaDeB));
            Assert.Equal(await AssinaturaAsync(respostaInventada), await AssinaturaAsync(respostaDeB));
        }

        // Nenhuma tentativa recusada consumiu número: a primeira entrega válida é a 000001.
        var valida = await CriarEntregaAsync(http, operadorDeA, clienteDeA, destinatarioDeA);
        Assert.EndsWith("-000001", valida.GetProperty("codigo").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CadastroInativoNaoRecebeNovaEntregaMasNaoTravaAsQueJaExistem()
    {
        using var http = Cliente();
        var (_, supervisor, operador) = await OrganizacaoAsync(http);
        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);
        var existente = await CriarEntregaAsync(http, operador, clienteId, destinatarioId);

        using var inativacaoDoCliente = await EnviarAsync(http, HttpMethod.Post, $"/api/clientes/{clienteId}/inativacao", supervisor);
        using var comClienteInativo = await EnviarAsync(http, HttpMethod.Post, "/api/entregas", operador, RoteirosDeEntrega.Corpo(clienteId, destinatarioId));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, comClienteInativo.StatusCode);
        Assert.Equal("cliente_inativo", await CodigoDoErroAsync(comClienteInativo));

        using var ativacaoDoCliente = await EnviarAsync(http, HttpMethod.Post, $"/api/clientes/{clienteId}/ativacao", supervisor);
        using var inativacaoDoDestinatario = await EnviarAsync(http, HttpMethod.Post, $"/api/destinatarios/{destinatarioId}/inativacao", supervisor);
        using var comDestinatarioInativo = await EnviarAsync(http, HttpMethod.Post, "/api/entregas", operador, RoteirosDeEntrega.Corpo(clienteId, destinatarioId));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, comDestinatarioInativo.StatusCode);
        Assert.Equal("destinatario_inativo", await CodigoDoErroAsync(comDestinatarioInativo));

        // A entrega que já existia continua corrigível: inativar não reescreve o passado.
        using var correcao = await EnviarAsync(http, HttpMethod.Put, $"/api/entregas/{existente.GetProperty("id").GetGuid()}", operador, Alteracao(
            existente.GetProperty("versao").GetUInt32(), clienteId, destinatarioId, cep: "13010-000", observacoes: "Portaria 24h", manterJanela: true));
        Assert.Equal(HttpStatusCode.OK, correcao.StatusCode);
    }

    public static TheoryData<string> CasosForaDaRegra() =>
        new("janela_invertida", "janela_longa", "janela_no_passado", "coordenada_sem_endereco", "cep_invalido", "observacao_com_controle");

    [Theory]
    [MemberData(nameof(CasosForaDaRegra))]
    public async Task DadoForaDaRegraDevolve422ComCodigo(string caso)
    {
        using var http = Cliente();
        var (_, supervisor, operador) = await OrganizacaoAsync(http);
        var (c, d) = await CriarClienteEDestinatarioAsync(http, supervisor);
        var agora = DateTimeOffset.UtcNow;

        var (corpo, codigo) = caso switch
        {
            "janela_invertida" => (RoteirosDeEntrega.Corpo(c, d, RoteirosDeEntrega.Amanha(12), RoteirosDeEntrega.Amanha(9)), "janela_invalida"),
            "janela_longa" => (RoteirosDeEntrega.Corpo(c, d, RoteirosDeEntrega.Amanha(9), RoteirosDeEntrega.Amanha(9).AddDays(8)), "janela_invalida"),
            "janela_no_passado" => (RoteirosDeEntrega.Corpo(c, d, agora.AddHours(-3), agora.AddHours(-1)), "janela_no_passado"),
            "coordenada_sem_endereco" => ((object)new
            {
                clienteId = c,
                destinatarioId = d,
                latitude = -22.9,
                longitude = -47.0,
                prometidaDe = RoteirosDeEntrega.Amanha(9),
                prometidaAte = RoteirosDeEntrega.Amanha(12),
            }, "coordenada_sem_endereco"),
            "cep_invalido" => (new
            {
                clienteId = c,
                destinatarioId = d,
                endereco = RoteirosDeCadastro.Endereco(cep: "1301-000"),
                prometidaDe = RoteirosDeEntrega.Amanha(9),
                prometidaAte = RoteirosDeEntrega.Amanha(12),
            }, "cep_invalido"),
            "observacao_com_controle" => (RoteirosDeEntrega.Corpo(c, d, observacoes: "linha\nforjada"), "observacoes_invalidas"),
            _ => throw new ArgumentOutOfRangeException(nameof(caso), caso, "Caso desconhecido."),
        };

        using var resposta = await EnviarAsync(http, HttpMethod.Post, "/api/entregas", operador, corpo);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        Assert.Equal(codigo, await CodigoDoErroAsync(resposta));
    }

    public static TheoryData<string> CorposMalformados() => new(
        """{"destinatarioId":"{d}","prometidaDe":"{de}","prometidaAte":"{ate}"}""",
        """{"clienteId":"{c}","destinatarioId":"{d}","prometidaAte":"{ate}"}""",
        """{"clienteId":"abc","destinatarioId":"{d}","prometidaDe":"{de}","prometidaAte":"{ate}"}""",
        """{"clienteId":"{c}","destinatarioId":"{d}","prometidaDe":"{de}","prometidaAte":"{ate}","status":"Entregue"}""",
        """{"clienteId":"{c}","destinatarioId":"{d}","prometidaDe":"{de}","prometidaAte":"{ate}","codigo":"ENT-2026-999999"}""",
        """{"clienteId":"{c}","destinatarioId":"{d}","prometidaDe":"{de}","prometidaAte":"{ate}","organizacaoId":"{c}"}""");

    [Theory]
    [MemberData(nameof(CorposMalformados))]
    public async Task CorpoMalformadoOuComCampoProtegidoDevolve400(string modelo)
    {
        using var http = Cliente();
        var (_, supervisor, operador) = await OrganizacaoAsync(http);
        var (c, d) = await CriarClienteEDestinatarioAsync(http, supervisor);
        var corpo = modelo
            .Replace("{c}", c.ToString(), StringComparison.Ordinal)
            .Replace("{d}", d.ToString(), StringComparison.Ordinal)
            .Replace("{de}", RoteirosDeEntrega.Amanha(9).ToString("O", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{ate}", RoteirosDeEntrega.Amanha(12).ToString("O", CultureInfo.InvariantCulture), StringComparison.Ordinal);

        using var resposta = await EnviarAsync(http, HttpMethod.Post, "/api/entregas", operador, Json(corpo));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        using var lista = await EnviarAsync(http, HttpMethod.Get, "/api/entregas", operador);
        Assert.Equal(0, (await JsonAsync(lista)).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ListaFiltraPorStatusClienteCodigoEJanela()
    {
        using var http = Cliente();
        var (_, supervisor, operador) = await OrganizacaoAsync(http);
        var (clienteUm, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);
        using var outroCliente = await EnviarAsync(http, HttpMethod.Post, "/api/clientes", supervisor, RoteirosDeCadastro.Obter("cliente").Corpo());
        var clienteDois = (await JsonAsync(outroCliente)).GetProperty("id").GetGuid();

        var amanha = RoteirosDeEntrega.Amanha(0);
        var primeira = await CriarEntregaAsync(http, operador, clienteUm, destinatarioId, amanha.AddHours(9), amanha.AddHours(12));
        var segunda = await CriarEntregaAsync(http, operador, clienteUm, destinatarioId, amanha.AddDays(1).AddHours(9), amanha.AddDays(1).AddHours(12));
        var terceira = await CriarEntregaAsync(http, operador, clienteDois, destinatarioId, amanha.AddDays(2).AddHours(9), amanha.AddDays(2).AddHours(12));
        using var cancelamento = await EnviarAsync(
            http, HttpMethod.Post, $"/api/entregas/{segunda.GetProperty("id").GetGuid()}/cancelamento", operador, new { motivo = "CadastroDuplicado" });

        async Task<List<string>> CodigosAsync(string consulta)
        {
            using var resposta = await EnviarAsync(http, HttpMethod.Get, $"/api/entregas{consulta}", operador);
            Assert.True(resposta.StatusCode == HttpStatusCode.OK, $"{consulta}: {(int)resposta.StatusCode}");
            return [.. (await JsonAsync(resposta)).GetProperty("itens").EnumerateArray().Select(item => item.GetProperty("codigo").GetString()!)];
        }

        string Codigo(JsonElement entrega) => entrega.GetProperty("codigo").GetString()!;
        string Instante(DateTimeOffset instante) => Uri.EscapeDataString(instante.ToString("O", CultureInfo.InvariantCulture));

        // Sem filtro: da janela que termina antes para a que termina depois.
        Assert.Equal([Codigo(primeira), Codigo(segunda), Codigo(terceira)], await CodigosAsync(string.Empty));
        Assert.Equal([Codigo(segunda)], await CodigosAsync("?status=Cancelada"));
        Assert.Equal(3, (await CodigosAsync("?status=Criada&status=Cancelada")).Count);
        Assert.Equal([Codigo(terceira)], await CodigosAsync($"?clienteId={clienteDois}"));
        Assert.Equal([Codigo(terceira)], await CodigosAsync($"?codigo={Codigo(terceira).ToLowerInvariant()}"));
        Assert.Equal(
            [Codigo(segunda)],
            await CodigosAsync($"?janelaAPartirDe={Instante(amanha.AddDays(1))}&janelaAte={Instante(amanha.AddDays(1).AddHours(23))}"));

        foreach (var invalida in new[]
        {
            "?status=criada",
            "?status=1",
            $"?codigo={new string('A', 31)}",
            $"?janelaAPartirDe={Instante(amanha.AddDays(2))}&janelaAte={Instante(amanha)}",
        })
        {
            using var resposta = await EnviarAsync(http, HttpMethod.Get, $"/api/entregas{invalida}", operador);
            Assert.True(resposta.StatusCode == HttpStatusCode.BadRequest, $"{invalida}: {(int)resposta.StatusCode}");
        }
    }

    [Fact]
    public async Task CancelamentosSimultaneosGeramUmUnicoEvento()
    {
        using var http = Cliente();
        var (_, supervisor, operador) = await OrganizacaoAsync(http);
        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);
        var id = (await CriarEntregaAsync(http, operador, clienteId, destinatarioId)).GetProperty("id").GetGuid();

        var respostas = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            EnviarAsync(http, HttpMethod.Post, $"/api/entregas/{id}/cancelamento", operador, new { motivo = "SolicitacaoDoCliente" })));
        var status = respostas.Select(resposta => resposta.StatusCode).ToList();
        foreach (var resposta in respostas)
        {
            resposta.Dispose();
        }

        Assert.Contains(HttpStatusCode.OK, status);
        Assert.All(status, codigo => Assert.True(codigo is HttpStatusCode.OK or HttpStatusCode.Conflict, $"veio {(int)codigo}"));
        Assert.Equal("1", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM eventos_da_entrega WHERE entrega_id = @id AND tipo = 'Cancelada'", ("id", id)));
    }

    /// <summary>
    /// Alteração e cancelamento disputando a mesma entrega: um pode perder, mas a história que fica
    /// é contínua e termina no status atual — nunca cancelamento seguido de alteração.
    /// </summary>
    [Fact]
    public async Task AlteracaoECancelamentoSimultaneosMantemAHistoriaCoerente()
    {
        using var http = Cliente();
        var (_, supervisor, operador) = await OrganizacaoAsync(http);
        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);

        for (var rodada = 0; rodada < 5; rodada++)
        {
            var criada = await CriarEntregaAsync(http, operador, clienteId, destinatarioId);
            var id = criada.GetProperty("id").GetGuid();

            var alteracao = EnviarAsync(http, HttpMethod.Put, $"/api/entregas/{id}", operador, Alteracao(
                criada.GetProperty("versao").GetUInt32(), clienteId, destinatarioId, cep: "13010-000", observacoes: $"Rodada {rodada}", manterJanela: true));
            var cancelamento = EnviarAsync(http, HttpMethod.Post, $"/api/entregas/{id}/cancelamento", operador, new { motivo = "SolicitacaoDoCliente" });
            using var respostaDaAlteracao = await alteracao;
            using var respostaDoCancelamento = await cancelamento;

            if (respostaDaAlteracao.StatusCode != HttpStatusCode.OK)
            {
                Assert.Equal(HttpStatusCode.Conflict, respostaDaAlteracao.StatusCode);
                Assert.Contains(await CodigoDoErroAsync(respostaDaAlteracao), ConflitosPossiveisNaAlteracao);
            }

            if (respostaDoCancelamento.StatusCode != HttpStatusCode.OK)
            {
                Assert.Equal(HttpStatusCode.Conflict, respostaDoCancelamento.StatusCode);
                Assert.Equal("conflito_de_versao", await CodigoDoErroAsync(respostaDoCancelamento));
            }

            using var releitura = await EnviarAsync(http, HttpMethod.Get, $"/api/entregas/{id}", operador);
            var atual = await JsonAsync(releitura);
            var eventos = await EventosAsync(http, operador, id);
            var tipos = eventos.Select(evento => evento.GetProperty("tipo").GetString()).ToList();

            Assert.Equal(Enumerable.Range(1, eventos.Count), eventos.Select(evento => evento.GetProperty("sequencia").GetInt32()));
            Assert.Equal(atual.GetProperty("status").GetString(), eventos[^1].GetProperty("statusResultante").GetString());
            Assert.Equal(respostaDaAlteracao.StatusCode == HttpStatusCode.OK, tipos.Contains("DadosAlterados"));
            Assert.Equal(respostaDoCancelamento.StatusCode == HttpStatusCode.OK, tipos.Contains("Cancelada"));

            if (tipos.Contains("DadosAlterados") && tipos.Contains("Cancelada"))
            {
                Assert.True(tipos.IndexOf("DadosAlterados") < tipos.IndexOf("Cancelada"), string.Join(",", tipos));
            }
        }
    }

    [Fact]
    public async Task TimelineAuditoriaELogNaoGuardamDadoPessoal()
    {
        using var http = Cliente();
        var (_, supervisor, operador) = await OrganizacaoAsync(http);
        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);
        var criada = await CriarEntregaAsync(http, operador, clienteId, destinatarioId);
        var id = criada.GetProperty("id").GetGuid();

        using var alteracao = await EnviarAsync(http, HttpMethod.Put, $"/api/entregas/{id}", operador, Alteracao(
            criada.GetProperty("versao").GetUInt32(), clienteId, destinatarioId, cep: "13015-904", observacoes: "Apartamento 1203, falar com Beatriz"));
        Assert.Equal(HttpStatusCode.OK, alteracao.StatusCode);

        var dados = string.Concat(
            await Banco.ConsultarEscalarAsync("SELECT string_agg(dados::text, '|') FROM eventos_da_entrega WHERE entrega_id = @id", ("id", id)),
            await Banco.ConsultarEscalarAsync("SELECT string_agg(dados::text, '|') FROM eventos_de_auditoria WHERE alvo_id = @id", ("id", id)));
        var logs = Fabrica.Logs.TextoCompleto();

        Assert.Contains("observacoes", dados, StringComparison.Ordinal);
        Assert.Contains(criada.GetProperty("codigo").GetString()!, logs, StringComparison.Ordinal);

        foreach (var valorPessoal in new[] { "Palmeiras", "13015904", "Beatriz", "1203", "Carla" })
        {
            Assert.DoesNotContain(valorPessoal, dados, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(valorPessoal, logs, StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task<(OrganizacaoDeTeste Organizacao, string Supervisor, string Operador)> OrganizacaoAsync(HttpClient http)
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Supervisor, Perfil.Operador);
        var supervisor = (await EntrarAsync(http, organizacao.Com(Perfil.Supervisor))).TokenDeAcesso;
        var operador = (await EntrarAsync(http, organizacao.Com(Perfil.Operador))).TokenDeAcesso;
        return (organizacao, supervisor, operador);
    }

    private static async Task<JsonElement> CriarEntregaAsync(
        HttpClient http,
        string token,
        Guid clienteId,
        Guid destinatarioId,
        DateTimeOffset? de = null,
        DateTimeOffset? ate = null)
    {
        using var resposta = await EnviarAsync(http, HttpMethod.Post, "/api/entregas", token, RoteirosDeEntrega.Corpo(clienteId, destinatarioId, de, ate));
        var corpo = await JsonAsync(resposta);
        Assert.True(resposta.StatusCode == HttpStatusCode.Created, corpo.ToString());
        return corpo;
    }

    private static async Task<List<JsonElement>> EventosAsync(HttpClient http, string token, Guid entregaId)
    {
        using var resposta = await EnviarAsync(http, HttpMethod.Get, $"/api/entregas/{entregaId}/eventos", token);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return [.. (await JsonAsync(resposta)).EnumerateArray()];
    }

    private Task<string?> TiposDeAuditoriaAsync(Guid entregaId) =>
        Banco.ConsultarEscalarAsync(
            "SELECT string_agg(tipo, ',' ORDER BY ocorrido_em) FROM eventos_de_auditoria WHERE alvo_id = @id", ("id", entregaId));

    /// <summary>Corpo de alteração. Por padrão muda a janela para 10h–14h de amanhã.</summary>
    private static object Alteracao(
        uint versao,
        Guid clienteId,
        Guid destinatarioId,
        string cep = "13015-904",
        string? observacoes = null,
        bool manterJanela = false) => new
        {
            versao,
            clienteId,
            destinatarioId,
            endereco = RoteirosDeCadastro.Endereco(cep: cep),
            latitude = manterJanela ? -22.91 : -22.95,
            longitude = manterJanela ? -47.065 : -47.1,
            prometidaDe = manterJanela ? RoteirosDeEntrega.Amanha(9) : RoteirosDeEntrega.Amanha(10),
            prometidaAte = manterJanela ? RoteirosDeEntrega.Amanha(12) : RoteirosDeEntrega.Amanha(14),
            observacoes,
        };

    private static bool SemValor(JsonElement objeto, string propriedade) =>
        !objeto.TryGetProperty(propriedade, out var valor) || valor.ValueKind == JsonValueKind.Null;

    private static StringContent Json(string corpo) => new(corpo, System.Text.Encoding.UTF8, "application/json");

    private static async Task<string> AssinaturaAsync(HttpResponseMessage resposta)
    {
        var json = await JsonAsync(resposta);
        var campos = json.EnumerateObject()
            .Where(campo => campo.Name is not ("traceId" or "idDeCorrelacao" or "instance"))
            .Select(campo => $"{campo.Name}={campo.Value.GetRawText()}");
        return $"{(int)resposta.StatusCode}|{string.Join("|", campos)}";
    }
}
