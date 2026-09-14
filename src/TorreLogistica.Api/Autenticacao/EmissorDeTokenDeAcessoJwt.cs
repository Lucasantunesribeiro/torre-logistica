using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TorreLogistica.Application.Abstracoes.Seguranca;
using TorreLogistica.Application.Identidade;
using TorreLogistica.Domain.Abstracoes.Identificadores;
using TorreLogistica.Domain.Abstracoes.Tempo;
using TorreLogistica.Domain.Identidade;

namespace TorreLogistica.Api.Autenticacao;

/// <summary>
/// Emite o token de acesso como JWT assinado com HMAC-SHA256.
/// </summary>
/// <remarks>
/// O token carrega só identificadores e perfil — nunca e-mail, nome ou qualquer dado
/// pessoal. JWT é apenas codificado, não cifrado: qualquer pessoa com o token lê o
/// conteúdo.
/// </remarks>
public sealed class EmissorDeTokenDeAcessoJwt(
    ChaveDeAssinaturaDeToken chave,
    IOptions<OpcoesDeTokenDeAcesso> opcoesDoToken,
    IOptions<OpcoesDeAutenticacao> opcoesDeAutenticacao,
    IRelogio relogio,
    IGeradorDeIdentificador identificadores) : IEmissorDeTokenDeAcesso
{
    private static readonly JsonWebTokenHandler Manipulador = new() { SetDefaultTimesOnTokenCreation = false };

    /// <inheritdoc />
    public TokenDeAcessoEmitido Emitir(Usuario usuario, Sessao sessao)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        ArgumentNullException.ThrowIfNull(sessao);

        var agora = relogio.AgoraUtc;
        var expiraEm = agora + opcoesDeAutenticacao.Value.DuracaoDoTokenDeAcesso;

        // O token de acesso nunca vale além da sessão que o originou.
        if (expiraEm > sessao.ExpiraEm)
        {
            expiraEm = sessao.ExpiraEm;
        }

        var descritor = new SecurityTokenDescriptor
        {
            Issuer = opcoesDoToken.Value.Emissor,
            Audience = EsquemasDeAutenticacao.AudienciaDe(sessao.Canal),
            IssuedAt = agora.UtcDateTime,
            NotBefore = agora.UtcDateTime,
            Expires = expiraEm.UtcDateTime,
            SigningCredentials = new SigningCredentials(chave.Chave, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [ReivindicacoesDaTorre.Usuario] = usuario.Id.ToString(),
                [ReivindicacoesDaTorre.Organizacao] = sessao.OrganizacaoId.ToString(),
                [ReivindicacoesDaTorre.Perfil] = usuario.Perfil.ToString(),
                [ReivindicacoesDaTorre.Sessao] = sessao.Id.ToString(),
                [ReivindicacoesDaTorre.Canal] = sessao.Canal.ToString(),
                [JwtRegisteredClaimNames.Jti] = identificadores.Novo().ToString("n"),
            },
        };

        return new TokenDeAcessoEmitido(Manipulador.CreateToken(descritor), expiraEm);
    }
}
