using TorreLogistica.Domain.Abstracoes.Erros;

namespace TorreLogistica.UnitTests.Dominio;

public sealed class ExcecaoDeDominioTestes
{
    [Fact]
    public void RegraVioladaCarregaCodigoECategoria()
    {
        var erro = ExcecaoDeDominio.RegraViolada(
            "motorista_inativo",
            "Motorista inativo não recebe nova atribuição.");

        Assert.Equal("motorista_inativo", erro.Codigo);
        Assert.Equal(CategoriaDeErroDeDominio.RegraViolada, erro.Categoria);
        Assert.Equal("Motorista inativo não recebe nova atribuição.", erro.Message);
    }

    [Fact]
    public void ConflitoUsaCategoriaDeConflito()
    {
        var erro = ExcecaoDeDominio.Conflito("versao_desatualizada", "O registro mudou.");

        Assert.Equal(CategoriaDeErroDeDominio.Conflito, erro.Categoria);
    }

    [Fact]
    public void NaoEncontradoUsaCategoriaDeNaoEncontrado()
    {
        var erro = ExcecaoDeDominio.NaoEncontrado("entrega_inexistente", "Entrega não encontrada.");

        Assert.Equal(CategoriaDeErroDeDominio.NaoEncontrado, erro.Categoria);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CodigoVazioEhRecusado(string codigo)
    {
        Assert.Throws<ArgumentException>(() =>
            ExcecaoDeDominio.RegraViolada(codigo, "mensagem válida"));
    }

    [Fact]
    public void MensagemVaziaEhRecusada()
    {
        Assert.Throws<ArgumentException>(() =>
            ExcecaoDeDominio.RegraViolada("codigo_valido", "   "));
    }

    [Fact]
    public void LancarSeNaoLancaQuandoCondicaoEhFalsa()
    {
        ExcecaoDeDominio.LancarSe(false, "nunca", "não deveria lançar");
    }

    [Fact]
    public void LancarSeLancaQuandoCondicaoEhVerdadeira()
    {
        var erro = Assert.Throws<ExcecaoDeDominio>(() =>
            ExcecaoDeDominio.LancarSe(true, "rota_concluida", "Rota concluída não aceita alteração."));

        Assert.Equal("rota_concluida", erro.Codigo);
    }
}
