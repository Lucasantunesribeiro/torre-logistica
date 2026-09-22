using Microsoft.Extensions.Hosting;
using TorreLogistica.Infrastructure.Armazenamento;

namespace TorreLogistica.UnitTests.Infraestrutura;

/// <summary>
/// O que a validação de subida do armazenamento promete.
/// </summary>
/// <remarks>
/// Antes, nada disso era conferido: o adaptador local era um singleton resolvido sob demanda, e um
/// diretório sem permissão só aparecia como <c>500</c> na primeira entrega com comprovante. Estes testes
/// travam o momento da falha na subida, que é quando ainda há quem esteja olhando.
/// </remarks>
public sealed class ArmazenamentoNaSubidaTestes : IDisposable
{
    private readonly string _raizTemporaria =
        Path.Combine(Path.GetTempPath(), $"torre-armazenamento-{Guid.CreateVersion7():N}");

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void ProvedorLocalComDiretorioGravavelSobe(string ambiente)
    {
        var diretorio = Path.Combine(_raizTemporaria, "gravavel");

        var resultado = Validar(ambiente, new OpcoesDeArmazenamento { Diretorio = diretorio });

        Assert.True(resultado.Succeeded, string.Join(" | ", resultado.Failures ?? []));
        Assert.True(Directory.Exists(diretorio), "a validação deveria ter criado o diretório.");
    }

    /// <summary>Exatamente o caso do contêiner: caminho que o usuário do processo não consegue escrever.</summary>
    [Fact]
    public void DiretorioSemPermissaoDeEscritaReprovaNaSubida()
    {
        // Um arquivo ocupando o nome que deveria ser pasta: nenhuma plataforma deixa criar o diretório
        // por cima dele. Reproduz a falha de escrita sem depender de ACL de Windows nem de root no Linux.
        Directory.CreateDirectory(_raizTemporaria);
        var ocupado = Path.Combine(_raizTemporaria, "ocupado");
        File.WriteAllText(ocupado, "não sou uma pasta");

        var resultado = Validar("Development", new OpcoesDeArmazenamento { Diretorio = ocupado });

        Assert.False(resultado.Succeeded);
        var falha = Assert.Single(resultado.Failures!);
        Assert.Contains("não é gravável", falha, StringComparison.Ordinal);
        Assert.Contains(ocupado, falha, StringComparison.Ordinal);
    }

    [Fact]
    public void ProvedorDesconhecidoReprova()
    {
        var resultado = Validar("Development", new OpcoesDeArmazenamento { Provedor = "s3", Diretorio = _raizTemporaria });

        Assert.False(resultado.Succeeded);
        Assert.Contains("não existe", Assert.Single(resultado.Failures!), StringComparison.Ordinal);
    }

    /// <summary>Sem adaptador, pedir Blob falha na subida — nunca cai no disco local por baixo do pano.</summary>
    [Fact]
    public void ProvedorBlobReprovaEnquantoNaoTemAdaptador()
    {
        var resultado = Validar("Production", new OpcoesDeArmazenamento { Provedor = "blob" });

        Assert.False(resultado.Succeeded);
        Assert.Contains("ainda não tem adaptador", Assert.Single(resultado.Failures!), StringComparison.Ordinal);
    }

    /// <summary>Produção sem declarar nada continua sendo recusa, não disco local silencioso.</summary>
    [Fact]
    public void ProducaoComLocalNaoDeclaradoReprova()
    {
        var resultado = Validar("Production", new OpcoesDeArmazenamento { Diretorio = _raizTemporaria });

        Assert.False(resultado.Succeeded);
        var falha = Assert.Single(resultado.Failures!);
        Assert.Contains("some no reinício", falha, StringComparison.Ordinal);
        Assert.Contains(nameof(OpcoesDeArmazenamento.PermitirLocalForaDeDesenvolvimento), falha, StringComparison.Ordinal);
    }

    /// <summary>Produção pode usar disco local, mas só dizendo isso em voz alta.</summary>
    [Fact]
    public void ProducaoComLocalDeclaradoSobe()
    {
        var resultado = Validar("Production", new OpcoesDeArmazenamento
        {
            Diretorio = Path.Combine(_raizTemporaria, "declarado"),
            PermitirLocalForaDeDesenvolvimento = true,
        });

        Assert.True(resultado.Succeeded, string.Join(" | ", resultado.Failures ?? []));
    }

    /// <summary>O caminho que a validação prova é o mesmo que o adaptador usa.</summary>
    [Fact]
    public void DiretorioEfetivoCaiNoPadraoQuandoNaoConfigurado()
    {
        var padrao = new OpcoesDeArmazenamento().DiretorioEfetivo;
        var explicito = new OpcoesDeArmazenamento { Diretorio = "  " }.DiretorioEfetivo;

        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "armazenamento"), padrao);
        Assert.Equal(padrao, explicito);
        Assert.Equal("/var/tmp/x", new OpcoesDeArmazenamento { Diretorio = "/var/tmp/x" }.DiretorioEfetivo);
    }

    /// <summary>As demais regras continuam valendo: provedor válido não dá passe livre.</summary>
    [Fact]
    public void EnderecoBaseInvalidoContinuaReprovando()
    {
        var resultado = Validar("Development", new OpcoesDeArmazenamento
        {
            Diretorio = _raizTemporaria,
            EnderecoBase = "nao-e-url",
        });

        Assert.False(resultado.Succeeded);
        Assert.Contains("URL absoluta", Assert.Single(resultado.Failures!), StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_raizTemporaria))
            {
                Directory.Delete(_raizTemporaria, recursive: true);
            }
        }
        catch (IOException)
        {
            // Limpeza de temporário não pode derrubar a suíte.
        }
    }

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validar(string ambiente, OpcoesDeArmazenamento opcoes) =>
        new ValidacaoDeOpcoesDeArmazenamento(new AmbienteDeTeste(ambiente)).Validate(name: null, opcoes);

    private sealed class AmbienteDeTeste(string nome) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = nome;

        public string ApplicationName { get; set; } = "TorreLogistica.Testes";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
