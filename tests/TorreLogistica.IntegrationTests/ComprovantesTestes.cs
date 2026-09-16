using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// Prova de entrega contra a API real: autorização de envio, upload por URL assinada, registro de metadados,
/// leitura assinada, expiração, isolamento entre organizações e repetição segura.
/// </summary>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class ComprovantesTestes(ContainerPostgis banco) : TesteDeComprovante(banco)
{
    /// <summary>Critério de aceite: a entrega concluída é provada sem tornar arquivo algum público.</summary>
    [Fact]
    public async Task ConclusaoComProvaGuardaMetadadosEServeArquivoSoPorUrlAssinada()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var imagem = ImagemFalsa(2_048);

        var autorizacao = await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/comprovante/autorizacao", cenario.TokenDoMotorista, new
        {
            tipo = "Foto",
            tipoDeConteudo = "image/jpeg",
        });

        var chave = autorizacao.GetProperty("chave").GetString()!;
        Assert.Equal(5 * 1024 * 1024, autorizacao.GetProperty("tamanhoMaximoEmBytes").GetInt64());
        Assert.Contains($"entregas/{cenario.Entrega:N}/", chave, StringComparison.Ordinal);

        // O arquivo vai direto ao storage, pela URL assinada — não pela API de negócio.
        var enviado = await EnviarArquivoAsync(http, autorizacao, imagem, "image/jpeg");
        Assert.Equal(imagem.Length, enviado.GetProperty("tamanhoEmBytes").GetInt64());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(imagem)).ToLowerInvariant(), enviado.GetProperty("hashSha256").GetString());

        var entrega = await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/comprovante", cenario.TokenDoMotorista, new
        {
            recebidoPor = "Carla Nunes",
            observacao = "Entregue na portaria.",
            latitude = -22.91,
            longitude = -47.06,
            arquivos = new[] { new { tipo = "Foto", chave } },
        });

        Assert.Equal("Entregue", entrega.GetProperty("status").GetString());

        var comprovante = await OkAsync(http, HttpMethod.Get, $"/api/entregas/{cenario.Entrega}/comprovante", cenario.Operador);
        Assert.Equal("Carla Nunes", comprovante.GetProperty("recebidoPor").GetString());
        Assert.Equal("Entregue na portaria.", comprovante.GetProperty("observacao").GetString());
        Assert.Equal(cenario.ContaDoMotorista, comprovante.GetProperty("autorUsuarioId").GetGuid());
        Assert.Equal(-22.91, comprovante.GetProperty("localizacao").GetProperty("latitude").GetDouble(), 2);

        var arquivo = Assert.Single(comprovante.GetProperty("arquivos").EnumerateArray());
        Assert.Equal("image/jpeg", arquivo.GetProperty("tipoDeConteudo").GetString());
        Assert.Equal(imagem.Length, arquivo.GetProperty("tamanhoEmBytes").GetInt64());

        // A leitura é por URL assinada curta, e o conteúdo devolvido é exatamente o que subiu.
        using var leitura = await http.GetAsync(Relativo(arquivo.GetProperty("url").GetString()!), Cancelamento);
        Assert.Equal(HttpStatusCode.OK, leitura.StatusCode);
        Assert.Equal(imagem, await leitura.Content.ReadAsByteArrayAsync(Cancelamento));
        Assert.Equal("no-store", leitura.Headers.CacheControl?.ToString());

        // Sem assinatura, o mesmo arquivo não sai.
        using var semAssinatura = await http.GetAsync($"/api/arquivos/{chave}", Cancelamento);
        Assert.Equal(HttpStatusCode.Forbidden, semAssinatura.StatusCode);

        // O binário não entrou no banco: lá ficam só os metadados.
        Assert.Equal("1", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM arquivos_do_comprovante WHERE chave = @chave", ("chave", chave)));
    }

    [Fact]
    public async Task TipoOuTamanhoForaDoAutorizadoEhRecusado()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);

        using var tipoRecusado = await EnviarAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/comprovante/autorizacao", cenario.TokenDoMotorista, new
        {
            tipo = "Foto",
            tipoDeConteudo = "application/pdf",
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tipoRecusado.StatusCode);
        Assert.Equal("tipo_de_arquivo_nao_aceito", await CodigoDoErroAsync(tipoRecusado));

        var autorizacao = await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/comprovante/autorizacao", cenario.TokenDoMotorista, new
        {
            tipo = "Foto",
            tipoDeConteudo = "image/jpeg",
        });

        // Tipo diferente do autorizado, com a mesma URL: recusado.
        using var outroTipo = await PutAsync(http, Relativo(autorizacao.GetProperty("url").GetString()!), ImagemFalsa(64), "image/png");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, outroTipo.StatusCode);

        // Acima do tamanho autorizado: recusado, sem gravar.
        using var grandeDemais = await PutAsync(http, Relativo(autorizacao.GetProperty("url").GetString()!), ImagemFalsa(6 * 1024 * 1024), "image/jpeg");
        Assert.Contains(grandeDemais.StatusCode, new[] { HttpStatusCode.RequestEntityTooLarge, HttpStatusCode.BadRequest });

        using var semArquivo = await EnviarAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/comprovante", cenario.TokenDoMotorista, new
        {
            recebidoPor = "Carla Nunes",
            arquivos = new[] { new { tipo = "Foto", chave = autorizacao.GetProperty("chave").GetString() } },
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, semArquivo.StatusCode);
        Assert.Equal("arquivo_nao_enviado", await CodigoDoErroAsync(semArquivo));
    }

    [Fact]
    public async Task UrlAssinadaExpiraEAssinaturaAdulteradaNaoVale()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);

        var autorizacao = await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/comprovante/autorizacao", cenario.TokenDoMotorista, new
        {
            tipo = "Foto",
            tipoDeConteudo = "image/jpeg",
        });
        var url = Relativo(autorizacao.GetProperty("url").GetString()!);

        // Assinatura trocada por outra: recusada sem revelar se o objeto existe.
        var adulterada = url.Replace("assinatura=", "assinatura=0", StringComparison.Ordinal);
        using var comAssinaturaFalsa = await PutAsync(http, adulterada, ImagemFalsa(64), "image/jpeg");
        Assert.Equal(HttpStatusCode.Forbidden, comAssinaturaFalsa.StatusCode);

        // Passada a validade, a mesma URL não vale mais.
        Tempo.Advance(TimeSpan.FromMinutes(11));
        using var expirada = await PutAsync(http, url, ImagemFalsa(64), "image/jpeg");
        Assert.Equal(HttpStatusCode.Forbidden, expirada.StatusCode);
        Assert.Equal("assinatura_invalida", await CodigoDoErroAsync(expirada));
    }

    [Fact]
    public async Task RepetirORegistroNaoDuplicaAProva()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var (chave, _) = await ArquivoEnviadoAsync(http, cenario);

        var corpo = new
        {
            recebidoPor = "Carla Nunes",
            arquivos = new[] { new { tipo = "Foto", chave } },
        };

        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/comprovante", cenario.TokenDoMotorista, corpo);
        var repetido = await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/comprovante", cenario.TokenDoMotorista, corpo);

        Assert.Equal("Entregue", repetido.GetProperty("status").GetString());
        Assert.Equal("1", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM comprovantes WHERE entrega_id = @id", ("id", cenario.Entrega)));
        Assert.Equal("1", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM eventos_da_entrega WHERE entrega_id = @id AND tipo = 'Entregue'", ("id", cenario.Entrega)));
    }

    [Fact]
    public async Task ComprovanteDeOutraOrganizacaoNaoApareceNemPodeSerRegistrado()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var outra = await CenarioAsync(http);
        var (chave, _) = await ArquivoEnviadoAsync(http, cenario);
        var (chaveDaOutra, _) = await ArquivoEnviadoAsync(http, outra);

        // Arquivo de outra entrega não vale como prova desta — conferido antes de existir comprovante,
        // porque com a prova já registrada o registro repetido nem chega a montar nada.
        using (var trocado = await EnviarAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/comprovante", cenario.TokenDoMotorista, new
        {
            recebidoPor = "Carla Nunes",
            arquivos = new[] { new { tipo = "Foto", chave = chaveDaOutra } },
        }))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, trocado.StatusCode);
            Assert.Equal("arquivo_de_outra_entrega", await CodigoDoErroAsync(trocado));
        }

        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/comprovante", cenario.TokenDoMotorista, new
        {
            recebidoPor = "Carla Nunes",
            arquivos = new[] { new { tipo = "Foto", chave } },
        });

        using var deOutra = await EnviarAsync(http, HttpMethod.Get, $"/api/entregas/{cenario.Entrega}/comprovante", outra.Operador);
        using var inventada = await EnviarAsync(http, HttpMethod.Get, $"/api/entregas/{Guid.CreateVersion7()}/comprovante", outra.Operador);
        Assert.Equal(HttpStatusCode.NotFound, deOutra.StatusCode);
        Assert.Equal(await AssinaturaAsync(inventada), await AssinaturaAsync(deOutra));
    }

    [Fact]
    public async Task ArquivoInexistenteEChaveComTravessiaSaoRecusados()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);

        using var inexistente = await http.GetAsync(
            $"/api/arquivos/organizacoes/x/entregas/y/naoexiste.jpg?expiraEm=99999999999&assinatura=abc", Cancelamento);
        Assert.Equal(HttpStatusCode.Forbidden, inexistente.StatusCode);

        // Barra escapada: a travessia chega ao endpoint em vez de ser normalizada pelo cliente, e mesmo
        // assim não passa — a assinatura nunca cobre uma chave dessas.
        using var travessia = await http.GetAsync(
            "/api/arquivos/organizacoes%2F..%2F..%2Fappsettings.json?expiraEm=99999999999&assinatura=abc", Cancelamento);
        Assert.Equal(HttpStatusCode.Forbidden, travessia.StatusCode);

        using var semComprovante = await EnviarAsync(http, HttpMethod.Get, $"/api/entregas/{cenario.Entrega}/comprovante", cenario.Operador);
        Assert.Equal(HttpStatusCode.NotFound, semComprovante.StatusCode);
        Assert.Equal("comprovante_nao_encontrado", await CodigoDoErroAsync(semComprovante));
    }
}

/// <summary>Com a política de prova obrigatória ligada, concluir sem comprovante é recusado.</summary>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class ComprovanteObrigatorioTestes(ContainerPostgis banco) : TesteDeComprovante(banco)
{
    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?> ConfiguracaoAdicional =>
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Torre:Comprovantes:ExigirNaConclusao"] = "true",
            ["Torre:Comprovantes:ExigirArquivo"] = "true",
        };

    [Fact]
    public async Task ConclusaoSemProvaEhRecusadaEComProvaPassa()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);

        using var semProva = await EnviarAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/conclusao", cenario.TokenDoMotorista);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, semProva.StatusCode);
        Assert.Equal("comprovante_obrigatorio", await CodigoDoErroAsync(semProva));
        Assert.Equal("EmRota", await StatusAsync(http, cenario));

        using var comprovanteSemArquivo = await EnviarAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/comprovante", cenario.TokenDoMotorista, new
        {
            recebidoPor = "Carla Nunes",
            arquivos = Array.Empty<object>(),
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, comprovanteSemArquivo.StatusCode);
        Assert.Equal("comprovante_sem_arquivo", await CodigoDoErroAsync(comprovanteSemArquivo));

        var (chave, _) = await ArquivoEnviadoAsync(http, cenario);
        var entrega = await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/comprovante", cenario.TokenDoMotorista, new
        {
            recebidoPor = "Carla Nunes",
            arquivos = new[] { new { tipo = "Foto", chave } },
        });

        Assert.Equal("Entregue", entrega.GetProperty("status").GetString());

        // Com a prova registrada, a conclusão avulsa continua respondendo o estado atual.
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/conclusao", cenario.TokenDoMotorista);
    }
}

/// <summary>Cenário comum: rota iniciada, uma entrega em rota e relógio sob controle do teste.</summary>
public abstract class TesteDeComprovante(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    /// <summary>Relógio da API.</summary>
    protected FakeTimeProvider Tempo { get; } = new(DateTimeOffset.UtcNow);

    /// <inheritdoc />
    protected override TimeProvider Relogio => Tempo;

    /// <summary>Organização, contas e a entrega em execução.</summary>
    protected sealed record CenarioDeComprovante(
        OrganizacaoDeTeste Organizacao,
        string Supervisor,
        string Operador,
        string TokenDoMotorista,
        Guid ContaDoMotorista,
        Guid Entrega);

    /// <summary>Bytes de uma "imagem" com tamanho controlado.</summary>
    protected static byte[] ImagemFalsa(int tamanho) => RandomNumberGenerator.GetBytes(tamanho);

    /// <summary>Caminho relativo de uma URL absoluta, para o cliente de teste.</summary>
    protected static string Relativo(string url) => new Uri(url).PathAndQuery;

    /// <summary>Cria organização, motorista, rota planejada e inicia a rota.</summary>
    protected async Task<CenarioDeComprovante> CenarioAsync(HttpClient http)
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Supervisor, Perfil.Operador, Perfil.Motorista);
        var contaDoMotorista = organizacao.Com(Perfil.Motorista);
        var supervisor = (await EntrarAsync(http, organizacao.Com(Perfil.Supervisor))).TokenDeAcesso;
        var operador = (await EntrarAsync(http, organizacao.Com(Perfil.Operador))).TokenDeAcesso;
        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, supervisor);

        var entrega = await CriarAsync(http, supervisor, "/api/entregas", RoteirosDeEntrega.Corpo(clienteId, destinatarioId));
        var motorista = await CriarAsync(http, supervisor, "/api/motoristas", RoteirosDeCadastro.Obter("motorista").Corpo());
        await OkAsync(http, HttpMethod.Put, $"/api/motoristas/{motorista}/conta", supervisor, new { usuarioId = contaDoMotorista.Id });
        var tokenDoMotorista = (await EntrarAsync(http, contaDoMotorista)).TokenDeAcesso;

        var veiculo = await CriarAsync(http, supervisor, "/api/veiculos", RoteirosDeCadastro.Obter("veiculo").Corpo());
        var rota = await CriarAsync(http, supervisor, "/api/rotas", new { data = DateOnly.FromDateTime(Tempo.GetUtcNow().UtcDateTime.Date.AddDays(1)) });
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/paradas", supervisor, new { entregas = new[] { entrega } });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/motorista", supervisor, new { motoristaId = motorista });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/veiculo", supervisor, new { veiculoId = veiculo });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/saida", supervisor, new
        {
            saidaPlanejada = new DateTimeOffset(Tempo.GetUtcNow().UtcDateTime.Date.AddDays(1).AddHours(8), TimeSpan.Zero),
        });
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/planejamento", supervisor);
        await OkAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{rota}/inicio", tokenDoMotorista);

        return new CenarioDeComprovante(organizacao, supervisor, operador, tokenDoMotorista, contaDoMotorista.Id, entrega);
    }

    /// <summary>Autoriza e envia um arquivo, devolvendo a chave e os bytes.</summary>
    protected static async Task<(string Chave, byte[] Conteudo)> ArquivoEnviadoAsync(HttpClient http, CenarioDeComprovante cenario)
    {
        var autorizacao = await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/comprovante/autorizacao", cenario.TokenDoMotorista, new
        {
            tipo = "Foto",
            tipoDeConteudo = "image/jpeg",
        });

        var conteudo = ImagemFalsa(1_024);
        await EnviarArquivoAsync(http, autorizacao, conteudo, "image/jpeg");

        return (autorizacao.GetProperty("chave").GetString()!, conteudo);
    }

    /// <summary>Envia o arquivo pela URL assinada e confere que o storage aceitou.</summary>
    protected static async Task<JsonElement> EnviarArquivoAsync(HttpClient http, JsonElement autorizacao, byte[] conteudo, string tipoDeConteudo)
    {
        using var resposta = await PutAsync(http, Relativo(autorizacao.GetProperty("url").GetString()!), conteudo, tipoDeConteudo);
        var json = await JsonAsync(resposta);
        Assert.True(resposta.StatusCode == HttpStatusCode.Created, $"upload: {(int)resposta.StatusCode} {json}");
        return json;
    }

    /// <summary>PUT de bytes com o tipo de conteúdo informado.</summary>
    protected static async Task<HttpResponseMessage> PutAsync(HttpClient http, string url, byte[] conteudo, string tipoDeConteudo)
    {
        using var corpo = new ByteArrayContent(conteudo);
        corpo.Headers.ContentType = new MediaTypeHeaderValue(tipoDeConteudo);

        using var requisicao = new HttpRequestMessage(HttpMethod.Put, url) { Content = corpo };
        return await http.SendAsync(requisicao, Cancelamento);
    }

    /// <summary>Status atual da entrega do cenário.</summary>
    protected static async Task<string?> StatusAsync(HttpClient http, CenarioDeComprovante cenario) =>
        (await OkAsync(http, HttpMethod.Get, $"/api/entregas/{cenario.Entrega}", cenario.Operador)).GetProperty("status").GetString();

    /// <summary>POST que precisa responder 201, devolvendo o identificador criado.</summary>
    protected static async Task<Guid> CriarAsync(HttpClient http, string token, string url, object corpo)
    {
        using var resposta = await EnviarAsync(http, HttpMethod.Post, url, token, corpo);
        var json = await JsonAsync(resposta);
        Assert.True(resposta.StatusCode == HttpStatusCode.Created, $"{url}: {json}");
        return json.GetProperty("id").GetGuid();
    }

    /// <summary>Requisição que precisa responder 200.</summary>
    protected static async Task<JsonElement> OkAsync(HttpClient http, HttpMethod metodo, string url, string token, object? corpo = null)
    {
        using var resposta = await EnviarAsync(http, metodo, url, token, corpo);
        var json = await JsonAsync(resposta);
        Assert.True(resposta.StatusCode == HttpStatusCode.OK, $"{metodo} {url}: {(int)resposta.StatusCode} {json}");
        return json;
    }

    /// <summary>Status e corpo sem campos de rastreio, para comparar respostas de erro.</summary>
    protected static async Task<string> AssinaturaAsync(HttpResponseMessage resposta)
    {
        var json = await JsonAsync(resposta);
        var campos = json.EnumerateObject()
            .Where(campo => campo.Name is not ("traceId" or "idDeCorrelacao" or "instance"))
            .Select(campo => $"{campo.Name}={campo.Value.GetRawText()}");
        return $"{(int)resposta.StatusCode}|{string.Join("|", campos)}";
    }
}
