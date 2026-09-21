using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using TorreLogistica.Api.Autenticacao;
using TorreLogistica.Application.Identidade;
using TorreLogistica.Domain.Identidade;

namespace TorreLogistica.Api.Demonstracao;

/// <summary>
/// A porta de entrada da demonstração pública.
/// </summary>
/// <remarks>
/// <para>
/// <b>Desligada por padrão.</b> Num ambiente comercial esta porta não existe: o endpoint responde 404, e
/// não 403, porque "existe mas você não pode" já é informação sobre o sistema.
/// </para>
/// <para>
/// A conta usada é de privilégio mínimo e o servidor recusa subir a sessão se ela for administrativa —
/// uma demonstração não precisa criar credencial de integração nem revogar webhook, e configurar isso por
/// engano não pode virar exposição.
/// </para>
/// </remarks>
public sealed class OpcoesDeDemonstracao
{
    /// <summary>Seção correspondente na configuração.</summary>
    public const string Secao = "Torre:Demonstracao";

    /// <summary>Se a entrada de demonstração existe neste ambiente.</summary>
    public bool Habilitada { get; set; }

    /// <summary>Organização onde o visitante entra.</summary>
    public string? Organizacao { get; set; }

    /// <summary>Conta usada pelo visitante.</summary>
    [EmailAddress]
    public string? Email { get; set; }

    /// <summary>
    /// Senha dessa conta, conhecida só pelo servidor.
    /// </summary>
    /// <remarks>
    /// O visitante nunca a recebe: ele pede uma sessão, e o servidor faz o login por ele. É o que permite
    /// convidar qualquer pessoa a explorar sem publicar credencial em lugar nenhum.
    /// </remarks>
    public string? Senha { get; set; }

    /// <summary>Frase curta que o console mostra ao lado do botão.</summary>
    public string Convite { get; set; } = "Entre como operador numa transportadora fictícia, com dados de demonstração.";

    /// <summary>A configuração está completa o bastante para abrir sessão.</summary>
    public bool EstaCompleta =>
        Habilitada
        && !string.IsNullOrWhiteSpace(Organizacao)
        && !string.IsNullOrWhiteSpace(Email)
        && !string.IsNullOrWhiteSpace(Senha);
}

/// <summary>O que o console precisa saber para oferecer a demonstração.</summary>
/// <param name="Habilitada">Se a porta existe neste ambiente.</param>
/// <param name="Convite">Frase mostrada ao lado do botão.</param>
public sealed record RespostaDeDemonstracao(bool Habilitada, string? Convite);

/// <summary>Endpoints da demonstração.</summary>
public static class EndpointsDeDemonstracao
{
    /// <summary>Registra os endpoints.</summary>
    public static IEndpointRouteBuilder MapearEndpointsDeDemonstracao(this IEndpointRouteBuilder rotas)
    {
        ArgumentNullException.ThrowIfNull(rotas);

        var grupo = rotas.MapGroup("/api/demonstracao").WithTags("Demonstração");

        grupo.MapGet("/", (IOptions<OpcoesDeDemonstracao> opcoes) =>
            {
                var configuracao = opcoes.Value;

                // Quando não há demonstração, a resposta não diz qual organização seria usada: o console
                // só precisa saber se mostra o botão.
                return Results.Ok(configuracao.EstaCompleta
                    ? new RespostaDeDemonstracao(true, configuracao.Convite)
                    : new RespostaDeDemonstracao(false, null));
            })
            .AllowAnonymous()
            .RequireRateLimiting(PoliticasDeLimite.Login);

        grupo.MapPost("/sessao", async (
                HttpContext http,
                IOptions<OpcoesDeDemonstracao> opcoes,
                AutenticarUsuario autenticar,
                ILoggerFactory registros,
                CancellationToken cancelamento) =>
            {
                var configuracao = opcoes.Value;
                var log = registros.CreateLogger("TorreLogistica.Api.Demonstracao");

                if (!configuracao.EstaCompleta)
                {
                    return Results.NotFound();
                }

                var sessao = await autenticar
                    .ExecutarAsync(
                        new ComandoDeAutenticacao(
                            configuracao.Organizacao, configuracao.Email, configuracao.Senha, CanalDeAcesso.Operacao),
                        cancelamento)
                    .ConfigureAwait(false);

                if (sessao is null)
                {
                    // Configuração errada é problema de quem hospeda, não de quem visita: o visitante
                    // recebe a mesma resposta de "não existe", e o operador encontra o motivo no log.
                    log.LogError(
                        "Demonstração habilitada, mas a conta {Email} da organização {Organizacao} não autenticou.",
                        configuracao.Email,
                        configuracao.Organizacao);

                    return Results.NotFound();
                }

                if (sessao.Usuario.Perfil == Perfil.Administrador)
                {
                    // Privilégio mínimo não é recomendação: uma conta administrativa aberta ao público
                    // daria a qualquer visitante o poder de criar credencial de integração e revogar
                    // webhook. Recusar aqui transforma um erro de configuração em indisponibilidade.
                    log.LogError(
                        "Demonstração recusada: a conta {Email} é administradora, e a porta pública exige privilégio mínimo.",
                        configuracao.Email);

                    return Results.NotFound();
                }

                CookiesDeRenovacao.Gravar(http.Response, sessao);

                return Results.Ok(new RespostaDeSessao(
                    sessao.TokenDeAcesso, "Bearer", sessao.TokenDeAcessoExpiraEm, sessao.Usuario));
            })
            .AllowAnonymous()
            .RequireRateLimiting(PoliticasDeLimite.Login)
            .AddEndpointFilter<FiltroDeOrigemConfiavel>();

        return rotas;
    }
}
