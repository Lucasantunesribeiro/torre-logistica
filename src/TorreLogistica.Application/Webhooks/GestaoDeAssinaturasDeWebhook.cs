using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TorreLogistica.Application.Abstracoes.Webhooks;
using TorreLogistica.Application.Cadastros;
using TorreLogistica.Application.Comum;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Webhooks;

namespace TorreLogistica.Application.Webhooks;

/// <summary>Assinatura recém-criada. O segredo aparece aqui e em nenhum outro lugar.</summary>
public sealed record AssinaturaCriada(Guid Id, string Nome, string Url, string Segredo, IReadOnlyList<string> Eventos);

/// <summary>Assinatura como devolvida pela API. Nunca inclui o segredo.</summary>
public sealed record AssinaturaResumo(
    Guid Id,
    string Nome,
    string Url,
    IReadOnlyList<string> Eventos,
    bool Ativa,
    DateTimeOffset CriadaEm,
    DateTimeOffset? RevogadaEm);

/// <summary>
/// Assinaturas de webhook, administradas pelo console.
/// </summary>
/// <remarks>
/// O segredo é sorteado aqui, mostrado uma vez e guardado cifrado. Perdido, a saída é revogar e criar
/// outra assinatura: devolver o segredo depois exigiria uma rota que decifra segredo sob demanda, e essa
/// rota seria o ponto fraco de todo o mecanismo.
/// </remarks>
public sealed class GestaoDeAssinaturasDeWebhook(SuporteDeCadastro suporte, IProtecaoDeSegredo protecao)
{
    private const string Recurso = "webhook";

    /// <summary>Cria a assinatura e devolve o segredo, uma única vez.</summary>
    public async Task<AssinaturaCriada> CriarAsync(
        string? nome,
        string? url,
        IReadOnlyCollection<string>? eventos,
        CancellationToken cancelamento)
    {
        var segredo = Convert.ToHexString(RandomNumberGenerator.GetBytes(PoliticaDeWebhook.BytesDoSegredo)).ToLowerInvariant();

        var assinatura = AssinaturaDeWebhook.Criar(
            suporte.NovoIdentificador(),
            suporte.OrganizacaoId,
            nome,
            url,
            protecao.Cifrar(segredo),
            eventos,
            suporte.AutorUsuarioId,
            suporte.Agora);

        suporte.Contexto.AssinaturasDeWebhook.Add(assinatura);

        // Auditoria registra o destino e os eventos — nunca o segredo.
        suporte.Auditar(Recurso, "assinatura_criada", assinatura.Id, new
        {
            assinatura.Nome,
            assinatura.Url,
            Eventos = assinatura.Eventos,
        });

        await suporte.SalvarAsync(cancelamento).ConfigureAwait(false);

        return new AssinaturaCriada(assinatura.Id, assinatura.Nome, assinatura.Url, segredo, assinatura.Eventos);
    }

    /// <summary>Lista as assinaturas da organização.</summary>
    public async Task<PaginaDeResultados<AssinaturaResumo>> ListarAsync(
        int pagina,
        int tamanhoDaPagina,
        CancellationToken cancelamento)
    {
        var consulta = suporte.Contexto.AssinaturasDeWebhook.AsNoTracking();
        var total = await consulta.CountAsync(cancelamento).ConfigureAwait(false);

        var itens = await consulta
            .OrderByDescending(assinatura => assinatura.CriadaEm)
            .ThenBy(assinatura => assinatura.Id)
            .Skip((pagina - 1) * tamanhoDaPagina)
            .Take(tamanhoDaPagina)
            .Select(assinatura => new AssinaturaResumo(
                assinatura.Id,
                assinatura.Nome,
                assinatura.Url,
                assinatura.Eventos,
                assinatura.RevogadaEm == null,
                assinatura.CriadaEm,
                assinatura.RevogadaEm))
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

        return new PaginaDeResultados<AssinaturaResumo>(itens, pagina, tamanhoDaPagina, total);
    }

    /// <summary>Uma assinatura da organização.</summary>
    public async Task<AssinaturaResumo> ObterAsync(Guid id, CancellationToken cancelamento) =>
        await suporte.Contexto.AssinaturasDeWebhook
            .AsNoTracking()
            .Where(assinatura => assinatura.Id == id)
            .Select(assinatura => new AssinaturaResumo(
                assinatura.Id,
                assinatura.Nome,
                assinatura.Url,
                assinatura.Eventos,
                assinatura.RevogadaEm == null,
                assinatura.CriadaEm,
                assinatura.RevogadaEm))
            .SingleOrDefaultAsync(cancelamento)
            .ConfigureAwait(false)
        ?? throw NaoEncontrada();

    /// <summary>Revoga a assinatura. Revogar de novo devolve o mesmo estado.</summary>
    public async Task<AssinaturaResumo> RevogarAsync(Guid id, CancellationToken cancelamento)
    {
        var assinatura = await suporte.Contexto.AssinaturasDeWebhook
            .SingleOrDefaultAsync(item => item.Id == id, cancelamento)
            .ConfigureAwait(false)
            ?? throw NaoEncontrada();

        if (assinatura.EstaAtiva)
        {
            assinatura.Revogar(suporte.Agora);
            suporte.Auditar(Recurso, "assinatura_revogada", assinatura.Id, new { assinatura.Nome, assinatura.Url });
            await suporte.SalvarAsync(cancelamento).ConfigureAwait(false);
        }

        return await ObterAsync(id, cancelamento).ConfigureAwait(false);
    }

    private static ExcecaoDeDominio NaoEncontrada() =>
        ExcecaoDeDominio.NaoEncontrado("assinatura_nao_encontrada", "Assinatura de webhook não encontrada.");
}
