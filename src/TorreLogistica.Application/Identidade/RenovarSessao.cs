using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Application.Abstracoes.Seguranca;
using TorreLogistica.Application.Auditoria;
using TorreLogistica.Domain.Abstracoes.Identificadores;
using TorreLogistica.Domain.Abstracoes.Tempo;
using TorreLogistica.Domain.Auditoria;
using TorreLogistica.Domain.Identidade;

namespace TorreLogistica.Application.Identidade;

/// <summary>
/// Troca um token de renovação por um novo par de tokens, detectando reuso.
/// </summary>
/// <remarks>
/// A decisão em si é de <see cref="PoliticaDeRenovacao"/>. Este caso de uso garante que
/// ela seja tomada sobre estado consistente: a linha da sessão fica travada durante toda
/// a operação, então duas renovações da mesma família nunca decidem ao mesmo tempo
/// olhando o mesmo token "ainda disponível".
/// </remarks>
public sealed class RenovarSessao(
    IContextoDePersistencia contexto,
    IEmissorDeTokenDeAcesso emissor,
    IGeradorDeIdentificador identificadores,
    IRelogio relogio,
    IOptions<OpcoesDeAutenticacao> opcoes,
    ILogger<RenovarSessao> log)
{
    /// <summary>Executa a renovação.</summary>
    /// <returns>Nova sessão emitida, ou <see langword="null"/> se a renovação foi recusada.</returns>
    public async Task<SessaoEmitida?> ExecutarAsync(
        string? tokenCru,
        CanalDeAcesso canal,
        CancellationToken cancelamento)
    {
        if (!SegredosDeRenovacao.FormatoEhPlausivel(tokenCru))
        {
            return null;
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
            log.LogWarning("Renovação recusada: token desconhecido, canal {Canal}.", canal);
            return null;
        }

        return await contexto.ExecutarEmTransacaoAsync(
            cancelamentoDaTransacao => RenovarNaTransacaoAsync(
                sessaoId.Value, hash, canal, cancelamentoDaTransacao),
            cancelamento).ConfigureAwait(false);
    }

    private async Task<SessaoEmitida?> RenovarNaTransacaoAsync(
        Guid sessaoId,
        byte[] hash,
        CanalDeAcesso canal,
        CancellationToken cancelamento)
    {
        await contexto.BloquearSessaoAsync(sessaoId, cancelamento).ConfigureAwait(false);

        var sessao = await contexto.Sessoes
            .IgnoreQueryFilters()
            .SingleAsync(item => item.Id == sessaoId, cancelamento)
            .ConfigureAwait(false);

        var apresentado = await contexto.TokensDeRenovacao
            .IgnoreQueryFilters()
            .SingleAsync(token => token.HashDoToken == hash, cancelamento)
            .ConfigureAwait(false);

        var sucessor = apresentado.SubstitutoId is { } substitutoId
            ? await contexto.TokensDeRenovacao
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(token => token.Id == substitutoId, cancelamento)
                .ConfigureAwait(false)
            : null;

        var candidato = await contexto.Usuarios
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(usuario => usuario.Id == sessao.UsuarioId && usuario.Ativo)
            .Join(
                contexto.Organizacoes.Where(organizacao => organizacao.Ativa),
                usuario => usuario.OrganizacaoId,
                organizacao => organizacao.Id,
                (usuario, organizacao) => new { Usuario = usuario, Organizacao = organizacao })
            .SingleOrDefaultAsync(cancelamento)
            .ConfigureAwait(false);

        var configuracao = opcoes.Value;
        var agora = relogio.AgoraUtc;

        var decisao = PoliticaDeRenovacao.Avaliar(
            apresentado, sucessor, sessao, canal, agora, configuracao.JanelaDeToleranciaDaRenovacao);

        if (candidato is null && decisao != DecisaoDeRenovacao.ReusoDetectado)
        {
            decisao = DecisaoDeRenovacao.Recusar;
        }

        switch (decisao)
        {
            case DecisaoDeRenovacao.Recusar:
                log.LogWarning(
                    "Renovação recusada: sessão {SessaoId} encerrada, expirada, de outro canal ou conta indisponível.",
                    sessao.Id);
                return null;

            case DecisaoDeRenovacao.ReusoDetectado:
                if (sessao.Revogar(MotivoDeRevogacao.ReusoDeToken, agora))
                {
                    contexto.EventosDeAuditoria.Add(RegistroDeAuditoria.Criar(
                        identificadores,
                        agora,
                        sessao.OrganizacaoId,
                        TiposDeEventoDeAuditoria.ReusoDeTokenDetectado,
                        autorUsuarioId: null,
                        alvoTipo: "sessao",
                        alvoId: sessao.Id,
                        new { usuarioId = sessao.UsuarioId, canal = sessao.Canal.ToString() }));

                    await contexto.SaveChangesAsync(cancelamento).ConfigureAwait(false);
                }

                log.LogWarning(
                    "Reuso de token de renovação detectado: sessão {SessaoId} do usuário {UsuarioId} revogada.",
                    sessao.Id,
                    sessao.UsuarioId);
                return null;

            case DecisaoDeRenovacao.RotacionarNovamenteNaJanelaDeTolerancia:
                sucessor!.Invalidar(agora);
                log.LogInformation(
                    "Renovação repetida dentro da janela de tolerância na sessão {SessaoId}; sucessor anterior descartado.",
                    sessao.Id);
                break;

            case DecisaoDeRenovacao.Rotacionar:
                break;

            default:
                throw new InvalidOperationException($"Decisão de renovação não tratada: {decisao}.");
        }

        var usuario = candidato!.Usuario;
        var (tokenCru, novoHash) = SegredosDeRenovacao.Gerar();
        var novoToken = TokenDeRenovacao.Emitir(
            identificadores.Novo(), sessao, novoHash, agora, configuracao.DuracaoDaSessao(sessao.Canal));

        apresentado.MarcarComoUsado(novoToken.Id, agora);
        sessao.RegistrarRenovacao(agora);
        contexto.TokensDeRenovacao.Add(novoToken);

        await contexto.SaveChangesAsync(cancelamento).ConfigureAwait(false);

        var acesso = emissor.Emitir(usuario, sessao);

        return new SessaoEmitida(
            new UsuarioAutenticado(
                usuario.Id,
                usuario.Nome,
                usuario.Email,
                usuario.Perfil,
                candidato.Organizacao.Id,
                candidato.Organizacao.Nome,
                candidato.Organizacao.Slug),
            acesso.Token,
            acesso.ExpiraEm,
            tokenCru,
            novoToken.ExpiraEm,
            sessao.Canal);
    }
}
