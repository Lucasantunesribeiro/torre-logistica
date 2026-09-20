using Microsoft.EntityFrameworkCore;
using TorreLogistica.Application.Cadastros;
using TorreLogistica.Application.Comum;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Webhooks;

namespace TorreLogistica.Application.Webhooks;

/// <summary>Uma tentativa, como devolvida pela API.</summary>
public sealed record TentativaResumo(
    int Numero,
    int? Status,
    int DuracaoEmMilissegundos,
    string? Erro,
    DateTimeOffset TentadaEm);

/// <summary>Entrega de webhook, como devolvida pela API.</summary>
public sealed record EntregaDeWebhookResumo(
    Guid Id,
    Guid AssinaturaId,
    Guid MensagemId,
    string Tipo,
    string Url,
    EstadoDaEntregaDeWebhook Estado,
    int Tentativas,
    DateTimeOffset DisponivelEm,
    DateTimeOffset CriadaEm,
    DateTimeOffset? ConcluidaEm,
    int? UltimoStatus,
    string? UltimoErro);

/// <summary>Entrega com o histórico completo de tentativas.</summary>
public sealed record EntregaDeWebhookDetalhada(
    EntregaDeWebhookResumo Entrega,
    string Conteudo,
    IReadOnlyList<TentativaResumo> Tentativas);

/// <summary>Filtro da lista de entregas de webhook.</summary>
public sealed record FiltroDeEntregasDeWebhook(
    int Pagina,
    int TamanhoDaPagina,
    EstadoDaEntregaDeWebhook? Estado,
    Guid? AssinaturaId);

/// <summary>
/// Situação das entregas de webhook e reenvio manual.
/// </summary>
/// <remarks>
/// É a janela que torna o critério de aceite verificável: nada se perde em silêncio porque tudo tem estado
/// visível, histórico de tentativa e um caminho explícito de volta para a fila.
/// </remarks>
public sealed class ConsultaDeWebhooks(SuporteDeCadastro suporte)
{
    private const string Recurso = "webhook";

    /// <summary>Lista entregas da organização, das mais recentes para as mais antigas.</summary>
    public async Task<PaginaDeResultados<EntregaDeWebhookResumo>> ListarAsync(
        FiltroDeEntregasDeWebhook filtro,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(filtro);

        var consulta = suporte.Contexto.EntregasDeWebhook.AsNoTracking();

        if (filtro.Estado is { } estado)
        {
            consulta = consulta.Where(entrega => entrega.Estado == estado);
        }

        if (filtro.AssinaturaId is { } assinaturaId)
        {
            consulta = consulta.Where(entrega => entrega.AssinaturaId == assinaturaId);
        }

        var total = await consulta.CountAsync(cancelamento).ConfigureAwait(false);

        var itens = await consulta
            .OrderByDescending(entrega => entrega.CriadaEm)
            .ThenBy(entrega => entrega.Id)
            .Skip((filtro.Pagina - 1) * filtro.TamanhoDaPagina)
            .Take(filtro.TamanhoDaPagina)
            .Select(entrega => Projetar(entrega))
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

        return new PaginaDeResultados<EntregaDeWebhookResumo>(itens, filtro.Pagina, filtro.TamanhoDaPagina, total);
    }

    /// <summary>Uma entrega com todas as tentativas.</summary>
    public async Task<EntregaDeWebhookDetalhada> ObterAsync(Guid id, CancellationToken cancelamento)
    {
        var entrega = await suporte.Contexto.EntregasDeWebhook
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancelamento)
            .ConfigureAwait(false)
            ?? throw NaoEncontrada();

        var tentativas = await suporte.Contexto.TentativasDeWebhook
            .AsNoTracking()
            .Where(tentativa => tentativa.EntregaDeWebhookId == id)
            .OrderBy(tentativa => tentativa.Numero)
            .Select(tentativa => new TentativaResumo(
                tentativa.Numero, tentativa.Status, tentativa.DuracaoEmMilissegundos, tentativa.Erro, tentativa.TentadaEm))
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

        return new EntregaDeWebhookDetalhada(Projetar(entrega), entrega.Conteudo, tentativas);
    }

    /// <summary>Recoloca na fila uma entrega que desistiu.</summary>
    public async Task<EntregaDeWebhookResumo> ReenviarAsync(Guid id, CancellationToken cancelamento)
    {
        var entrega = await suporte.Contexto.EntregasDeWebhook
            .SingleOrDefaultAsync(item => item.Id == id, cancelamento)
            .ConfigureAwait(false)
            ?? throw NaoEncontrada();

        entrega.Reenviar(suporte.Agora);

        // Reenvio é ação de gente sobre um efeito externo: fica na trilha, com quem mandou.
        suporte.Auditar(Recurso, "entrega_reenviada", entrega.Id, new { entrega.Tipo, entrega.Url });

        await suporte.SalvarAsync(cancelamento).ConfigureAwait(false);

        return Projetar(entrega);
    }

    private static EntregaDeWebhookResumo Projetar(EntregaDeWebhook entrega) => new(
        entrega.Id,
        entrega.AssinaturaId,
        entrega.MensagemId,
        entrega.Tipo,
        entrega.Url,
        entrega.Estado,
        entrega.Tentativas,
        entrega.DisponivelEm,
        entrega.CriadaEm,
        entrega.ConcluidaEm,
        entrega.UltimoStatus,
        entrega.UltimoErro);

    private static ExcecaoDeDominio NaoEncontrada() =>
        ExcecaoDeDominio.NaoEncontrado("entrega_de_webhook_nao_encontrada", "Entrega de webhook não encontrada.");
}
