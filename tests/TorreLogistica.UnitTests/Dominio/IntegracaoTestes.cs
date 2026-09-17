using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Integracoes;

namespace TorreLogistica.UnitTests.Dominio;

public sealed class IntegracaoTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 17, 9, 0, 0, TimeSpan.Zero);

    private readonly Guid _organizacao = Guid.CreateVersion7();
    private readonly Guid _autor = Guid.CreateVersion7();

    [Fact]
    public void EmiteCredencialAtivaComNomeAparado()
    {
        var integracao = Emitir(nome: "  ERP do cliente  ");

        Assert.Equal("ERP do cliente", integracao.Nome);
        Assert.Equal(_organizacao, integracao.OrganizacaoId);
        Assert.Equal(_autor, integracao.AutorUsuarioId);
        Assert.Equal(Agora, integracao.CriadaEm);
        Assert.True(integracao.EstaAtiva);
        Assert.Null(integracao.RevogadaEm);
        Assert.Null(integracao.UltimoUsoEm);
    }

    [Fact]
    public void NomeEhObrigatorio() =>
        Assert.Equal("nome_invalido", Assert.Throws<ExcecaoDeDominio>(() => Emitir(nome: "   ")).Codigo);

    [Fact]
    public void HashForaDeTrintaEDoisBytesEhRecusado() =>
        Assert.Equal(
            "hash_do_segredo_invalido",
            Assert.Throws<ExcecaoDeDominio>(() => Emitir(hash: new byte[31])).Codigo);

    [Fact]
    public void IdentificadorPublicoForaDoTamanhoEhRecusado()
    {
        Assert.Equal(
            "identificador_publico_invalido",
            Assert.Throws<ExcecaoDeDominio>(() => Emitir(identificador: "curto")).Codigo);

        Assert.Equal(
            "identificador_publico_invalido",
            Assert.Throws<ExcecaoDeDominio>(() => Emitir(identificador: "   ")).Codigo);
    }

    [Fact]
    public void RevogarEhIdempotenteEGuardaQuemRevogou()
    {
        var integracao = Emitir();
        var revogador = Guid.CreateVersion7();

        integracao.Revogar(revogador, Agora.AddHours(1));
        integracao.Revogar(Guid.CreateVersion7(), Agora.AddHours(2));

        Assert.False(integracao.EstaAtiva);
        Assert.Equal(Agora.AddHours(1), integracao.RevogadaEm);
        Assert.Equal(revogador, integracao.RevogadaPorUsuarioId);
    }

    /// <summary>
    /// Gravar o uso a cada requisição faria de uma linha o ponto de contenção de todo o tráfego da
    /// integração; o minuto basta para responder "esta credencial ainda é usada?".
    /// </summary>
    [Fact]
    public void UsoEhAnotadoNoMaximoUmaVezPorMinuto()
    {
        var integracao = Emitir();

        Assert.True(integracao.RegistrarUso(Agora));
        Assert.Equal(Agora, integracao.UltimoUsoEm);

        Assert.False(integracao.RegistrarUso(Agora.AddSeconds(30)));
        Assert.Equal(Agora, integracao.UltimoUsoEm);

        Assert.True(integracao.RegistrarUso(Agora.AddSeconds(61)));
        Assert.Equal(Agora.AddSeconds(61), integracao.UltimoUsoEm);
    }

    private Integracao Emitir(string? nome = "ERP do cliente", byte[]? hash = null, string? identificador = null) =>
        Integracao.Emitir(
            Guid.CreateVersion7(),
            _organizacao,
            nome,
            identificador ?? new string('a', PoliticaDeIntegracao.TamanhoDoIdentificador),
            hash ?? new byte[PoliticaDeIntegracao.TamanhoDoHashEmBytes],
            _autor,
            Agora);
}

public sealed class RequisicaoDeIntegracaoTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 17, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RegistraOProcessamentoComAChaveAparada()
    {
        var requisicao = Registrar(chave: "  pedido-4821  ");

        Assert.Equal("pedido-4821", requisicao.Chave);
        Assert.Equal("entrega", requisicao.Recurso);
        Assert.Equal(Agora, requisicao.ProcessadaEm);
    }

    [Fact]
    public void ChaveEhObrigatoria() =>
        Assert.Equal(
            "chave_de_idempotencia_invalida",
            Assert.Throws<ExcecaoDeDominio>(() => Registrar(chave: "   ")).Codigo);

    [Fact]
    public void ChaveAcimaDoLimiteEhRecusada() =>
        Assert.Equal(
            "chave_de_idempotencia_invalida",
            Assert.Throws<ExcecaoDeDominio>(() =>
                Registrar(chave: new string('k', PoliticaDeIntegracao.TamanhoMaximoDaChaveDeIdempotencia + 1))).Codigo);

    [Fact]
    public void HashForaDeTrintaEDoisBytesEhRecusado() =>
        Assert.Equal(
            "hash_da_requisicao_invalido",
            Assert.Throws<ExcecaoDeDominio>(() => Registrar(hash: new byte[16])).Codigo);

    private static RequisicaoDeIntegracao Registrar(string? chave = "pedido-4821", byte[]? hash = null) =>
        RequisicaoDeIntegracao.Registrar(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            chave,
            hash ?? new byte[PoliticaDeIntegracao.TamanhoDoHashEmBytes],
            "entrega",
            Guid.CreateVersion7(),
            Agora);
}

public sealed class ReferenciaExternaDaEntregaTestes
{
    [Fact]
    public void RegistraOVinculoComOIdentificadorAparado()
    {
        var referencia = ReferenciaExternaDaEntrega.Registrar(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "  PED-99  ",
            new DateTimeOffset(2026, 9, 17, 9, 0, 0, TimeSpan.Zero));

        Assert.Equal("PED-99", referencia.IdentificadorExterno);
    }

    [Fact]
    public void IdentificadorEhObrigatorio() =>
        Assert.Equal(
            "identificador_externo_invalido",
            Assert.Throws<ExcecaoDeDominio>(() => ReferenciaExternaDaEntrega.Registrar(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                "   ",
                DateTimeOffset.UtcNow)).Codigo);
}
