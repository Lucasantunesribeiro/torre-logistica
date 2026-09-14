namespace TorreLogistica.Domain.Rotas;

/// <summary>
/// Uma entrega na sequência de uma rota.
/// </summary>
/// <remarks>
/// <para>
/// A parada é a associação rota–entrega do ROADMAP: uma parada, uma entrega. Agrupar várias
/// entregas do mesmo endereço numa só parada é otimização de roteirização, fora do escopo da v1
/// (CLAUDE.md, seção 5); se vier, é uma parada com várias entregas, sem mudar quem a referencia.
/// </para>
/// <para>
/// Parada retirada não é apagada: fica inativa, com instante e motivo. A rota conta a própria
/// história — inclusive de onde cada entrega saiu.
/// </para>
/// </remarks>
public sealed class Parada
{
    private Parada()
    {
    }

    /// <summary>Identificador (UUIDv7).</summary>
    public Guid Id { get; private set; }

    /// <summary>Organização dona da rota.</summary>
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Rota da parada.</summary>
    public Guid RotaId { get; private set; }

    /// <summary>Entrega desta parada.</summary>
    public Guid EntregaId { get; private set; }

    /// <summary>Posição na rota, a partir de 1; na parada inativa, a última posição que teve.</summary>
    public int Sequencia { get; private set; }

    /// <summary>Faz parte da rota agora.</summary>
    public bool Ativa { get; private set; }

    /// <summary>Instante da inclusão.</summary>
    public DateTimeOffset AdicionadaEm { get; private set; }

    /// <summary>Instante da retirada.</summary>
    public DateTimeOffset? RemovidaEm { get; private set; }

    /// <summary>Por que saiu da rota.</summary>
    public MotivoDeRemocaoDeParada? MotivoDaRemocao { get; private set; }

    internal static Parada Criar(Guid id, Guid organizacaoId, Guid rotaId, Guid entregaId, int sequencia, DateTimeOffset instante) =>
        new()
        {
            Id = id,
            OrganizacaoId = organizacaoId,
            RotaId = rotaId,
            EntregaId = entregaId,
            Sequencia = sequencia,
            Ativa = true,
            AdicionadaEm = instante,
        };

    internal void MoverPara(int sequencia) => Sequencia = sequencia;

    internal void Desativar(MotivoDeRemocaoDeParada motivo, DateTimeOffset instante)
    {
        Ativa = false;
        RemovidaEm = instante;
        MotivoDaRemocao = motivo;
    }
}
