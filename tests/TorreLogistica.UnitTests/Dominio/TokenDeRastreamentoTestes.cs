using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Rastreamento;

namespace TorreLogistica.UnitTests.Dominio;

public sealed class TokenDeRastreamentoTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly Guid _organizacao = Guid.CreateVersion7();
    private readonly Guid _entrega = Guid.CreateVersion7();
    private readonly Guid _autor = Guid.CreateVersion7();

    [Fact]
    public void EmiteComValidadeEHashDeTrintaEDoisBytes()
    {
        var token = Emitir();

        Assert.Equal(_organizacao, token.OrganizacaoId);
        Assert.Equal(_entrega, token.EntregaId);
        Assert.Equal(_autor, token.AutorUsuarioId);
        Assert.Equal(Agora, token.EmitidoEm);
        Assert.Equal(Agora.AddDays(30), token.ExpiraEm);
        Assert.Null(token.RevogadoEm);
        Assert.Equal(PoliticaDeRastreamentoPublico.TamanhoDoHashEmBytes, token.HashDoToken.Length);
    }

    [Fact]
    public void HashForaDeTrintaEDoisBytesEhRecusado()
    {
        Assert.Equal(
            "hash_do_token_invalido",
            Assert.Throws<ExcecaoDeDominio>(() => Emitir(hash: new byte[31])).Codigo);

        Assert.Equal(
            "hash_do_token_invalido",
            Assert.Throws<ExcecaoDeDominio>(() => Emitir(hash: [])).Codigo);
    }

    [Fact]
    public void ValidadeNaoPositivaEhRecusada()
    {
        Assert.Equal(
            "validade_invalida",
            Assert.Throws<ExcecaoDeDominio>(() => Emitir(validade: TimeSpan.Zero)).Codigo);

        Assert.Equal(
            "validade_invalida",
            Assert.Throws<ExcecaoDeDominio>(() => Emitir(validade: TimeSpan.FromDays(-1))).Codigo);
    }

    [Fact]
    public void ValeAteOInstanteDaExpiracao()
    {
        var token = Emitir(validade: TimeSpan.FromHours(2));

        Assert.True(token.EstaValido(Agora));
        Assert.True(token.EstaValido(Agora.AddHours(2).AddTicks(-1)));
        Assert.False(token.EstaValido(Agora.AddHours(2)));
        Assert.False(token.EstaValido(Agora.AddDays(1)));
    }

    [Fact]
    public void RevogarInvalidaEGuardaOPrimeiroInstante()
    {
        var token = Emitir();

        token.Revogar(Agora.AddMinutes(5));
        token.Revogar(Agora.AddMinutes(9));

        Assert.Equal(Agora.AddMinutes(5), token.RevogadoEm);
        Assert.False(token.EstaValido(Agora.AddMinutes(6)));
    }

    private TokenDeRastreamento Emitir(byte[]? hash = null, TimeSpan? validade = null) =>
        TokenDeRastreamento.Emitir(
            Guid.CreateVersion7(),
            _organizacao,
            _entrega,
            hash ?? new byte[PoliticaDeRastreamentoPublico.TamanhoDoHashEmBytes],
            _autor,
            validade ?? PoliticaDeRastreamentoPublico.ValidadePadrao,
            Agora);
}

public sealed class PoliticaDeRastreamentoPublicoTestes
{
    [Theory]
    [InlineData(StatusDaEntrega.EmRota, true)]
    [InlineData(StatusDaEntrega.ProximaDoDestino, true)]
    [InlineData(StatusDaEntrega.Criada, false)]
    [InlineData(StatusDaEntrega.Planejada, false)]
    [InlineData(StatusDaEntrega.Atribuida, false)]
    [InlineData(StatusDaEntrega.Entregue, false)]
    [InlineData(StatusDaEntrega.TentativaFrustrada, false)]
    [InlineData(StatusDaEntrega.Reagendada, false)]
    [InlineData(StatusDaEntrega.Cancelada, false)]
    public void PosicaoSoApareceComAEntregaACaminho(StatusDaEntrega status, bool esperado) =>
        Assert.Equal(esperado, PoliticaDeRastreamentoPublico.MostraPosicao(status));

    [Theory]
    [InlineData(-22.9137, -22.91)]
    [InlineData(-22.9149, -22.91)]
    [InlineData(-47.0069, -47.01)]
    [InlineData(0.004, 0.0)]
    [InlineData(0.006, 0.01)]
    public void ArredondaParaOCentroDaCelula(double bruto, double esperado) =>
        Assert.Equal(esperado, PoliticaDeRastreamentoPublico.Arredondar(bruto), 6);

    /// <summary>
    /// O ponto publicado nunca fica a mais de meia célula do real — é essa a garantia que a página
    /// anuncia quando declara a precisão aproximada.
    /// </summary>
    [Fact]
    public void ErroDoArredondamentoNuncaPassaDeMeiaCelula()
    {
        for (var passo = 0; passo <= 400; passo++)
        {
            var bruto = -23.5 + (passo * 0.0025);
            var erro = Math.Abs(PoliticaDeRastreamentoPublico.Arredondar(bruto) - bruto);

            Assert.True(
                erro <= (PoliticaDeRastreamentoPublico.GradeEmGraus / 2) + 1e-9,
                $"Erro de {erro} grau em {bruto}.");
        }
    }

    /// <summary>
    /// A precisão anunciada não pode ser melhor que a célula real: um grau de latitude vale cerca de
    /// 111 km, então meia célula de 0,01° já passa de 500 m.
    /// </summary>
    [Fact]
    public void PrecisaoAnunciadaNaoPrometeMaisQueAGrade()
    {
        const double metrosPorGrauDeLatitude = 111_320;
        var celulaReal = PoliticaDeRastreamentoPublico.GradeEmGraus * metrosPorGrauDeLatitude;

        Assert.True(PoliticaDeRastreamentoPublico.LadoDaCelulaEmMetros <= celulaReal);
        Assert.True(PoliticaDeRastreamentoPublico.LadoDaCelulaEmMetros >= celulaReal * 0.9);
    }

    [Theory]
    [InlineData(TipoDeEventoDaEntrega.Criada, true)]
    [InlineData(TipoDeEventoDaEntrega.SaiuParaRota, true)]
    [InlineData(TipoDeEventoDaEntrega.ProximidadeDetectada, true)]
    [InlineData(TipoDeEventoDaEntrega.ChegadaRegistrada, true)]
    [InlineData(TipoDeEventoDaEntrega.Entregue, true)]
    [InlineData(TipoDeEventoDaEntrega.TentativaFrustrada, true)]
    [InlineData(TipoDeEventoDaEntrega.Reagendada, true)]
    [InlineData(TipoDeEventoDaEntrega.Cancelada, true)]
    [InlineData(TipoDeEventoDaEntrega.Planejada, false)]
    [InlineData(TipoDeEventoDaEntrega.Atribuida, false)]
    [InlineData(TipoDeEventoDaEntrega.Reatribuida, false)]
    [InlineData(TipoDeEventoDaEntrega.RetiradaDaRota, false)]
    [InlineData(TipoDeEventoDaEntrega.DadosAlterados, false)]
    public void TimelinePublicaEsconderDecisaoInternaDaOperacao(TipoDeEventoDaEntrega tipo, bool esperado) =>
        Assert.Equal(esperado, PoliticaDeRastreamentoPublico.EhMarcoPublico(tipo));

    /// <summary>Todo tipo de evento precisa ter decisão consciente sobre aparecer ou não.</summary>
    [Fact]
    public void TodoTipoDeEventoFoiClassificado()
    {
        var classificados = Enum.GetValues<TipoDeEventoDaEntrega>()
            .Count(tipo => PoliticaDeRastreamentoPublico.EhMarcoPublico(tipo) || !PoliticaDeRastreamentoPublico.EhMarcoPublico(tipo));

        Assert.Equal(Enum.GetValues<TipoDeEventoDaEntrega>().Length, classificados);
        Assert.Equal(8, Enum.GetValues<TipoDeEventoDaEntrega>().Count(PoliticaDeRastreamentoPublico.EhMarcoPublico));
    }
}
