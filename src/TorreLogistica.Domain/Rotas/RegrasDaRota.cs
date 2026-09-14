namespace TorreLogistica.Domain.Rotas;

/// <summary>Estados de uma rota.</summary>
/// <remarks>
/// Nesta fase existem as transições para <see cref="EmMontagem"/>, <see cref="Planejada"/> e
/// <see cref="Cancelada"/>. Iniciar e concluir nascem com a execução da rota pelo motorista.
/// </remarks>
public enum StatusDaRota
{
    /// <summary>Sendo montada: paradas, ordem, motorista, veículo e saída.</summary>
    EmMontagem = 1,

    /// <summary>Completa e confirmada para sair.</summary>
    Planejada = 2,

    /// <summary>Motorista saiu.</summary>
    EmAndamento = 3,

    /// <summary>Encerrada.</summary>
    Concluida = 4,

    /// <summary>Cancelada antes de sair.</summary>
    Cancelada = 5,
}

/// <summary>Tipos de evento da timeline da rota.</summary>
public enum TipoDeEventoDaRota
{
    /// <summary>Rota criada.</summary>
    Criada = 1,

    /// <summary>Entregas incluídas como paradas.</summary>
    ParadasAdicionadas = 2,

    /// <summary>Parada retirada.</summary>
    ParadaRemovida = 3,

    /// <summary>Ordem das paradas alterada.</summary>
    ParadasReordenadas = 4,

    /// <summary>Motorista definido ou trocado.</summary>
    MotoristaAtribuido = 5,

    /// <summary>Veículo definido ou trocado.</summary>
    VeiculoAtribuido = 6,

    /// <summary>Saída planejada definida ou alterada.</summary>
    SaidaPlanejada = 7,

    /// <summary>Rota confirmada como planejada.</summary>
    Planejada = 8,

    /// <summary>Rota planejada voltou para montagem.</summary>
    RetornouParaMontagem = 9,

    /// <summary>Rota cancelada.</summary>
    Cancelada = 10,

    /// <summary>Motorista saiu para a rota.</summary>
    Iniciada = 11,

    /// <summary>Rota encerrada com todas as entregas resolvidas.</summary>
    Concluida = 12,
}

/// <summary>Por que uma parada deixou a rota.</summary>
public enum MotivoDeRemocaoDeParada
{
    /// <summary>Quem monta a rota retirou.</summary>
    DecisaoDoPlanejamento = 1,

    /// <summary>A entrega foi cancelada.</summary>
    EntregaCancelada = 2,

    /// <summary>A rota foi cancelada.</summary>
    RotaCancelada = 3,

    /// <summary>A rota foi concluída.</summary>
    RotaConcluida = 4,
}

/// <summary>Regras de status da rota, em um lugar só.</summary>
public static class RegrasDaRota
{
    /// <summary>Paradas ativas numa rota.</summary>
    public const int QuantidadeMaximaDeParadas = 200;

    /// <summary>Entregas incluídas numa única operação.</summary>
    public const int QuantidadeMaximaPorInclusao = 100;

    /// <summary>
    /// Aceita mudança de estrutura — paradas, ordem, motorista, veículo, saída. Depois que o
    /// motorista sai, o que ele executa não muda por baixo dele; rota encerrada não muda mais.
    /// </summary>
    public static bool PermiteAlteracaoEstrutural(StatusDaRota status) =>
        status is StatusDaRota.EmMontagem or StatusDaRota.Planejada;

    /// <summary>
    /// Aceita troca de motorista. Além da montagem, também em andamento: é a reatribuição
    /// operacional — motorista que passou mal, veículo que quebrou com outro motorista assumindo.
    /// </summary>
    public static bool PermiteTrocaDeMotorista(StatusDaRota status) =>
        status is StatusDaRota.EmMontagem or StatusDaRota.Planejada or StatusDaRota.EmAndamento;

    /// <summary>Ocupa motorista, veículo e entregas.</summary>
    public static bool EhAtiva(StatusDaRota status) =>
        status is StatusDaRota.EmMontagem or StatusDaRota.Planejada or StatusDaRota.EmAndamento;

    /// <summary>Não sai mais deste status.</summary>
    public static bool EhFinal(StatusDaRota status) =>
        status is StatusDaRota.Concluida or StatusDaRota.Cancelada;

    /// <summary>Pode cancelar: só antes de sair.</summary>
    public static bool PermiteCancelamento(StatusDaRota status) =>
        status is StatusDaRota.EmMontagem or StatusDaRota.Planejada;
}
