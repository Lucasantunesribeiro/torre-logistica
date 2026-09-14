using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Domain.Abstracoes.Tempo;
using TorreLogistica.Domain.Identidade;

namespace TorreLogistica.Application.Identidade;

/// <summary>
/// Logout: revoga a sessão a que o token de renovação pertence.
/// </summary>
/// <remarks>
/// Idempotente e sem resposta diferenciada. Encerrar uma sessão já encerrada, ou
/// apresentar um token que não existe, termina do mesmo jeito que um logout normal —
/// o endpoint não serve para descobrir se um token é válido.
/// </remarks>
public sealed class EncerrarSessao(
    IContextoDePersistencia contexto,
    IRelogio relogio,
    ILogger<EncerrarSessao> log)
{
    /// <summary>Executa o logout.</summary>
    public async Task ExecutarAsync(string? tokenCru, CanalDeAcesso canal, CancellationToken cancelamento)
    {
        if (!SegredosDeRenovacao.FormatoEhPlausivel(tokenCru))
        {
            return;
        }

        var hash = SegredosDeRenovacao.CalcularHash(tokenCru!);

        var sessaoId = await contexto.TokensDeRenovacao
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(token => token.HashDoToken == hash)
            .Select(token => (Guid?)token.SessaoId)
            .SingleOrDefaultAsync(cancelamento)
            .ConfigureAwait(false);

        if (sessaoId is null)
        {
            return;
        }

        await contexto.ExecutarEmTransacaoAsync(
            async cancelamentoDaTransacao =>
            {
                await contexto.BloquearSessaoAsync(sessaoId.Value, cancelamentoDaTransacao)
                    .ConfigureAwait(false);

                var sessao = await contexto.Sessoes
                    .IgnoreQueryFilters()
                    .SingleAsync(item => item.Id == sessaoId.Value, cancelamentoDaTransacao)
                    .ConfigureAwait(false);

                if (sessao.Canal != canal || !sessao.Revogar(MotivoDeRevogacao.Logout, relogio.AgoraUtc))
                {
                    return false;
                }

                await contexto.SaveChangesAsync(cancelamentoDaTransacao).ConfigureAwait(false);

                log.LogInformation(
                    "Logout: sessão {SessaoId} do usuário {UsuarioId} encerrada.",
                    sessao.Id,
                    sessao.UsuarioId);

                return true;
            },
            cancelamento).ConfigureAwait(false);
    }
}
