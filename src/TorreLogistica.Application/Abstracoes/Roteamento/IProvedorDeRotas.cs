using TorreLogistica.Domain.Comum;
using TorreLogistica.Domain.Previsao;

namespace TorreLogistica.Application.Abstracoes.Roteamento;

/// <summary>
/// Fronteira com quem sabe quanto tempo leva ir de um ponto a outro — o <c>IRoutingProvider</c> do
/// CLAUDE.md, seção 24.
/// </summary>
/// <remarks>
/// <para>
/// O provedor responde só pelo deslocamento. Paradas anteriores, tempo de atendimento, janela e situação
/// do SLA são do serviço interno de previsão: trocar o provedor não muda como a previsão é composta nem
/// explicada.
/// </para>
/// <para>
/// O provedor pode demorar, falhar ou não existir. Quem chama impõe tempo limite e cai numa contingência
/// explícita, registrada na previsão — nenhuma dessas falhas derruba a operação.
/// </para>
/// </remarks>
public interface IProvedorDeRotas
{
    /// <summary>Nome configurável do provedor, registrado em cada previsão que ele produzir.</summary>
    string Nome { get; }

    /// <summary>
    /// Estima o deslocamento entre pontos consecutivos: um trecho a menos que a quantidade de pontos, na
    /// mesma ordem.
    /// </summary>
    Task<IReadOnlyList<TrechoDeTrajeto>> EstimarTrajetoAsync(IReadOnlyList<CoordenadaGeografica> pontos, CancellationToken cancelamento);
}
