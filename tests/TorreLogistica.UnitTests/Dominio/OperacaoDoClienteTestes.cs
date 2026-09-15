using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Sincronizacao;

namespace TorreLogistica.UnitTests.Dominio;

public sealed class OperacaoDoClienteTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RegistraAOperacaoAplicadaComOIdentificadorDoAparelho()
    {
        var id = Guid.CreateVersion7();
        var alvo = Guid.CreateVersion7();

        var operacao = Registrar(id, TipoDeOperacaoDoCliente.ConcluirEntrega, alvo, null, Agora.AddHours(-3));

        Assert.Equal(id, operacao.OperacaoDoClienteId);
        Assert.Equal(ResultadoDaOperacaoDoCliente.Aplicada, operacao.Resultado);
        Assert.Null(operacao.Codigo);
        Assert.True(operacao.MesmoPedido(TipoDeOperacaoDoCliente.ConcluirEntrega, alvo, null));
        Assert.False(operacao.MesmoPedido(TipoDeOperacaoDoCliente.RegistrarChegada, alvo, null));
        Assert.False(operacao.MesmoPedido(TipoDeOperacaoDoCliente.ConcluirEntrega, Guid.CreateVersion7(), null));
    }

    [Fact]
    public void TentativaGuardaOMotivoEOMotivoFazParteDoPedido()
    {
        var alvo = Guid.CreateVersion7();
        var operacao = Registrar(Guid.CreateVersion7(), TipoDeOperacaoDoCliente.RegistrarTentativaFrustrada, alvo, MotivoDeTentativaFrustrada.LocalFechado, Agora);

        Assert.True(operacao.MesmoPedido(TipoDeOperacaoDoCliente.RegistrarTentativaFrustrada, alvo, MotivoDeTentativaFrustrada.LocalFechado));
        Assert.False(operacao.MesmoPedido(TipoDeOperacaoDoCliente.RegistrarTentativaFrustrada, alvo, MotivoDeTentativaFrustrada.DestinatarioAusente));
    }

    public static TheoryData<Guid, TipoDeOperacaoDoCliente, MotivoDeTentativaFrustrada?, int, string> Invalidas() => new()
    {
        { Guid.Empty, TipoDeOperacaoDoCliente.ConcluirEntrega, null, 0, "identificador_de_operacao_invalido" },
        { Guid.NewGuid(), TipoDeOperacaoDoCliente.ConcluirEntrega, null, 0, "identificador_de_operacao_invalido" },
        { Guid.CreateVersion7(), (TipoDeOperacaoDoCliente)99, null, 0, "tipo_de_operacao_invalido" },
        { Guid.CreateVersion7(), TipoDeOperacaoDoCliente.RegistrarTentativaFrustrada, null, 0, "motivo_obrigatorio" },
        { Guid.CreateVersion7(), TipoDeOperacaoDoCliente.ConcluirEntrega, MotivoDeTentativaFrustrada.LocalFechado, 0, "motivo_nao_se_aplica" },
        { Guid.CreateVersion7(), TipoDeOperacaoDoCliente.ConcluirEntrega, null, 3, "operacao_no_futuro" },
        { Guid.CreateVersion7(), TipoDeOperacaoDoCliente.ConcluirEntrega, null, -(7 * 24 * 60 + 1), "operacao_antiga" },
    };

    [Theory]
    [MemberData(nameof(Invalidas))]
    public void RecusaOperacaoInvalida(Guid id, TipoDeOperacaoDoCliente tipo, MotivoDeTentativaFrustrada? motivo, int minutosDeDiferenca, string codigo)
    {
        var erro = Assert.Throws<ExcecaoDeDominio>(() =>
            OperacaoDoCliente.Validar(id, tipo, Guid.CreateVersion7(), motivo, Agora.AddMinutes(minutosDeDiferenca), Agora));

        Assert.Equal(codigo, erro.Codigo);
    }

    [Fact]
    public void RelogioDoAparelhoAdiantadoDentroDaToleranciaEAceito() =>
        OperacaoDoCliente.Validar(Guid.CreateVersion7(), TipoDeOperacaoDoCliente.IniciarRota, Guid.CreateVersion7(), null, Agora.AddMinutes(2), Agora);

    [Fact]
    public void DesfechoNegativoExigeCodigoEAplicadaNaoTem()
    {
        Assert.Throws<ExcecaoDeDominio>(() => Registrar(Guid.CreateVersion7(), TipoDeOperacaoDoCliente.ConcluirEntrega, Guid.CreateVersion7(), null, Agora, ResultadoDaOperacaoDoCliente.Conflito));
        Assert.Throws<ExcecaoDeDominio>(() => Registrar(Guid.CreateVersion7(), TipoDeOperacaoDoCliente.ConcluirEntrega, Guid.CreateVersion7(), null, Agora, ResultadoDaOperacaoDoCliente.Aplicada, "x"));

        var conflito = Registrar(Guid.CreateVersion7(), TipoDeOperacaoDoCliente.ConcluirEntrega, Guid.CreateVersion7(), null, Agora, ResultadoDaOperacaoDoCliente.Conflito, "transicao_invalida", new string('m', 400));
        Assert.Equal(PoliticaDeOperacaoDoCliente.TamanhoMaximoDaMensagem, conflito.Mensagem!.Length);
    }

    private static OperacaoDoCliente Registrar(
        Guid id,
        TipoDeOperacaoDoCliente tipo,
        Guid alvo,
        MotivoDeTentativaFrustrada? motivo,
        DateTimeOffset criadaEm,
        ResultadoDaOperacaoDoCliente resultado = ResultadoDaOperacaoDoCliente.Aplicada,
        string? codigo = null,
        string? mensagem = null) =>
        OperacaoDoCliente.Registrar(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), id, tipo, alvo, motivo, criadaEm, Agora, resultado, codigo, mensagem);
}
