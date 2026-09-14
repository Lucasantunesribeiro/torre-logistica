using System.Collections.Frozen;

namespace TorreLogistica.Domain.Entregas;

/// <summary>
/// Estados de uma entrega.
/// </summary>
/// <remarks>
/// A lista é a do CLAUDE.md (seção 10) desde já, para que as tabelas de regra abaixo sejam
/// completas. Nesta fase só existem as transições para <see cref="Criada"/> e
/// <see cref="Cancelada"/>; planejar, atribuir, sair para rota e concluir nascem nas fases
/// seguintes, cada uma com a sua operação de domínio — nunca por atribuição de status.
/// </remarks>
public enum StatusDaEntrega
{
    /// <summary>Recebida, ainda sem planejamento.</summary>
    Criada = 1,

    /// <summary>Incluída em planejamento.</summary>
    Planejada = 2,

    /// <summary>Com motorista definido.</summary>
    Atribuida = 3,

    /// <summary>Motorista saiu para rota.</summary>
    EmRota = 4,

    /// <summary>Motorista próximo do destino.</summary>
    ProximaDoDestino = 5,

    /// <summary>Concluída com sucesso.</summary>
    Entregue = 6,

    /// <summary>Tentativa sem sucesso.</summary>
    TentativaFrustrada = 7,

    /// <summary>Nova tentativa marcada.</summary>
    Reagendada = 8,

    /// <summary>Cancelada.</summary>
    Cancelada = 9,
}

/// <summary>Motivo informado no cancelamento.</summary>
public enum MotivoDeCancelamento
{
    /// <summary>O cliente pediu.</summary>
    SolicitacaoDoCliente = 1,

    /// <summary>Endereço incorreto ou inexistente.</summary>
    EnderecoIncorreto = 2,

    /// <summary>Entrega registrada em duplicidade.</summary>
    CadastroDuplicado = 3,

    /// <summary>Outro motivo, descrito em texto.</summary>
    Outro = 4,
}

/// <summary>Tipos de evento da timeline da entrega.</summary>
public enum TipoDeEventoDaEntrega
{
    /// <summary>Entrega criada.</summary>
    Criada = 1,

    /// <summary>Dados da entrega alterados.</summary>
    DadosAlterados = 2,

    /// <summary>Entrega cancelada.</summary>
    Cancelada = 3,
}

/// <summary>Nomes dos campos alteráveis, usados nas regras, na timeline e na auditoria.</summary>
public static class CamposDaEntrega
{
    /// <summary>Cliente contratante.</summary>
    public const string Cliente = "cliente";

    /// <summary>Destinatário.</summary>
    public const string Destinatario = "destinatario";

    /// <summary>Endereço de entrega.</summary>
    public const string Endereco = "endereco";

    /// <summary>Coordenada de destino.</summary>
    public const string Localizacao = "localizacao";

    /// <summary>Janela prometida.</summary>
    public const string JanelaPrometida = "janelaPrometida";

    /// <summary>Observações operacionais.</summary>
    public const string Observacoes = "observacoes";

    /// <summary>Todos os campos alteráveis.</summary>
    public static IReadOnlyList<string> Todos { get; } =
        [Cliente, Destinatario, Endereco, Localizacao, JanelaPrometida, Observacoes];
}

/// <summary>
/// Tabelas de regra da entrega: o que pode mudar e de onde se pode cancelar, por status.
/// </summary>
/// <remarks>
/// <para>
/// Ficam em tabela, e não espalhadas em <c>if</c> pelos métodos, para que a regra inteira
/// seja lida num lugar só e testada para todos os status — inclusive os que só passam a ser
/// alcançáveis em fases futuras.
/// </para>
/// <para>
/// A ideia central: depois que a entrega sai para rota, o que o motorista está executando
/// não muda por baixo dele. Cliente congela ainda antes, no planejamento, porque a entrega
/// passa a compor o compromisso assumido com aquele cliente.
/// </para>
/// </remarks>
public static class RegrasDaEntrega
{
    private static readonly FrozenSet<StatusDaEntrega> AntesDaSaidaParaRota = new[]
    {
        StatusDaEntrega.Criada,
        StatusDaEntrega.Planejada,
        StatusDaEntrega.Atribuida,
        StatusDaEntrega.Reagendada,
    }.ToFrozenSet();

    private static readonly FrozenSet<StatusDaEntrega> Cancelaveis = new[]
    {
        StatusDaEntrega.Criada,
        StatusDaEntrega.Planejada,
        StatusDaEntrega.Atribuida,
        StatusDaEntrega.TentativaFrustrada,
        StatusDaEntrega.Reagendada,
    }.ToFrozenSet();

    /// <summary>Status do qual a entrega não sai mais.</summary>
    public static bool EhFinal(StatusDaEntrega status) =>
        status is StatusDaEntrega.Entregue or StatusDaEntrega.Cancelada;

    /// <summary>
    /// Pode cancelar. Com motorista em rota, não: cancelamento em movimento exige tratar a
    /// carga que está no veículo, assunto das fases de rota e ocorrência.
    /// </summary>
    public static bool PermiteCancelamento(StatusDaEntrega status) => Cancelaveis.Contains(status);

    /// <summary>O campo pode mudar com a entrega neste status.</summary>
    public static bool CampoEditavel(string campo, StatusDaEntrega status) => campo switch
    {
        CamposDaEntrega.Cliente => status == StatusDaEntrega.Criada,
        CamposDaEntrega.Destinatario
            or CamposDaEntrega.Endereco
            or CamposDaEntrega.Localizacao
            or CamposDaEntrega.JanelaPrometida => AntesDaSaidaParaRota.Contains(status),
        CamposDaEntrega.Observacoes => !EhFinal(status),
        _ => throw new ArgumentOutOfRangeException(nameof(campo), campo, "Campo de entrega desconhecido."),
    };
}
