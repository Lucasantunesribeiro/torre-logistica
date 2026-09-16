using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Comum;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Ocorrencias;

namespace TorreLogistica.UnitTests.Dominio;

public sealed class OcorrenciaTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _organizacao = Guid.CreateVersion7();
    private readonly Guid _entrega = Guid.CreateVersion7();
    private readonly Guid _rota = Guid.CreateVersion7();
    private readonly Guid _motorista = Guid.CreateVersion7();
    private readonly Guid _autor = Guid.CreateVersion7();

    public static TheoryData<MotivoDeTentativaFrustrada, SeveridadeDaOcorrencia> MotivosDaTentativa() => new()
    {
        { MotivoDeTentativaFrustrada.DestinatarioAusente, SeveridadeDaOcorrencia.Media },
        { MotivoDeTentativaFrustrada.EnderecoNaoLocalizado, SeveridadeDaOcorrencia.Media },
        { MotivoDeTentativaFrustrada.RecusadaPeloDestinatario, SeveridadeDaOcorrencia.Media },
        { MotivoDeTentativaFrustrada.LocalFechado, SeveridadeDaOcorrencia.Media },
        { MotivoDeTentativaFrustrada.AcessoImpedido, SeveridadeDaOcorrencia.Media },
        { MotivoDeTentativaFrustrada.ProblemaComVeiculo, SeveridadeDaOcorrencia.Alta },
        { MotivoDeTentativaFrustrada.ProblemaComMercadoria, SeveridadeDaOcorrencia.Critica },
    };

    /// <summary>Roadmap, Fase 13: cada motivo tipado registra ocorrência, com a severidade do catálogo.</summary>
    [Theory]
    [MemberData(nameof(MotivosDaTentativa))]
    public void CadaMotivoDaTentativaVirouOcorrenciaComSeveridadeDoCatalogo(MotivoDeTentativaFrustrada motivo, SeveridadeDaOcorrencia esperada)
    {
        var ocorrencia = Registrar(TipoDeOcorrencia.TentativaDeEntrega, motivo: motivo);

        Assert.Equal(esperada, ocorrencia.Severidade);
        Assert.Equal(motivo, ocorrencia.MotivoDaTentativa);
        Assert.Equal(OrigemDaOcorrencia.Motorista, ocorrencia.Origem);
        Assert.Equal(_autor, ocorrencia.AutorUsuarioId);
        Assert.Equal(Agora, ocorrencia.RegistradaEm);
        Assert.Equal(esperada == SeveridadeDaOcorrencia.Critica, ocorrencia.EhCritica);
    }

    public static TheoryData<TipoDeOcorrencia, SeveridadeDaOcorrencia> TiposEOSeuPadrao() => new()
    {
        { TipoDeOcorrencia.ProblemaComVeiculo, SeveridadeDaOcorrencia.Alta },
        { TipoDeOcorrencia.ProblemaComMercadoria, SeveridadeDaOcorrencia.Critica },
        { TipoDeOcorrencia.AcidenteOuIncidente, SeveridadeDaOcorrencia.Critica },
        { TipoDeOcorrencia.DificuldadeDeAcesso, SeveridadeDaOcorrencia.Media },
    };

    [Theory]
    [MemberData(nameof(TiposEOSeuPadrao))]
    public void TipoSemSeveridadeInformadaUsaAPadrao(TipoDeOcorrencia tipo, SeveridadeDaOcorrencia esperada)
    {
        Assert.Equal(esperada, Registrar(tipo).Severidade);
        Assert.Equal(SeveridadeDaOcorrencia.Baixa, Registrar(tipo, severidade: SeveridadeDaOcorrencia.Baixa).Severidade);
    }

    /// <summary>Texto livre é complemento: só o vocabulário "Outro" o exige.</summary>
    [Fact]
    public void ObservacaoSoEhObrigatoriaQuandoOVocabularioNaoDizOQueHouve()
    {
        Assert.Equal("observacao_obrigatoria", Assert.Throws<ExcecaoDeDominio>(() => Registrar(TipoDeOcorrencia.Outro)).Codigo);
        Assert.Equal(
            "observacao_obrigatoria",
            Assert.Throws<ExcecaoDeDominio>(() => Registrar(TipoDeOcorrencia.TentativaDeEntrega, motivo: MotivoDeTentativaFrustrada.Outro)).Codigo);

        Assert.Equal("Rua interditada por obra.", Registrar(TipoDeOcorrencia.Outro, observacao: "  Rua interditada por obra.  ").Observacao);
        Assert.Null(Registrar(TipoDeOcorrencia.ProblemaComVeiculo).Observacao);
    }

    [Fact]
    public void MotivoTipadoSoExisteNaTentativaDeEntrega()
    {
        Assert.Equal(
            "motivo_obrigatorio",
            Assert.Throws<ExcecaoDeDominio>(() => Registrar(TipoDeOcorrencia.TentativaDeEntrega)).Codigo);
        Assert.Equal(
            "motivo_nao_se_aplica",
            Assert.Throws<ExcecaoDeDominio>(() => Registrar(TipoDeOcorrencia.ProblemaComVeiculo, motivo: MotivoDeTentativaFrustrada.LocalFechado)).Codigo);
    }

    [Fact]
    public void GuardaOndeEQuandoAconteceu()
    {
        var ocorrencia = Registrar(
            TipoDeOcorrencia.AcidenteOuIncidente,
            localizacao: CoordenadaGeografica.Criar(-22.91, -47.06),
            ocorridaEm: Agora.AddMinutes(-40));

        Assert.Equal(Agora.AddMinutes(-40), ocorrencia.OcorridaEm);
        Assert.Equal(Agora, ocorrencia.RegistradaEm);
        Assert.Equal(-22.91, ocorrencia.Localizacao!.Latitude);
        Assert.Equal(_rota, ocorrencia.RotaId);
        Assert.Equal(_motorista, ocorrencia.MotoristaId);

        // Sem instante informado, vale o do registro.
        Assert.Equal(Agora, Registrar(TipoDeOcorrencia.DificuldadeDeAcesso).OcorridaEm);
    }

    [Theory]
    [InlineData(3, "ocorrencia_no_futuro")]
    [InlineData(-(7 * 24 * 60 + 1), "ocorrencia_antiga")]
    public void InstanteForaDaJanelaAceitaEhRecusado(int minutos, string codigo)
    {
        var erro = Assert.Throws<ExcecaoDeDominio>(() => Registrar(TipoDeOcorrencia.DificuldadeDeAcesso, ocorridaEm: Agora.AddMinutes(minutos)));

        Assert.Equal(codigo, erro.Codigo);
    }

    [Fact]
    public void RelogioAdiantadoDentroDaToleranciaEAceito() =>
        Assert.Equal(Agora.AddMinutes(2), Registrar(TipoDeOcorrencia.DificuldadeDeAcesso, ocorridaEm: Agora.AddMinutes(2)).OcorridaEm);

    [Fact]
    public void TipoOrigemESeveridadeDesconhecidosSaoRecusados()
    {
        Assert.Equal("tipo_de_ocorrencia_invalido", Assert.Throws<ExcecaoDeDominio>(() => Registrar((TipoDeOcorrencia)99)).Codigo);
        Assert.Equal(
            "severidade_invalida",
            Assert.Throws<ExcecaoDeDominio>(() => Registrar(TipoDeOcorrencia.ProblemaComVeiculo, severidade: (SeveridadeDaOcorrencia)99)).Codigo);
        Assert.Equal(
            "origem_invalida",
            Assert.Throws<ExcecaoDeDominio>(() => Registrar(TipoDeOcorrencia.ProblemaComVeiculo, origem: (OrigemDaOcorrencia)99)).Codigo);
        Assert.Equal(
            "observacao_invalida",
            Assert.Throws<ExcecaoDeDominio>(() =>
                Registrar(TipoDeOcorrencia.Outro, observacao: new string('x', Ocorrencia.TamanhoMaximoDaObservacao + 1))).Codigo);
    }

    private Ocorrencia Registrar(
        TipoDeOcorrencia tipo,
        SeveridadeDaOcorrencia? severidade = null,
        MotivoDeTentativaFrustrada? motivo = null,
        string? observacao = null,
        CoordenadaGeografica? localizacao = null,
        DateTimeOffset? ocorridaEm = null,
        OrigemDaOcorrencia origem = OrigemDaOcorrencia.Motorista) =>
        Ocorrencia.Registrar(
            Guid.CreateVersion7(),
            _organizacao,
            _entrega,
            _rota,
            _motorista,
            tipo,
            severidade,
            motivo,
            observacao ?? (CatalogoDeOcorrencias.ExigeObservacao(tipo, motivo) ? null : null),
            localizacao,
            ocorridaEm,
            origem,
            _autor,
            Agora);
}
