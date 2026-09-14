namespace TorreLogistica.Domain.Rotas;

/// <summary>
/// Um fato da timeline da rota: inclusão, retirada e reordenação de paradas, atribuições,
/// planejamento e cancelamento.
/// </summary>
/// <remarks>
/// Mesmo desenho da timeline da entrega (ADR 0012): somente-inserção garantida pelo banco,
/// sequência sem lacuna e status resultante. É aqui que "a ordem das paradas é registrada quando
/// alterada": cada reordenação guarda a ordem anterior, a nova e a versão da ordem.
/// <see cref="Dados"/> leva só identificadores e códigos — nunca nome ou endereço.
/// </remarks>
public sealed class EventoDaRota
{
    private EventoDaRota()
    {
        Dados = "{}";
    }

    /// <summary>Identificador (UUIDv7).</summary>
    public Guid Id { get; private set; }

    /// <summary>Organização dona da rota.</summary>
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Rota do evento.</summary>
    public Guid RotaId { get; private set; }

    /// <summary>Posição do evento na timeline, a partir de 1, sem lacunas.</summary>
    public int Sequencia { get; private set; }

    /// <summary>O que aconteceu.</summary>
    public TipoDeEventoDaRota Tipo { get; private set; }

    /// <summary>Status da rota logo depois do evento.</summary>
    public StatusDaRota StatusResultante { get; private set; }

    /// <summary>Conta que causou o evento.</summary>
    public Guid? AutorUsuarioId { get; private set; }

    /// <summary>Detalhes em JSON, sem dado pessoal.</summary>
    public string Dados { get; private set; }

    /// <summary>Instante do evento, em UTC.</summary>
    public DateTimeOffset OcorridoEm { get; private set; }

    internal static EventoDaRota Registrar(
        Guid id,
        Guid organizacaoId,
        Guid rotaId,
        int sequencia,
        TipoDeEventoDaRota tipo,
        StatusDaRota statusResultante,
        Guid? autorUsuarioId,
        string dadosEmJson,
        DateTimeOffset ocorridoEm) => new()
        {
            Id = id,
            OrganizacaoId = organizacaoId,
            RotaId = rotaId,
            Sequencia = sequencia,
            Tipo = tipo,
            StatusResultante = statusResultante,
            AutorUsuarioId = autorUsuarioId,
            Dados = dadosEmJson,
            OcorridoEm = ocorridoEm.ToUniversalTime(),
        };
}
