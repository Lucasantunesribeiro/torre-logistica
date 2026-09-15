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

    /// <summary>Código humano da rota único dentro da organização.</summary>
    public const string CodigoDaRotaPorOrganizacao = "ux_rotas_organizacao_id_codigo";

    /// <summary>Uma entrega em no máximo uma parada ativa.</summary>
    public const string ParadaAtivaDaEntrega = "ux_paradas_entrega_id_ativa";

    /// <summary>Um motorista em no máximo uma rota ativa por dia.</summary>
    public const string MotoristaEmRotaAtivaNoDia = "ux_rotas_motorista_por_data_ativa";

    /// <summary>Um veículo em no máximo uma rota ativa por dia.</summary>
    public const string VeiculoEmRotaAtivaNoDia = "ux_rotas_veiculo_por_data_ativa";

    /// <summary>Um evento de localização por motorista: a mesma posição reenviada não duplica.</summary>
    public const string EventoDeLocalizacaoDoMotorista = "ux_posicoes_evento_de_localizacao";

    /// <summary>Posição do evento na timeline única por rota.</summary>
    public const string SequenciaDoEventoDaRota = "ux_eventos_da_rota_rota_id_sequencia";

    /// <summary>Posição do registro no histórico de previsões única por entrega.</summary>
    public const string SequenciaDoRegistroDePrevisao = "ux_registros_de_previsao_entrega_id_sequencia";

    /// <summary>Uma previsão atual por entrega.</summary>
    public const string PrevisaoDaEntrega = "pk_previsoes_da_entrega";
}
