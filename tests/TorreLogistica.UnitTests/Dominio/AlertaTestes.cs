using TorreLogistica.Application.Alertas;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Alertas;
using TorreLogistica.Domain.Comum;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Previsao;

namespace TorreLogistica.UnitTests.Dominio;

public sealed class CicloDeVidaDoAlertaTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan JanelaDeReabertura = TimeSpan.FromMinutes(30);
    private readonly Guid _organizacao = Guid.CreateVersion7();
    private readonly Guid _motorista = Guid.CreateVersion7();
    private readonly Guid _rota = Guid.CreateVersion7();

    [Fact]
    public void AbreComEventoEEvidenciaDeAbertura()
    {
        var (alerta, evento) = Abrir(minutos: 12);

        Assert.Equal(EstadoDoAlerta.Aberto, alerta.Estado);
        Assert.Equal(SeveridadeDoAlerta.Alta, alerta.Severidade);
        Assert.Equal($"MotoristaOffline:rota:{_rota:N}:motorista:{_motorista:N}", alerta.Chave);
        Assert.Equal(TipoDeEventoDoAlerta.Aberto, evento.Tipo);
        Assert.Equal(1, evento.Sequencia);
        Assert.Contains("\"minutos\":12", alerta.EvidenciaDeAbertura, StringComparison.Ordinal);
    }

    [Fact]
    public void NaoAbreDeCondicaoQueNaoVale()
    {
        var erro = Assert.Throws<ExcecaoDeDominio>(() =>
            AlertaOperacional.Abrir(Guid.CreateVersion7(), _organizacao, Offline(false, 3), Guid.CreateVersion7(), Agora));

        Assert.Equal("condicao_ausente", erro.Codigo);
    }

    /// <summary>R11: constatar de novo a mesma condição não gera alerta nem evento.</summary>
    [Fact]
    public void MesmaCondicaoAtualizaAEvidenciaSemEventoNovo()
    {
        var (alerta, _) = Abrir(minutos: 12);

        for (var minuto = 13; minuto < 60; minuto++)
        {
            var (resultado, evento) = alerta.Constatar(Offline(true, minuto), JanelaDeReabertura, Guid.CreateVersion7(), Agora.AddMinutes(minuto - 12));
            Assert.Equal(ResultadoDaConstatacao.SemMudanca, resultado);
            Assert.Null(evento);
        }

        Assert.Equal(1, alerta.UltimaSequenciaDeEvento);
        Assert.Contains("\"minutos\":59", alerta.UltimaEvidencia, StringComparison.Ordinal);
        Assert.Contains("\"minutos\":12", alerta.EvidenciaDeAbertura, StringComparison.Ordinal);
        Assert.Equal(Agora.AddMinutes(47), alerta.UltimaConstatacaoEm);
    }

    [Fact]
    public void ResolveSozinhoQuandoACondicaoDeixaDeValer()
    {
        var (alerta, _) = Abrir(minutos: 12);

        var (resultado, evento) = alerta.Constatar(Offline(false, 0), JanelaDeReabertura, Guid.CreateVersion7(), Agora.AddMinutes(3));

        Assert.Equal(ResultadoDaConstatacao.Resolvido, resultado);
        Assert.Equal(TipoDeEventoDoAlerta.Resolvido, evento!.Tipo);
        Assert.Equal(EstadoDoAlerta.Resolvido, alerta.Estado);
        Assert.Equal(FormaDeResolucao.Automatica, alerta.FormaDeResolucao);
        Assert.Equal(Agora.AddMinutes(3), alerta.ResolvidoEm);
        Assert.False(alerta.CondicaoAtiva);
    }

    [Fact]
    public void ReabreDentroDaJanelaEPedeAlertaNovoDepoisDela()
    {
        var (alerta, _) = Abrir(minutos: 12);
        alerta.Constatar(Offline(false, 0), JanelaDeReabertura, Guid.CreateVersion7(), Agora);

        var (reabertura, eventoDaReabertura) = alerta.Constatar(Offline(true, 11), JanelaDeReabertura, Guid.CreateVersion7(), Agora.AddMinutes(30));

        Assert.Equal(ResultadoDaConstatacao.Reaberto, reabertura);
        Assert.Equal(TipoDeEventoDoAlerta.Reaberto, eventoDaReabertura!.Tipo);
        Assert.Equal(3, eventoDaReabertura.Sequencia);
        Assert.Equal(EstadoDoAlerta.Aberto, alerta.Estado);
        Assert.Equal(1, alerta.Reaberturas);
        Assert.Null(alerta.ResolvidoEm);

        alerta.Constatar(Offline(false, 0), JanelaDeReabertura, Guid.CreateVersion7(), Agora.AddMinutes(40));
        var (depoisDaJanela, semEvento) = alerta.Constatar(Offline(true, 11), JanelaDeReabertura, Guid.CreateVersion7(), Agora.AddMinutes(70).AddSeconds(1));

        Assert.Equal(ResultadoDaConstatacao.ExigeNovoAlerta, depoisDaJanela);
        Assert.Null(semEvento);
        Assert.Equal(EstadoDoAlerta.Resolvido, alerta.Estado);
    }

    [Fact]
    public void ResolvidoPeloOperadorNaoReabreEnquantoACondicaoPersiste()
    {
        var (alerta, _) = Abrir(minutos: 12);
        var operador = Guid.CreateVersion7();

        var resolucao = alerta.ResolverPeloOperador(operador, "  Motorista ligou: sem sinal na serra.  ", Guid.CreateVersion7(), Agora);
        Assert.Equal(TipoDeEventoDoAlerta.ResolvidoPeloOperador, resolucao!.Tipo);
        Assert.Equal("Motorista ligou: sem sinal na serra.", alerta.ObservacaoDaResolucao);
        Assert.Equal(operador, alerta.ResolvidoPorUsuarioId);
        Assert.Null(alerta.ResolverPeloOperador(operador, null, Guid.CreateVersion7(), Agora.AddMinutes(1)));

        // Continua valendo: fica resolvido.
        Assert.Equal(ResultadoDaConstatacao.SemMudanca, alerta.Constatar(Offline(true, 14), JanelaDeReabertura, Guid.CreateVersion7(), Agora.AddMinutes(2)).Resultado);
        Assert.Equal(EstadoDoAlerta.Resolvido, alerta.Estado);

        // Some e volta: agora é problema novo, e reabre.
        alerta.Constatar(Offline(false, 0), JanelaDeReabertura, Guid.CreateVersion7(), Agora.AddMinutes(3));
        Assert.Equal(ResultadoDaConstatacao.Reaberto, alerta.Constatar(Offline(true, 11), JanelaDeReabertura, Guid.CreateVersion7(), Agora.AddMinutes(15)).Resultado);
        Assert.Null(alerta.ResolvidoPorUsuarioId);
    }

    [Fact]
    public void ObservacaoLongaEConstatacaoDeOutraChaveSaoRecusadas()
    {
        var (alerta, _) = Abrir(minutos: 12);

        Assert.Throws<ExcecaoDeDominio>(() =>
            alerta.ResolverPeloOperador(Guid.CreateVersion7(), new string('x', AlertaOperacional.TamanhoMaximoDaObservacao + 1), Guid.CreateVersion7(), Agora));

        var outra = Constatacao.Criar(TipoDeAlerta.ParadoTempoExcessivo, null, _motorista, _rota, true, new { });
        var erro = Assert.Throws<ExcecaoDeDominio>(() => alerta.Constatar(outra, JanelaDeReabertura, Guid.CreateVersion7(), Agora));
        Assert.Equal("constatacao_de_outro_alerta", erro.Codigo);
    }

    [Fact]
    public void CatalogoDefineSeveridadeEAlvo()
    {
        var entrega = Guid.CreateVersion7();

        Assert.Equal(SeveridadeDoAlerta.Critica, CatalogoDeAlertas.Severidade(TipoDeAlerta.OcorrenciaCritica));
        Assert.Equal(SeveridadeDoAlerta.Media, CatalogoDeAlertas.Severidade(TipoDeAlerta.RiscoDeAtraso));
        Assert.Equal($"RiscoDeAtraso:entrega:{entrega:N}", CatalogoDeAlertas.Chave(TipoDeAlerta.RiscoDeAtraso, entrega, _motorista, _rota));
        Assert.Throws<ExcecaoDeDominio>(() => Constatacao.Criar(TipoDeAlerta.EntregaAtrasada, null, _motorista, _rota, true, new { }));
        Assert.Throws<ExcecaoDeDominio>(() => Constatacao.Criar(TipoDeAlerta.MotoristaOffline, null, _motorista, null, true, new { }));
    }

    private (AlertaOperacional Alerta, EventoDoAlerta Evento) Abrir(int minutos) =>
        AlertaOperacional.Abrir(Guid.CreateVersion7(), _organizacao, Offline(true, minutos), Guid.CreateVersion7(), Agora);

    private Constatacao Offline(bool condicao, int minutos) =>
        Constatacao.Criar(TipoDeAlerta.MotoristaOffline, null, _motorista, _rota, condicao, new { minutos });
}

public sealed class RegrasDeAlertaTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Dez = TimeSpan.FromMinutes(10);
    private readonly Guid _motorista = Guid.CreateVersion7();
    private readonly Guid _rota = Guid.CreateVersion7();

    [Theory]
    [InlineData(9, true, 1, false)]
    [InlineData(10, true, 1, true)]
    [InlineData(45, false, 1, false)]
    [InlineData(45, true, 0, false)]
    public void MotoristaOfflinePeloTempoSemPosicao(int minutosSemPosicao, bool emAndamento, int pendentes, bool esperado)
    {
        var constatacao = RegrasDeAlerta.MotoristaOffline(
            _motorista, _rota, emAndamento, Agora.AddHours(-2), Agora.AddMinutes(-minutosSemPosicao), pendentes, Agora, Dez);

        Assert.Equal(esperado, constatacao.Condicao);
        Assert.Contains($"\"minutosSemPosicao\":{minutosSemPosicao}", constatacao.Evidencia, StringComparison.Ordinal);
    }

    [Fact]
    public void SemNenhumaPosicaoContaDesdeASaida()
    {
        var constatacao = RegrasDeAlerta.MotoristaOffline(_motorista, _rota, true, Agora.AddMinutes(-12), null, 3, Agora, Dez);

        Assert.True(constatacao.Condicao);
        Assert.Contains("\"ultimaPosicaoCapturadaEm\":null", constatacao.Evidencia, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(16, false, false, true)]
    [InlineData(14, false, false, false)]
    [InlineData(16, true, false, false)]
    [InlineData(31, true, false, true)]
    [InlineData(40, false, true, false)]
    public void ParadoPelaPermanenciaComLimiteMaiorAtendendoParada(int minutosParado, bool atendendoParada, bool offline, bool esperado)
    {
        var permanencia = new PermanenciaNoLocal(Agora.AddMinutes(-minutosParado), Agora, 5);

        var constatacao = RegrasDeAlerta.ParadoTempoExcessivo(
            _motorista, _rota, true, offline, permanencia, atendendoParada, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), 100);

        Assert.Equal(esperado, constatacao.Condicao);
    }

    [Fact]
    public void SemPermanenciaNaoHaParado() =>
        Assert.False(RegrasDeAlerta.ParadoTempoExcessivo(
            _motorista, _rota, true, false, null, false, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), 100).Condicao);

    [Fact]
    public void TentativasExcedidasAteAEntregaSairDaOperacao()
    {
        var entrega = EntregaComTentativaFrustrada();

        Assert.True(RegrasDeAlerta.TentativasExcedidas(entrega, _rota, 1).Condicao);
        Assert.False(RegrasDeAlerta.TentativasExcedidas(entrega, _rota, 2).Condicao);

        entrega.Cancelar(MotivoDeCancelamento.SolicitacaoDoCliente, null, null, Guid.CreateVersion7(), Agora.AddMinutes(30));
        var cancelada = RegrasDeAlerta.TentativasExcedidas(entrega, _rota, 1);
        Assert.False(cancelada.Condicao);
        Assert.Contains("\"ultimoMotivo\":\"LocalFechado\"", cancelada.Evidencia, StringComparison.Ordinal);
    }

    [Fact]
    public void RiscoEAtrasoVemDaPrevisaoAtiva()
    {
        var entregaId = Guid.CreateVersion7();
        var emRisco = Previsao(entregaId, janelaFim: Agora.AddMinutes(12), deslocamentoEmMinutos: 10);

        Assert.True(RegrasDeAlerta.RiscoDeAtraso(entregaId, _rota, emRisco, "explicação").Condicao);
        Assert.False(RegrasDeAlerta.EntregaAtrasada(entregaId, _rota, emRisco, "explicação").Condicao);
        Assert.False(RegrasDeAlerta.RiscoDeAtraso(entregaId, _rota, null, null).Condicao);

        emRisco.Encerrar(StatusDaEntrega.Entregue, Guid.CreateVersion7(), Agora);
        Assert.False(RegrasDeAlerta.RiscoDeAtraso(entregaId, _rota, emRisco, null).Condicao);
    }

    private Entrega EntregaComTentativaFrustrada()
    {
        var entrega = Entrega.Criar(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "ENT-2026-000001",
            new DadosDaEntrega(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Endereco.Criar("Rua das Palmeiras", "120", null, "Centro", "Campinas", "SP", "13010000"),
                null,
                JanelaDeEntrega.Criar(Agora.AddHours(1), Agora.AddHours(3)),
                null),
            null,
            Guid.CreateVersion7(),
            Agora).Entrega;

        entrega.Planejar(_rota, null, Guid.CreateVersion7(), Agora);
        entrega.Atribuir(_rota, _motorista, null, Guid.CreateVersion7(), Agora);
        entrega.IniciarRota(_rota, null, Guid.CreateVersion7(), Agora);
        entrega.RegistrarTentativaFrustrada(MotivoDeTentativaFrustrada.LocalFechado, null, Guid.CreateVersion7(), Agora.AddMinutes(20));
        return entrega;
    }

    private PrevisaoDaEntrega Previsao(Guid entregaId, DateTimeOffset janelaFim, int deslocamentoEmMinutos)
    {
        var limiares = LimiaresDeSla.Criar(TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(5));
        var janela = JanelaDeEntrega.Criar(janelaFim.AddHours(-2), janelaFim);
        var composicao = CalculadoraDeChegada.Calcular(
            Agora,
            true,
            [new ParadaParaPrevisao(entregaId, StatusDaEntrega.EmRota, true, null)],
            [TrechoDeTrajeto.Criar(TimeSpan.FromMinutes(deslocamentoEmMinutos), 6_000)],
            TimeSpan.FromMinutes(5))[0];

        return PrevisaoDaEntrega.Iniciar(
            Guid.CreateVersion7(),
            new CalculoDaPrevisao(
                _rota,
                composicao,
                RegrasDeSla.Classificar(janela, composicao.ChegadaPrevista, Agora, limiares),
                janela,
                limiares,
                TimeSpan.FromMinutes(5),
                FonteDaPrevisao.Provedor,
                "simulado",
                null,
                Agora,
                Agora),
            Guid.CreateVersion7()).Previsao;
    }
}

public sealed class DescricaoDoAlertaTestes
{
    [Fact]
    public void DescreveCadaTipoPelaEvidencia()
    {
        Assert.Equal(
            "Motorista sem enviar posição há 14 min (limite 10 min), com 2 entrega(s) pendente(s).",
            DescricaoDoAlerta.Montar(TipoDeAlerta.MotoristaOffline,
                """{"ultimaPosicaoCapturadaEm":"2026-09-15T12:00:00+00:00","minutosSemPosicao":14,"limiteEmMinutos":10,"entregasPendentes":2}"""));
        Assert.Equal(
            "Motorista parado há 32 min num raio de 100 m (limite 30 min, atendendo uma parada).",
            DescricaoDoAlerta.Montar(TipoDeAlerta.ParadoTempoExcessivo,
                """{"minutosParado":32,"raioEmMetros":100,"limiteEmMinutos":30,"atendendoParada":true}"""));
        Assert.Equal(
            "Entrega atrasada: a janela prometida terminou há 1 h 05 min sem a entrega concluída.",
            DescricaoDoAlerta.Montar(TipoDeAlerta.EntregaAtrasada, """{"folgaEmSegundos":-3900}"""));
        Assert.Equal(
            "2 tentativa(s) sem sucesso (limite 2); último motivo: DestinatarioAusente.",
            DescricaoDoAlerta.Montar(TipoDeAlerta.TentativasExcedidas, """{"tentativas":2,"limite":2,"ultimoMotivo":"DestinatarioAusente"}"""));
        Assert.StartsWith(
            "O alvo deste alerta saiu da avaliação",
            DescricaoDoAlerta.Montar(TipoDeAlerta.MotoristaOffline, """{"foraDaAvaliacaoDaRota":true}"""),
            StringComparison.Ordinal);
    }

    [Fact]
    public void OpcoesIncoerentesSaoRecusadas()
    {
        var validacao = new ValidacaoDeOpcoesDeAlertas();

        Assert.True(validacao.Validate(null, new OpcoesDeAlertas()).Succeeded);

        var resultado = validacao.Validate(null, new OpcoesDeAlertas
        {
            TempoParadoParaAlerta = TimeSpan.FromMinutes(30),
            TempoParadoAtendendoParadaParaAlerta = TimeSpan.FromMinutes(15),
            LimiteDeTentativas = 0,
            RaioDeImobilidadeEmMetros = 5,
        });

        Assert.True(resultado.Failed);
        Assert.Equal(3, resultado.Failures!.Count());
    }
}
