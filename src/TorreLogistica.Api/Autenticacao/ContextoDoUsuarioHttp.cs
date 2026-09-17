using System.Security.Claims;
using TorreLogistica.Application.Abstracoes.Identidade;
using TorreLogistica.Domain.Identidade;

namespace TorreLogistica.Api.Autenticacao;

/// <summary>
/// Usuário e tenant da requisição, lidos do principal autenticado.
/// </summary>
/// <remarks>
/// <para>
/// O principal só existe depois que o token passou por assinatura, audiência, validade
/// e conferência da sessão no banco. Por isso a organização lida aqui é confiável — e é a
/// única fonte de tenant do sistema. Nada vem de rota, query string ou corpo.
/// </para>
/// <para>
/// Os valores são lidos a cada acesso, nunca guardados: o contexto de persistência é
/// criado antes da autenticação terminar e precisa enxergar o tenant quando consultar,
/// não quando nasceu.
/// </para>
/// </remarks>
public sealed class ContextoDoUsuarioHttp(IHttpContextAccessor acessor) : IContextoDoUsuario
{
    private ClaimsPrincipal? Principal =>
        acessor.HttpContext?.User is { Identity.IsAuthenticated: true } principal ? principal : null;

    /// <inheritdoc />
    public Guid? OrganizacaoId =>
        Principal is { } principal
        && Guid.TryParse(principal.FindFirstValue(ReivindicacoesDaTorre.Organizacao), out var organizacaoId)
            ? organizacaoId
            : null;

    /// <inheritdoc />
    public bool EstaAutenticado => OrganizacaoId is not null;

    /// <inheritdoc />
    public Guid UsuarioId => LerIdentificador(ReivindicacoesDaTorre.Usuario);

    /// <inheritdoc />
    public Guid? AutorUsuarioId =>
        Principal is { } principal && Guid.TryParse(principal.FindFirstValue(ReivindicacoesDaTorre.Usuario), out var valor)
            ? valor
            : null;

    /// <inheritdoc />
    public Guid OrganizacaoIdAutenticada => LerIdentificador(ReivindicacoesDaTorre.Organizacao);

    /// <inheritdoc />
    public Guid SessaoId => LerIdentificador(ReivindicacoesDaTorre.Sessao);

    /// <inheritdoc />
    public Perfil Perfil => LerEnumeracao<Perfil>(ReivindicacoesDaTorre.Perfil);

    /// <inheritdoc />
    public CanalDeAcesso Canal => LerEnumeracao<CanalDeAcesso>(ReivindicacoesDaTorre.Canal);

    private Guid LerIdentificador(string reivindicacao) =>
        Principal is { } principal && Guid.TryParse(principal.FindFirstValue(reivindicacao), out var valor)
            ? valor
            : throw SemSessao();

    private TEnum LerEnumeracao<TEnum>(string reivindicacao)
        where TEnum : struct, Enum =>
        Principal is { } principal && Enum.TryParse<TEnum>(principal.FindFirstValue(reivindicacao), out var valor)
            ? valor
            : throw SemSessao();

    // Chegar aqui sem sessão é defeito de configuração de endpoint, não erro do cliente.
    private static InvalidOperationException SemSessao() =>
        new("Operação que exige sessão autenticada executada sem sessão.");
}
