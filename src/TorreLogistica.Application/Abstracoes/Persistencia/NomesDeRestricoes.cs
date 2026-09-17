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

    /// <summary>Um alerta aberto por chave de deduplicação na organização.</summary>
    public const string AlertaAbertoPorChave = "ux_alertas_operacionais_chave_aberto";

    /// <summary>Posição do evento no ciclo de vida única por alerta.</summary>
    public const string SequenciaDoEventoDoAlerta = "ux_eventos_de_alerta_alerta_id_sequencia";

    /// <summary>Uma operação do aparelho por motorista: a mesma operação repetida não executa de novo.</summary>
    public const string OperacaoDoCliente = "ux_operacoes_do_cliente_operacao";

    /// <summary>Um comprovante por entrega: repetir a conclusão não cria outra prova.</summary>
    public const string ComprovantePorEntrega = "ux_comprovantes_entrega_id";

    /// <summary>Uma chave de objeto por arquivo: o mesmo arquivo não é registrado duas vezes.</summary>
    public const string ChaveDoArquivoDoComprovante = "ux_arquivos_do_comprovante_chave";

    /// <summary>Hash do link de rastreamento: é por ele que o token apresentado vira uma entrega.</summary>
    public const string HashDoTokenDeRastreamento = "ux_tokens_de_rastreamento_hash";

    /// <summary>Um link de rastreamento ativo por entrega: emitir outro revoga o anterior.</summary>
    public const string TokenDeRastreamentoAtivoPorEntrega = "ux_tokens_de_rastreamento_entrega_ativo";

    /// <summary>Parte pública da chave de integração: é por ela que a credencial apresentada acha a linha.</summary>
    public const string IdentificadorPublicoDaIntegracao = "ux_integracoes_identificador_publico";

    /// <summary>Uma chave de idempotência por integração: o reenvio não repete o efeito.</summary>
    public const string ChaveDeIdempotenciaPorIntegracao = "ux_requisicoes_de_integracao_chave";

    /// <summary>Um identificador de origem por integração: o mesmo pedido do ERP não vira duas entregas.</summary>
    public const string ReferenciaExternaPorIntegracao = "ux_referencias_externas_identificador";
}
