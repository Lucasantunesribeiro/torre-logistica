using TorreLogistica.Api.Correlacao;

namespace TorreLogistica.UnitTests.Api;

public sealed class CorrelacaoHttpTestes
{
    [Theory]
    [InlineData("019712ab34cd7ef89012345678abcdef")]
    [InlineData("pedido-4821")]
    [InlineData("A.B_C-1")]
    public void IdentificadorBemFormadoEhAceito(string valor)
    {
        Assert.True(CorrelacaoHttp.EhIdentificadorAceitavel(valor));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IdentificadorAusenteNaoEhAceito(string? valor)
    {
        Assert.False(CorrelacaoHttp.EhIdentificadorAceitavel(valor));
    }

    /// <summary>
    /// O valor recebido volta no cabeçalho da resposta e entra em cada linha de log.
    /// Quebra de linha permitiria forjar uma linha de log inteira ou um cabeçalho
    /// extra; espaço, aspas e caracteres de controle abrem a mesma porta.
    /// </summary>
    [Theory]
    [InlineData("abc\r\nX-Injetado: sim")]
    [InlineData("abc\ndef")]
    [InlineData("com espaço")]
    [InlineData("aspas\"e'coisas")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("caminho/../../etc")]
    public void IdentificadorComCaractereDeRiscoEhRecusado(string valor)
    {
        Assert.False(CorrelacaoHttp.EhIdentificadorAceitavel(valor));
    }

    [Fact]
    public void IdentificadorNoLimiteDeTamanhoEhAceito()
    {
        var valor = new string('a', CorrelacaoHttp.TamanhoMaximo);

        Assert.True(CorrelacaoHttp.EhIdentificadorAceitavel(valor));
    }

    [Fact]
    public void IdentificadorAcimaDoLimiteDeTamanhoEhRecusado()
    {
        var valor = new string('a', CorrelacaoHttp.TamanhoMaximo + 1);

        Assert.False(CorrelacaoHttp.EhIdentificadorAceitavel(valor));
    }
}
