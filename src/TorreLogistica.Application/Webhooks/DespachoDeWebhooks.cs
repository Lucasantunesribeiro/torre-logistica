using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Application.Observabilidade;
using TorreLogistica.Domain.Abstracoes.Identificadores;
using TorreLogistica.Domain.Abstracoes.Tempo;
using TorreLogistica.Domain.Webhooks;

namespace TorreLogistica.Application.Webhooks;

/// <summary>
/// Transforma mensagens do outbox em entregas, uma por assinante interessado.
/// </summary>
/// <remarks>
/// <para>
/// Roda sem sessão, em processo de fundo, e por isso atravessa o filtro de tenant de propósito: a
/// organização vem dentro da mensagem e é ela que decide quais assinaturas recebem.
/// </para>
/// <para>
/// A reserva usa <c>FOR UPDATE SKIP LOCKED</c> e tudo acontece numa transação: ou a mensagem é marcada
/// despachada junto com as entregas criadas, ou nada acontece e outra rodada tenta de novo. Mensagem
/// processada duas vezes esbarra no índice único de (assinatura, mensagem) — o assinante recebe uma vez só.
/// </para>
/// </remarks>
public sealed class DespachoDeWebhooks(
    IContextoDePersistencia contexto,
    IGeradorDeIdentificador identificadores,
    IRelogio relogio,
    ILogger<DespachoDeWebhooks> log)
{
    /// <summary>Mensagens processadas por rodada.</summary>
    public const int TamanhoDoLote = 50;

    /// <summary>Despacha um lote. Devolve quantas mensagens saíram do outbox.</summary>
    public async Task<int> DespacharLoteAsync(CancellationToken cancelamento)
    {
        // O lote junta eventos de rastros diferentes, então este span não herda nenhum deles: ele conta a
        // história do despachante. O elo com a operação de origem viaja na mensagem, e reaparece na entrega.
        using var rastro = RastroDaOperacao.Fonte.StartActivity("outbox.despacho");
        var criadas = await DespacharInternoAsync(cancelamento).ConfigureAwait(false);
        rastro?.SetTag("outbox.entregas_criadas", criadas);
        return criadas;
    }

    private async Task<int> DespacharInternoAsync(CancellationToken cancelamento) =>
        await contexto.ExecutarEmTransacaoAsync(
            async token =>
            {
                var agora = relogio.AgoraUtc;
                var ids = await contexto.ReservarMensagensDoOutboxAsync(TamanhoDoLote, agora, token).ConfigureAwait(false);

                if (ids.Count == 0)
                {
                    return 0;
                }

                var mensagens = await contexto.Outbox
                    .Where(mensagem => ids.Contains(mensagem.Id))
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                var organizacoes = mensagens.Select(mensagem => mensagem.OrganizacaoId).Distinct().ToList();

                var assinaturas = await contexto.AssinaturasDeWebhook
                    .IgnoreQueryFilters()
                    .Where(assinatura => organizacoes.Contains(assinatura.OrganizacaoId) && assinatura.RevogadaEm == null)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                var criadas = 0;

                foreach (var mensagem in mensagens)
                {
                    foreach (var assinatura in assinaturas.Where(assinatura =>
                        assinatura.OrganizacaoId == mensagem.OrganizacaoId && assinatura.Assina(mensagem.Tipo)))
                    {
                        contexto.EntregasDeWebhook.Add(EntregaDeWebhook.Criar(
                            identificadores.Novo(), mensagem.OrganizacaoId, assinatura, mensagem, agora));

                        criadas++;
                    }

                    // Sem assinante interessado a mensagem também sai da fila: ela cumpriu o papel de
                    // registrar o fato, e deixá-la pendente faria a fila crescer para sempre.
                    mensagem.MarcarDespachada(agora);
                }

                await contexto.SaveChangesAsync(token).ConfigureAwait(false);

                log.LogInformation(
                    "Outbox: {Mensagens} mensagens despachadas em {Entregas} entregas de webhook.", mensagens.Count, criadas);

                return mensagens.Count;
            },
            cancelamento).ConfigureAwait(false);
}
