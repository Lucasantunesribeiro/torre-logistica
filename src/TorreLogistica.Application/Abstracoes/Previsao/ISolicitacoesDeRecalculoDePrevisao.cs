namespace TorreLogistica.Application.Abstracoes.Previsao;

/// <summary>O que motivou o pedido de recálculo.</summary>
public enum OrigemDoRecalculo
{
    /// <summary>A posição atual de um motorista avançou.</summary>
    Posicao = 1,

    /// <summary>Uma entrega mudou na execução.</summary>
    Entrega = 2,

    /// <summary>Reavaliação periódica: o tempo passa mesmo sem evento.</summary>
    Reavaliacao = 3,
}

/// <summary>Pedido de recálculo da previsão de uma rota, identificada direta ou indiretamente.</summary>
/// <param name="OrganizacaoId">Organização — define o tenant do processamento.</param>
/// <param name="Origem">O que motivou.</param>
/// <param name="RotaId">Rota, quando conhecida.</param>
/// <param name="EntregaId">Entrega que mudou.</param>
/// <param name="MotoristaId">Motorista cuja posição avançou.</param>
public sealed record SolicitacaoDeRecalculo(
    Guid OrganizacaoId,
    OrigemDoRecalculo Origem,
    Guid? RotaId,
    Guid? EntregaId,
    Guid? MotoristaId);

/// <summary>
/// Pedidos de recálculo de previsão, feitos depois do commit de quem mudou o estado.
/// </summary>
/// <remarks>
/// O recálculo consulta o provedor de rotas, que pode demorar. Por isso não roda na requisição que
/// originou a mudança: a ingestão de GPS e os comandos do motorista respondem sem esperar o provedor.
/// Pedido perdido (processo que cai) é coberto pela reavaliação periódica.
/// </remarks>
public interface ISolicitacoesDeRecalculoDePrevisao
{
    /// <summary>Enfileira os pedidos. Não bloqueia.</summary>
    void Solicitar(IReadOnlyCollection<SolicitacaoDeRecalculo> solicitacoes);
}
