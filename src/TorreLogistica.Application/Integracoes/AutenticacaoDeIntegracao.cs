using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Domain.Abstracoes.Tempo;

namespace TorreLogistica.Application.Integracoes;

/// <summary>Credencial reconhecida, com a organização que ela autoriza.</summary>
public sealed record IntegracaoAutenticada(Guid Id, Guid OrganizacaoId, string Nome);

/// <summary>
/// Reconhece a chave de API apresentada por um sistema externo.
/// </summary>
/// <remarks>
/// <para>
/// Roda antes de existir tenant — é justamente a chave que decide a organização —, por isso usa
/// <c>IgnoreQueryFilters()</c> explícito. Nada é consultado antes de a chave ter forma válida.
/// </para>
/// <para>
/// Chave desconhecida, segredo errado e credencial revogada devolvem o mesmo <see langword="null"/>: a
/// borda HTTP responde 401 idêntico nos três casos. Distinguir "revogada" de "inexistente" confirmaria ao
/// atacante que aquela chave já foi válida um dia.
/// </para>
/// </remarks>
public sealed class AutenticacaoDeIntegracao(
    IContextoDePersistencia contexto,
    IRelogio relogio,
    ILogger<AutenticacaoDeIntegracao> log)
{
    /// <summary>Reconhece a chave, ou devolve <see langword="null"/>.</summary>
    public async Task<IntegracaoAutenticada?> AutenticarAsync(string? chaveApresentada, CancellationToken cancelamento)
    {
        // Descarta sem tocar no banco o que nem tem forma de chave. O identificador público tem 96 bits
        // sorteados: não há espaço para enumerar, e por isso a diferença de tempo entre "não existe" e
        // "segredo errado" não é um oráculo útil aqui.
        if (!SegredosDeIntegracao.TentarAnalisar(chaveApresentada, out var identificadorPublico, out var segredo))
        {
            return null;
        }

        var integracao = await contexto.Integracoes
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.IdentificadorPublico == identificadorPublico, cancelamento)
            .ConfigureAwait(false);

        if (integracao is null || !SegredosDeIntegracao.Confere(integracao.HashDoSegredo, segredo))
        {
            return null;
        }

        if (!integracao.EstaAtiva)
        {
            log.LogWarning("Credencial de integração revogada apresentada: {IntegracaoId}.", integracao.Id);
            return null;
        }

        // O uso é anotado com granularidade de minuto: gravar a cada requisição faria de uma linha o
        // ponto de contenção de todo o tráfego da integração.
        if (integracao.RegistrarUso(relogio.AgoraUtc))
        {
            await contexto.SaveChangesAsync(cancelamento).ConfigureAwait(false);
        }

        return new IntegracaoAutenticada(integracao.Id, integracao.OrganizacaoId, integracao.Nome);
    }
}
