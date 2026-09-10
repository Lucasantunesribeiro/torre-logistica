using TorreLogistica.Api.Erros;
using TorreLogistica.Domain.Abstracoes.Erros;

namespace TorreLogistica.UnitTests.Api;

public sealed class ContratoDeErrosTestes
{
    [Theory]
    [InlineData(CategoriaDeErroDeDominio.RegraViolada, 422)]
    [InlineData(CategoriaDeErroDeDominio.Conflito, 409)]
    [InlineData(CategoriaDeErroDeDominio.NaoEncontrado, 404)]
    public void CategoriaDeErroTemStatusHttpEstavel(
        CategoriaDeErroDeDominio categoria,
        int statusEsperado)
    {
        Assert.Equal(statusEsperado, MapeamentoDeErrosDeDominio.ParaStatusHttp(categoria));
    }

    /// <summary>
    /// Uma categoria nova não pode virar 200 por descuido: o padrão do mapeamento é
    /// falhar como erro do servidor, e o teste de cobertura abaixo obriga a decidir.
    /// </summary>
    [Fact]
    public void CategoriaDesconhecidaCaiEmErroDoServidor()
    {
        var inexistente = (CategoriaDeErroDeDominio)999;

        Assert.Equal(500, MapeamentoDeErrosDeDominio.ParaStatusHttp(inexistente));
    }

    [Fact]
    public void TodaCategoriaDeclaradaTemStatusEspecifico()
    {
        foreach (var categoria in Enum.GetValues<CategoriaDeErroDeDominio>())
        {
            var status = MapeamentoDeErrosDeDominio.ParaStatusHttp(categoria);

            Assert.InRange(status, 400, 499);
        }
    }

    [Fact]
    public void TodaCategoriaDeclaradaTemTituloProprio()
    {
        var titulos = Enum.GetValues<CategoriaDeErroDeDominio>()
            .Select(MapeamentoDeErrosDeDominio.ParaTitulo)
            .ToList();

        Assert.All(titulos, titulo => Assert.False(string.IsNullOrWhiteSpace(titulo)));
        Assert.Equal(titulos.Count, titulos.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void TipoDoProblemaDerivaDoCodigoDoErro()
    {
        Assert.Equal(
            "urn:torre-logistica:erro:entrega_ja_concluida",
            MapeamentoDeErrosDeDominio.ParaTipo("entrega_ja_concluida"));
    }

    [Fact]
    public void TipoDoProblemaRecusaCodigoVazio()
    {
        Assert.Throws<ArgumentException>(() => MapeamentoDeErrosDeDominio.ParaTipo("  "));
    }
}
