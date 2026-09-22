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

    /// <summary>Blob declarado por inteiro sobe — inclusive em produção, que é onde ele vive.</summary>
    [Fact]
    public void ProvedorBlobComConfiguracaoCompletaSobe()
    {
        var resultado = Validar("Production", BlobValido());

        Assert.True(resultado.Succeeded, string.Join(" | ", resultado.Failures ?? []));
    }

    /// <summary>Configuração obrigatória ausente derruba a subida, nome por nome.</summary>
    [Theory]
    [InlineData(null, "comprovantes", "Conta é obrigatória")]
    [InlineData("torrelogarquivos", null, "Contedor é obrigatório")]
    public void ProvedorBlobSemConfiguracaoObrigatoriaReprova(string? conta, string? contedor, string trecho)
    {
        var opcoes = BlobValido();
        opcoes.Blob.Conta = conta;
        opcoes.Blob.Contedor = contedor;

        var resultado = Validar("Production", opcoes);

        Assert.False(resultado.Succeeded);
        Assert.Contains(trecho, Assert.Single(resultado.Failures!), StringComparison.Ordinal);
    }

    /// <summary>Nome de contêiner que o Azure recusaria é recusado aqui, na subida.</summary>
    [Theory]
    [InlineData("ab")]
    [InlineData("Comprovantes")]
    [InlineData("com--provantes")]
    [InlineData("-comprovantes")]
    [InlineData("comprovantes_")]
    public void NomeDeContedorInvalidoReprova(string contedor)
    {
        var opcoes = BlobValido();
        opcoes.Blob.Contedor = contedor;

        var resultado = Validar("Production", opcoes);

        Assert.False(resultado.Succeeded);
        Assert.Contains("não é um nome válido", Assert.Single(resultado.Failures!), StringComparison.Ordinal);
    }

    /// <summary>Chave de conta é segredo permanente com acesso total: fora de teste, não passa.</summary>
    [Fact]
    public void AutenticacaoPorChaveDeContaReprovaEmProducao()
    {
        var opcoes = BlobValido();
        opcoes.Blob.Autenticacao = OpcoesDoBlob.AutenticacaoPorChave;
        opcoes.Blob.ChaveDaConta = "ZmFsc2E=";

        var resultado = Validar("Production", opcoes);

        Assert.False(resultado.Succeeded);
        var falha = Assert.Single(resultado.Failures!);
        Assert.Contains("não é aceito no ambiente Production", falha, StringComparison.Ordinal);
        Assert.Contains(OpcoesDoBlob.AutenticacaoPorIdentidade, falha, StringComparison.Ordinal);
    }

    /// <summary>Em desenvolvimento a chave é aceita — mas só se ela existir.</summary>
    [Fact]
    public void AutenticacaoPorChaveExigeAChave()
    {
        var opcoes = BlobValido();
        opcoes.Blob.Autenticacao = OpcoesDoBlob.AutenticacaoPorChave;

        var resultado = Validar("Development", opcoes);

        Assert.False(resultado.Succeeded);
        Assert.Contains("ChaveDaConta é obrigatória", Assert.Single(resultado.Failures!), StringComparison.Ordinal);
    }

    [Fact]
    public void AutenticacaoDesconhecidaReprova()
    {
        var opcoes = BlobValido();
        opcoes.Blob.Autenticacao = "sas-eterna";

        var resultado = Validar("Production", opcoes);

        Assert.False(resultado.Succeeded);
        Assert.Contains("não existe", Assert.Single(resultado.Failures!), StringComparison.Ordinal);
    }

    [Fact]
    public void VersaoDeServicoDesconhecidaReprova()
    {
        var opcoes = BlobValido();
        opcoes.Blob.VersaoDoServico = "2030-01-01";

        var resultado = Validar("Production", opcoes);

        Assert.False(resultado.Succeeded);
        Assert.Contains("não é uma versão conhecida", Assert.Single(resultado.Failures!), StringComparison.Ordinal);
    }

    /// <summary>Sem endereço configurado, o destino é o domínio público do Azure derivado da conta.</summary>
    [Fact]
    public void EnderecoDoServicoVemDaContaQuandoNaoConfigurado()
    {
        var derivado = ArmazenamentoBlobDeObjetos.EnderecoDoServico(new OpcoesDoBlob { Conta = "torrelogarquivos" });
        var explicito = ArmazenamentoBlobDeObjetos.EnderecoDoServico(
            new OpcoesDoBlob { Conta = "torrelogarquivos", EndpointDoServico = "http://127.0.0.1:10000/devstoreaccount1" });

        Assert.Equal("https://torrelogarquivos.blob.core.windows.net/", derivado.ToString());
        Assert.Equal("http://127.0.0.1:10000/devstoreaccount1", explicito.ToString());
    }

    /// <summary>Escolher blob não exige diretório gravável: o disco local deixa de estar no caminho.</summary>
    [Fact]
    public void ProvedorBlobNaoCobraDiretorioLocal()
    {
        var opcoes = BlobValido();
        opcoes.Diretorio = Path.Combine(_raizTemporaria, "nao-deve-ser-criado");

        var resultado = Validar("Production", opcoes);

        Assert.True(resultado.Succeeded, string.Join(" | ", resultado.Failures ?? []));
        Assert.False(Directory.Exists(opcoes.Diretorio));
    }

    private static OpcoesDeArmazenamento BlobValido() => new()
    {
        Provedor = OpcoesDeArmazenamento.ProvedorBlob,
        Blob = new OpcoesDoBlob
        {
            Conta = "torrelogarquivos",
            Contedor = "comprovantes",
        },
    };

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
