using Microsoft.EntityFrameworkCore;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Domain.Abstracoes.Tempo;
using TorreLogistica.Domain.Identidade;

namespace TorreLogistica.Application.Identidade;

/// <summary>
/// Confere, a cada requisição autenticada, se a sessão do token ainda vale.
/// </summary>
/// <remarks>
/// <para>
/// Token de acesso assinado continua matematicamente válido até expirar, mesmo depois de
/// logout, desativação da conta ou detecção de reuso. Sem esta conferência, cada uma
/// dessas ações só teria efeito até 15 minutos depois.
/// </para>
/// <para>
/// O custo é uma consulta por chave primária por requisição. Para a escala do projeto é
/// desprezível; se deixar de ser, o próximo passo é cache curto com invalidação na
/// revogação — nunca desistir da conferência.
/// </para>
/// </remarks>
public sealed class ValidarSessaoAtiva(IContextoDePersistencia contexto, IRelogio relogio)
{
    /// <summary>Indica se sessão, conta e organização continuam aptas.</summary>
    public Task<bool> ExecutarAsync(
        Guid sessaoId,
        Guid usuarioId,
        Guid organizacaoId,
        CanalDeAcesso canal,
        CancellationToken cancelamento)
    {
        var agora = relogio.AgoraUtc;

        // A autenticação acontece antes de haver tenant resolvido; os dados do token são
        // conferidos contra o banco aqui, justamente para decidir se o tenant é confiável.
        return contexto.Sessoes
            .IgnoreQueryFilters()
            .AnyAsync(
                sessao => sessao.Id == sessaoId
                    && sessao.UsuarioId == usuarioId
                    && sessao.OrganizacaoId == organizacaoId
                    && sessao.Canal == canal
                    && sessao.RevogadaEm == null
                    && sessao.ExpiraEm > agora
                    && contexto.Usuarios.IgnoreQueryFilters().Any(usuario =>
                        usuario.Id == usuarioId
                        && usuario.OrganizacaoId == organizacaoId
                        && usuario.Ativo)
                    && contexto.Organizacoes.Any(organizacao =>
                        organizacao.Id == organizacaoId && organizacao.Ativa),
                cancelamento);
    }
}
