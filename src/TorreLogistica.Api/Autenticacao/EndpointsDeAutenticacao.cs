using System.ComponentModel.DataAnnotations;
using TorreLogistica.Api.Erros;
using TorreLogistica.Application.Identidade;
using TorreLogistica.Application.Organizacoes;
using TorreLogistica.Application.Usuarios;
using TorreLogistica.Domain.Identidade;

namespace TorreLogistica.Api.Autenticacao;

/// <summary>Corpo do login.</summary>
public sealed record RequisicaoDeLogin(
    [property: Required(ErrorMessage = "Informe a organização.")]
    [property: StringLength(Organizacao.TamanhoMaximoDoSlug)]
    string? Organizacao,
    [property: Required(ErrorMessage = "Informe o e-mail.")]
    [property: StringLength(EnderecoDeEmail.TamanhoMaximo)]
    string? Email,
    [property: Required(ErrorMessage = "Informe a senha.")]
    [property: StringLength(PoliticaDeSenha.TamanhoMaximo)]
    string? Senha);

/// <summary>Resposta de login e renovação. O token de renovação nunca aparece aqui.</summary>
public sealed record RespostaDeSessao(
    string TokenDeAcesso,
    string TipoDoToken,
    DateTimeOffset ExpiraEm,
    UsuarioAutenticado Usuario);

/// <summary>
/// Endpoints de login, renovação, logout e conta atual, um conjunto por canal.
/// </summary>
/// <remarks>
/// <code>
/// /api/autenticacao/*             console operacional
/// /api/motorista/autenticacao/*   PWA do motorista
/// </code>
/// Cada canal tem cookie próprio, com <c>Path</c> restrito ao seu prefixo — o navegador
/// nem envia o cookie do console para as rotas do motorista, e vice-versa.
/// </remarks>
public static class EndpointsDeAutenticacao
{
    /// <summary>Registra os endpoints dos dois canais.</summary>
    public static IEndpointRouteBuilder MapearEndpointsDeAutenticacao(this IEndpointRouteBuilder rotas)
    {
        ArgumentNullException.ThrowIfNull(rotas);

        MapearCanal(rotas, CanalDeAcesso.Operacao, Politicas.Console);
        MapearCanal(rotas, CanalDeAcesso.Motorista, Politicas.Motorista);

        return rotas;
    }

    private static void MapearCanal(IEndpointRouteBuilder rotas, CanalDeAcesso canal, string politicaDaConta)
    {
        var grupo = rotas
            .MapGroup(CookiesDeRenovacao.Caminho(canal))
            .WithTags($"Autenticação — {canal}")
            .AddEndpointFilter<FiltroSemCache>();

        grupo.MapPost("/login", async (
                RequisicaoDeLogin requisicao,
                HttpContext http,
                AutenticarUsuario autenticar,
                CancellationToken cancelamento) =>
            {
                var sessao = await autenticar
                    .ExecutarAsync(
                        new ComandoDeAutenticacao(requisicao.Organizacao, requisicao.Email, requisicao.Senha, canal),
                        cancelamento)
                    .ConfigureAwait(false);

                if (sessao is null)
                {
                    return RespostasDeProblema.CredenciaisInvalidas();
                }

                CookiesDeRenovacao.Gravar(http.Response, sessao);
                return Results.Ok(ParaResposta(sessao));
            })
            .AllowAnonymous()
            .RequireRateLimiting(PoliticasDeLimite.Login)
            .AddEndpointFilter<FiltroDeOrigemConfiavel>()
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDeLogin>>();

        grupo.MapPost("/renovar", async (
                HttpContext http,
                RenovarSessao renovar,
                CancellationToken cancelamento) =>
            {
                var tokenCru = CookiesDeRenovacao.Ler(http.Request, canal);
                var sessao = await renovar.ExecutarAsync(tokenCru, canal, cancelamento).ConfigureAwait(false);

                if (sessao is null)
                {
                    CookiesDeRenovacao.Remover(http.Response, canal);
                    return RespostasDeProblema.SessaoInvalida();
                }

                CookiesDeRenovacao.Gravar(http.Response, sessao);
                return Results.Ok(ParaResposta(sessao));
            })
            .AllowAnonymous()
            .RequireRateLimiting(PoliticasDeLimite.Renovacao)
            .AddEndpointFilter<FiltroDeOrigemConfiavel>();

        grupo.MapPost("/sair", async (
                HttpContext http,
                EncerrarSessao encerrar,
                CancellationToken cancelamento) =>
            {
                var tokenCru = CookiesDeRenovacao.Ler(http.Request, canal);
                await encerrar.ExecutarAsync(tokenCru, canal, cancelamento).ConfigureAwait(false);

                // Sempre 204 e sempre remove o cookie: logout não informa se havia sessão.
                CookiesDeRenovacao.Remover(http.Response, canal);
                return Results.NoContent();
            })
            .AllowAnonymous()
            .RequireRateLimiting(PoliticasDeLimite.Renovacao)
            .AddEndpointFilter<FiltroDeOrigemConfiavel>();

        grupo.MapGet("/eu", (ConsultaDeOrganizacao consulta, CancellationToken cancelamento) =>
                consulta.ObterUsuarioAtualAsync(cancelamento))
            .RequireAuthorization(politicaDaConta);
    }

    private static RespostaDeSessao ParaResposta(SessaoEmitida sessao) =>
        new(sessao.TokenDeAcesso, "Bearer", sessao.TokenDeAcessoExpiraEm, sessao.Usuario);
}
