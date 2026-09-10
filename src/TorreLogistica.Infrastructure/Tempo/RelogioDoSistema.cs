using TorreLogistica.Domain.Abstracoes.Tempo;

namespace TorreLogistica.Infrastructure.Tempo;

/// <summary>
/// Relógio real, ancorado no <see cref="TimeProvider"/> da plataforma.
/// </summary>
/// <remarks>
/// Delegar ao <see cref="TimeProvider"/> em vez de chamar <c>DateTimeOffset.UtcNow</c>
/// permite que teste e simulador troquem a fonte de tempo sem trocar esta classe.
/// </remarks>
public sealed class RelogioDoSistema(TimeProvider provedorDeTempo) : IRelogio
{
    private readonly TimeProvider _provedorDeTempo =
        provedorDeTempo ?? throw new ArgumentNullException(nameof(provedorDeTempo));

    /// <inheritdoc />
    /// <remarks>
    /// O <c>ToUniversalTime</c> não é redundante: um <see cref="TimeProvider"/> pode
    /// devolver o instante com deslocamento diferente de zero, e deixar isso passar
    /// faria comparações e gravações herdarem um fuso que o contrato proíbe.
    /// </remarks>
    public DateTimeOffset AgoraUtc => _provedorDeTempo.GetUtcNow().ToUniversalTime();
}
