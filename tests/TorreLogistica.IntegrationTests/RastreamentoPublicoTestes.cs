using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// Rastreamento público: link com token forte, resposta neutra e o que a página não mostra.
/// </summary>
/// <remarks>
/// Herda o cenário de <see cref="TesteDeComprovante"/> — organização, motorista, rota iniciada e uma
/// entrega em rota, com relógio sob controle do teste. É exatamente o estado de que esta fase precisa, e
/// duplicá-lo aqui só criaria duas montagens para manter em sincronia.
/// </remarks>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class RastreamentoPublicoTestes(ContainerPostgis banco) : TesteDeComprovante(banco)
{
    private const string TokenComFormatoValido = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task LinkMostraAEncomendaESilenciaSobreAOperacao()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);

        var token = await EmitirAsync(http, cenario);
        using var resposta = await http.GetAsync(Publico(token), Cancelamento);
        var bruto = await resposta.Content.ReadAsStringAsync(Cancelamento);
        var pagina = JsonDocument.Parse(bruto).RootElement;

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("no-store", resposta.Headers.CacheControl?.ToString());

        Assert.StartsWith("ENT-", pagina.GetProperty("codigo").GetString(), StringComparison.Ordinal);
        Assert.Equal("EmRota", pagina.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(pagina.GetProperty("destino").GetProperty("cidade").GetString()));

        // A timeline pública tem os marcos da encomenda, não as decisões internas da operação.
        var marcos = pagina.GetProperty("marcos").EnumerateArray().Select(marco => marco.GetProperty("tipo").GetString()).ToArray();
        Assert.Contains("Criada", marcos);
        Assert.Contains("SaiuParaRota", marcos);
        Assert.DoesNotContain("Planejada", marcos);
        Assert.DoesNotContain("Atribuida", marcos);

        // O que não pode vazar para quem só tem o link. A busca é por nome de campo, entre aspas: procurar
        // "rota" solto acusaria o marco legítimo "SaiuParaRota".
        foreach (var proibido in new[]
        {
            "\"motoristaId\"", "\"rotaId\"", "\"logradouro\"", "\"numero\"", "\"complemento\"", "\"cep\"",
            "\"autorUsuarioId\"", "\"organizacaoId\"", "\"dados\"", "\"id\"", "\"destinatarioNome\"", "\"clienteNome\"",
        })
        {
            Assert.DoesNotContain(proibido, bruto, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(cenario.Entrega.ToString(), bruto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(token, bruto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BancoGuardaSoOHashEReemitirDerrubaOLinkAnterior()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);

        var primeiro = await EmitirAsync(http, cenario);

        var hashEsperado = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(primeiro))).ToLowerInvariant();
        var hashGravado = await Banco.ConsultarEscalarAsync(
            "SELECT encode(hash_do_token, 'hex') FROM tokens_de_rastreamento WHERE entrega_id = @id", ("id", cenario.Entrega));

        Assert.Equal(hashEsperado, hashGravado);

        // Nenhuma coluna guarda o valor emitido: procurar o token em toda a linha não acha nada.
        Assert.Equal("0", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM tokens_de_rastreamento WHERE tokens_de_rastreamento::text LIKE @busca",
            ("busca", $"%{primeiro}%")));

        var segundo = await EmitirAsync(http, cenario);
        Assert.NotEqual(primeiro, segundo);

        using var comOAntigo = await http.GetAsync(Publico(primeiro), Cancelamento);
        using var comONovo = await http.GetAsync(Publico(segundo), Cancelamento);

        Assert.Equal(HttpStatusCode.NotFound, comOAntigo.StatusCode);
        Assert.Equal(HttpStatusCode.OK, comONovo.StatusCode);

        Assert.Equal("1", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM tokens_de_rastreamento WHERE entrega_id = @id AND revogado_em IS NULL", ("id", cenario.Entrega)));
        Assert.Equal("2", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM tokens_de_rastreamento WHERE entrega_id = @id", ("id", cenario.Entrega)));
    }

    /// <summary>
    /// Malformado, desconhecido, revogado e expirado precisam ser indistinguíveis: qualquer diferença
    /// conta ao atacante que aquele link já existiu.
    /// </summary>
    [Fact]
    public async Task TokenQueNaoAbreNadaRespondeSempreAMesmaCoisa()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);

        var revogado = await EmitirAsync(http, cenario);
        await EmitirAsync(http, cenario);

        var paraExpirar = await CenarioAsync(http);
        var expirado = await EmitirAsync(http, paraExpirar);

        using var malformado = await http.GetAsync(Publico("nao-e-um-token"), Cancelamento);
        using var desconhecido = await http.GetAsync(Publico(TokenComFormatoValido), Cancelamento);
        using var comRevogado = await http.GetAsync(Publico(revogado), Cancelamento);

        Tempo.Advance(TimeSpan.FromDays(31));
        using var comExpirado = await http.GetAsync(Publico(expirado), Cancelamento);

        var referencia = await AssinaturaAsync(desconhecido);
        Assert.Equal(HttpStatusCode.NotFound, desconhecido.StatusCode);
        Assert.Equal(referencia, await AssinaturaAsync(malformado));
        Assert.Equal(referencia, await AssinaturaAsync(comRevogado));
        Assert.Equal(referencia, await AssinaturaAsync(comExpirado));
        Assert.Equal("rastreamento_nao_encontrado", await CodigoDoErroAsync(desconhecido));
    }

    [Fact]
    public async Task PosicaoApareceAproximadaSoEnquantoAEntregaEstaACaminho()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var token = await EmitirAsync(http, cenario);

        await PosicoesTestes.EnviarAsync(
            http,
            cenario.TokenDoMotorista,
            PosicoesTestes.Posicao(1, Tempo.GetUtcNow(), latitude: -22.9137, longitude: -47.0612));

        var comPosicao = await PublicoAsync(http, token);
        var posicao = comPosicao.GetProperty("posicao");

        // Arredondada na grade da política: a rua some, a região fica.
        Assert.Equal(-22.91, posicao.GetProperty("latitude").GetDouble(), 6);
        Assert.Equal(-47.06, posicao.GetProperty("longitude").GetDouble(), 6);
        Assert.Equal(1_100, posicao.GetProperty("precisaoAproximadaEmMetros").GetInt32());

        // Passados 16 minutos sem posição nova, a página deixa de mostrar um ponto que já não informa nada.
        Tempo.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal(JsonValueKind.Null, (await PublicoAsync(http, token)).GetProperty("posicao").ValueKind);
    }

    [Fact]
    public async Task ProvaDaEntregaApareceDepoisDaConclusaoESoPorUrlAssinada()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var token = await EmitirAsync(http, cenario);

        Assert.Equal(JsonValueKind.Null, (await PublicoAsync(http, token)).GetProperty("comprovante").ValueKind);

        var (chave, conteudo) = await ArquivoEnviadoAsync(http, cenario);
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/comprovante", cenario.TokenDoMotorista, new
        {
            recebidoPor = "Carla Nunes",
            arquivos = new[] { new { tipo = "Foto", chave } },
        });

        var pagina = await PublicoAsync(http, token);
        var comprovante = pagina.GetProperty("comprovante");

        Assert.Equal("Entregue", pagina.GetProperty("status").GetString());
        Assert.Equal("Carla Nunes", comprovante.GetProperty("recebidoPor").GetString());

        var url = comprovante.GetProperty("arquivos")[0].GetProperty("url").GetString()!;
        using var baixado = await http.GetAsync(Relativo(url), Cancelamento);
        Assert.Equal(HttpStatusCode.OK, baixado.StatusCode);
        Assert.Equal(conteudo, await baixado.Content.ReadAsByteArrayAsync(Cancelamento));

        // A mesma chave sem a assinatura não abre: o link público não vira porta para o storage.
        using var semAssinatura = await http.GetAsync($"/api/arquivos/{chave}", Cancelamento);
        Assert.Equal(HttpStatusCode.Forbidden, semAssinatura.StatusCode);
    }

    [Fact]
    public async Task OutraOrganizacaoNaoEmiteLinkNemAlcancaAEntrega()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var outra = await CenarioAsync(http);

        using var emissaoAlheia = await EnviarAsync(
            http, HttpMethod.Post, $"/api/entregas/{cenario.Entrega}/link-de-rastreamento", outra.Supervisor);
        using var inventada = await EnviarAsync(
            http, HttpMethod.Post, $"/api/entregas/{Guid.CreateVersion7()}/link-de-rastreamento", outra.Supervisor);

        Assert.Equal(HttpStatusCode.NotFound, emissaoAlheia.StatusCode);
        Assert.Equal(await AssinaturaAsync(inventada), await AssinaturaAsync(emissaoAlheia));

        // O link legítimo continua sendo só desta entrega.
        var token = await EmitirAsync(http, cenario);
        var pagina = await PublicoAsync(http, token);
        Assert.Equal(
            (await OkAsync(http, HttpMethod.Get, $"/api/entregas/{cenario.Entrega}", cenario.Operador)).GetProperty("codigo").GetString(),
            pagina.GetProperty("codigo").GetString());
    }

    /// <summary>A emissão é anotada na auditoria — o ato, nunca o valor emitido.</summary>
    [Fact]
    public async Task EmissaoFicaNaAuditoriaSemOValorDoLink()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);

        var token = await EmitirAsync(http, cenario);

        var registros = await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM eventos_de_auditoria WHERE alvo_tipo = 'rastreamento' AND alvo_id = @id", ("id", cenario.Entrega));
        Assert.Equal("1", registros);

        Assert.Equal("0", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM eventos_de_auditoria WHERE dados::text LIKE @busca", ("busca", $"%{token}%")));
    }

    private static string Publico(string token) => $"/api/publico/rastreamento/{token}";

    private static async Task<string> EmitirAsync(HttpClient http, CenarioDeComprovante cenario) =>
        (await OkAsync(http, HttpMethod.Post, $"/api/entregas/{cenario.Entrega}/link-de-rastreamento", cenario.Supervisor))
            .GetProperty("token").GetString()!;

    private static async Task<JsonElement> PublicoAsync(HttpClient http, string token)
    {
        using var resposta = await http.GetAsync(Publico(token), Cancelamento);
        var json = await JsonAsync(resposta);
        Assert.True(resposta.StatusCode == HttpStatusCode.OK, $"público: {(int)resposta.StatusCode} {json}");
        return json;
    }

}

/// <summary>Com o limite baixo, consultar demais é recusado antes de qualquer trabalho.</summary>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class RastreamentoPublicoComLimiteBaixoTestes(ContainerPostgis banco) : TesteDeComprovante(banco)
{
    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?> ConfiguracaoAdicional =>
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Torre:LimiteDeRequisicoes:ConsultasPublicasPorMinuto"] = "2",
        };

    [Fact]
    public async Task ConsultaPublicaTemLimitePorEndereco()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);

        var token = (await OkAsync(http, HttpMethod.Post, $"/api/entregas/{cenario.Entrega}/link-de-rastreamento", cenario.Supervisor))
            .GetProperty("token").GetString()!;

        using var primeira = await http.GetAsync($"/api/publico/rastreamento/{token}", Cancelamento);
        using var segunda = await http.GetAsync($"/api/publico/rastreamento/{token}", Cancelamento);
        using var terceira = await http.GetAsync($"/api/publico/rastreamento/{token}", Cancelamento);

        Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, terceira.StatusCode);
        Assert.Equal("limite_de_requisicoes", await CodigoDoErroAsync(terceira));

        // Força bruta sobre tokens cai no mesmo limite: não há como varrer o espaço de links.
        using var comOutroToken = await http.GetAsync("/api/publico/rastreamento/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", Cancelamento);
        Assert.Equal(HttpStatusCode.TooManyRequests, comOutroToken.StatusCode);
    }
}
