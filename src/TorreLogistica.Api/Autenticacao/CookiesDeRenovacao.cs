using TorreLogistica.Application.Identidade;
using TorreLogistica.Domain.Identidade;

namespace TorreLogistica.Api.Autenticacao;

/// <summary>
/// Cookie que transporta o token de renovação.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description><c>HttpOnly</c>: JavaScript da página não lê o token. Um XSS no
/// console rouba no máximo o token de acesso, que dura minutos.</description></item>
/// <item><description><c>Secure</c>: nunca trafega sem TLS. Navegadores tratam
/// <c>localhost</c> como contexto seguro, então funciona também em desenvolvimento.</description></item>
/// <item><description><c>SameSite=Strict</c>: não acompanha requisição iniciada por outro site.</description></item>
/// <item><description><c>Path</c> restrito ao prefixo de autenticação do canal: não é
/// enviado em nenhuma outra rota da API.</description></item>
/// </list>
/// </remarks>
public static class CookiesDeRenovacao
{
    /// <summary>Nome do cookie do canal.</summary>
    public static string Nome(CanalDeAcesso canal) => canal switch
    {
        CanalDeAcesso.Operacao => "torre_renovacao_operacao",
        CanalDeAcesso.Motorista => "torre_renovacao_motorista",
        _ => throw new ArgumentOutOfRangeException(nameof(canal), canal, "Canal desconhecido."),
    };

    /// <summary>Prefixo das rotas de autenticação do canal, que também é o <c>Path</c> do cookie.</summary>
    public static string Caminho(CanalDeAcesso canal) => canal switch
    {
        CanalDeAcesso.Operacao => "/api/autenticacao",
        CanalDeAcesso.Motorista => "/api/motorista/autenticacao",
        _ => throw new ArgumentOutOfRangeException(nameof(canal), canal, "Canal desconhecido."),
    };

    /// <summary>Grava o token de renovação da sessão emitida.</summary>
    public static void Gravar(HttpResponse resposta, SessaoEmitida sessao)
    {
        ArgumentNullException.ThrowIfNull(resposta);
        ArgumentNullException.ThrowIfNull(sessao);

        resposta.Cookies.Append(Nome(sessao.Canal), sessao.TokenDeRenovacao, Opcoes(sessao.Canal, sessao.TokenDeRenovacaoExpiraEm));
    }

    /// <summary>Lê o token de renovação do canal, se houver.</summary>
    public static string? Ler(HttpRequest requisicao, CanalDeAcesso canal)
    {
        ArgumentNullException.ThrowIfNull(requisicao);
        return requisicao.Cookies.TryGetValue(Nome(canal), out var valor) ? valor : null;
    }

    /// <summary>Instrui o navegador a apagar o cookie do canal.</summary>
    public static void Remover(HttpResponse resposta, CanalDeAcesso canal)
    {
        ArgumentNullException.ThrowIfNull(resposta);
        resposta.Cookies.Delete(Nome(canal), Opcoes(canal, expiraEm: null));
    }

    private static CookieOptions Opcoes(CanalDeAcesso canal, DateTimeOffset? expiraEm) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = Caminho(canal),
        Expires = expiraEm,
        IsEssential = true,
    };
}
