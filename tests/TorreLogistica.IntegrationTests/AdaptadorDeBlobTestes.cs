using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TorreLogistica.Infrastructure.Armazenamento;
using TorreLogistica.Infrastructure.Tempo;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// O adaptador do Blob direto, sem a API no meio.
/// </summary>
/// <remarks>
/// Aqui cabem as provas que dependem de controlar o relógio de quem assina. A validade da assinatura é
/// conferida pelo Storage, com o relógio dele; pondo o relógio do adaptador no passado, a URL que ele
/// emite já nasce vencida — que é o mesmo que aconteceria com uma URL guardada e reusada horas depois,
/// só que sem esperar.
/// </remarks>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class AdaptadorDeBlobTestes(ContainerAzurite azurite) : IClassFixture<ContainerAzurite>
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LeituraComAssinaturaVencidaEhRecusada()
    {
        using var direto = new HttpClient();
        var chave = $"expiracao/{Guid.CreateVersion7():N}.jpg";
        var conteudo = RandomNumberGenerator.GetBytes(256);

        await GravarDiretoAsync(chave, conteudo);

        // Relógio no presente: a URL vale.
        using (var adaptador = Adaptador(DateTimeOffset.UtcNow))
        {
            var valida = await adaptador.AutorizarLeituraAsync(chave, Cancelamento);
            using var resposta = await direto.GetAsync(valida.Url, Cancelamento);

            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            Assert.Equal(conteudo, await resposta.Content.ReadAsByteArrayAsync(Cancelamento));
        }

        // Relógio duas horas atrás: a URL emitida agora já expirou para o Storage.
        using (var adaptador = Adaptador(DateTimeOffset.UtcNow.AddHours(-2)))
        {
            var vencida = await adaptador.AutorizarLeituraAsync(chave, Cancelamento);
            using var resposta = await direto.GetAsync(vencida.Url, Cancelamento);

            Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        }
    }

    [Fact]
    public async Task ObterDevolveNuloParaObjetoInexistente()
    {
        using var adaptador = Adaptador(DateTimeOffset.UtcNow);

        Assert.Null(await adaptador.ObterAsync($"nao-existe/{Guid.CreateVersion7():N}.jpg", Cancelamento));
    }

    [Fact]
    public async Task ObterDevolveTamanhoTipoEResumoReais()
    {
        using var adaptador = Adaptador(DateTimeOffset.UtcNow);
        var chave = $"metadados/{Guid.CreateVersion7():N}.jpg";
        var conteudo = RandomNumberGenerator.GetBytes(1_111);

        await GravarDiretoAsync(chave, conteudo);
        var objeto = await adaptador.ObterAsync(chave, Cancelamento);

        Assert.NotNull(objeto);
        Assert.Equal(chave, objeto.Chave);
        Assert.Equal(conteudo.Length, objeto.TamanhoEmBytes);
        Assert.Equal("image/jpeg", objeto.TipoDeConteudo);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(conteudo)).ToLowerInvariant(), objeto.HashSha256);
    }

    /// <summary>A URL de envio autoriza escrever, e não ler: são poderes separados.</summary>
    [Fact]
    public async Task AssinaturaDeEnvioNaoServeParaLer()
    {
        using var direto = new HttpClient();
        using var adaptador = Adaptador(DateTimeOffset.UtcNow);
        var chave = $"poderes/{Guid.CreateVersion7():N}.jpg";

        await GravarDiretoAsync(chave, RandomNumberGenerator.GetBytes(64));

        var envio = await adaptador.AutorizarEnvioAsync(chave, "image/jpeg", 5_000_000, Cancelamento);
        using var leituraIndevida = await direto.GetAsync(envio.Url, Cancelamento);

        Assert.Equal(HttpStatusCode.Forbidden, leituraIndevida.StatusCode);
    }

    /// <summary>Nenhuma URL emitida é permanente: todas carregam assinatura e prazo.</summary>
    [Fact]
    public async Task TodaUrlEmitidaTemAssinaturaEPrazo()
    {
        using var adaptador = Adaptador(DateTimeOffset.UtcNow);
        var chave = $"prazo/{Guid.CreateVersion7():N}.jpg";

        var envio = await adaptador.AutorizarEnvioAsync(chave, "image/jpeg", 5_000_000, Cancelamento);
        var leitura = await adaptador.AutorizarLeituraAsync(chave, Cancelamento);

        foreach (var url in new[] { envio, leitura })
        {
            Assert.Contains("sig=", url.Url.Query, StringComparison.Ordinal);
            Assert.Contains("se=", url.Url.Query, StringComparison.Ordinal);
            Assert.True(url.ExpiraEm > DateTimeOffset.UtcNow, "a URL já nasceu vencida.");
            Assert.True(
                url.ExpiraEm < DateTimeOffset.UtcNow.AddHours(1),
                $"a URL vale até {url.ExpiraEm:o}, o que é longo demais para um link que é a própria credencial.");
        }
    }

    private ArmazenamentoBlobDeObjetos Adaptador(DateTimeOffset agora)
    {
        var opcoes = new OpcoesDeArmazenamento
        {
            Provedor = OpcoesDeArmazenamento.ProvedorBlob,
            Blob = new OpcoesDoBlob
            {
                Conta = ContainerAzurite.Conta,
                Contedor = ContainerAzurite.Contedor,
                EndpointDoServico = azurite.EndpointDoServico,
                Autenticacao = OpcoesDoBlob.AutenticacaoPorChave,
                ChaveDaConta = ContainerAzurite.Chave,
                VersaoDoServico = ContainerAzurite.VersaoDoServico,
            },
        };

        return new ArmazenamentoBlobDeObjetos(
            Options.Create(opcoes),
            new RelogioDoSistema(new FakeTimeProvider(agora)),
            NullLogger<ArmazenamentoBlobDeObjetos>.Instance);
    }

    private async Task GravarDiretoAsync(string chave, byte[] conteudo)
    {
        using var memoria = new MemoryStream(conteudo);

        await azurite.ClienteDoContedor().GetBlobClient(chave).UploadAsync(
            memoria,
            new Azure.Storage.Blobs.Models.BlobUploadOptions
            {
                HttpHeaders = new Azure.Storage.Blobs.Models.BlobHttpHeaders { ContentType = "image/jpeg" },
            },
            Cancelamento);
    }
}
