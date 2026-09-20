using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Application.Abstracoes.Webhooks;
using TorreLogistica.Domain.Abstracoes.Identificadores;
using TorreLogistica.Domain.Abstracoes.Tempo;
using TorreLogistica.Domain.Webhooks;

namespace TorreLogistica.Application.Webhooks;

/// <summary>
/// Entrega ao assinante o que o despachante preparou, com tentativa, backoff e desistência visível.
/// </summary>
/// <remarks>
/// <para>
/// A reserva e o envio ficam em transações <b>separadas</b> de propósito. Manter a transação aberta
/// durante o POST prenderia conexão e trava do banco pelo tempo do assinante — que pode ser dez segundos
/// de tempo limite, repetidos por lote. A reserva empurra a disponibilidade para a frente (um arrendamento)
/// e libera o banco; se o processo morrer no meio do envio, a entrega volta a ficar disponível sozinha
/// quando esse prazo vence.
/// </para>
/// <para>
/// Consequência aceita e documentada: um assinante pode receber o mesmo evento duas vezes se o processo
/// cair depois do POST e antes de gravar o resultado. É por isso que cada entrega leva o identificador do
/// evento num cabeçalho — a deduplicação final é do assinante, e o contrato diz isso.
/// </para>
/// </remarks>
public sealed class EntregaDeWebhooks(
    IContextoDePersistencia contexto,
    IClienteDeWebhook cliente,
    IProtecaoDeSegredo protecao,
    IGeradorDeIdentificador identificadores,
    IRelogio relogio,
    ILogger<EntregaDeWebhooks> log)
{
    /// <summary>Entregas tentadas por rodada.</summary>
    public const int TamanhoDoLote = 20;

    /// <summary>Tenta um lote. Devolve quantas entregas foram tentadas.</summary>
    public async Task<int> EntregarLoteAsync(CancellationToken cancelamento)
    {
        var reservadas = await ReservarAsync(cancelamento).ConfigureAwait(false);

        foreach (var reservada in reservadas)
        {
            var resultado = await cliente
                .EntregarAsync(
                    reservada.Url, reservada.Tipo, reservada.MensagemId, reservada.Conteudo, reservada.Segredo, cancelamento)
                .ConfigureAwait(false);

            await RegistrarResultadoAsync(reservada.Id, resultado, cancelamento).ConfigureAwait(false);
        }

        return reservadas.Count;
    }

    private async Task<IReadOnlyList<EntregaReservada>> ReservarAsync(CancellationToken cancelamento) =>
        await contexto.ExecutarEmTransacaoAsync(
            async token =>
            {
                var agora = relogio.AgoraUtc;
                var ids = await contexto.ReservarEntregasDeWebhookAsync(TamanhoDoLote, agora, token).ConfigureAwait(false);

                if (ids.Count == 0)
                {
                    return (IReadOnlyList<EntregaReservada>)[];
                }

                var entregas = await contexto.EntregasDeWebhook
                    .IgnoreQueryFilters()
                    .Where(entrega => ids.Contains(entrega.Id))
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                var assinaturaIds = entregas.Select(entrega => entrega.AssinaturaId).Distinct().ToList();

                var segredos = await contexto.AssinaturasDeWebhook
                    .IgnoreQueryFilters()
                    .Where(assinatura => assinaturaIds.Contains(assinatura.Id))
                    .Select(assinatura => new { assinatura.Id, assinatura.SegredoCifrado })
                    .ToDictionaryAsync(item => item.Id, item => item.SegredoCifrado, token)
                    .ConfigureAwait(false);

                var reservadas = new List<EntregaReservada>(entregas.Count);

                foreach (var entrega in entregas)
                {
                    // Arrendamento: enquanto esta rodada tenta, ninguém mais pega a mesma entrega. Se o
                    // processo cair, ela volta sozinha quando o prazo vencer.
                    entrega.AdiarPorArrendamento(agora, PoliticaDeWebhook.TempoLimiteDaTentativa * 3);

                    if (!segredos.TryGetValue(entrega.AssinaturaId, out var cifrado))
                    {
                        continue;
                    }

                    reservadas.Add(new EntregaReservada(
                        entrega.Id, entrega.Url, entrega.Tipo, entrega.MensagemId, entrega.Conteudo, protecao.Decifrar(cifrado)));
                }

                await contexto.SaveChangesAsync(token).ConfigureAwait(false);
                return (IReadOnlyList<EntregaReservada>)reservadas;
            },
            cancelamento).ConfigureAwait(false);

    private async Task RegistrarResultadoAsync(
        Guid entregaId,
        ResultadoDaEntregaDeWebhook resultado,
        CancellationToken cancelamento) =>
        await contexto.ExecutarEmTransacaoAsync(
            async token =>
            {
                var agora = relogio.AgoraUtc;

                var entrega = await contexto.EntregasDeWebhook
                    .IgnoreQueryFilters()
                    .SingleOrDefaultAsync(item => item.Id == entregaId, token)
                    .ConfigureAwait(false);

                if (entrega is null)
                {
                    return 0;
                }

                var tentativa = resultado.Sucesso
                    ? entrega.RegistrarSucesso(
                        identificadores.Novo(), resultado.Status ?? 200, resultado.DuracaoEmMilissegundos, agora)
                    : entrega.RegistrarFalha(
                        identificadores.Novo(), resultado.Status, resultado.Erro, resultado.DuracaoEmMilissegundos, agora);

                contexto.TentativasDeWebhook.Add(tentativa);

                if (entrega.Estado == EstadoDaEntregaDeWebhook.Falhada)
                {
                    log.LogWarning(
                        "Webhook desistiu depois de {Tentativas} tentativas: entrega {EntregaId} para {Url}.",
                        entrega.Tentativas,
                        entrega.Id,
                        entrega.Url);
                }

                return await contexto.SaveChangesAsync(token).ConfigureAwait(false);
            },
            cancelamento).ConfigureAwait(false);

    private sealed record EntregaReservada(Guid Id, string Url, string Tipo, Guid MensagemId, string Conteudo, string Segredo);
}
