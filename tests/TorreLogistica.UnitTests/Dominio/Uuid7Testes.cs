using TorreLogistica.Domain.Abstracoes.Identificadores;

namespace TorreLogistica.UnitTests.Dominio;

public sealed class Uuid7Testes
{
    [Fact]
    public void IdentificadorCriadoPelaPlataformaEhVersaoSete()
    {
        var identificador = Guid.CreateVersion7();

        Assert.Equal(7, Uuid7.Versao(identificador));
        Assert.True(Uuid7.EhUuidV7(identificador));
    }

    [Fact]
    public void IdentificadorVersaoQuatroNaoEhAceitoComoVersaoSete()
    {
        var identificador = Guid.NewGuid();

        Assert.NotEqual(7, Uuid7.Versao(identificador));
        Assert.False(Uuid7.EhUuidV7(identificador));
    }

    [Fact]
    public void IdentificadorVazioNaoEhVersaoSete()
    {
        Assert.False(Uuid7.EhUuidV7(Guid.Empty));
    }

    [Fact]
    public void InstanteDeCriacaoEhPreservadoComPrecisaoDeMilissegundo()
    {
        var instante = new DateTimeOffset(2026, 3, 14, 9, 26, 53, 589, TimeSpan.Zero);

        var identificador = Guid.CreateVersion7(instante);

        Assert.Equal(instante, Uuid7.ExtrairInstanteDeCriacao(identificador));
    }

    [Fact]
    public void ExtrairInstanteRecusaIdentificadorQueNaoEhVersaoSete()
    {
        Assert.Throws<ArgumentException>(() => Uuid7.ExtrairInstanteDeCriacao(Guid.NewGuid()));
    }

    /// <summary>
    /// A razão de existir do UUIDv7: identificadores criados depois ordenam depois.
    /// É isso que evita fragmentação de índice em tabelas que só crescem, como
    /// <c>posicoes</c> e <c>eventos_da_entrega</c>.
    /// </summary>
    [Fact]
    public void IdentificadoresOrdenamNaMesmaSequenciaEmQueForamCriados()
    {
        var inicio = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var identificadores = Enumerable.Range(0, 50)
            .Select(passo => Guid.CreateVersion7(inicio.AddMilliseconds(passo)))
            .ToList();

        var ordenadosPorTexto = identificadores
            .OrderBy(identificador => identificador.ToString("n"), StringComparer.Ordinal)
            .ToList();

        Assert.Equal(identificadores, ordenadosPorTexto);
    }
}
