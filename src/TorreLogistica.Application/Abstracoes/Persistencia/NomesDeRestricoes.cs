namespace TorreLogistica.Application.Abstracoes.Persistencia;

/// <summary>
/// Nomes físicos de restrições que a aplicação traduz em erro de domínio.
/// </summary>
/// <remarks>
/// A checagem prévia ("já existe?") não basta: duas requisições simultâneas passam por
/// ela ao mesmo tempo. A restrição do banco é a garantia; o nome dela é o que permite
/// transformar a violação em <c>409</c> em vez de <c>500</c>.
/// </remarks>
public static class NomesDeRestricoes
{
    /// <summary>E-mail único dentro da organização.</summary>
    public const string EmailDoUsuarioPorOrganizacao = "ux_usuarios_organizacao_id_email_normalizado";

    /// <summary>Identificador público da organização.</summary>
    public const string SlugDaOrganizacao = "ux_organizacoes_slug";

    /// <summary>Hash do token de renovação.</summary>
    public const string HashDoTokenDeRenovacao = "ux_tokens_de_renovacao_hash_do_token";
}
