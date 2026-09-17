using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using TorreLogistica.Api.Autenticacao;
using TorreLogistica.Api.Erros;
using TorreLogistica.Application.Integracoes;

namespace TorreLogistica.Api.Integracoes;

/// <summary>
/// Autentica sistemas externos pela chave de API apresentada em <c>Authorization: Bearer</c>.
/// </summary>
/// <remarks>
/// <para>
/// Não há sessão, não há renovação e não há perfil: a credencial vale até ser revogada. O principal
/// produzido carrega a organização — que é o que o filtro de tenant precisa — e o identificador da
/// integração, mas <b>nenhuma</b> reivindicação de usuário: assim, tudo o que a integração fizer nasce com
/// autor vazio na timeline, e não com uma pessoa inventada.
/// </para>
/// <para>
/// Chave ausente, malformada, desconhecida, com segredo errado ou revogada produzem o mesmo 401.
/// </para>
/// </remarks>
public sealed class ManipuladorDeChaveDeIntegracao(
    IOptionsMonitor<AuthenticationSchemeOptions> opcoes,
    ILoggerFactory log,
    UrlEncoder codificador,
    AutenticacaoDeIntegracao autenticacao) : AuthenticationHandler<AuthenticationSchemeOptions>(opcoes, log, codificador)
{
    private const string PrefixoDoCabecalho = "Bearer ";

    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.Authorization is not { Count: 1 } cabecalho
            || cabecalho[0] is not { } valor
            || !valor.StartsWith(PrefixoDoCabecalho, StringComparison.Ordinal))
        {
            // NoResult, e não Fail: sem cabeçalho não há tentativa de autenticação para reprovar.
            return AuthenticateResult.NoResult();
        }

        var integracao = await autenticacao
            .AutenticarAsync(valor[PrefixoDoCabecalho.Length..], Context.RequestAborted)
            .ConfigureAwait(false);

        if (integracao is null)
        {
            return AuthenticateResult.Fail("Credencial de integração inválida.");
        }

        var identidade = new ClaimsIdentity(
            [
                new Claim(ReivindicacoesDaTorre.Organizacao, integracao.OrganizacaoId.ToString()),
                new Claim(ReivindicacoesDaTorre.Integracao, integracao.Id.ToString()),
            ],
            Scheme.Name);

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identidade), Scheme.Name));
    }

    /// <inheritdoc />
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";

        await RespostasDeProblema.EscreverAsync(
            Context,
            StatusCodes.Status401Unauthorized,
            "credencial_de_integracao_invalida",
            "Não autenticado",
            "Apresente uma chave de integração válida em Authorization: Bearer.").ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        await RespostasDeProblema.EscreverAsync(
            Context,
            StatusCodes.Status403Forbidden,
            "acesso_negado",
            "Acesso negado",
            "Esta credencial não permite a operação.").ConfigureAwait(false);
}
