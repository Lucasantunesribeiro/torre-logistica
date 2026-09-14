using System.Net;
using System.Text.Json;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// Os cinco cadastros da estrutura operacional contra PostgreSQL real.
/// </summary>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class CadastrosOperacionaisTestes(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    public static TheoryData<string> Recursos() => RoteirosDeCadastro.Recursos();

    [Theory]
    [MemberData(nameof(Recursos))]
    public async Task CicloCompletoDeCadastroComVersaoEAuditoria(string recurso)
    {
        var roteiro = RoteirosDeCadastro.Obter(recurso);
        using var cliente = Cliente();
        var token = await TokenAsync(cliente, Perfil.Supervisor);

        using var criacao = await EnviarAsync(cliente, HttpMethod.Post, roteiro.Rota, token, roteiro.Corpo());
        var criado = await JsonAsync(criacao);
        Assert.True(criacao.StatusCode == HttpStatusCode.Created, $"{recurso}: {criado}");
        var id = criado.GetProperty("id").GetGuid();
        var versaoInicial = criado.GetProperty("versao").GetUInt32();
        Assert.Equal($"{roteiro.Rota}/{id}", criacao.Headers.Location?.OriginalString);
        Assert.True(criado.GetProperty("ativo").GetBoolean());

        using var leitura = await EnviarAsync(cliente, HttpMethod.Get, $"{roteiro.Rota}/{id}", token);
        Assert.Equal(HttpStatusCode.OK, leitura.StatusCode);

        using var listagem = await EnviarAsync(cliente, HttpMethod.Get, roteiro.Rota, token);
        var lista = await JsonAsync(listagem);
        Assert.Equal(1, lista.GetProperty("total").GetInt32());

        using var alteracao = await EnviarAsync(cliente, HttpMethod.Put, $"{roteiro.Rota}/{id}", token, roteiro.CorpoDeAlteracao(versaoInicial));
        var alterado = await JsonAsync(alteracao);
        Assert.True(alteracao.StatusCode == HttpStatusCode.OK, $"{recurso}: {alterado}");
        Assert.Equal(roteiro.ValorAlterado, alterado.GetProperty(roteiro.CampoAlterado).GetString());
        Assert.NotEqual(versaoInicial, alterado.GetProperty("versao").GetUInt32());

        // A versão lida antes da alteração já não vale: ninguém sobrescreve às cegas.
        using var comVersaoVelha = await EnviarAsync(cliente, HttpMethod.Put, $"{roteiro.Rota}/{id}", token, roteiro.CorpoDeAlteracao(versaoInicial));
        Assert.Equal(HttpStatusCode.Conflict, comVersaoVelha.StatusCode);
        Assert.Equal("conflito_de_versao", await CodigoDoErroAsync(comVersaoVelha));

        using var primeiraInativacao = await EnviarAsync(cliente, HttpMethod.Post, $"{roteiro.Rota}/{id}/inativacao", token);
        using var segundaInativacao = await EnviarAsync(cliente, HttpMethod.Post, $"{roteiro.Rota}/{id}/inativacao", token);
        Assert.Equal(HttpStatusCode.OK, primeiraInativacao.StatusCode);
        Assert.False((await JsonAsync(segundaInativacao)).GetProperty("ativo").GetBoolean());

        using var soInativos = await EnviarAsync(cliente, HttpMethod.Get, $"{roteiro.Rota}?ativo=false", token);
        using var soAtivos = await EnviarAsync(cliente, HttpMethod.Get, $"{roteiro.Rota}?ativo=true", token);
        Assert.Equal(1, (await JsonAsync(soInativos)).GetProperty("total").GetInt32());
        Assert.Equal(0, (await JsonAsync(soAtivos)).GetProperty("total").GetInt32());

        using var ativacao = await EnviarAsync(cliente, HttpMethod.Post, $"{roteiro.Rota}/{id}/ativacao", token);
        Assert.True((await JsonAsync(ativacao)).GetProperty("ativo").GetBoolean());

        // Uma linha por mudança real: a segunda inativação, que não mudou nada, não entra.
        var tipos = await Banco.ConsultarEscalarAsync(
            "SELECT string_agg(tipo, ',' ORDER BY ocorrido_em) FROM eventos_de_auditoria WHERE alvo_id = @id", ("id", id));
        Assert.Equal($"{recurso}_criado,{recurso}_alterado,{recurso}_inativado,{recurso}_ativado", tipos);
    }

    /// <summary>
    /// Leitura, alteração e inativação de cadastro de outra organização respondem como
    /// identificador inexistente — e o cadastro da outra organização fica intacto.
    /// </summary>
    [Theory]
    [MemberData(nameof(Recursos))]
    public async Task CadastroDeOutraOrganizacaoRespondeComoInexistente(string recurso)
    {
        var roteiro = RoteirosDeCadastro.Obter(recurso);
        using var cliente = Cliente();
        var tokenDeA = await TokenAsync(cliente, Perfil.Administrador);
        var tokenDeB = await TokenAsync(cliente, Perfil.Administrador);

        using var criacaoEmB = await EnviarAsync(cliente, HttpMethod.Post, roteiro.Rota, tokenDeB, roteiro.Corpo());
        var deB = await JsonAsync(criacaoEmB);
        var idDeB = deB.GetProperty("id").GetGuid();
        var versaoDeB = deB.GetProperty("versao").GetUInt32();
        var inventado = Guid.CreateVersion7();

        foreach (var (metodo, sufixo, corpo) in new (HttpMethod, string, object?)[]
        {
            (HttpMethod.Get, string.Empty, null),
            (HttpMethod.Put, string.Empty, roteiro.CorpoDeAlteracao(versaoDeB)),
            (HttpMethod.Post, "/inativacao", null),
        })
        {
            using var sobreB = await EnviarAsync(cliente, metodo, $"{roteiro.Rota}/{idDeB}{sufixo}", tokenDeA, corpo);
            using var sobreInventado = await EnviarAsync(cliente, metodo, $"{roteiro.Rota}/{inventado}{sufixo}", tokenDeA, corpo);

            Assert.True(sobreB.StatusCode == HttpStatusCode.NotFound, $"{recurso} {metodo}{sufixo}: veio {(int)sobreB.StatusCode}");
            Assert.Equal(await AssinaturaAsync(sobreInventado), await AssinaturaAsync(sobreB));
        }

        using var listaDeA = await EnviarAsync(cliente, HttpMethod.Get, roteiro.Rota, tokenDeA);
        Assert.Equal(0, (await JsonAsync(listaDeA)).GetProperty("total").GetInt32());

        using var releituraEmB = await EnviarAsync(cliente, HttpMethod.Get, $"{roteiro.Rota}/{idDeB}", tokenDeB);
        var intacto = await JsonAsync(releituraEmB);
        Assert.True(intacto.GetProperty("ativo").GetBoolean());
        Assert.Equal(versaoDeB, intacto.GetProperty("versao").GetUInt32());
    }

    [Fact]
    public async Task PlacaEhUnicaPorOrganizacaoSemDiferencaDeFormato()
    {
        using var cliente = Cliente();
        var tokenDeA = await TokenAsync(cliente, Perfil.Supervisor);
        var tokenDeB = await TokenAsync(cliente, Perfil.Supervisor);
        var placa = RoteirosDeCadastro.PlacaAleatoria();
        var comHifen = $"{placa[..3].ToLowerInvariant()}-{placa[3..]}";

        using var primeira = await EnviarAsync(cliente, HttpMethod.Post, "/api/veiculos", tokenDeA, Veiculo(placa));
        using var repetida = await EnviarAsync(cliente, HttpMethod.Post, "/api/veiculos", tokenDeA, Veiculo(comHifen));
        using var outraOrganizacao = await EnviarAsync(cliente, HttpMethod.Post, "/api/veiculos", tokenDeB, Veiculo(placa));

        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, repetida.StatusCode);
        Assert.Equal("placa_ja_cadastrada", await CodigoDoErroAsync(repetida));
        Assert.Equal(HttpStatusCode.Created, outraOrganizacao.StatusCode);
    }

    [Fact]
    public async Task NomeDeHubEhUnicoSemDiferencaDeAcentoOuCaixa()
    {
        using var cliente = Cliente();
        var token = await TokenAsync(cliente, Perfil.Supervisor);

        using var primeiro = await EnviarAsync(cliente, HttpMethod.Post, "/api/hubs", token, Hub("Hub São José"));
        using var repetido = await EnviarAsync(cliente, HttpMethod.Post, "/api/hubs", token, Hub("  hub  SAO jose "));

        Assert.Equal(HttpStatusCode.Created, primeiro.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        Assert.Equal("hub_ja_cadastrado", await CodigoDoErroAsync(repetido));
    }

    [Fact]
    public async Task CnpjEhUnicoQuandoInformadoEClienteSemCnpjNaoConflita()
    {
        using var cliente = Cliente();
        var token = await TokenAsync(cliente, Perfil.Supervisor);
        var cnpj = RoteirosDeCadastro.CnpjAleatorio();
        var formatado = $"{cnpj[..2]}.{cnpj[2..5]}.{cnpj[5..8]}/{cnpj[8..12]}-{cnpj[12..]}";

        using var primeiro = await EnviarAsync(cliente, HttpMethod.Post, "/api/clientes", token, new { nome = "Cliente Um", cnpj });
        using var repetido = await EnviarAsync(cliente, HttpMethod.Post, "/api/clientes", token, new { nome = "Cliente Dois", cnpj = formatado });
        using var semCnpj1 = await EnviarAsync(cliente, HttpMethod.Post, "/api/clientes", token, new { nome = "Cliente Três" });
        using var semCnpj2 = await EnviarAsync(cliente, HttpMethod.Post, "/api/clientes", token, new { nome = "Cliente Quatro" });

        Assert.Equal(HttpStatusCode.Created, primeiro.StatusCode);
        Assert.Equal(formatado, (await JsonAsync(primeiro)).GetProperty("cnpj").GetString());
        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        Assert.Equal("cnpj_ja_cadastrado", await CodigoDoErroAsync(repetido));
        Assert.Equal(HttpStatusCode.Created, semCnpj1.StatusCode);
        Assert.Equal(HttpStatusCode.Created, semCnpj2.StatusCode);
    }

    public static TheoryData<string, string, string> DadosInvalidosPorRegra() => new()
    {
        { "/api/veiculos", """{"placa":"AB12345","identificacao":"Moto 1","tipo":"Motocicleta"}""", "placa_invalida" },
        { "/api/veiculos", """{"placa":"ABC1234","identificacao":"Moto 1","tipo":"Motocicleta","capacidadeEmKg":0}""", "capacidade_invalida" },
        { "/api/hubs", HubJson(cep: "1301-000"), "cep_invalido" },
        { "/api/hubs", HubJson(uf: "XX"), "uf_invalida" },
        { "/api/hubs", HubJson(latitude: "91"), "coordenada_invalida" },
        { "/api/hubs", HubJson(latitude: "0", longitude: "0"), "coordenada_invalida" },
        { "/api/clientes", """{"nome":"Cliente","cnpj":"11.222.333/0001-82"}""", "cnpj_invalido" },
        { "/api/motoristas", """{"nome":"Rafael","telefone":"123"}""", "telefone_invalido" },
        { "/api/destinatarios", """{"nome":"Carla","endereco":{"logradouro":"Rua A","numero":"1","bairro":"Centro","cidade":"Campinas","uf":"SP","cep":"13010-000"},"latitude":-22.9}""", "coordenada_invalida" },
    };

    [Theory]
    [MemberData(nameof(DadosInvalidosPorRegra))]
    public async Task DadoForaDaRegraDevolve422ComCodigo(string rota, string corpo, string codigo)
    {
        using var cliente = Cliente();
        var token = await TokenAsync(cliente, Perfil.Supervisor);

        using var resposta = await EnviarAsync(cliente, HttpMethod.Post, rota, token, Json(corpo));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        Assert.Equal(codigo, await CodigoDoErroAsync(resposta));
    }

    public static TheoryData<string, string> CorposMalformados() => new()
    {
        { "/api/veiculos", """{"placa":"ABC1234","identificacao":"Moto","tipo":"Trator"}""" },
        { "/api/veiculos", """{"placa":"ABC1234","identificacao":"Moto","tipo":1}""" },
        { "/api/hubs", """{"nome":"Hub","latitude":-22.9,"longitude":-47.0}""" },
        { "/api/motoristas", """{"telefone":"11987654321"}""" },
        { "/api/clientes", $$"""{"nome":"Cliente","organizacaoId":"{{Guid.CreateVersion7()}}"}""" },
        { "/api/destinatarios", """{"nome":"Carla","endereco":{"logradouro":"Rua A","numero":"1","bairro":"Centro","cidade":"Campinas","uf":"SP","cep":"13010-000","pais":"BR"}}""" },
    };

    [Theory]
    [MemberData(nameof(CorposMalformados))]
    public async Task CorpoMalformadoOuComCampoDesconhecidoDevolve400(string rota, string corpo)
    {
        using var cliente = Cliente();
        var token = await TokenAsync(cliente, Perfil.Supervisor);

        using var resposta = await EnviarAsync(cliente, HttpMethod.Post, rota, token, Json(corpo));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task AlteracoesSimultaneasComAMesmaVersaoTemUmUnicoVencedor()
    {
        using var cliente = Cliente();
        var token = await TokenAsync(cliente, Perfil.Supervisor);
        using var criacao = await EnviarAsync(cliente, HttpMethod.Post, "/api/veiculos", token, Veiculo(RoteirosDeCadastro.PlacaAleatoria()));
        var criado = await JsonAsync(criacao);
        var id = criado.GetProperty("id").GetGuid();
        var versao = criado.GetProperty("versao").GetUInt32();

        var respostas = await Task.WhenAll(Enumerable.Range(0, 6).Select(indice =>
            EnviarAsync(cliente, HttpMethod.Put, $"/api/veiculos/{id}", token, new
            {
                versao,
                placa = RoteirosDeCadastro.PlacaAleatoria(),
                identificacao = $"Fiorino {indice}",
                tipo = "Utilitario",
                capacidadeEmKg = 650,
            })));
        var status = respostas.Select(resposta => resposta.StatusCode).ToList();
        foreach (var resposta in respostas)
        {
            resposta.Dispose();
        }

        Assert.Equal(1, status.Count(codigo => codigo == HttpStatusCode.OK));
        Assert.All(status.Where(codigo => codigo != HttpStatusCode.OK), codigo => Assert.Equal(HttpStatusCode.Conflict, codigo));
    }

    [Fact]
    public async Task BuscaIgnoraAcentoECaixaETrataCuringaComoTexto()
    {
        using var cliente = Cliente();
        var token = await TokenAsync(cliente, Perfil.Supervisor);
        using var um = await EnviarAsync(cliente, HttpMethod.Post, "/api/motoristas", token, new { nome = "João da Conceição" });
        using var dois = await EnviarAsync(cliente, HttpMethod.Post, "/api/motoristas", token, new { nome = "Márcia Lopes" });

        using var semAcento = await EnviarAsync(cliente, HttpMethod.Get, "/api/motoristas?busca=JOAO%20DA%20CONCEICAO", token);
        using var curinga = await EnviarAsync(cliente, HttpMethod.Get, "/api/motoristas?busca=%25", token);
        using var buscaLonga = await EnviarAsync(cliente, HttpMethod.Get, $"/api/motoristas?busca={new string('a', 101)}", token);

        Assert.Equal(1, (await JsonAsync(semAcento)).GetProperty("total").GetInt32());
        Assert.Equal(0, (await JsonAsync(curinga)).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, buscaLonga.StatusCode);
    }

    /// <summary>
    /// A trilha é somente-inserção: dado pessoal gravado nela não poderia ser apagado depois.
    /// Por isso a alteração registra o nome do campo, nunca o valor.
    /// </summary>
    [Fact]
    public async Task AuditoriaDeDestinatarioNaoGuardaDadoPessoal()
    {
        using var cliente = Cliente();
        var token = await TokenAsync(cliente, Perfil.Supervisor);
        using var criacao = await EnviarAsync(cliente, HttpMethod.Post, "/api/destinatarios", token, RoteirosDeCadastro.Obter("destinatario").Corpo());
        var criado = await JsonAsync(criacao);
        var id = criado.GetProperty("id").GetGuid();

        using var alteracao = await EnviarAsync(cliente, HttpMethod.Put, $"/api/destinatarios/{id}", token, new
        {
            versao = criado.GetProperty("versao").GetUInt32(),
            nome = "Carla Nunes Prado",
            telefone = "(19) 99123-4567",
            endereco = RoteirosDeCadastro.Endereco(cep: "13015-904"),
            latitude = -22.91,
            longitude = -47.065,
            instrucoesDeEntrega = "Portão lateral, interfone 12",
        });
        Assert.Equal(HttpStatusCode.OK, alteracao.StatusCode);

        var dados = await Banco.ConsultarEscalarAsync(
            "SELECT dados::text FROM eventos_de_auditoria WHERE alvo_id = @id AND tipo = 'destinatario_alterado'", ("id", id));

        Assert.NotNull(dados);
        Assert.Contains("telefone", dados, StringComparison.Ordinal);
        Assert.Contains("endereco", dados, StringComparison.Ordinal);
        foreach (var valorPessoal in new[] { "Prado", "991234567", "13015904", "Palmeiras" })
        {
            Assert.DoesNotContain(valorPessoal, dados, StringComparison.OrdinalIgnoreCase);
        }

        var logs = Fabrica.Logs.TextoCompleto();
        Assert.Contains("Destinatário", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("Carla", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("99123", logs, StringComparison.Ordinal);
    }

    private async Task<string> TokenAsync(HttpClient cliente, Perfil perfil)
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(perfil);
        return (await EntrarAsync(cliente, organizacao.Com(perfil))).TokenDeAcesso;
    }

    private static object Veiculo(string placa) => new { placa, identificacao = "Fiorino 03", tipo = "Utilitario", capacidadeEmKg = 650 };

    private static object Hub(string nome) => new { nome, endereco = RoteirosDeCadastro.Endereco(), latitude = -22.9056, longitude = -47.0608 };

    private static string HubJson(string cep = "13010-000", string uf = "SP", string latitude = "-22.9056", string longitude = "-47.0608") =>
        $$"""{"nome":"Hub","endereco":{"logradouro":"Rua A","numero":"1","bairro":"Centro","cidade":"Campinas","uf":"{{uf}}","cep":"{{cep}}"},"latitude":{{latitude}},"longitude":{{longitude}}}""";

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
