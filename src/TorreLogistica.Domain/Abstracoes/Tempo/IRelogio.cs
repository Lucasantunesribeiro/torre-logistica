namespace TorreLogistica.Domain.Abstracoes.Tempo;

/// <summary>
/// Fonte de tempo do sistema.
/// </summary>
/// <remarks>
/// Regras que dependem de tempo (SLA, ETA, motorista offline, timeline, simulador)
/// nunca devem chamar <c>DateTimeOffset.UtcNow</c> diretamente: sem esta fronteira o
/// comportamento deixa de ser determinístico e não há como testar o passar das horas.
/// Todo instante trafega em UTC; fuso local é assunto de apresentação.
/// </remarks>
public interface IRelogio
{
    /// <summary>Instante atual, sempre em UTC.</summary>
    DateTimeOffset AgoraUtc { get; }
}
