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
        Assert.True(operacao.MesmoPedido(TipoDeOperacaoDoCliente.ConcluirEntrega, alvo, null, null));
        Assert.False(operacao.MesmoPedido(TipoDeOperacaoDoCliente.RegistrarChegada, alvo, null, null));
        Assert.False(operacao.MesmoPedido(TipoDeOperacaoDoCliente.ConcluirEntrega, Guid.CreateVersion7(), null, null));
    }

    [Fact]
    public void TentativaGuardaOMotivoEADescricaoEOsDoisFazemParteDoPedido()
    {
        var alvo = Guid.CreateVersion7();
        var operacao = Registrar(
            Guid.CreateVersion7(),
            TipoDeOperacaoDoCliente.RegistrarTentativaFrustrada,
            alvo,
            MotivoDeTentativaFrustrada.LocalFechado,
            Agora,
            observacao: "Portão fechado, sem porteiro.");

        Assert.Equal("Portão fechado, sem porteiro.", operacao.Observacao);
        Assert.True(operacao.MesmoPedido(TipoDeOperacaoDoCliente.RegistrarTentativaFrustrada, alvo, MotivoDeTentativaFrustrada.LocalFechado, "Portão fechado, sem porteiro."));
        Assert.False(operacao.MesmoPedido(TipoDeOperacaoDoCliente.RegistrarTentativaFrustrada, alvo, MotivoDeTentativaFrustrada.DestinatarioAusente, "Portão fechado, sem porteiro."));

        // A mesma tentativa com outra descrição é outro pedido: não herda o desfecho registrado.
        Assert.False(operacao.MesmoPedido(TipoDeOperacaoDoCliente.RegistrarTentativaFrustrada, alvo, MotivoDeTentativaFrustrada.LocalFechado, "Outra coisa."));
    }

    public static TheoryData<Guid, TipoDeOperacaoDoCliente, MotivoDeTentativaFrustrada?, string?, int, string> Invalidas() => new()
    {
        { Guid.Empty, TipoDeOperacaoDoCliente.ConcluirEntrega, null, null, 0, "identificador_de_operacao_invalido" },
        { Guid.NewGuid(), TipoDeOperacaoDoCliente.ConcluirEntrega, null, null, 0, "identificador_de_operacao_invalido" },
        { Guid.CreateVersion7(), (TipoDeOperacaoDoCliente)99, null, null, 0, "tipo_de_operacao_invalido" },
        { Guid.CreateVersion7(), TipoDeOperacaoDoCliente.RegistrarTentativaFrustrada, null, null, 0, "motivo_obrigatorio" },
        { Guid.CreateVersion7(), TipoDeOperacaoDoCliente.ConcluirEntrega, MotivoDeTentativaFrustrada.LocalFechado, null, 0, "motivo_nao_se_aplica" },
        { Guid.CreateVersion7(), TipoDeOperacaoDoCliente.ConcluirEntrega, null, "descrição solta", 0, "observacao_nao_se_aplica" },
        { Guid.CreateVersion7(), TipoDeOperacaoDoCliente.ConcluirEntrega, null, null, 3, "operacao_no_futuro" },
        { Guid.CreateVersion7(), TipoDeOperacaoDoCliente.ConcluirEntrega, null, null, -(7 * 24 * 60 + 1), "operacao_antiga" },
    };

    [Theory]
    [MemberData(nameof(Invalidas))]
    public void RecusaOperacaoInvalida(
        Guid id,
        TipoDeOperacaoDoCliente tipo,
        MotivoDeTentativaFrustrada? motivo,
        string? observacao,
        int minutosDeDiferenca,
        string codigo)
    {
        var erro = Assert.Throws<ExcecaoDeDominio>(() =>
            OperacaoDoCliente.Validar(id, tipo, Guid.CreateVersion7(), motivo, observacao, Agora.AddMinutes(minutosDeDiferenca), Agora));

        Assert.Equal(codigo, erro.Codigo);
    }

    [Fact]
    public void RelogioDoAparelhoAdiantadoDentroDaToleranciaEAceito() =>
        OperacaoDoCliente.Validar(Guid.CreateVersion7(), TipoDeOperacaoDoCliente.IniciarRota, Guid.CreateVersion7(), null, null, Agora.AddMinutes(2), Agora);

    [Fact]
    public void DesfechoNegativoExigeCodigoEAplicadaNaoTem()
    {
        Assert.Throws<ExcecaoDeDominio>(() => Registrar(Guid.CreateVersion7(), TipoDeOperacaoDoCliente.ConcluirEntrega, Guid.CreateVersion7(), null, Agora, resultado: ResultadoDaOperacaoDoCliente.Conflito));
        Assert.Throws<ExcecaoDeDominio>(() => Registrar(Guid.CreateVersion7(), TipoDeOperacaoDoCliente.ConcluirEntrega, Guid.CreateVersion7(), null, Agora, resultado: ResultadoDaOperacaoDoCliente.Aplicada, codigo: "x"));

        var conflito = Registrar(
            Guid.CreateVersion7(),
            TipoDeOperacaoDoCliente.ConcluirEntrega,
            Guid.CreateVersion7(),
            null,
            Agora,
            resultado: ResultadoDaOperacaoDoCliente.Conflito,
            codigo: "transicao_invalida",
            mensagem: new string('m', 400));
        Assert.Equal(PoliticaDeOperacaoDoCliente.TamanhoMaximoDaMensagem, conflito.Mensagem!.Length);
    }

    private static OperacaoDoCliente Registrar(
        Guid id,
        TipoDeOperacaoDoCliente tipo,
        Guid alvo,
        MotivoDeTentativaFrustrada? motivo,
        DateTimeOffset criadaEm,
        string? observacao = null,
        ResultadoDaOperacaoDoCliente resultado = ResultadoDaOperacaoDoCliente.Aplicada,
        string? codigo = null,
        string? mensagem = null) =>
        OperacaoDoCliente.Registrar(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            id,
            tipo,
            alvo,
            motivo,
            observacao,
            criadaEm,
            Agora,
            resultado,
            codigo,
            mensagem);
}
