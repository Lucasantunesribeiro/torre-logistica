using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Comum;
using TorreLogistica.Domain.Entregas;

namespace TorreLogistica.UnitTests.Dominio;

public sealed class CodigoDaEntregaTestes
{
    [Theory]
    [InlineData(2026, 1, "ENT-2026-000001")]
    [InlineData(2026, 4821, "ENT-2026-004821")]
    [InlineData(2027, 1234567, "ENT-2027-1234567")]
    public void CodigoTemPrefixoAnoENumeroComSeisDigitosNoMinimo(int ano, long numero, string esperado)
    {
        var codigo = CodigoDaEntrega.Gerar(ano, numero);

        Assert.Equal(esperado, codigo);
        Assert.True(CodigoDaEntrega.EhValido(codigo));
    }

    [Theory]
    [InlineData(1999, 1)]
    [InlineData(10000, 1)]
    [InlineData(2026, 0)]
    public void AnoOuNumeroForaDaFaixaEhErroDeProgramacao(int ano, long numero)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CodigoDaEntrega.Gerar(ano, numero));
    }

    [Theory]
    [InlineData("ent-2026-000001")]
    [InlineData("ENT-26-000001")]
    [InlineData("ENT-2026-1")]
    [InlineData(" ENT-2026-000001")]
    [InlineData("ENT-2026-000001\n")]
    [InlineData(null)]
    public void FormatoDiferenteNaoEhCodigo(string? codigo)
    {
        Assert.False(CodigoDaEntrega.EhValido(codigo));
    }
}

public sealed class JanelaDeEntregaTestes
{
    private static readonly DateTimeOffset Base = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void JanelaGuardaUtcTruncadoAoSegundo()
    {
        var inicio = new DateTimeOffset(2026, 9, 14, 9, 0, 0, TimeSpan.FromHours(-3)).AddTicks(1_234_567);

        var janela = JanelaDeEntrega.Criar(inicio, inicio.AddHours(2));

        Assert.Equal(new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero), janela.Inicio);
        Assert.Equal(TimeSpan.Zero, janela.Inicio.Offset);

        // Reenviar a mesma janela com fração de segundo diferente não conta como mudança.
        Assert.Equal(janela, JanelaDeEntrega.Criar(inicio.AddTicks(1), inicio.AddHours(2).AddTicks(1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public void FimQueNaoPassaDoInicioEhRecusado(int minutos)
    {
        var erro = Assert.Throws<ExcecaoDeDominio>(() => JanelaDeEntrega.Criar(Base, Base.AddMinutes(minutos)));
        Assert.Equal("janela_invalida", erro.Codigo);
    }

    [Fact]
    public void JanelaTemNoMaximoSeteDias()
    {
        Assert.NotNull(JanelaDeEntrega.Criar(Base, Base.AddDays(7)));

        var erro = Assert.Throws<ExcecaoDeDominio>(() => JanelaDeEntrega.Criar(Base, Base.AddDays(7).AddSeconds(1)));
        Assert.Equal("janela_invalida", erro.Codigo);
    }
}

public sealed class EntregaTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Organizacao = Guid.CreateVersion7();
    private static readonly Guid Autor = Guid.CreateVersion7();
    private static readonly Guid ClienteFixo = Guid.CreateVersion7();
    private static readonly Guid DestinatarioFixo = Guid.CreateVersion7();

    [Fact]
    public void CriarGeraEntregaCriadaEOPrimeiroEventoDaTimeline()
    {
        var id = Guid.CreateVersion7();

        var (entrega, evento) = Entrega.Criar(id, Organizacao, "ENT-2026-000001", Dados(), Autor, Guid.CreateVersion7(), Agora);

        Assert.Equal(StatusDaEntrega.Criada, entrega.Status);
        Assert.Equal(1, entrega.UltimaSequenciaDeEvento);
        Assert.Equal(Agora, entrega.CriadaEm);
        Assert.Null(entrega.CanceladaEm);

        Assert.Equal(id, evento.EntregaId);
        Assert.Equal(Organizacao, evento.OrganizacaoId);
        Assert.Equal(1, evento.Sequencia);
        Assert.Equal(TipoDeEventoDaEntrega.Criada, evento.Tipo);
        Assert.Equal(StatusDaEntrega.Criada, evento.StatusResultante);
        Assert.Equal(Autor, evento.AutorUsuarioId);
        Assert.Equal(Agora, evento.OcorridoEm);
        Assert.Equal("{}", evento.Dados);
    }

    [Theory]
    [InlineData(-3, -1)]
    [InlineData(-2, 0)]
    public void JanelaQueJaTerminouEhRecusadaNaCriacao(int inicioEmHoras, int fimEmHoras)
    {
        var janela = JanelaDeEntrega.Criar(Agora.AddHours(inicioEmHoras), Agora.AddHours(fimEmHoras));

        var erro = Assert.Throws<ExcecaoDeDominio>(() =>
            Entrega.Criar(Guid.CreateVersion7(), Organizacao, "ENT-2026-000001", Dados(janela: janela), Autor, Guid.CreateVersion7(), Agora));

        Assert.Equal("janela_no_passado", erro.Codigo);
    }

    [Fact]
    public void JanelaQueJaComecouMasNaoTerminouEhAceita()
    {
        var janela = JanelaDeEntrega.Criar(Agora.AddHours(-1), Agora.AddHours(1));

        var (entrega, _) = Entrega.Criar(Guid.CreateVersion7(), Organizacao, "ENT-2026-000001", Dados(janela: janela), Autor, Guid.CreateVersion7(), Agora);

        Assert.Equal(janela, entrega.Janela);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ROT-2026-000001")]
    [InlineData("ENT-2026-00001")]
    public void CodigoForaDoFormatoEhRecusado(string codigo)
    {
        var erro = Assert.Throws<ExcecaoDeDominio>(() =>
            Entrega.Criar(Guid.CreateVersion7(), Organizacao, codigo, Dados(), Autor, Guid.CreateVersion7(), Agora));

        Assert.Equal("codigo_invalido", erro.Codigo);
    }

    [Fact]
    public void AlteracaoRegistraSoOsCamposQueMudaramESemValores()
    {
        var entrega = NovaEntrega();
        var depois = Agora.AddMinutes(5);

        var alteracao = entrega.AtualizarDados(
            Dados(cep: "13015904", observacoes: "Ligar antes"), Autor, Guid.CreateVersion7(), depois);

        Assert.Equal(new[] { CamposDaEntrega.Endereco, CamposDaEntrega.Observacoes }, alteracao.Campos);
        Assert.NotNull(alteracao.Evento);
        Assert.Equal(2, alteracao.Evento.Sequencia);
        Assert.Equal(TipoDeEventoDaEntrega.DadosAlterados, alteracao.Evento.Tipo);
        Assert.Equal(StatusDaEntrega.Criada, alteracao.Evento.StatusResultante);
        Assert.Contains("endereco", alteracao.Evento.Dados, StringComparison.Ordinal);
        Assert.DoesNotContain("13015904", alteracao.Evento.Dados, StringComparison.Ordinal);
        Assert.DoesNotContain("Ligar", alteracao.Evento.Dados, StringComparison.Ordinal);
        Assert.Equal(depois, entrega.AtualizadaEm);
        Assert.Equal("13015904", entrega.Endereco.Cep);
    }

    [Fact]
    public void AlteracaoSemMudancaNaoGeraEvento()
    {
        var entrega = NovaEntrega();

        var alteracao = entrega.AtualizarDados(Dados(), Autor, Guid.CreateVersion7(), Agora.AddMinutes(5));

        Assert.Empty(alteracao.Campos);
        Assert.Null(alteracao.Evento);
        Assert.Equal(1, entrega.UltimaSequenciaDeEvento);
        Assert.Equal(Agora, entrega.AtualizadaEm);
    }

    [Fact]
    public void AlterarJanelaParaOPassadoEhRecusadoSemAplicarNada()
    {
        var entrega = NovaEntrega();
        var janelaOriginal = entrega.Janela;
        var depois = Agora.AddHours(5);

        var erro = Assert.Throws<ExcecaoDeDominio>(() => entrega.AtualizarDados(
            Dados(janela: JanelaDeEntrega.Criar(Agora.AddHours(1), Agora.AddHours(4)), observacoes: "nova"),
            Autor,
            Guid.CreateVersion7(),
            depois));

        Assert.Equal("janela_no_passado", erro.Codigo);
        Assert.Equal(janelaOriginal, entrega.Janela);
        Assert.Null(entrega.Observacoes);
        Assert.Equal(1, entrega.UltimaSequenciaDeEvento);
    }

    [Fact]
    public void CancelamentoEncerraAEntregaERegistraOMotivo()
    {
        var entrega = NovaEntrega();
        var depois = Agora.AddMinutes(10);

        var evento = entrega.Cancelar(MotivoDeCancelamento.SolicitacaoDoCliente, null, Autor, Guid.CreateVersion7(), depois);

        Assert.NotNull(evento);
        Assert.Equal(StatusDaEntrega.Cancelada, entrega.Status);
        Assert.Equal(MotivoDeCancelamento.SolicitacaoDoCliente, entrega.MotivoDoCancelamento);
        Assert.Equal(depois, entrega.CanceladaEm);
        Assert.Equal(2, evento.Sequencia);
        Assert.Equal(TipoDeEventoDaEntrega.Cancelada, evento.Tipo);
        Assert.Equal(StatusDaEntrega.Cancelada, evento.StatusResultante);
        Assert.Contains("SolicitacaoDoCliente", evento.Dados, StringComparison.Ordinal);
        Assert.Contains("Criada", evento.Dados, StringComparison.Ordinal);
    }

    [Fact]
    public void CancelarDeNovoNaoTemEfeitoNemGeraEvento()
    {
        var entrega = NovaEntrega();
        entrega.Cancelar(MotivoDeCancelamento.CadastroDuplicado, null, Autor, Guid.CreateVersion7(), Agora.AddMinutes(1));
        var canceladaEm = entrega.CanceladaEm;

        var repeticao = entrega.Cancelar(MotivoDeCancelamento.SolicitacaoDoCliente, null, Autor, Guid.CreateVersion7(), Agora.AddMinutes(9));

        Assert.Null(repeticao);
        Assert.Equal(2, entrega.UltimaSequenciaDeEvento);
        Assert.Equal(canceladaEm, entrega.CanceladaEm);
        Assert.Equal(MotivoDeCancelamento.CadastroDuplicado, entrega.MotivoDoCancelamento);
    }

    [Fact]
    public void MotivoOutroExigeDescricao()
    {
        var entrega = NovaEntrega();

        var erro = Assert.Throws<ExcecaoDeDominio>(() =>
            entrega.Cancelar(MotivoDeCancelamento.Outro, "   ", Autor, Guid.CreateVersion7(), Agora));
        Assert.Equal("motivo_exige_descricao", erro.Codigo);
        Assert.Equal(StatusDaEntrega.Criada, entrega.Status);

        entrega.Cancelar(MotivoDeCancelamento.Outro, "Loja fechou", Autor, Guid.CreateVersion7(), Agora);
        Assert.Equal("Loja fechou", entrega.DescricaoDoCancelamento);
    }

    [Fact]
    public void MotivoForaDaListaEhRecusado()
    {
        var entrega = NovaEntrega();

        var erro = Assert.Throws<ExcecaoDeDominio>(() =>
            entrega.Cancelar((MotivoDeCancelamento)99, null, Autor, Guid.CreateVersion7(), Agora));

        Assert.Equal("motivo_invalido", erro.Codigo);
    }

    [Fact]
    public void EntregaCanceladaNaoAceitaAlteracao()
    {
        var entrega = NovaEntrega();
        entrega.Cancelar(MotivoDeCancelamento.EnderecoIncorreto, null, Autor, Guid.CreateVersion7(), Agora);

        var erro = Assert.Throws<ExcecaoDeDominio>(() =>
            entrega.AtualizarDados(Dados(observacoes: "tarde demais"), Autor, Guid.CreateVersion7(), Agora.AddMinutes(1)));

        Assert.Equal("entrega_nao_editavel", erro.Codigo);
        Assert.Equal(CategoriaDeErroDeDominio.Conflito, erro.Categoria);
        Assert.Null(entrega.Observacoes);
    }

    private static Entrega NovaEntrega() =>
        Entrega.Criar(Guid.CreateVersion7(), Organizacao, "ENT-2026-000001", Dados(), Autor, Guid.CreateVersion7(), Agora).Entrega;

    private static DadosDaEntrega Dados(string cep = "13010000", JanelaDeEntrega? janela = null, string? observacoes = null) =>
        new(
            ClienteFixo,
            DestinatarioFixo,
            Endereco.Criar("Rua das Palmeiras", "120", null, "Centro", "Campinas", "SP", cep),
            null,
            janela ?? JanelaDeEntrega.Criar(Agora.AddHours(1), Agora.AddHours(3)),
            observacoes);
}

/// <summary>
/// As tabelas de regra como especificação: uma linha por campo, uma coluna por status.
/// Mudar a regra exige mudar esta tabela — de propósito.
/// </summary>
public sealed class RegrasDaEntregaTestes
{
    // Ordem das colunas: a ordem de declaração de StatusDaEntrega.
    //                                            Cr     Pl     At     ER     PD     En     TF     Re     Ca
    private static readonly bool[] Cancelavel = [true, true, true, false, false, false, true, true, false];

    private static readonly Dictionary<string, bool[]> Editavel = new(StringComparer.Ordinal)
    {
        [CamposDaEntrega.Cliente] = [true, false, false, false, false, false, false, false, false],
        [CamposDaEntrega.Destinatario] = [true, true, true, false, false, false, false, true, false],
        [CamposDaEntrega.Endereco] = [true, true, true, false, false, false, false, true, false],
        [CamposDaEntrega.Localizacao] = [true, true, true, false, false, false, false, true, false],
        [CamposDaEntrega.JanelaPrometida] = [true, true, true, false, false, false, false, true, false],
        [CamposDaEntrega.Observacoes] = [true, true, true, true, true, false, true, true, false],
    };

    public static TheoryData<StatusDaEntrega, bool> Cancelamentos()
    {
        var dados = new TheoryData<StatusDaEntrega, bool>();
        foreach (var (status, indice) in Enum.GetValues<StatusDaEntrega>().Select((status, indice) => (status, indice)))
        {
            dados.Add(status, Cancelavel[indice]);
        }

        return dados;
    }

    public static TheoryData<string, StatusDaEntrega, bool> Edicoes()
    {
        var dados = new TheoryData<string, StatusDaEntrega, bool>();
        foreach (var (campo, colunas) in Editavel)
        {
            foreach (var (status, indice) in Enum.GetValues<StatusDaEntrega>().Select((status, indice) => (status, indice)))
            {
                dados.Add(campo, status, colunas[indice]);
            }
        }

        return dados;
    }

    [Fact]
    public void TabelasCobremTodoStatusETodoCampo()
    {
        var quantidadeDeStatus = Enum.GetValues<StatusDaEntrega>().Length;

        Assert.Equal(quantidadeDeStatus, Cancelavel.Length);
        Assert.All(Editavel.Values, colunas => Assert.Equal(quantidadeDeStatus, colunas.Length));
        Assert.Equal(CamposDaEntrega.Todos.Order(StringComparer.Ordinal), Editavel.Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Cancelamentos))]
    public void CancelamentoPorStatus(StatusDaEntrega status, bool permitido)
    {
        Assert.Equal(permitido, RegrasDaEntrega.PermiteCancelamento(status));
    }

    [Theory]
    [MemberData(nameof(Edicoes))]
    public void EdicaoPorCampoEStatus(string campo, StatusDaEntrega status, bool permitido)
    {
        Assert.Equal(permitido, RegrasDaEntrega.CampoEditavel(campo, status));
    }

    [Fact]
    public void CampoDesconhecidoEhErroDeProgramacao()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RegrasDaEntrega.CampoEditavel("status", StatusDaEntrega.Criada));
    }
}
