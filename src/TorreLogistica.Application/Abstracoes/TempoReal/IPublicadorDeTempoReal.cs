using TorreLogistica.Domain.Alertas;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Previsao;

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

/// <summary>A situação do SLA de uma entrega mudou (<c>DeliveryRiskChanged</c>).</summary>
/// <remarks>
/// Leva a regra que decidiu e a sequência no histórico de previsões; a explicação completa vem da API.
/// </remarks>
public sealed record RiscoDaEntregaAlterado(
    Guid OrganizacaoId,
    Guid EntregaId,
    SituacaoDoSla? SituacaoAnterior,
    SituacaoDoSla Situacao,
    MotivoDaSituacao Motivo,
    DateTimeOffset? ChegadaPrevistaEm,
    int? FolgaEmSegundos,
    int Sequencia,
    DateTimeOffset OcorridoEm) : NotificacaoDaOperacao(OrganizacaoId);

/// <summary>Um alerta foi aberto ou reaberto (<c>AlertCreated</c>).</summary>
/// <remarks>Sem evidência: a descrição e a evidência vêm da API, com a autorização de quem consulta.</remarks>
public sealed record AlertaCriado(
    Guid OrganizacaoId,
    Guid AlertaId,
    TipoDeAlerta Tipo,
    SeveridadeDoAlerta Severidade,
    Guid? EntregaId,
    Guid? MotoristaId,
    Guid? RotaId,
    bool Reaberto,
    int Sequencia,
    DateTimeOffset OcorridoEm) : NotificacaoDaOperacao(OrganizacaoId);

/// <summary>Um alerta foi resolvido (<c>AlertResolved</c>).</summary>
public sealed record AlertaResolvido(
    Guid OrganizacaoId,
    Guid AlertaId,
    TipoDeAlerta Tipo,
    FormaDeResolucao Forma,
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
