using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// Prova de entrega com o provedor <c>blob</c>, contra o emulador oficial do Azure Storage.
/// </summary>
/// <remarks>
/// <para>
/// O mesmo fluxo de <c>ComprovantesTestes</c>, com uma diferença que é o ponto: a URL assinada não aponta
/// para a API, e sim para o Storage. O arquivo nunca passa pelo processo da aplicação — por isso ele
/// sobrevive ao reinício dela, que é o que faltava para a prova de entrega ser durável.
/// </para>
/// <para>
/// O que o emulador não implementa está documentado em <see cref="ContainerAzurite"/>.
/// </para>
/// </remarks>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class ComprovantesNoBlobTestes(ContainerPostgis banco, ContainerAzurite azurite)
    : TesteDeComprovante(banco), IClassFixture<ContainerAzurite>
{
    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?> ConfiguracaoAdicional => azurite.Configuracao();

    /// <summary>O fluxo inteiro, com o binário indo e voltando pelo Storage.</summary>
    [Fact]
    public async Task ComprovanteVaiEVoltaPeloBlobSemPassarPelaApi()
    {
        using var http = Cliente();
        using var direto = new HttpClient();
        var cenario = await CenarioAsync(http);
        var imagem = ImagemFalsa(4_096);

        var autorizacao = await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/comprovante/autorizacao", cenario.TokenDoMotorista, new
        {
            tipo = "Foto",
            tipoDeConteudo = "image/jpeg",
        });

        var chave = autorizacao.GetProperty("chave").GetString()!;
        var urlDeEnvio = autorizacao.GetProperty("url").GetString()!;

        // A URL aponta para o Storage, não para a API — e carrega assinatura com prazo.
        Assert.Contains(azurite.EndpointDoServico, urlDeEnvio, StringComparison.Ordinal);
        Assert.Contains("sig=", urlDeEnvio, StringComparison.Ordinal);
        Assert.Contains("se=", urlDeEnvio, StringComparison.Ordinal);

        // Os cabeçalhos exigidos vêm junto: sem x-ms-blob-type o Azure recusa o PUT.
        var cabecalhos = autorizacao.GetProperty("cabecalhosObrigatorios");
        Assert.Equal("image/jpeg", cabecalhos.GetProperty("Content-Type").GetString());
        Assert.Equal("BlockBlob", cabecalhos.GetProperty("x-ms-blob-type").GetString());

        using (var envio = await EnviarAoBlobAsync(direto, urlDeEnvio, imagem, "image/jpeg"))
        {
            Assert.Equal(HttpStatusCode.Created, envio.StatusCode);
        }

        var entrega = await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/comprovante", cenario.TokenDoMotorista, new
        {
            recebidoPor = "Carla Nunes",
            latitude = -22.91,
            longitude = -47.06,
            arquivos = new[] { new { tipo = "Foto", chave } },
        });

        Assert.Equal("Entregue", entrega.GetProperty("status").GetString());

        var comprovante = await OkAsync(http, HttpMethod.Get, $"/api/entregas/{cenario.Entrega}/comprovante", cenario.Operador);
        var arquivo = Assert.Single(comprovante.GetProperty("arquivos").EnumerateArray());

        // O que o banco guardou veio do storage, não do cliente.
        Assert.Equal(imagem.Length, arquivo.GetProperty("tamanhoEmBytes").GetInt64());
        Assert.Equal("image/jpeg", arquivo.GetProperty("tipoDeConteudo").GetString());

        // A leitura é assinada e devolve exatamente o que subiu.
        var urlDeLeitura = arquivo.GetProperty("url").GetString()!;
        using var leitura = await direto.GetAsync(new Uri(urlDeLeitura), Cancelamento);
        Assert.Equal(HttpStatusCode.OK, leitura.StatusCode);
        Assert.Equal(imagem, await leitura.Content.ReadAsByteArrayAsync(Cancelamento));

        // O binário não entrou no banco; lá está só o metadado.
        Assert.Equal("1", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM arquivos_do_comprovante WHERE chave = @chave", ("chave", chave)));
    }

    /// <summary>O resumo gravado é o do conteúdo real, e fica no objeto para não ser recalculado.</summary>
    [Fact]
    public async Task ResumoDoConteudoEhCalculadoDoObjetoEGuardadoComoMetadado()
    {
        using var http = Cliente();
        using var direto = new HttpClient();
        var cenario = await CenarioAsync(http);
        var imagem = ImagemFalsa(3_000);
        var esperado = Convert.ToHexString(SHA256.HashData(imagem)).ToLowerInvariant();

        var chave = await EnviarComprovanteAsync(http, direto, cenario, imagem);

        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/comprovante", cenario.TokenDoMotorista, new
        {
            recebidoPor = "Carla Nunes",
            arquivos = new[] { new { tipo = "Foto", chave } },
        });

        Assert.Equal(esperado, await Banco.ConsultarEscalarAsync(
            "SELECT hash_sha256 FROM arquivos_do_comprovante WHERE chave = @chave", ("chave", chave)));

        // E ficou guardado no próprio objeto: a próxima leitura não baixa o arquivo de novo.
        var propriedades = await azurite.ClienteDoContedor().GetBlobClient(chave)
            .GetPropertiesAsync(cancellationToken: Cancelamento);
        Assert.Equal(esperado, propriedades.Value.Metadata["sha256"]);
    }

    /// <summary>Sem a assinatura, o objeto não sai — o contêiner é privado de verdade.</summary>
    [Fact]
    public async Task SemAssinaturaOObjetoNaoEhServido()
    {
        using var http = Cliente();
        using var direto = new HttpClient();
        var cenario = await CenarioAsync(http);
        var imagem = ImagemFalsa(1_500);

        var chave = await EnviarComprovanteAsync(http, direto, cenario, imagem);
        var semAssinatura = new Uri($"{azurite.EndpointDoServico}/{ContainerAzurite.Contedor}/{chave}");

        using var anonima = await direto.GetAsync(semAssinatura, Cancelamento);

        Assert.Contains(anonima.StatusCode, new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound });
    }

    /// <summary>Nenhuma URL permanente vai para o banco: lá fica a chave lógica, e mais nada.</summary>
    [Fact]
    public async Task BancoGuardaChaveLogicaENuncaUrl()
    {
        using var http = Cliente();
        using var direto = new HttpClient();
        var cenario = await CenarioAsync(http);

        var chave = await EnviarComprovanteAsync(http, direto, cenario, ImagemFalsa(900));

        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/comprovante", cenario.TokenDoMotorista, new
        {
            recebidoPor = "Carla Nunes",
            arquivos = new[] { new { tipo = "Foto", chave } },
        });

        var gravado = await Banco.ConsultarEscalarAsync(
            "SELECT chave FROM arquivos_do_comprovante WHERE chave = @chave", ("chave", chave));

        Assert.Equal(chave, gravado);
        Assert.DoesNotContain("http", gravado!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sig=", gravado, StringComparison.Ordinal);

        // E a coluna de URL não existe: não há onde guardar link permanente.
        Assert.Equal("0", await Banco.ConsultarEscalarAsync(
            "SELECT count(*) FROM information_schema.columns "
            + "WHERE table_name = 'arquivos_do_comprovante' AND column_name IN ('url', 'endereco')"));
    }

    /// <summary>Registrar comprovante de arquivo que não foi enviado é recusado.</summary>
    [Fact]
    public async Task ArquivoInexistenteNoStorageImpedeORegistro()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);

        var autorizacao = await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/comprovante/autorizacao", cenario.TokenDoMotorista, new
        {
            tipo = "Foto",
            tipoDeConteudo = "image/jpeg",
        });

        // Autorizado, mas nada foi enviado ao storage.
        using var resposta = await EnviarAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/comprovante", cenario.TokenDoMotorista, new
        {
            recebidoPor = "Carla Nunes",
            arquivos = new[] { new { tipo = "Foto", chave = autorizacao.GetProperty("chave").GetString() } },
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        Assert.Equal("arquivo_nao_enviado", await CodigoDoErroAsync(resposta));
    }

    /// <summary>A chave carrega organização e entrega: o objeto de um tenant não colide com o de outro.</summary>
    [Fact]
    public async Task ChaveIsolaOrganizacaoEEntrega()
    {
        using var http = Cliente();
        using var direto = new HttpClient();
        var primeira = await CenarioAsync(http);
        var segunda = await CenarioAsync(http);

        var chaveDaPrimeira = await EnviarComprovanteAsync(http, direto, primeira, ImagemFalsa(700));
        var chaveDaSegunda = await EnviarComprovanteAsync(http, direto, segunda, ImagemFalsa(700));

        Assert.StartsWith($"organizacoes/{primeira.Organizacao.Id:N}/", chaveDaPrimeira, StringComparison.Ordinal);
        Assert.StartsWith($"organizacoes/{segunda.Organizacao.Id:N}/", chaveDaSegunda, StringComparison.Ordinal);
        Assert.NotEqual(chaveDaPrimeira, chaveDaSegunda);

        // E o comprovante de uma organização não é visível pela outra.
        using var cruzada = await EnviarAsync(http, HttpMethod.Get, $"/api/entregas/{primeira.Entrega}/comprovante", segunda.Operador);
        Assert.Equal(HttpStatusCode.NotFound, cruzada.StatusCode);
    }

    /// <summary>Tipo fora da política continua recusado antes de chegar ao storage.</summary>
    [Fact]
    public async Task TipoDeConteudoForaDaPoliticaContinuaRecusado()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);

        using var resposta = await EnviarAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/comprovante/autorizacao", cenario.TokenDoMotorista, new
        {
            tipo = "Foto",
            tipoDeConteudo = "application/x-msdownload",
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
    }

    /// <summary>
    /// O que a assinatura de envio NÃO promete, e quem promete no lugar dela.
    /// </summary>
    /// <remarks>
    /// O Azure Blob não tem cláusula de tipo nem de tamanho na SAS: subir outra coisa com a URL emitida
    /// é aceito pelo Storage. Quem barra é o registro, que lê o tipo real gravado e o confronta com a
    /// política. Este teste trava as duas metades — a permissiva e a que segura.
    /// </remarks>
    [Fact]
    public async Task TipoDivergenteEhAceitoPeloStorageERecusadoNoRegistro()
    {
        using var http = Cliente();
        using var direto = new HttpClient();
        var cenario = await CenarioAsync(http);

        var autorizacao = await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/comprovante/autorizacao", cenario.TokenDoMotorista, new
        {
            tipo = "Foto",
            tipoDeConteudo = "image/jpeg",
        });

        var chave = autorizacao.GetProperty("chave").GetString()!;

        // O Storage aceita: a assinatura autoriza escrever nesta chave, e só.
        using (var divergente = await EnviarAoBlobAsync(
            direto, autorizacao.GetProperty("url").GetString()!, ImagemFalsa(512), "text/plain"))
        {
            Assert.Equal(HttpStatusCode.Created, divergente.StatusCode);
        }

        // O registro não aceita: o tipo que vale é o que o storage gravou.
        using var registro = await EnviarAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/comprovante", cenario.TokenDoMotorista, new
        {
            recebidoPor = "Carla Nunes",
            arquivos = new[] { new { tipo = "Foto", chave } },
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, registro.StatusCode);
        Assert.Equal("tipo_de_arquivo_nao_aceito", await CodigoDoErroAsync(registro));

        // E a entrega não foi concluída com prova inválida.
        var entrega = await OkAsync(http, HttpMethod.Get, $"/api/entregas/{cenario.Entrega}", cenario.Operador);
        Assert.NotEqual("Entregue", entrega.GetProperty("status").GetString());
    }

    private static async Task<HttpResponseMessage> EnviarAoBlobAsync(
        HttpClient http,
        string url,
        byte[] conteudo,
        string tipoDeConteudo)
    {
        using var corpo = new ByteArrayContent(conteudo);
        corpo.Headers.ContentType = new MediaTypeHeaderValue(tipoDeConteudo);

        using var requisicao = new HttpRequestMessage(HttpMethod.Put, new Uri(url)) { Content = corpo };
        requisicao.Headers.Add("x-ms-blob-type", "BlockBlob");

        return await http.SendAsync(requisicao, TestContext.Current.CancellationToken);
    }

    private static async Task<string> EnviarComprovanteAsync(
        HttpClient http,
        HttpClient direto,
        CenarioDeComprovante cenario,
        byte[] conteudo)
    {
        var autorizacao = await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/comprovante/autorizacao", cenario.TokenDoMotorista, new
        {
            tipo = "Foto",
            tipoDeConteudo = "image/jpeg",
        });

        using var envio = await EnviarAoBlobAsync(direto, autorizacao.GetProperty("url").GetString()!, conteudo, "image/jpeg");
        Assert.Equal(HttpStatusCode.Created, envio.StatusCode);

        return autorizacao.GetProperty("chave").GetString()!;
    }
}
