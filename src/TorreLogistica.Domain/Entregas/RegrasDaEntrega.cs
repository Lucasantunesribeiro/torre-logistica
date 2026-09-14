using System.Collections.Frozen;

namespace TorreLogistica.Domain.Entregas;

/// <summary>
/// Estados de uma entrega (CLAUDE.md, seção 10).
/// </summary>
/// <remarks>
/// O status só muda por um <see cref="ComandoDaEntrega"/>, conforme a
/// <see cref="MaquinaDeEstadosDaEntrega"/>. Não existe atribuição direta de status.
/// </remarks>
public enum StatusDaEntrega
{
    /// <summary>Recebida, ainda sem planejamento.</summary>
    Criada = 1,

    /// <summary>Incluída numa rota.</summary>
    Planejada = 2,

    /// <summary>Com motorista definido.</summary>
    Atribuida = 3,

    /// <summary>Motorista saiu para rota.</summary>
    EmRota = 4,

    /// <summary>Motorista chegou ao destino.</summary>
    ProximaDoDestino = 5,

    /// <summary>Concluída com sucesso.</summary>
    Entregue = 6,

    /// <summary>Tentativa sem sucesso.</summary>
    TentativaFrustrada = 7,

    /// <summary>Nova tentativa marcada.</summary>
    Reagendada = 8,

    /// <summary>Cancelada.</summary>
    Cancelada = 9,
}

/// <summary>Comandos que mudam o status da entrega — cada um, um caso de uso explícito.</summary>
public enum ComandoDaEntrega
{
    /// <summary>Incluir numa rota.</summary>
    Planejar = 1,

    /// <summary>Definir o motorista da rota antes da saída.</summary>
    Atribuir = 2,

    /// <summary>Trocar o motorista com a entrega já em execução.</summary>
    Reatribuir = 3,

    /// <summary>Retirar de uma rota que ainda não saiu.</summary>
    RetirarDaRota = 4,

    /// <summary>Saída do motorista para a rota.</summary>
    IniciarRota = 5,

    /// <summary>Motorista chegou ao destino.</summary>
    RegistrarChegada = 6,

    /// <summary>Entrega feita.</summary>
    Concluir = 7,

    /// <summary>Tentativa sem sucesso.</summary>
    RegistrarTentativaFrustrada = 8,

    /// <summary>Marcar nova janela depois de tentativa sem sucesso.</summary>
    Reagendar = 9,

    /// <summary>Cancelar.</summary>
    Cancelar = 10,
}

/// <summary>Motivo informado no cancelamento.</summary>
public enum MotivoDeCancelamento
{
    /// <summary>O cliente pediu.</summary>
    SolicitacaoDoCliente = 1,

    /// <summary>Endereço incorreto ou inexistente.</summary>
    EnderecoIncorreto = 2,

    /// <summary>Entrega registrada em duplicidade.</summary>
    CadastroDuplicado = 3,

    /// <summary>Outro motivo, descrito em texto.</summary>
    Outro = 4,
}

/// <summary>Por que a tentativa de entrega não deu certo.</summary>
public enum MotivoDeTentativaFrustrada
{
    /// <summary>Ninguém para receber.</summary>
    DestinatarioAusente = 1,

    /// <summary>Endereço não encontrado no local.</summary>
    EnderecoNaoLocalizado = 2,

    /// <summary>Destinatário recusou.</summary>
    RecusadaPeloDestinatario = 3,

    /// <summary>Estabelecimento fechado.</summary>
    LocalFechado = 4,

    /// <summary>Acesso impedido (portaria, área restrita).</summary>
    AcessoImpedido = 5,
}

/// <summary>Tipos de evento da timeline da entrega.</summary>
public enum TipoDeEventoDaEntrega
{
    /// <summary>Entrega criada.</summary>
    Criada = 1,

    /// <summary>Dados da entrega alterados.</summary>
    DadosAlterados = 2,

    /// <summary>Entrega cancelada.</summary>
    Cancelada = 3,

    /// <summary>Entrega incluída numa rota.</summary>
    Planejada = 4,

    /// <summary>Motorista da rota definido para a entrega, ou trocado antes da saída.</summary>
    Atribuida = 5,

    /// <summary>Entrega retirada da rota antes da saída.</summary>
    RetiradaDaRota = 6,

    /// <summary>Motorista trocado com a entrega em execução.</summary>
    Reatribuida = 7,

    /// <summary>Motorista saiu para a rota.</summary>
    SaiuParaRota = 8,

    /// <summary>Chegada ao destino registrada.</summary>
    ChegadaRegistrada = 9,

    /// <summary>Entrega concluída.</summary>
    Entregue = 10,

    /// <summary>Tentativa sem sucesso registrada.</summary>
    TentativaFrustrada = 11,

    /// <summary>Nova janela marcada.</summary>
    Reagendada = 12,
}

/// <summary>Nomes dos campos alteráveis, usados nas regras, na timeline e na auditoria.</summary>
public static class CamposDaEntrega
{
    /// <summary>Cliente contratante.</summary>
    public const string Cliente = "cliente";

    /// <summary>Destinatário.</summary>
    public const string Destinatario = "destinatario";

    /// <summary>Endereço de entrega.</summary>
    public const string Endereco = "endereco";

    /// <summary>Coordenada de destino.</summary>
    public const string Localizacao = "localizacao";

    /// <summary>Janela prometida.</summary>
    public const string JanelaPrometida = "janelaPrometida";

    /// <summary>Observações operacionais.</summary>
    public const string Observacoes = "observacoes";

    /// <summary>Todos os campos alteráveis.</summary>
    public static IReadOnlyList<string> Todos { get; } =
        [Cliente, Destinatario, Endereco, Localizacao, JanelaPrometida, Observacoes];
}

/// <summary>
/// A máquina de estados da entrega: para cada comando, de quais status ele parte e para qual
/// status leva.
/// </summary>
/// <remarks>
/// <para>
/// É a única fonte da regra de transição. Os métodos da <see cref="Entrega"/> consultam esta
/// tabela; as regras derivadas (<see cref="RegrasDaEntrega"/>) também. Uma transição nova é uma
/// linha aqui e um teste que a especifica.
/// </para>
/// <para>
/// O que não está na tabela é proibido. Em particular: nada sai de <c>Entregue</c> ou de
/// <c>Cancelada</c> — uma entrega concluída nunca volta para rota (CLAUDE.md, seção 10).
/// </para>
/// </remarks>
public static class MaquinaDeEstadosDaEntrega
{
    private static readonly FrozenDictionary<ComandoDaEntrega, FrozenDictionary<StatusDaEntrega, StatusDaEntrega>> Transicoes =
        new Dictionary<ComandoDaEntrega, FrozenDictionary<StatusDaEntrega, StatusDaEntrega>>
        {
            [ComandoDaEntrega.Planejar] = De(
                (StatusDaEntrega.Criada, StatusDaEntrega.Planejada),
                (StatusDaEntrega.Reagendada, StatusDaEntrega.Planejada)),
            [ComandoDaEntrega.Atribuir] = De(
                (StatusDaEntrega.Planejada, StatusDaEntrega.Atribuida),
                (StatusDaEntrega.Atribuida, StatusDaEntrega.Atribuida)),
            [ComandoDaEntrega.Reatribuir] = De(
                (StatusDaEntrega.EmRota, StatusDaEntrega.EmRota),
                (StatusDaEntrega.ProximaDoDestino, StatusDaEntrega.ProximaDoDestino)),
            [ComandoDaEntrega.RetirarDaRota] = De(
                (StatusDaEntrega.Planejada, StatusDaEntrega.Criada),
                (StatusDaEntrega.Atribuida, StatusDaEntrega.Criada)),
            [ComandoDaEntrega.IniciarRota] = De(
                (StatusDaEntrega.Atribuida, StatusDaEntrega.EmRota)),
            [ComandoDaEntrega.RegistrarChegada] = De(
                (StatusDaEntrega.EmRota, StatusDaEntrega.ProximaDoDestino)),
            [ComandoDaEntrega.Concluir] = De(
                (StatusDaEntrega.EmRota, StatusDaEntrega.Entregue),
                (StatusDaEntrega.ProximaDoDestino, StatusDaEntrega.Entregue)),
            [ComandoDaEntrega.RegistrarTentativaFrustrada] = De(
                (StatusDaEntrega.EmRota, StatusDaEntrega.TentativaFrustrada),
                (StatusDaEntrega.ProximaDoDestino, StatusDaEntrega.TentativaFrustrada)),
            [ComandoDaEntrega.Reagendar] = De(
                (StatusDaEntrega.TentativaFrustrada, StatusDaEntrega.Reagendada),
                (StatusDaEntrega.Reagendada, StatusDaEntrega.Reagendada)),
            [ComandoDaEntrega.Cancelar] = De(
                (StatusDaEntrega.Criada, StatusDaEntrega.Cancelada),
                (StatusDaEntrega.Planejada, StatusDaEntrega.Cancelada),
                (StatusDaEntrega.Atribuida, StatusDaEntrega.Cancelada),
                (StatusDaEntrega.TentativaFrustrada, StatusDaEntrega.Cancelada),
                (StatusDaEntrega.Reagendada, StatusDaEntrega.Cancelada)),
        }.ToFrozenDictionary();

    /// <summary>Status resultante do comando, ou <see langword="null"/> se a transição é proibida.</summary>
    public static StatusDaEntrega? Destino(ComandoDaEntrega comando, StatusDaEntrega origem) =>
        Transicoes.TryGetValue(comando, out var transicoes) && transicoes.TryGetValue(origem, out var destino)
            ? destino
            : null;

    private static FrozenDictionary<StatusDaEntrega, StatusDaEntrega> De(params (StatusDaEntrega Origem, StatusDaEntrega Destino)[] pares) =>
        pares.ToFrozenDictionary(par => par.Origem, par => par.Destino);
}

/// <summary>
/// Regras derivadas da máquina de estados e tabela de campos editáveis por status.
/// </summary>
/// <remarks>
/// A ideia central da edição: depois que a entrega sai para rota, o que o motorista está
/// executando não muda por baixo dele. Cliente congela ainda antes, no planejamento.
/// </remarks>
public static class RegrasDaEntrega
{
    private static readonly FrozenSet<StatusDaEntrega> AntesDaSaidaParaRota = new[]
    {
        StatusDaEntrega.Criada,
        StatusDaEntrega.Planejada,
        StatusDaEntrega.Atribuida,
        StatusDaEntrega.Reagendada,
    }.ToFrozenSet();

    /// <summary>Status do qual a entrega não sai mais.</summary>
    public static bool EhFinal(StatusDaEntrega status) =>
        status is StatusDaEntrega.Entregue or StatusDaEntrega.Cancelada;

    /// <summary>Pode cancelar.</summary>
    public static bool PermiteCancelamento(StatusDaEntrega status) =>
        MaquinaDeEstadosDaEntrega.Destino(ComandoDaEntrega.Cancelar, status) is not null;

    /// <summary>Pode entrar numa rota: recém-criada ou reagendada.</summary>
    public static bool PodeEntrarEmRota(StatusDaEntrega status) =>
        MaquinaDeEstadosDaEntrega.Destino(ComandoDaEntrega.Planejar, status) is not null;

    /// <summary>Está numa rota que ainda não saiu.</summary>
    public static bool EstaEmRotaNaoIniciada(StatusDaEntrega status) =>
        MaquinaDeEstadosDaEntrega.Destino(ComandoDaEntrega.RetirarDaRota, status) is not null;

    /// <summary>Está sendo executada pelo motorista.</summary>
    public static bool EstaEmExecucao(StatusDaEntrega status) =>
        status is StatusDaEntrega.EmRota or StatusDaEntrega.ProximaDoDestino;

    /// <summary>
    /// Já tem resultado para a rota em que está: entregue, tentativa sem sucesso (reagendada ou
    /// não) ou cancelada. A rota só é concluída quando todas as paradas estão assim.
    /// </summary>
    public static bool EstaResolvidaNaRota(StatusDaEntrega status) =>
        status is StatusDaEntrega.Entregue or StatusDaEntrega.TentativaFrustrada or StatusDaEntrega.Reagendada or StatusDaEntrega.Cancelada;

    /// <summary>O campo pode mudar com a entrega neste status.</summary>
    public static bool CampoEditavel(string campo, StatusDaEntrega status) => campo switch
    {
        CamposDaEntrega.Cliente => status == StatusDaEntrega.Criada,
        CamposDaEntrega.Destinatario
            or CamposDaEntrega.Endereco
            or CamposDaEntrega.Localizacao
            or CamposDaEntrega.JanelaPrometida => AntesDaSaidaParaRota.Contains(status),
        CamposDaEntrega.Observacoes => !EhFinal(status),
        _ => throw new ArgumentOutOfRangeException(nameof(campo), campo, "Campo de entrega desconhecido."),
    };
}
