using TorreLogistica.Application.Previsao;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Previsao;

namespace TorreLogistica.UnitTests.Dominio;

public sealed class RegrasDeSlaTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly LimiaresDeSla Limiares = LimiaresDeSla.Criar(TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(5));
    private static readonly JanelaDeEntrega Janela = JanelaDeEntrega.Criar(Agora.AddHours(-1), Agora.AddHours(2));

    /// <summary>Cada limiar dos dois lados da borda: "abaixo" é estrito.</summary>
    [Theory]
    [InlineData(3_600, SituacaoDoSla.Normal, MotivoDaSituacao.FolgaSuficiente)]
    [InlineData(1_200, SituacaoDoSla.Normal, MotivoDaSituacao.FolgaSuficiente)]
    [InlineData(1_199, SituacaoDoSla.Atencao, MotivoDaSituacao.FolgaAbaixoDoLimiarDeAtencao)]
    [InlineData(300, SituacaoDoSla.Atencao, MotivoDaSituacao.FolgaAbaixoDoLimiarDeAtencao)]
    [InlineData(299, SituacaoDoSla.Risco, MotivoDaSituacao.FolgaAbaixoDoLimiarDeRisco)]
    [InlineData(0, SituacaoDoSla.Risco, MotivoDaSituacao.FolgaAbaixoDoLimiarDeRisco)]
    [InlineData(-1, SituacaoDoSla.Risco, MotivoDaSituacao.ChegadaPrevistaDepoisDaJanela)]
    public void ClassificaPelaFolgaAteOFimDaJanela(int folgaEmSegundos, SituacaoDoSla situacao, MotivoDaSituacao motivo)
    {
        var chegada = Janela.Fim.AddSeconds(-folgaEmSegundos);

        var classificacao = RegrasDeSla.Classificar(Janela, chegada, Agora, Limiares);

        Assert.Equal(situacao, classificacao.Situacao);
        Assert.Equal(motivo, classificacao.Motivo);
        Assert.Equal(TimeSpan.FromSeconds(folgaEmSegundos), classificacao.Folga);
    }

    [Fact]
    public void JanelaEncerradaEhAtrasadaMesmoComChegadaPrevistaBoa()
    {
        var noFim = RegrasDeSla.Classificar(Janela, Janela.Fim, Janela.Fim, Limiares);
        var depois = RegrasDeSla.Classificar(Janela, Janela.Fim.AddHours(-1), Janela.Fim.AddSeconds(1), Limiares);

        Assert.NotEqual(SituacaoDoSla.Atrasada, noFim.Situacao);
        Assert.Equal(SituacaoDoSla.Atrasada, depois.Situacao);
        Assert.Equal(MotivoDaSituacao.JanelaEncerrada, depois.Motivo);
        Assert.Equal(TimeSpan.FromSeconds(-1), depois.Folga);
    }

    [Fact]
    public void SemChegadaPrevistaSoEntraEmAtencaoPertoDoFimDaJanela()
    {
        var longe = RegrasDeSla.Classificar(Janela, null, Agora, Limiares);
        var perto = RegrasDeSla.Classificar(Janela, null, Janela.Fim.AddMinutes(-10), Limiares);

        Assert.Equal((SituacaoDoSla.Normal, MotivoDaSituacao.SemPrevisao), (longe.Situacao, longe.Motivo));
        Assert.Equal((SituacaoDoSla.Atencao, MotivoDaSituacao.SemPrevisaoComJanelaProximaDoFim), (perto.Situacao, perto.Motivo));
        Assert.Null(perto.Folga);
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(5, 10)]
    [InlineData(20, -1)]
    public void LimiaresIncoerentesSaoRecusados(int atencaoEmMinutos, int riscoEmMinutos)
    {
        var erro = Assert.Throws<ExcecaoDeDominio>(() =>
            LimiaresDeSla.Criar(TimeSpan.FromMinutes(atencaoEmMinutos), TimeSpan.FromMinutes(riscoEmMinutos)));

        Assert.Equal("limiares_invalidos", erro.Codigo);
    }
}

public sealed class CalculadoraDeChegadaTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 15, 12, 0, 0, 250, TimeSpan.Zero);
    private static readonly TimeSpan TempoPorParada = TimeSpan.FromMinutes(5);

    [Fact]
    public void SomaDeslocamentoAcumuladoETempoDasParadasAnteriores()
    {
        var (a, b) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        var chegadas = CalculadoraDeChegada.Calcular(
            Agora,
            temPosicao: true,
            [EmRota(a), EmRota(b)],
            [Trecho(10, 4_000), Trecho(15, 7_000)],
            TempoPorParada);

        Assert.Equal(Truncado(Agora.AddMinutes(10)), chegadas[0].ChegadaPrevista);
        Assert.Equal(0, chegadas[0].ParadasAntes);

        Assert.Equal(Truncado(Agora.AddMinutes(10 + 15 + 5)), chegadas[1].ChegadaPrevista);
        Assert.Equal(1, chegadas[1].ParadasAntes);
        Assert.Equal(TimeSpan.FromMinutes(5), chegadas[1].TempoDasParadasAntes);
        Assert.Equal(TimeSpan.FromMinutes(25), chegadas[1].Deslocamento);
        Assert.Equal(11_000, chegadas[1].DistanciaEmMetros);
    }

    [Theory]
    [InlineData(2, 3)]
    [InlineData(8, 0)]
    public void ParadaJaNoDestinoAtrasaAsSeguintesSoPeloAtendimentoQueFalta(int minutosDesdeAChegada, int minutosQueFaltam)
    {
        var (a, b) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var chegada = Agora.AddMinutes(-minutosDesdeAChegada);

        var chegadas = CalculadoraDeChegada.Calcular(
            Agora,
            temPosicao: true,
            [new ParadaParaPrevisao(a, StatusDaEntrega.ProximaDoDestino, true, chegada), EmRota(b)],
            [Trecho(10, 3_000)],
            TempoPorParada);

        Assert.True(chegadas[0].JaNoDestino);
        Assert.Equal(Truncado(chegada), chegadas[0].ChegadaPrevista);
        Assert.Equal(Truncado(Agora.AddMinutes(10 + minutosQueFaltam)), chegadas[1].ChegadaPrevista);
        Assert.Equal(TimeSpan.FromMinutes(minutosQueFaltam), chegadas[1].TempoDasParadasAntes);
    }

    [Fact]
    public void SemPosicaoNaoHaChegadaPrevistaParaQuemAindaPrecisaSeDeslocar()
    {
        var (a, b) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        var chegadas = CalculadoraDeChegada.Calcular(
            Agora,
            temPosicao: false,
            [new ParadaParaPrevisao(a, StatusDaEntrega.ProximaDoDestino, true, Agora), EmRota(b)],
            [],
            TempoPorParada);

        Assert.NotNull(chegadas[0].ChegadaPrevista);
        Assert.Null(chegadas[1].ChegadaPrevista);
        Assert.Equal(MotivoSemChegadaPrevista.SemPosicaoDoMotorista, chegadas[1].MotivoSemChegada);
    }

    [Fact]
    public void ParadaSemCoordenadaFicaSemPrevisaoMasOAtendimentoDelaContaParaAsSeguintes()
    {
        var (a, semCoordenada, b) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        var chegadas = CalculadoraDeChegada.Calcular(
            Agora,
            temPosicao: true,
            [EmRota(a), new ParadaParaPrevisao(semCoordenada, StatusDaEntrega.EmRota, false, null), EmRota(b)],
            [Trecho(10, 3_000), Trecho(20, 6_000)],
            TempoPorParada);

        Assert.Equal(MotivoSemChegadaPrevista.DestinoSemCoordenada, chegadas[1].MotivoSemChegada);
        Assert.Equal(Truncado(Agora.AddMinutes(10 + 20 + 5 + 5)), chegadas[2].ChegadaPrevista);
        Assert.Equal(2, chegadas[2].ParadasAntes);
    }

    [Fact]
    public void TrajetoIncompletoOuParadaForaDaExecucaoSaoErroDeUso()
    {
        Assert.Throws<ArgumentException>(() =>
            CalculadoraDeChegada.Calcular(Agora, true, [EmRota(Guid.CreateVersion7())], [], TempoPorParada));
        Assert.Throws<ArgumentException>(() =>
            CalculadoraDeChegada.Calcular(Agora, false, [new ParadaParaPrevisao(Guid.CreateVersion7(), StatusDaEntrega.Entregue, true, null)], [], TempoPorParada));
        Assert.Throws<ExcecaoDeDominio>(() => TrechoDeTrajeto.Criar(TimeSpan.FromSeconds(-1), 10));
    }

    internal static ParadaParaPrevisao EmRota(Guid entregaId) => new(entregaId, StatusDaEntrega.EmRota, true, null);

    internal static TrechoDeTrajeto Trecho(int minutos, double metros) => TrechoDeTrajeto.Criar(TimeSpan.FromMinutes(minutos), metros);

    internal static DateTimeOffset Truncado(DateTimeOffset instante) =>
        new(instante.UtcTicks - (instante.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
}

public sealed class PrevisaoDaEntregaTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly LimiaresDeSla Limiares = LimiaresDeSla.Criar(TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(5));
    private static readonly TimeSpan MudancaRelevante = TimeSpan.FromMinutes(2);
    private readonly Guid _entrega = Guid.CreateVersion7();
    private readonly Guid _rota = Guid.CreateVersion7();

    [Fact]
    public void PrimeiroCalculoCriaAPrevisaoEORegistroInicial()
    {
        var (previsao, registro) = PrevisaoDaEntrega.Iniciar(Guid.CreateVersion7(), Calculo(Agora, 10), Guid.CreateVersion7());

        Assert.True(previsao.Ativa);
        Assert.Equal(SituacaoDoSla.Normal, previsao.Situacao);
        Assert.Equal(TipoDeRegistroDePrevisao.Inicial, registro.Tipo);
        Assert.Equal(1, registro.Sequencia);
        Assert.Null(registro.SituacaoAnterior);
        Assert.Equal(600, registro.DeslocamentoEmSegundos);
        Assert.Equal(1_200, registro.LimiarDeAtencaoEmSegundos);
    }

    [Fact]
    public void ChegadaQueAndaPoucoAtualizaOPresenteSemEscreverHistorico()
    {
        var (previsao, _) = PrevisaoDaEntrega.Iniciar(Guid.CreateVersion7(), Calculo(Agora, 10), Guid.CreateVersion7());

        var registro = previsao.Atualizar(Calculo(Agora.AddSeconds(30), 11), MudancaRelevante, Guid.CreateVersion7());

        Assert.Null(registro);
        Assert.Equal(Agora.AddSeconds(30).AddMinutes(11), previsao.ChegadaPrevistaEm);
        Assert.Equal(Agora.AddSeconds(30), previsao.CalculadaEm);
        Assert.Equal(1, previsao.UltimaSequenciaDeRegistro);
    }

    [Fact]
    public void ChegadaQueAndaAlemDaMudancaRelevanteEntraNoHistoricoComOValorAnterior()
    {
        var (previsao, inicial) = PrevisaoDaEntrega.Iniciar(Guid.CreateVersion7(), Calculo(Agora, 10), Guid.CreateVersion7());

        var registro = previsao.Atualizar(Calculo(Agora, 12), MudancaRelevante, Guid.CreateVersion7());

        Assert.NotNull(registro);
        Assert.Equal(TipoDeRegistroDePrevisao.ChegadaPrevistaAlterada, registro.Tipo);
        Assert.Equal(2, registro.Sequencia);
        Assert.Equal(Agora.AddMinutes(10), registro.ChegadaPrevistaAnteriorEm);
        Assert.Equal(Agora.AddMinutes(12), registro.ChegadaPrevistaEm);

        // O registro anterior é fotografia: não acompanha o presente.
        Assert.Equal(Agora.AddMinutes(10), inicial.ChegadaPrevistaEm);
        Assert.Equal(600, inicial.DeslocamentoEmSegundos);
    }

    [Fact]
    public void MudancaDeSituacaoSempreEntraNoHistoricoComASituacaoAnterior()
    {
        var (previsao, _) = PrevisaoDaEntrega.Iniciar(Guid.CreateVersion7(), Calculo(Agora, 10), Guid.CreateVersion7());

        // Mesma chegada prevista, janela mais curta: a folga cai para 4 min.
        var registro = previsao.Atualizar(Calculo(Agora, 10, janelaFim: Agora.AddMinutes(14)), MudancaRelevante, Guid.CreateVersion7());

        Assert.NotNull(registro);
        Assert.Equal(TipoDeRegistroDePrevisao.SituacaoAlterada, registro.Tipo);
        Assert.Equal(SituacaoDoSla.Normal, registro.SituacaoAnterior);
        Assert.Equal(SituacaoDoSla.Risco, registro.Situacao);
        Assert.Equal(MotivoDaSituacao.FolgaAbaixoDoLimiarDeRisco, registro.MotivoDaSituacao);
        Assert.Equal(240, registro.FolgaEmSegundos);
    }

    [Fact]
    public void CalculoMaisAntigoQueOVigenteNaoReescreveOPresente()
    {
        var (previsao, _) = PrevisaoDaEntrega.Iniciar(Guid.CreateVersion7(), Calculo(Agora, 10), Guid.CreateVersion7());

        var registro = previsao.Atualizar(Calculo(Agora.AddMinutes(-1), 40), MudancaRelevante, Guid.CreateVersion7());

        Assert.Null(registro);
        Assert.Equal(Agora.AddMinutes(10), previsao.ChegadaPrevistaEm);
    }

    [Fact]
    public void EncerrarRegistraUmaVezEUmaNovaExecucaoRecomecaComRegistroInicial()
    {
        var (previsao, _) = PrevisaoDaEntrega.Iniciar(Guid.CreateVersion7(), Calculo(Agora, 10), Guid.CreateVersion7());

        var encerramento = previsao.Encerrar(StatusDaEntrega.TentativaFrustrada, Guid.CreateVersion7(), Agora.AddMinutes(5));
        var repetido = previsao.Encerrar(StatusDaEntrega.TentativaFrustrada, Guid.CreateVersion7(), Agora.AddMinutes(6));

        Assert.NotNull(encerramento);
        Assert.Equal(TipoDeRegistroDePrevisao.Encerrada, encerramento.Tipo);
        Assert.Equal(StatusDaEntrega.TentativaFrustrada, encerramento.StatusDaEntrega);
        Assert.False(previsao.Ativa);
        Assert.Null(repetido);

        var reinicio = previsao.Atualizar(Calculo(Agora.AddDays(1), 10), MudancaRelevante, Guid.CreateVersion7());

        Assert.NotNull(reinicio);
        Assert.Equal(TipoDeRegistroDePrevisao.Inicial, reinicio.Tipo);
        Assert.Equal(3, reinicio.Sequencia);
        Assert.True(previsao.Ativa);
    }

    [Fact]
    public void ContingenciaExigeMotivoEFonteDoProvedorNaoTem()
    {
        var composicao = Composicao(Agora, 10);
        var classificacao = RegrasDeSla.Classificar(Janela(Agora.AddHours(2)), composicao.ChegadaPrevista, Agora, Limiares);

        Assert.Throws<ExcecaoDeDominio>(() => new CalculoDaPrevisao(
            _rota, composicao, classificacao, Janela(Agora.AddHours(2)), Limiares, TimeSpan.FromMinutes(5), FonteDaPrevisao.Contingencia, null, null, Agora, Agora));
        Assert.Throws<ExcecaoDeDominio>(() => new CalculoDaPrevisao(
            _rota, composicao, classificacao, Janela(Agora.AddHours(2)), Limiares, TimeSpan.FromMinutes(5), FonteDaPrevisao.Provedor, "simulado", MotivoDaContingencia.TempoLimite, Agora, Agora));
    }

    private CalculoDaPrevisao Calculo(DateTimeOffset agora, int minutosDeDeslocamento, DateTimeOffset? janelaFim = null)
    {
        var composicao = Composicao(agora, minutosDeDeslocamento);
        var janela = Janela(janelaFim ?? agora.AddHours(2));

        return new CalculoDaPrevisao(
            _rota,
            composicao,
            RegrasDeSla.Classificar(janela, composicao.ChegadaPrevista, agora, Limiares),
            janela,
            Limiares,
            TimeSpan.FromMinutes(5),
            FonteDaPrevisao.Provedor,
            "simulado",
            null,
            agora.AddSeconds(-20),
            agora);
    }

    private ComposicaoDaChegada Composicao(DateTimeOffset agora, int minutosDeDeslocamento) =>
        CalculadoraDeChegada.Calcular(
            agora,
            temPosicao: true,
            [CalculadoraDeChegadaTestes.EmRota(_entrega)],
            [CalculadoraDeChegadaTestes.Trecho(minutosDeDeslocamento, 5_000)],
            TimeSpan.FromMinutes(5))[0];

    private static JanelaDeEntrega Janela(DateTimeOffset fim) => JanelaDeEntrega.Criar(fim.AddHours(-3), fim);
}

public sealed class ExplicacaoDaPrevisaoTestes
{
    private static readonly DateTimeOffset Calculo = new(2026, 9, 15, 13, 40, 0, TimeSpan.Zero);

    /// <summary>Critério de aceite da Fase 9, na forma de texto.</summary>
    [Fact]
    public void ExplicaAPassagemDeNormalParaRiscoComARegraEAsParcelas()
    {
        var texto = ExplicacaoDaPrevisao.Montar(Dados(
            SituacaoDoSla.Normal, SituacaoDoSla.Risco, MotivoDaSituacao.FolgaAbaixoDoLimiarDeRisco, folgaEmSegundos: 200));

        Assert.Equal(
            "Passou de Normal para Risco porque a folga até o fim da janela prometida caiu para 3 min, abaixo do limiar de risco de 5 min. "
            + "A chegada prevista soma 17 min de deslocamento (10,0 km, pelo provedor de rotas simulado) e 10 min de 2 paradas antes desta. "
            + "A posição do motorista usada foi capturada 1 h 05 min antes do cálculo.",
            texto);
    }

    [Theory]
    [InlineData(MotivoDaContingencia.ProvedorAusente, "não há provedor de rotas configurado")]
    [InlineData(MotivoDaContingencia.TempoLimite, "o provedor de rotas não respondeu no tempo limite")]
    [InlineData(MotivoDaContingencia.FalhaDoProvedor, "o provedor de rotas falhou")]
    public void ContingenciaDizPorQueOProvedorNaoFoiUsado(MotivoDaContingencia motivo, string trecho)
    {
        var texto = ExplicacaoDaPrevisao.Montar(Dados(SituacaoDoSla.Normal, SituacaoDoSla.Normal, MotivoDaSituacao.FolgaSuficiente, 3_000) with
        {
            Fonte = FonteDaPrevisao.Contingencia,
            Provedor = null,
            MotivoDaContingencia = motivo,
        });

        Assert.Contains($"estimado em linha reta porque {trecho}", texto, StringComparison.Ordinal);
        Assert.StartsWith("Normal porque a chegada prevista deixa 50 min de folga", texto, StringComparison.Ordinal);
    }

    [Fact]
    public void AtrasadaEEncerradaTemTextoProprio()
    {
        var atrasada = ExplicacaoDaPrevisao.Montar(Dados(SituacaoDoSla.Risco, SituacaoDoSla.Atrasada, MotivoDaSituacao.JanelaEncerrada, -600));
        var encerrada = ExplicacaoDaPrevisao.Montar(Dados(SituacaoDoSla.Atrasada, SituacaoDoSla.Atrasada, MotivoDaSituacao.JanelaEncerrada, -600) with
        {
            Tipo = TipoDeRegistroDePrevisao.Encerrada,
            StatusDaEntrega = StatusDaEntrega.Entregue,
        });

        Assert.StartsWith("Passou de Risco para Atrasada porque a janela prometida terminou há 10 min sem a entrega concluída.", atrasada, StringComparison.Ordinal);
        Assert.Equal("Previsão encerrada: a entrega saiu da execução com o status Entregue. A última situação era Atrasada.", encerrada);
    }

    [Theory]
    [InlineData(20, "menos de 1 min")]
    [InlineData(90, "2 min")]
    [InlineData(3_900, "1 h 05 min")]
    public void DuracaoLegivel(int segundos, string esperado) =>
        Assert.Equal(esperado, ExplicacaoDaPrevisao.Duracao(TimeSpan.FromSeconds(segundos)));

    private static DadosDaExplicacao Dados(SituacaoDoSla anterior, SituacaoDoSla situacao, MotivoDaSituacao motivo, int folgaEmSegundos) =>
        new(
            TipoDeRegistroDePrevisao.SituacaoAlterada,
            anterior,
            situacao,
            motivo,
            Calculo.AddMinutes(30),
            null,
            false,
            folgaEmSegundos,
            1_000,
            10_000,
            2,
            600,
            FonteDaPrevisao.Provedor,
            "simulado",
            null,
            1_200,
            300,
            Calculo.AddMinutes(-65),
            Calculo,
            null);
}

public sealed class OpcoesDePrevisaoTestes
{
    [Fact]
    public void PadraoEhValido() =>
        Assert.True(new ValidacaoDeOpcoesDePrevisao().Validate(null, new OpcoesDePrevisao()).Succeeded);

    [Fact]
    public void ConfiguracaoIncoerenteFalhaNaSubida()
    {
        var resultado = new ValidacaoDeOpcoesDePrevisao().Validate(null, new OpcoesDePrevisao
        {
            Provedor = " ",
            FolgaParaAtencao = TimeSpan.FromMinutes(5),
            FolgaParaRisco = TimeSpan.FromMinutes(5),
            TempoLimiteDoProvedor = TimeSpan.Zero,
            VelocidadeDeContingenciaEmMetrosPorSegundo = 0,
        });

        Assert.True(resultado.Failed);
        Assert.Equal(4, resultado.Failures!.Count());
    }
}
