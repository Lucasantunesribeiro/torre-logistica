using TorreLogistica.Domain.Entregas;

namespace TorreLogistica.Application.Abstracoes.TempoReal;

/// <summary>Aviso de tempo real para o console de uma organização.</summary>
/// <param name="OrganizacaoId">Organização destinatária — define o grupo; nunca vem do cliente.</param>
public abstract record NotificacaoDaOperacao(Guid OrganizacaoId);

/// <summary>A posição atual de um motorista avançou (<c>DriverPositionUpdated</c>).</summary>
public sealed record PosicaoDoMotoristaAtualizada(
    Guid OrganizacaoId,
    Guid MotoristaId,
    Guid? RotaId,
    double Latitude,
    double Longitude,
    double PrecisaoEmMetros,
    double? VelocidadeEmMetrosPorSegundo,
    double? DirecaoEmGraus,
    long Sequencia,
    DateTimeOffset CapturadaEm) : NotificacaoDaOperacao(OrganizacaoId);

/// <summary>O status de uma entrega mudou (<c>DeliveryStatusChanged</c>).</summary>
public sealed record StatusDaEntregaAlterado(
    Guid OrganizacaoId,
    Guid EntregaId,
    StatusDaEntrega Status,
    TipoDeEventoDaEntrega Evento,
    int Sequencia,
    DateTimeOffset OcorridoEm) : NotificacaoDaOperacao(OrganizacaoId);

/// <summary>
/// Publicação de avisos de tempo real para o console operacional.
/// </summary>
/// <remarks>
/// <para>
/// Tempo real é transporte de aviso, nunca fonte de verdade (ADR 0005): quem perde um aviso
/// recupera o estado pela API. Por isso a publicação acontece só depois do commit e nunca derruba
/// a operação que a originou — falha de publicação é registrada, não propagada.
/// </para>
/// <para>
/// O domínio não conhece esta interface. Quem a chama é o contexto de persistência, no único ponto
/// em que se sabe que a mudança foi confirmada.
/// </para>
/// </remarks>
public interface IPublicadorDeTempoReal
{
    /// <summary>Publica os avisos, cada um só para a própria organização.</summary>
    Task PublicarAsync(IReadOnlyList<NotificacaoDaOperacao> notificacoes, CancellationToken cancelamento);

    /// <summary>
    /// Encerra as conexões abertas pelas sessões revogadas. Sem isto, quem saiu ou foi desativado
    /// continuaria recebendo dados da organização até o token vencer.
    /// </summary>
    Task EncerrarConexoesDasSessoesAsync(IReadOnlyCollection<Guid> sessoes, CancellationToken cancelamento);
}
