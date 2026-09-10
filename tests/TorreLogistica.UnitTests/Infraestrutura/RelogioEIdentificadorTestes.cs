using Microsoft.Extensions.Time.Testing;
using TorreLogistica.Domain.Abstracoes.Identificadores;
using TorreLogistica.Infrastructure.Identificadores;
using TorreLogistica.Infrastructure.Tempo;

namespace TorreLogistica.UnitTests.Infraestrutura;

public sealed class RelogioEIdentificadorTestes
{
    private static readonly DateTimeOffset InstanteFixo =
        new(2026, 5, 20, 13, 45, 30, TimeSpan.Zero);

    [Fact]
    public void RelogioDevolveOInstanteDoProvedorDeTempo()
    {
        var provedor = new FakeTimeProvider(InstanteFixo);
        var relogio = new RelogioDoSistema(provedor);

        Assert.Equal(InstanteFixo, relogio.AgoraUtc);
    }

    [Fact]
    public void RelogioAcompanhaOAvancoDoProvedorDeTempo()
    {
        var provedor = new FakeTimeProvider(InstanteFixo);
        var relogio = new RelogioDoSistema(provedor);

        provedor.Advance(TimeSpan.FromMinutes(17));

        Assert.Equal(InstanteFixo.AddMinutes(17), relogio.AgoraUtc);
    }

    [Fact]
    public void RelogioSempreRespondeEmUtc()
    {
        var provedor = new FakeTimeProvider(
            new DateTimeOffset(2026, 5, 20, 10, 0, 0, TimeSpan.FromHours(-3)));

        var relogio = new RelogioDoSistema(provedor);

        Assert.Equal(TimeSpan.Zero, relogio.AgoraUtc.Offset);
    }

    [Fact]
    public void GeradorProduzUuidV7AncoradoNoRelogio()
    {
        var relogio = new RelogioDoSistema(new FakeTimeProvider(InstanteFixo));
        var gerador = new GeradorDeIdentificadorUuidV7(relogio);

        var identificador = gerador.Novo();

        Assert.True(Uuid7.EhUuidV7(identificador));
        Assert.Equal(
            InstanteFixo.ToUnixTimeMilliseconds(),
            Uuid7.ExtrairInstanteDeCriacao(identificador).ToUnixTimeMilliseconds());
    }

    [Fact]
    public void GeradorNaoRepeteIdentificadorNemComORelogioParado()
    {
        var relogio = new RelogioDoSistema(new FakeTimeProvider(InstanteFixo));
        var gerador = new GeradorDeIdentificadorUuidV7(relogio);

        var identificadores = Enumerable.Range(0, 1_000)
            .Select(_ => gerador.Novo())
            .ToHashSet();

        Assert.Equal(1_000, identificadores.Count);
    }

    [Fact]
    public void GeradorNaoAceitaRelogioNulo()
    {
        Assert.Throws<ArgumentNullException>(() => new GeradorDeIdentificadorUuidV7(null!));
    }

    [Fact]
    public void RelogioNaoAceitaProvedorNulo()
    {
        Assert.Throws<ArgumentNullException>(() => new RelogioDoSistema(null!));
    }
}
