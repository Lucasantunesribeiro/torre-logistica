using TorreLogistica.Application.Indicadores;
using TorreLogistica.Domain.Abstracoes.Erros;

namespace TorreLogistica.UnitTests.Aplicacao;

/// <summary>
/// O período pedido é a única entrada dos indicadores, e ela chega de fora.
/// </summary>
/// <remarks>
/// Duas coisas se decidem aqui antes de qualquer consulta: que o intervalo faz sentido, e que o fuso em
/// que ele foi escrito não muda o recorte. A segunda é a que mais engana — a mesma janela pedida do
/// Brasil e de Lisboa tem de ser o mesmo instante, e "a mesma hora local" não é.
/// </remarks>
public sealed class PeriodoDoIndicadorTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SemPeriodoInformadoValemOsUltimosTrintaDias()
    {
        var periodo = PeriodoDoIndicador.Criar(de: null, ate: null, Agora);

        Assert.Equal(Agora, periodo.Ate);
        Assert.Equal(Agora.AddDays(-30), periodo.De);
    }

    [Fact]
    public void OMesmoInstanteEmOutroFusoDaOMesmoPeriodo()
    {
        var emUtc = PeriodoDoIndicador.Criar(Agora.AddDays(-7), Agora, Agora);
        var emBrasilia = PeriodoDoIndicador.Criar(
            Agora.AddDays(-7).ToOffset(TimeSpan.FromHours(-3)),
            Agora.ToOffset(TimeSpan.FromHours(-3)),
            Agora);

        Assert.Equal(emUtc, emBrasilia);
        Assert.Equal(TimeSpan.Zero, emBrasilia.De.Offset);
    }

    [Fact]
    public void AMesmaHoraLocalEmOutroFusoEOutroInstante()
    {
        var emUtc = PeriodoDoIndicador.Criar(Agora.AddDays(-7), Agora, Agora);
        var horaLocal = new DateTimeOffset(Agora.DateTime, TimeSpan.FromHours(-3));
        var emOutroFuso = PeriodoDoIndicador.Criar(horaLocal.AddDays(-7), horaLocal, Agora);

        Assert.NotEqual(emUtc, emOutroFuso);
        Assert.Equal(TimeSpan.FromHours(3), emOutroFuso.Ate - emUtc.Ate);
    }

    [Fact]
    public void FimAntesDoInicioERecusado()
    {
        var erro = Assert.Throws<ExcecaoDeDominio>(() => PeriodoDoIndicador.Criar(Agora, Agora.AddDays(-1), Agora));

        Assert.Equal("periodo_invalido", erro.Codigo);
    }

    [Fact]
    public void InicioIgualAoFimERecusado()
    {
        var erro = Assert.Throws<ExcecaoDeDominio>(() => PeriodoDoIndicador.Criar(Agora, Agora, Agora));

        Assert.Equal("periodo_invalido", erro.Codigo);
    }

    [Fact]
    public void PeriodoMaiorQueOLimiteERecusado()
    {
        var limite = Agora.Add(PeriodoDoIndicador.DuracaoMaxima);

        Assert.Equal(limite, PeriodoDoIndicador.Criar(Agora, limite, Agora).Ate);

        var erro = Assert.Throws<ExcecaoDeDominio>(
            () => PeriodoDoIndicador.Criar(Agora, limite.AddSeconds(1), Agora));

        Assert.Equal("periodo_grande_demais", erro.Codigo);
    }
}
