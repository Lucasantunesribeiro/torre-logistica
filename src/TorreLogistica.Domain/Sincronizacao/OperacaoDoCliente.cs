using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Entregas;

namespace TorreLogistica.Domain.Sincronizacao;

/// <summary>Ações do motorista que podem nascer sem conexão e ser enviadas depois.</summary>
public enum TipoDeOperacaoDoCliente
{
    /// <summary>Saída para a rota.</summary>
    IniciarRota = 1,

    /// <summary>Chegada ao destino.</summary>
    RegistrarChegada = 2,

    /// <summary>Entrega feita.</summary>
    ConcluirEntrega = 3,

    /// <summary>Tentativa sem sucesso, com motivo.</summary>
    RegistrarTentativaFrustrada = 4,

    /// <summary>Encerramento da rota.</summary>
    ConcluirRota = 5,
}

/// <summary>Desfecho definitivo de uma operação do aparelho.</summary>
public enum ResultadoDaOperacaoDoCliente
{
    /// <summary>Aplicada — inclusive quando o efeito já estava aplicado por outro caminho.</summary>
    Aplicada = 1,

    /// <summary>O estado mudou enquanto o aparelho estava sem conexão, e a operação não vale mais.</summary>
    Conflito = 2,

    /// <summary>Recusada por regra: alvo que não é do motorista, operação inválida.</summary>
    Recusada = 3,
}

/// <summary>Limites das operações que chegam do aparelho.</summary>
public static class PoliticaDeOperacaoDoCliente
{
    /// <summary>Operações num único envio.</summary>
    public const int TamanhoMaximoDoLote = 100;

    /// <summary>Tamanho máximo da mensagem guardada com o desfecho.</summary>
    public const int TamanhoMaximoDaMensagem = 280;

    /// <summary>Horizonte offline: operação criada há mais tempo que isto é recusada.</summary>
    public static readonly TimeSpan IdadeMaxima = TimeSpan.FromDays(7);

    /// <summary>Quanto o relógio do aparelho pode estar adiantado.</summary>
    public static readonly TimeSpan ToleranciaDeRelogioDoAparelho = TimeSpan.FromMinutes(2);

    /// <summary>O alvo da operação é uma rota (e não uma entrega).</summary>
    public static bool AlvoEhRota(TipoDeOperacaoDoCliente tipo) =>
        tipo is TipoDeOperacaoDoCliente.IniciarRota or TipoDeOperacaoDoCliente.ConcluirRota;
}

/// <summary>
/// Registro de uma operação do aparelho, pelo identificador que ela recebeu ao nascer
/// (<c>ClientOperationId</c>, CLAUDE.md, seção 30).
/// </summary>
/// <remarks>
/// <para>
/// É o que garante "exatamente uma vez": o registro é gravado <b>na mesma transação</b> do efeito. Se a
/// resposta se perder e o aparelho repetir, o registro existe e devolve o mesmo desfecho, sem executar de
/// novo. Duas repetições simultâneas não passam juntas: o índice único decide.
/// </para>
/// <para>
/// Conflito e recusa também são definitivos e ficam registrados: repetir uma conclusão recusada porque a
/// entrega foi cancelada devolve o mesmo conflito, e não uma nova tentativa contra um estado que pode ter
/// mudado de novo. Somente-inserção.
/// </para>
/// </remarks>
public sealed class OperacaoDoCliente
{
    private OperacaoDoCliente()
    {
    }

    /// <summary>Identificador do registro (UUIDv7 do servidor).</summary>
    public Guid Id { get; private set; }

    /// <summary>Organização.</summary>
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Motorista da sessão que enviou.</summary>
    public Guid MotoristaId { get; private set; }

    /// <summary>Identificador gerado no aparelho (UUIDv7).</summary>
    public Guid OperacaoDoClienteId { get; private set; }

    /// <summary>Tipo.</summary>
    public TipoDeOperacaoDoCliente Tipo { get; private set; }

    /// <summary>Rota ou entrega.</summary>
    public Guid AlvoId { get; private set; }

    /// <summary>Motivo, na tentativa sem sucesso.</summary>
    public MotivoDeTentativaFrustrada? Motivo { get; private set; }

    /// <summary>Descrição do motivo, quando o aparelho a enviou.</summary>
    public string? Observacao { get; private set; }

    /// <summary>Quando o motorista fez a ação no aparelho.</summary>
    public DateTimeOffset CriadaNoAparelhoEm { get; private set; }

    /// <summary>Quando o servidor processou.</summary>
    public DateTimeOffset RecebidaEm { get; private set; }

    /// <summary>Desfecho.</summary>
    public ResultadoDaOperacaoDoCliente Resultado { get; private set; }

    /// <summary>Código do conflito ou da recusa.</summary>
    public string? Codigo { get; private set; }

    /// <summary>Mensagem do conflito ou da recusa.</summary>
    public string? Mensagem { get; private set; }

    /// <summary>Confere a operação como chegou do aparelho.</summary>
    public static void Validar(
        Guid operacaoDoClienteId,
        TipoDeOperacaoDoCliente tipo,
        Guid alvoId,
        MotivoDeTentativaFrustrada? motivo,
        string? observacao,
        DateTimeOffset criadaNoAparelhoEm,
        DateTimeOffset recebidaEm)
    {
        ExcecaoDeDominio.LancarSe(
            observacao is not null && tipo != TipoDeOperacaoDoCliente.RegistrarTentativaFrustrada,
            "observacao_nao_se_aplica",
            "Só a tentativa sem sucesso leva descrição.");
        ExcecaoDeDominio.LancarSe(
            operacaoDoClienteId == Guid.Empty || operacaoDoClienteId.Version != 7,
            "identificador_de_operacao_invalido",
            "O identificador da operação precisa ser um UUIDv7 gerado no aparelho.");
        ExcecaoDeDominio.LancarSe(!Enum.IsDefined(tipo), "tipo_de_operacao_invalido", "Tipo de operação desconhecido.");
        ExcecaoDeDominio.LancarSe(alvoId == Guid.Empty, "alvo_invalido", "Informe a rota ou a entrega da operação.");

        if (tipo == TipoDeOperacaoDoCliente.RegistrarTentativaFrustrada)
        {
            ExcecaoDeDominio.LancarSe(motivo is null || !Enum.IsDefined(motivo.Value), "motivo_obrigatorio", "Informe o motivo da tentativa sem sucesso.");
        }
        else
        {
            ExcecaoDeDominio.LancarSe(motivo is not null, "motivo_nao_se_aplica", "Só a tentativa sem sucesso tem motivo.");
        }

        var criada = criadaNoAparelhoEm.ToUniversalTime();
        var recebida = recebidaEm.ToUniversalTime();

        ExcecaoDeDominio.LancarSe(
            criada > recebida + PoliticaDeOperacaoDoCliente.ToleranciaDeRelogioDoAparelho,
            "operacao_no_futuro",
            "A operação diz ter sido feita no futuro. Confira o relógio do aparelho.");
        ExcecaoDeDominio.LancarSe(
            criada < recebida - PoliticaDeOperacaoDoCliente.IdadeMaxima,
            "operacao_antiga",
            "A operação foi feita há mais de 7 dias e não pode mais ser aplicada.");
    }

    /// <summary>Registra o desfecho de uma operação válida.</summary>
    public static OperacaoDoCliente Registrar(
        Guid id,
        Guid organizacaoId,
        Guid motoristaId,
        Guid operacaoDoClienteId,
        TipoDeOperacaoDoCliente tipo,
        Guid alvoId,
        MotivoDeTentativaFrustrada? motivo,
        string? observacao,
        DateTimeOffset criadaNoAparelhoEm,
        DateTimeOffset recebidaEm,
        ResultadoDaOperacaoDoCliente resultado,
        string? codigo = null,
        string? mensagem = null)
    {
        ExcecaoDeDominio.LancarSe(
            id == Guid.Empty || organizacaoId == Guid.Empty || motoristaId == Guid.Empty,
            "identificador_invalido",
            "Identificador inválido.");
        ExcecaoDeDominio.LancarSe(!Enum.IsDefined(resultado), "resultado_invalido", "Resultado inválido.");
        ExcecaoDeDominio.LancarSe(
            (resultado == ResultadoDaOperacaoDoCliente.Aplicada) != string.IsNullOrWhiteSpace(codigo),
            "resultado_invalido",
            "Conflito e recusa exigem código; operação aplicada não tem.");
        Validar(operacaoDoClienteId, tipo, alvoId, motivo, observacao, criadaNoAparelhoEm, recebidaEm);

        return new OperacaoDoCliente
        {
            Observacao = observacao,
            Id = id,
            OrganizacaoId = organizacaoId,
            MotoristaId = motoristaId,
            OperacaoDoClienteId = operacaoDoClienteId,
            Tipo = tipo,
            AlvoId = alvoId,
            Motivo = motivo,
            CriadaNoAparelhoEm = criadaNoAparelhoEm.ToUniversalTime(),
            RecebidaEm = recebidaEm.ToUniversalTime(),
            Resultado = resultado,
            Codigo = codigo,
            Mensagem = mensagem is { Length: > PoliticaDeOperacaoDoCliente.TamanhoMaximoDaMensagem }
                ? mensagem[..PoliticaDeOperacaoDoCliente.TamanhoMaximoDaMensagem]
                : mensagem,
        };
    }

    /// <summary>
    /// A repetição pede o mesmo que o registro. Mesmo identificador com outro conteúdo é outra coisa, e não
    /// herda o desfecho.
    /// </summary>
    public bool MesmoPedido(TipoDeOperacaoDoCliente tipo, Guid alvoId, MotivoDeTentativaFrustrada? motivo, string? observacao) =>
        Tipo == tipo && AlvoId == alvoId && Motivo == motivo && string.Equals(Observacao, observacao, StringComparison.Ordinal);
}
