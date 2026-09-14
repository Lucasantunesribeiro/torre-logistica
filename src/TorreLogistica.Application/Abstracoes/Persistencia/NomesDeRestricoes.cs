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

    /// <summary>Placa única dentro da organização.</summary>
    public const string PlacaDoVeiculoPorOrganizacao = "ux_veiculos_organizacao_id_placa";

    /// <summary>Nome de hub único dentro da organização.</summary>
    public const string NomeDoHubPorOrganizacao = "ux_hubs_organizacao_id_nome_normalizado";

    /// <summary>CNPJ de cliente único dentro da organização, quando informado.</summary>
    public const string CnpjDoClientePorOrganizacao = "ux_clientes_organizacao_id_cnpj";

    /// <summary>Uma conta de acesso associada a no máximo um motorista.</summary>
    public const string ContaDoMotorista = "ux_motoristas_usuario_id";

    /// <summary>Código humano da entrega único dentro da organização.</summary>
    public const string CodigoDaEntregaPorOrganizacao = "ux_entregas_organizacao_id_codigo";

    /// <summary>Posição do evento na timeline única por entrega.</summary>
    public const string SequenciaDoEventoDaEntrega = "ux_eventos_da_entrega_entrega_id_sequencia";
}
