using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Rastreamento;

namespace TorreLogistica.UnitTests.Dominio;

public sealed class PosicaoDoMotoristaTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Rota = Guid.CreateVersion7();
    private static readonly JanelaDeExecucaoDaRota[] RotaEmAndamento = [new(Rota, Agora.AddHours(-2), null)];

    [Fact]
    public void PosicaoValidaGuardaCapturaERecebimentoSeparadosEAssociaARota()
    {
        var capturada = Agora.AddSeconds(-30);

        var posicao = Registrar(capturadaEm: capturada, velocidade: 8.5, direcao: 90);

        Assert.Equal(capturada, posicao.CapturadaEm);
        Assert.Equal(Agora, posicao.RecebidaEm);
        Assert.Equal(Rota, posicao.RotaId);
        Assert.Equal(QualidadeDaPosicao.Confiavel, posicao.Qualidade);
        Assert.Equal(-22.9056, posicao.Localizacao.Latitude);
        Assert.Equal(8.5, posicao.VelocidadeEmMetrosPorSegundo);
    }

    [Theory]
    [InlineData(100, QualidadeDaPosicao.Confiavel)]
    [InlineData(100.1, QualidadeDaPosicao.Imprecisa)]
    [InlineData(2_000, QualidadeDaPosicao.Imprecisa)]
    public void PrecisaoDefineAQualidade(double precisao, QualidadeDaPosicao esperada)
    {
        Assert.Equal(esperada, Registrar(precisao: precisao).Qualidade);
    }

    public static TheoryData<string, string> Recusas() => new()
    {
        { "latitude acima de 90", "coordenada_invalida" },
        { "ponto zero", "coordenada_invalida" },
        { "precisão zero", "precisao_invalida" },
        { "precisão acima do aceito", "precisao_invalida" },
        { "precisão não finita", "precisao_invalida" },
        { "velocidade negativa", "velocidade_invalida" },
        { "velocidade implausível", "velocidade_invalida" },
        { "direção 360", "direcao_invalida" },
        { "sequência negativa", "sequencia_invalida" },
        { "evento vazio", "evento_de_localizacao_invalido" },
        { "futuro além da tolerância", "capturada_no_futuro" },
        { "antiga demais", "capturada_antiga_demais" },
        { "antes da rota", "fora_de_rota" },
        { "sem rota", "fora_de_rota" },
    };

    [Theory]
    [MemberData(nameof(Recusas))]
    public void PosicaoForaDaPoliticaEhRecusadaComMotivo(string caso, string codigo)
    {
        Action registrar = caso switch
        {
            "latitude acima de 90" => () => Registrar(latitude: 90.5),
            "ponto zero" => () => Registrar(latitude: 0, longitude: 0),
            "precisão zero" => () => Registrar(precisao: 0),
            "precisão acima do aceito" => () => Registrar(precisao: 2_000.1),
            "precisão não finita" => () => Registrar(precisao: double.NaN),
            "velocidade negativa" => () => Registrar(velocidade: -1),
            "velocidade implausível" => () => Registrar(velocidade: 71),
            "direção 360" => () => Registrar(direcao: 360),
            "sequência negativa" => () => Registrar(sequencia: -1),
            "evento vazio" => () => Registrar(evento: Guid.Empty),
            "futuro além da tolerância" => () => Registrar(capturadaEm: Agora.AddMinutes(2).AddSeconds(1)),
            "antiga demais" => () => Registrar(capturadaEm: Agora.AddDays(-7).AddSeconds(-1), janelas: [new(Rota, Agora.AddDays(-8), null)]),
            "antes da rota" => () => Registrar(capturadaEm: Agora.AddHours(-2).AddMinutes(-16)),
            "sem rota" => () => Registrar(janelas: []),
            _ => throw new ArgumentOutOfRangeException(nameof(caso), caso, "Caso desconhecido."),
        };

        Assert.Equal(codigo, Assert.Throws<ExcecaoDeDominio>(registrar).Codigo);
    }

    [Fact]
    public void LimitesDasToleranciasSaoInclusivos()
    {
        Assert.NotNull(Registrar(capturadaEm: Agora.AddMinutes(2)));
        Assert.NotNull(Registrar(capturadaEm: Agora.AddDays(-7), janelas: [new(Rota, Agora.AddDays(-8), null)]));
        Assert.NotNull(Registrar(capturadaEm: Agora.AddHours(-2).AddMinutes(-15)));
    }

    [Fact]
    public void RotaConcluidaAceitaPosicoesAteAToleranciaDepoisDaConclusao()
    {
        JanelaDeExecucaoDaRota[] concluida = [new(Rota, Agora.AddHours(-3), Agora.AddHours(-1))];

        Assert.NotNull(Registrar(capturadaEm: Agora.AddHours(-1).AddMinutes(5), janelas: concluida));
        Assert.Equal("fora_de_rota", Assert.Throws<ExcecaoDeDominio>(() =>
            Registrar(capturadaEm: Agora.AddHours(-1).AddMinutes(6), janelas: concluida)).Codigo);
    }

    [Fact]
    public void ComRotasSobrepostasValeAIniciadaPorUltimo()
    {
        var anterior = Guid.CreateVersion7();
        var nova = Guid.CreateVersion7();

        var posicao = Registrar(janelas: [new(anterior, Agora.AddHours(-3), Agora.AddMinutes(-2)), new(nova, Agora.AddMinutes(-5), null)]);

        Assert.Equal(nova, posicao.RotaId);
    }

    [Fact]
    public void InstantesSaoTruncadosAoMicrossegundoEmUtc()
    {
        var capturada = new DateTimeOffset(2026, 9, 14, 8, 59, 0, TimeSpan.FromHours(-3)).AddTicks(1_234_567);

        var posicao = Registrar(capturadaEm: capturada);

        Assert.Equal(TimeSpan.Zero, posicao.CapturadaEm.Offset);
        Assert.Equal(0, posicao.CapturadaEm.Ticks % 10);
        Assert.Equal(capturada.UtcTicks - (capturada.UtcTicks % 10), posicao.CapturadaEm.UtcTicks);
    }

    private static PosicaoDoMotorista Registrar(
        double latitude = -22.9056,
        double longitude = -47.0608,
        double precisao = 8,
        double? velocidade = null,
        double? direcao = null,
        long sequencia = 1,
        Guid? evento = null,
        DateTimeOffset? capturadaEm = null,
        JanelaDeExecucaoDaRota[]? janelas = null) =>
        PosicaoDoMotorista.Registrar(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            evento ?? Guid.CreateVersion7(),
            sequencia,
            latitude,
            longitude,
            precisao,
            velocidade,
            direcao,
            capturadaEm ?? Agora.AddSeconds(-10),
            Agora,
            janelas ?? RotaEmAndamento);
}
