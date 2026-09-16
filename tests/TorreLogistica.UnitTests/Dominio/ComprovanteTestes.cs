using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Comprovantes;
using TorreLogistica.Domain.Comum;

namespace TorreLogistica.UnitTests.Dominio;

public sealed class ComprovanteTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    private const string Hash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    private readonly Guid _organizacao = Guid.CreateVersion7();
    private readonly Guid _entrega = Guid.CreateVersion7();
    private readonly Guid _rota = Guid.CreateVersion7();
    private readonly Guid _motorista = Guid.CreateVersion7();
    private readonly Guid _autor = Guid.CreateVersion7();

    [Fact]
    public void RegistraAProvaComQuemRecebeuArquivosEOnde()
    {
        var comprovante = Registrar(
            recebidoPor: "  Carla Nunes  ",
            observacao: "  Entregue na portaria.  ",
            localizacao: CoordenadaGeografica.Criar(-22.91, -47.06),
            arquivos: [Arquivo(TipoDeArquivoDoComprovante.Foto), Arquivo(TipoDeArquivoDoComprovante.Assinatura, "b.png", "image/png")]);

        Assert.Equal("Carla Nunes", comprovante.RecebidoPor);
        Assert.Equal("Entregue na portaria.", comprovante.Observacao);
        Assert.Equal(-22.91, comprovante.Localizacao!.Latitude);
        Assert.Equal(Agora, comprovante.RegistradoEm);
        Assert.Equal(_autor, comprovante.AutorUsuarioId);
        Assert.Equal(_rota, comprovante.RotaId);
        Assert.Equal(_motorista, comprovante.MotoristaId);
        Assert.True(comprovante.TemEvidencia);

        var foto = comprovante.Arquivos[0];
        Assert.Equal(TipoDeArquivoDoComprovante.Foto, foto.Tipo);
        Assert.Equal("image/jpeg", foto.TipoDeConteudo);
        Assert.Equal(Hash, foto.HashSha256);
        Assert.Equal(_entrega, comprovante.EntregaId);
        Assert.All(comprovante.Arquivos, arquivo => Assert.Equal(comprovante.Id, arquivo.ComprovanteId));
    }

    [Fact]
    public void ProvaSemArquivoEhValidaNoDominio()
    {
        // Exigir foto é política da operação, conferida na aplicação — não invariante do agregado.
        var comprovante = Registrar(arquivos: []);

        Assert.False(comprovante.TemEvidencia);
        Assert.Empty(comprovante.Arquivos);
    }

    [Fact]
    public void NomeDeQuemRecebeuEhObrigatorio()
    {
        Assert.Equal("recebedor_invalido", Assert.Throws<ExcecaoDeDominio>(() => Registrar(recebidoPor: "   ")).Codigo);
        Assert.Equal(
            "recebedor_invalido",
            Assert.Throws<ExcecaoDeDominio>(() => Registrar(recebidoPor: new string('x', PoliticaDeComprovante.TamanhoMaximoDoRecebedor + 1))).Codigo);
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    public void TipoDeArquivoForaDaListaEhRecusado(string tipoDeConteudo)
    {
        var erro = Assert.Throws<ExcecaoDeDominio>(() =>
            Registrar(arquivos: [Arquivo(TipoDeArquivoDoComprovante.Foto, "a.bin", tipoDeConteudo)]));

        Assert.Equal("tipo_de_arquivo_nao_aceito", erro.Codigo);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(PoliticaDeComprovante.TamanhoMaximoEmBytes + 1)]
    public void TamanhoForaDoLimiteEhRecusado(long tamanho)
    {
        var erro = Assert.Throws<ExcecaoDeDominio>(() =>
            Registrar(arquivos: [Arquivo(TipoDeArquivoDoComprovante.Foto, tamanho: tamanho)]));

        Assert.Equal("arquivo_grande_demais", erro.Codigo);
    }

    [Theory]
    [InlineData("nao-e-hash")]
    [InlineData("9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08")]
    public void HashPrecisaSerSha256HexadecimalMinusculo(string hash)
    {
        var erro = Assert.Throws<ExcecaoDeDominio>(() => Registrar(arquivos: [Arquivo(TipoDeArquivoDoComprovante.Foto, hash: hash)]));

        Assert.Equal("hash_invalido", erro.Codigo);
    }

    [Fact]
    public void ArquivoRepetidoEQuantidadeAcimaDoLimiteSaoRecusados()
    {
        var mesmo = Arquivo(TipoDeArquivoDoComprovante.Foto);
        Assert.Equal("arquivo_repetido", Assert.Throws<ExcecaoDeDominio>(() => Registrar(arquivos: [mesmo, mesmo])).Codigo);

        var muitos = Enumerable.Range(0, PoliticaDeComprovante.QuantidadeMaximaDeArquivos + 1)
            .Select(indice => Arquivo(TipoDeArquivoDoComprovante.Foto, $"a{indice}.jpg"))
            .ToArray();
        Assert.Equal("arquivos_demais", Assert.Throws<ExcecaoDeDominio>(() => Registrar(arquivos: muitos)).Codigo);
    }

    [Fact]
    public void CatalogoDefineTiposAceitosEExtensoes()
    {
        Assert.True(PoliticaDeComprovante.TipoAceito("image/jpeg"));
        Assert.False(PoliticaDeComprovante.TipoAceito("image/gif"));
        Assert.False(PoliticaDeComprovante.TipoAceito(null));
        Assert.Equal("jpg", PoliticaDeComprovante.ExtensaoDe("image/jpeg"));
        Assert.Equal("webp", PoliticaDeComprovante.ExtensaoDe("image/webp"));
        Assert.Throws<ExcecaoDeDominio>(() => PoliticaDeComprovante.ExtensaoDe("image/gif"));
    }

    private static ArquivoConfirmado Arquivo(
        TipoDeArquivoDoComprovante tipo,
        string nome = "a.jpg",
        string tipoDeConteudo = "image/jpeg",
        long tamanho = 120_000,
        string hash = Hash) =>
        new(tipo, $"organizacoes/o/entregas/e/{nome}", tipoDeConteudo, tamanho, hash, Agora);

    private Comprovante Registrar(
        string? recebidoPor = "Carla Nunes",
        string? observacao = null,
        CoordenadaGeografica? localizacao = null,
        IReadOnlyList<ArquivoConfirmado>? arquivos = null) =>
        Comprovante.Registrar(
            Guid.CreateVersion7(),
            _organizacao,
            _entrega,
            _rota,
            _motorista,
            recebidoPor,
            observacao,
            localizacao,
            arquivos ?? [Arquivo(TipoDeArquivoDoComprovante.Foto)],
            _autor,
            Guid.CreateVersion7,
            Agora);
}
