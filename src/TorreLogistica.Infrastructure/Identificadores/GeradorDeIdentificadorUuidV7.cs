using TorreLogistica.Domain.Abstracoes.Identificadores;
using TorreLogistica.Domain.Abstracoes.Tempo;

namespace TorreLogistica.Infrastructure.Identificadores;

/// <summary>
/// Gerador padrão de identificadores: UUIDv7 ancorado no relógio do sistema.
/// </summary>
/// <remarks>
/// Usar o mesmo relógio do resto do sistema mantém o identificador coerente com os
/// carimbos de tempo gravados na mesma operação, inclusive quando o simulador
/// acelera o tempo.
/// </remarks>
public sealed class GeradorDeIdentificadorUuidV7(IRelogio relogio) : IGeradorDeIdentificador
{
    private readonly IRelogio _relogio = relogio ?? throw new ArgumentNullException(nameof(relogio));

    /// <inheritdoc />
    public Guid Novo() => Guid.CreateVersion7(_relogio.AgoraUtc);
}
