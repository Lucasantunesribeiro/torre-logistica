using Microsoft.EntityFrameworkCore;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Domain.Abstracoes.Tempo;
using TorreLogistica.Domain.Entregas;

namespace TorreLogistica.Application.Operacao;

/// <summary>
/// Pulso da operação: quando um motorista reportou posição pela última vez, agora.
/// </summary>
/// <param name="Agora">Relógio do servidor no momento da leitura.</param>
/// <param name="UltimaPosicaoRecebidaEm">
/// Recebimento da posição atual mais recente da organização, ou <see langword="null"/> se nenhum motorista
/// tem posição.
/// </param>
/// <param name="MotoristasComPosicao">Quantos motoristas têm posição atual registrada.</param>
/// <param name="EntregasEmAndamento">Entregas a caminho (<c>EmRota</c> ou <c>ProximaDoDestino</c>).</param>
public sealed record PulsoDaOperacao(
    DateTimeOffset Agora,
    DateTimeOffset? UltimaPosicaoRecebidaEm,
    int MotoristasComPosicao,
    int EntregasEmAndamento);

/// <summary>
/// Responde "a operação está viva agora?" com um dado só: a idade da informação mais recente.
/// </summary>
/// <remarks>
/// <para>
/// O console mostra "conectado" quando fala com a API — mas falar com a API não é o mesmo que ter operação
/// acontecendo. Numa demonstração sob demanda, a encenação termina e os dados envelhecem com a API no ar e
/// respondendo. Sem esta leitura, o visitante veria a luz verde da conexão e concluiria, errado, que está
/// vendo uma operação ao vivo.
/// </para>
/// <para>
/// O instante devolvido é o <see cref="PosicaoDoMotorista.RecebidaEm"/> da posição atual mais recente — o
/// relógio do servidor quando a posição chegou, não o do aparelho. A idade é a diferença para
/// <see cref="PulsoDaOperacao.Agora"/>, que também vem do servidor: comparar os dois no mesmo relógio evita
/// que o fuso ou o atraso do navegador do visitante invente envelhecimento que não existe.
/// </para>
/// <para>Só leitura, e só da organização da sessão — o filtro de tenant já recorta o contexto.</para>
/// </remarks>
public sealed class ConsultaDoPulsoDaOperacao(IContextoDePersistencia contexto, IRelogio relogio)
{
    /// <summary>Lê o pulso da organização da sessão.</summary>
    public async Task<PulsoDaOperacao> ObterAsync(CancellationToken cancelamento)
    {
        var posicoes = contexto.PosicoesAtuais.AsNoTracking();

        var motoristasComPosicao = await posicoes.CountAsync(cancelamento).ConfigureAwait(false);

        // Max sobre projeção anulável: num conjunto vazio o SQL MAX devolve NULL, então a consulta não
        // estoura quando ainda não há posição nenhuma — vira o null que o console sabe apresentar.
        var ultima = await posicoes
            .MaxAsync(posicao => (DateTimeOffset?)posicao.RecebidaEm, cancelamento)
            .ConfigureAwait(false);

        var emAndamento = await contexto.Entregas
            .AsNoTracking()
            .CountAsync(
                entrega => entrega.Status == StatusDaEntrega.EmRota
                    || entrega.Status == StatusDaEntrega.ProximaDoDestino,
                cancelamento)
            .ConfigureAwait(false);

        return new PulsoDaOperacao(relogio.AgoraUtc, ultima, motoristasComPosicao, emAndamento);
    }
}
