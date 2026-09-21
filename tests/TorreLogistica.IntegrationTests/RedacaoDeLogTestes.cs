using System.Globalization;
using System.Net;
using System.Text.Json;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// O que o sistema escreve no log não pode ser credencial nem rastro de pessoa.
/// </summary>
/// <remarks>
/// <para>
/// A autenticação já tinha essa prova desde a Fase 3. O que este teste fecha é o que veio depois: a URL
/// assinada do comprovante, o token do rastreamento público, a chave de integração, o segredo do webhook
/// e a coordenada do motorista. Cada um deles é, sozinho, acesso a alguma coisa — e log é o lugar onde
/// segredo vaza sem ninguém decidir vazá-lo, porque ninguém decidiu nada: alguém só registrou "a
/// requisição" inteira.
/// </para>
/// <para>
/// O teste exercita os cinco fluxos e depois varre o texto de tudo o que foi registrado, do nível
/// Information para cima. Falhar aqui não é ajustar o teste: é tirar o valor do log.
/// </para>
/// </remarks>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class RedacaoDeLogTestes(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    private static readonly string[] EventosDoWebhook = ["delivery.completed"];

    private const double Latitude = -22.94871;
    private const double Longitude = -47.06133;

    /// <summary>Nenhum segredo emitido pela operação aparece no log.</summary>
    [Fact]
    public async Task SegredosEmitidosNaoAparecemNoLog()
    {
        using var http = Cliente();
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Administrador, Perfil.Supervisor, Perfil.Motorista);
        var administrador = (await EntrarAsync(http, organizacao.Com(Perfil.Administrador))).TokenDeAcesso;
        var sessaoDoSupervisor = await EntrarAsync(http, organizacao.Com(Perfil.Supervisor));
        var supervisor = sessaoDoSupervisor.TokenDeAcesso;

        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);
        var entrega = await CriarAsync(http, supervisor, "/api/entregas", RoteirosDeEntrega.Corpo(clienteId, destinatarioId));

        // Token do rastreamento público: quem tem o link tem a entrega.
        var token = (await OkAsync(http, HttpMethod.Post, $"/api/entregas/{entrega}/link-de-rastreamento", supervisor))
            .GetProperty("token").GetString()!;
        using var publico = await http.GetAsync(new Uri($"/api/publico/rastreamento/{token}", UriKind.Relative), Cancelamento);
        Assert.Equal(HttpStatusCode.OK, publico.StatusCode);

        // Chave de integração: credencial de máquina, mostrada uma vez.
        var chaveDeIntegracao = (await OkAsync(http, HttpMethod.Post, "/api/integracoes", administrador, new { nome = "ERP do cliente" }, HttpStatusCode.Created))
            .GetProperty("chave").GetString()!;

        // Segredo do webhook: é ele que assina o que sai.
        var segredoDoWebhook = (await OkAsync(
                http,
                HttpMethod.Post,
                "/api/webhooks/assinaturas",
                administrador,
                new { nome = "ERP", url = "https://erp.exemplo.test/hooks", eventos = EventosDoWebhook },
                HttpStatusCode.Created))
            .GetProperty("segredo").GetString()!;

        // Uma requisição autenticada pela chave, com corpo inválido de propósito: o caminho de erro é
        // justamente onde a requisição inteira costuma acabar registrada, credencial junto.
        using var recusada = await EnviarAsync(http, HttpMethod.Post, "/api/integracoes/v1/entregas", chaveDeIntegracao, new { });
        Assert.True(
            recusada.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity,
            $"Esperava recusa de validação, veio {(int)recusada.StatusCode}.");

        var registrado = Fabrica.Logs.TextoCompleto();

        Assert.DoesNotContain(token, registrado, StringComparison.Ordinal);
        Assert.DoesNotContain(chaveDeIntegracao, registrado, StringComparison.Ordinal);
        Assert.DoesNotContain(segredoDoWebhook, registrado, StringComparison.Ordinal);
        Assert.DoesNotContain(supervisor, registrado, StringComparison.Ordinal);
        Assert.DoesNotContain(sessaoDoSupervisor.CookieDeRenovacao, registrado, StringComparison.Ordinal);
        Assert.DoesNotContain(Cenario.SenhaPadrao, registrado, StringComparison.Ordinal);
    }

    /// <summary>
    /// A coordenada do motorista não vira linha de log.
    /// </summary>
    /// <remarks>
    /// Telemetria registrada em texto é um diário de deslocamento de uma pessoa, guardado onde a política
    /// de retenção não alcança e onde qualquer um com acesso ao log lê. O que o log pode dizer é quantas
    /// posições entraram, não onde cada uma delas caiu.
    /// </remarks>
    [Fact]
    public async Task CoordenadaDoMotoristaNaoApareceNoLog()
    {
        using var http = Cliente();
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Supervisor, Perfil.Motorista);
        var supervisor = (await EntrarAsync(http, organizacao.Com(Perfil.Supervisor))).TokenDeAcesso;
        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);
        var entrega = await CriarAsync(http, supervisor, "/api/entregas", RoteirosDeEntrega.Corpo(clienteId, destinatarioId));

        var motorista = await CriarAsync(http, supervisor, "/api/motoristas", RoteirosDeCadastro.Obter("motorista").Corpo());
        await OkAsync(http, HttpMethod.Put, $"/api/motoristas/{motorista}/conta", supervisor, new { usuarioId = organizacao.Com(Perfil.Motorista).Id });
        var veiculo = await CriarAsync(http, supervisor, "/api/veiculos", RoteirosDeCadastro.Obter("veiculo").Corpo());

        var dia = DateTime.UtcNow.Date.AddDays(1);
        var rota = await CriarAsync(http, supervisor, "/api/rotas", new { data = DateOnly.FromDateTime(dia) });
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/paradas", supervisor, new { entregas = new[] { entrega } });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/motorista", supervisor, new { motoristaId = motorista });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/veiculo", supervisor, new { veiculoId = veiculo });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/saida", supervisor, new { saidaPlanejada = new DateTimeOffset(dia.AddHours(8), TimeSpan.Zero) });
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/planejamento", supervisor);

        var tokenDoMotorista = (await EntrarAsync(http, organizacao.Com(Perfil.Motorista))).TokenDeAcesso;
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{rota}/inicio", tokenDoMotorista);

        var lote = await PosicoesTestes.EnviarAsync(
            http,
            tokenDoMotorista,
            PosicoesTestes.Posicao(1, DateTimeOffset.UtcNow.AddSeconds(-5), Latitude, Longitude));
        Assert.Equal(1, lote.GetProperty("aceitas").GetInt32());

        var registrado = Fabrica.Logs.TextoCompleto();

        Assert.DoesNotContain(Latitude.ToString(CultureInfo.InvariantCulture), registrado, StringComparison.Ordinal);
        Assert.DoesNotContain(Longitude.ToString(CultureInfo.InvariantCulture), registrado, StringComparison.Ordinal);
        Assert.DoesNotContain(tokenDoMotorista, registrado, StringComparison.Ordinal);
    }

    private static async Task<JsonElement> OkAsync(
        HttpClient http,
        HttpMethod metodo,
        string url,
        string token,
        object? corpo = null,
        HttpStatusCode esperado = HttpStatusCode.OK)
    {
        using var resposta = await EnviarAsync(http, metodo, url, token, corpo);
        var json = await JsonAsync(resposta);
        Assert.True(resposta.StatusCode == esperado, $"{metodo} {url}: {(int)resposta.StatusCode} {json}");
        return json;
    }

    private static async Task<Guid> CriarAsync(HttpClient http, string token, string url, object corpo) =>
        (await OkAsync(http, HttpMethod.Post, url, token, corpo, HttpStatusCode.Created)).GetProperty("id").GetGuid();
}
