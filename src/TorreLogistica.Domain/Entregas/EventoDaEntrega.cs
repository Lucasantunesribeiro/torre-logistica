namespace TorreLogistica.Domain.Entregas;

/// <summary>
/// Um fato da timeline da entrega.
/// </summary>
/// <remarks>
/// <para>
/// Somente-inserção (CLAUDE.md, seção 11): estado atual e histórico são coisas diferentes, e
/// o histórico nunca é reescrito para parecer com o estado atual. O banco recusa
/// <c>UPDATE</c>, <c>DELETE</c> e <c>TRUNCATE</c> nesta tabela.
/// </para>
/// <para>
/// Só a <see cref="Entrega"/> cria eventos, numerando-os em <see cref="Sequencia"/>. A
/// sequência dá ordem estável mesmo com dois eventos no mesmo instante, e o índice único
/// por entrega e sequência é a segunda barreira contra duas gravações concorrentes.
/// </para>
/// <para>
/// <see cref="Dados"/> nunca carrega endereço, nome ou telefone: registra nomes de campos e
/// códigos de motivo. A timeline é permanente; dado pessoal nela não poderia ser apagado.
/// </para>
/// </remarks>
public sealed class EventoDaEntrega
{
    private EventoDaEntrega()
    {
        Dados = "{}";
    }

    /// <summary>Identificador (UUIDv7).</summary>
    public Guid Id { get; private set; }

    /// <summary>Organização dona da entrega.</summary>
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Entrega do evento.</summary>
    public Guid EntregaId { get; private set; }

    /// <summary>Posição do evento na timeline, a partir de 1, sem lacunas.</summary>
    public int Sequencia { get; private set; }

    /// <summary>O que aconteceu.</summary>
    public TipoDeEventoDaEntrega Tipo { get; private set; }

    /// <summary>Status da entrega logo depois do evento.</summary>
    public StatusDaEntrega StatusResultante { get; private set; }

    /// <summary>Conta que causou o evento; vazio quando foi o sistema.</summary>
    public Guid? AutorUsuarioId { get; private set; }

    /// <summary>Detalhes em JSON, sem dado pessoal.</summary>
    public string Dados { get; private set; }

    /// <summary>Instante do evento, em UTC.</summary>
    public DateTimeOffset OcorridoEm { get; private set; }

    internal static EventoDaEntrega Registrar(
        Guid id,
        Guid organizacaoId,
        Guid entregaId,
        int sequencia,
        TipoDeEventoDaEntrega tipo,
        StatusDaEntrega statusResultante,
        Guid? autorUsuarioId,
        string dadosEmJson,
        DateTimeOffset ocorridoEm) => new()
        {
            Id = id,
            OrganizacaoId = organizacaoId,
            EntregaId = entregaId,
            Sequencia = sequencia,
            Tipo = tipo,
            StatusResultante = statusResultante,
            AutorUsuarioId = autorUsuarioId,
            Dados = dadosEmJson,
            OcorridoEm = ocorridoEm.ToUniversalTime(),
        };
}
