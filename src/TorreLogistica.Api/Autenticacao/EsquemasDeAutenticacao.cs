using TorreLogistica.Domain.Identidade;

namespace TorreLogistica.Api.Autenticacao;

/// <summary>
/// Esquemas, audiências e políticas de autenticação da API.
/// </summary>
/// <remarks>
/// Um esquema por canal, cada um validando só a sua audiência. Um token emitido para a
/// PWA do motorista apresentado ao console falha na <b>autenticação</b> — não chega a ser
/// avaliado pela autorização. Não é "motorista sem permissão": para o console, aquilo não
/// é credencial.
/// </remarks>
public static class EsquemasDeAutenticacao
{
    /// <summary>Esquema do console operacional.</summary>
    public const string Operacao = "Operacao";

    /// <summary>Esquema da PWA do motorista.</summary>
    public const string Motorista = "Motorista";

    /// <summary>Esquema que valida o canal.</summary>
    public static string Para(CanalDeAcesso canal) => canal switch
    {
        CanalDeAcesso.Operacao => Operacao,
        CanalDeAcesso.Motorista => Motorista,
        _ => throw new ArgumentOutOfRangeException(nameof(canal), canal, "Canal desconhecido."),
    };

    /// <summary>Audiência (<c>aud</c>) dos tokens do canal.</summary>
    public static string AudienciaDe(CanalDeAcesso canal) => canal switch
    {
        CanalDeAcesso.Operacao => "torre-logistica:operacao",
        CanalDeAcesso.Motorista => "torre-logistica:motorista",
        _ => throw new ArgumentOutOfRangeException(nameof(canal), canal, "Canal desconhecido."),
    };
}

/// <summary>Nomes das reivindicações (claims) carregadas no token de acesso.</summary>
public static class ReivindicacoesDaTorre
{
    /// <summary>Conta autenticada.</summary>
    public const string Usuario = "sub";

    /// <summary>Organização da sessão — a única fonte de tenant da requisição.</summary>
    public const string Organizacao = "org";

    /// <summary>Perfil de acesso.</summary>
    public const string Perfil = "perfil";

    /// <summary>Sessão, conferida contra o banco a cada requisição.</summary>
    public const string Sessao = "sid";

    /// <summary>Canal da sessão.</summary>
    public const string Canal = "canal";
}

/// <summary>Políticas de autorização.</summary>
/// <remarks>A matriz completa está em <c>docs/seguranca/matriz-de-autorizacao.md</c>.</remarks>
public static class Politicas
{
    /// <summary>Qualquer perfil do console.</summary>
    public const string Console = "console";

    /// <summary>Consultar contas da organização: administrador e supervisor.</summary>
    public const string LeituraDeUsuarios = "usuarios:leitura";

    /// <summary>Criar, alterar perfil e desativar contas: só administrador.</summary>
    public const string GestaoDeUsuarios = "usuarios:gestao";

    /// <summary>Consultar a estrutura operacional: administrador, supervisor e operador.</summary>
    public const string LeituraDaOperacao = "operacao:leitura";

    /// <summary>Cadastrar e alterar a estrutura operacional: administrador e supervisor.</summary>
    public const string GestaoDaOperacao = "operacao:gestao";

    /// <summary>Criar, alterar e cancelar entregas: administrador, supervisor e operador.</summary>
    public const string OperacaoDeEntregas = "entregas:operacao";

    /// <summary>Conta de motorista autenticada pela PWA.</summary>
    public const string Motorista = "motorista";
}

/// <summary>Políticas de limite de requisições.</summary>
public static class PoliticasDeLimite
{
    /// <summary>Login, por endereço de origem.</summary>
    public const string Login = "limite-login";

    /// <summary>Renovação de sessão, por endereço de origem.</summary>
    public const string Renovacao = "limite-renovacao";

    /// <summary>Envio de posições, por motorista autenticado.</summary>
    public const string Telemetria = "limite-telemetria";

    /// <summary>Sincronização de operações offline, por motorista autenticado.</summary>
    public const string Sincronizacao = "limite-sincronizacao";
}
