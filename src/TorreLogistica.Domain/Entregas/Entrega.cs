using System.Text.Json;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Comum;

namespace TorreLogistica.Domain.Entregas;

/// <summary>Dados alteráveis de uma entrega, como a operação os informa.</summary>
/// <remarks>
/// Classe com propriedades somente-leitura, e não <c>record</c> posicional: o posicional gera
/// <c>init</c> público, e o domínio não expõe setter (teste de arquitetura).
/// </remarks>
/// <param name="clienteId">Cliente contratante.</param>
/// <param name="destinatarioId">Quem recebe.</param>
/// <param name="endereco">Endereço de entrega.</param>
/// <param name="localizacao">Coordenada de destino, quando conhecida.</param>
/// <param name="janela">Janela prometida.</param>
/// <param name="observacoes">Observações operacionais.</param>
public sealed class DadosDaEntrega(
    Guid clienteId,
    Guid destinatarioId,
    Endereco endereco,
    CoordenadaGeografica? localizacao,
    JanelaDeEntrega janela,
    string? observacoes)
{
    /// <summary>Cliente contratante.</summary>
    public Guid ClienteId { get; } = clienteId;

    /// <summary>Quem recebe.</summary>
    public Guid DestinatarioId { get; } = destinatarioId;

    /// <summary>Endereço de entrega.</summary>
    public Endereco Endereco { get; } = endereco;

    /// <summary>Coordenada de destino, quando conhecida.</summary>
    public CoordenadaGeografica? Localizacao { get; } = localizacao;

    /// <summary>Janela prometida.</summary>
    public JanelaDeEntrega Janela { get; } = janela;

    /// <summary>Observações operacionais.</summary>
    public string? Observacoes { get; } = observacoes;
}

/// <summary>Resultado de uma alteração de dados.</summary>
/// <param name="campos">Campos que mudaram.</param>
/// <param name="evento">Evento da timeline, ou <see langword="null"/> quando nada mudou.</param>
public sealed class AlteracaoDaEntrega(IReadOnlyList<string> campos, EventoDaEntrega? evento)
{
    /// <summary>Campos que mudaram.</summary>
    public IReadOnlyList<string> Campos { get; } = campos;

    /// <summary>Evento da timeline, ou <see langword="null"/> quando nada mudou.</summary>
    public EventoDaEntrega? Evento { get; } = evento;
}

/// <summary>
/// Entrega: o agregado central da operação.
/// </summary>
/// <remarks>
/// <para>
/// O status só muda por operação com intenção explícita — <see cref="Criar"/>,
/// <see cref="Cancelar"/> e, nas fases seguintes, planejar, atribuir, sair para rota e concluir.
/// Não existe <c>entrega.Status = ...</c>: toda operação confere o status atual, aplica a
/// regra e devolve o evento que entra na timeline.
/// </para>
/// <para>
/// O endereço é uma <b>cópia</b> tirada na criação, não uma referência ao endereço do
/// destinatário. Se o cadastro do destinatário mudar amanhã, a entrega de hoje continua
/// dizendo para onde foi — o histórico não é reescrito por tabela.
/// </para>
/// </remarks>
public sealed class Entrega
{
    /// <summary>Tamanho máximo das observações.</summary>
    public const int TamanhoMaximoDasObservacoes = 500;

    /// <summary>Tamanho máximo da descrição do cancelamento.</summary>
    public const int TamanhoMaximoDaDescricaoDoCancelamento = 280;

    private static readonly JsonSerializerOptions OpcoesDeJson = new(JsonSerializerDefaults.Web);

    private Entrega()
    {
        Codigo = string.Empty;
        Endereco = null!;
        Janela = null!;
    }

    /// <summary>Identificador (UUIDv7).</summary>
    public Guid Id { get; private set; }

    /// <summary>Organização dona da entrega.</summary>
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Código humano, único na organização.</summary>
    public string Codigo { get; private set; }

    /// <summary>Cliente contratante.</summary>
    public Guid ClienteId { get; private set; }

    /// <summary>Quem recebe.</summary>
    public Guid DestinatarioId { get; private set; }

    /// <summary>Endereço de entrega, copiado no momento em que foi informado.</summary>
    public Endereco Endereco { get; private set; }

    /// <summary>Coordenada de destino, quando conhecida.</summary>
    public CoordenadaGeografica? Localizacao { get; private set; }

    /// <summary>Janela prometida.</summary>
    public JanelaDeEntrega Janela { get; private set; }

    /// <summary>Observações operacionais.</summary>
    public string? Observacoes { get; private set; }

    /// <summary>Status atual.</summary>
    public StatusDaEntrega Status { get; private set; }

    /// <summary>Motivo, se cancelada.</summary>
    public MotivoDeCancelamento? MotivoDoCancelamento { get; private set; }

    /// <summary>Descrição do cancelamento, quando informada.</summary>
    public string? DescricaoDoCancelamento { get; private set; }

    /// <summary>Instante da criação.</summary>
    public DateTimeOffset CriadaEm { get; private set; }

    /// <summary>Instante da última mudança.</summary>
    public DateTimeOffset AtualizadaEm { get; private set; }

    /// <summary>Instante do cancelamento.</summary>
    public DateTimeOffset? CanceladaEm { get; private set; }

    /// <summary>Sequência do último evento da timeline.</summary>
    public int UltimaSequenciaDeEvento { get; private set; }

    /// <summary>Versão da linha, para concorrência otimista.</summary>
    public uint Versao { get; private set; }

    /// <summary>Cria a entrega e o primeiro evento da timeline.</summary>
    public static (Entrega Entrega, EventoDaEntrega Evento) Criar(
        Guid id,
        Guid organizacaoId,
        string codigo,
        DadosDaEntrega dados,
        Guid? autorUsuarioId,
        Guid idDoEvento,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(dados);
        ExcecaoDeDominio.LancarSe(id == Guid.Empty || idDoEvento == Guid.Empty, "identificador_invalido", "Identificador inválido.");
        ExcecaoDeDominio.LancarSe(organizacaoId == Guid.Empty, "organizacao_invalida", "Organização inválida.");
        ExcecaoDeDominio.LancarSe(!CodigoDaEntrega.EhValido(codigo), "codigo_invalido", "Código de entrega inválido.");
        ValidarReferencias(dados);

        var instante = agora.ToUniversalTime();
        GarantirJanelaFutura(dados.Janela, instante);

        var entrega = new Entrega
        {
            Id = id,
            OrganizacaoId = organizacaoId,
            Codigo = codigo,
            ClienteId = dados.ClienteId,
            DestinatarioId = dados.DestinatarioId,
            Endereco = dados.Endereco,
            Localizacao = dados.Localizacao,
            Janela = dados.Janela,
            Observacoes = ValidarObservacoes(dados.Observacoes),
            Status = StatusDaEntrega.Criada,
            CriadaEm = instante,
            AtualizadaEm = instante,
        };

        var evento = entrega.RegistrarEvento(
            TipoDeEventoDaEntrega.Criada, new { }, autorUsuarioId, idDoEvento, instante);

        return (entrega, evento);
    }

    /// <summary>
    /// Altera dados. Cada campo que muda precisa ser editável no status atual; se um só não
    /// for, nada é aplicado.
    /// </summary>
    public AlteracaoDaEntrega AtualizarDados(
        DadosDaEntrega dados,
        Guid? autorUsuarioId,
        Guid idDoEvento,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(dados);

        if (RegrasDaEntrega.EhFinal(Status))
        {
            throw ExcecaoDeDominio.Conflito(
                "entrega_nao_editavel",
                "Entrega cancelada ou concluída não aceita alteração.");
        }

        ValidarReferencias(dados);
        var observacoes = ValidarObservacoes(dados.Observacoes);
        var instante = agora.ToUniversalTime();

        var campos = new List<string>();
        AnotarSeMudou(campos, CamposDaEntrega.Cliente, ClienteId, dados.ClienteId);
        AnotarSeMudou(campos, CamposDaEntrega.Destinatario, DestinatarioId, dados.DestinatarioId);
        AnotarSeMudou(campos, CamposDaEntrega.Endereco, Endereco, dados.Endereco);
        AnotarSeMudou(campos, CamposDaEntrega.Localizacao, Localizacao, dados.Localizacao);
        AnotarSeMudou(campos, CamposDaEntrega.JanelaPrometida, Janela, dados.Janela);
        AnotarSeMudou(campos, CamposDaEntrega.Observacoes, Observacoes, observacoes);

        // Tudo conferido antes de aplicar qualquer coisa: alteração parcial não existe.
        foreach (var campo in campos.Where(campo => !RegrasDaEntrega.CampoEditavel(campo, Status)))
        {
            throw ExcecaoDeDominio.Conflito(
                "campo_nao_editavel",
                $"O campo {campo} não pode mudar com a entrega no status {Status}.");
        }

        if (campos.Contains(CamposDaEntrega.JanelaPrometida))
        {
            GarantirJanelaFutura(dados.Janela, instante);
        }

        if (campos.Count == 0)
        {
            return new AlteracaoDaEntrega([], null);
        }

        ClienteId = dados.ClienteId;
        DestinatarioId = dados.DestinatarioId;
        Endereco = dados.Endereco;
        Localizacao = dados.Localizacao;
        Janela = dados.Janela;
        Observacoes = observacoes;
        AtualizadaEm = instante;

        var evento = RegistrarEvento(
            TipoDeEventoDaEntrega.DadosAlterados, new { campos }, autorUsuarioId, idDoEvento, instante);

        return new AlteracaoDaEntrega(campos, evento);
    }

    /// <summary>
    /// Cancela. Repetir o cancelamento de entrega já cancelada não tem efeito e não gera
    /// evento — o pedido foi atendido da primeira vez.
    /// </summary>
    /// <returns>O evento, ou <see langword="null"/> se já estava cancelada.</returns>
    public EventoDaEntrega? Cancelar(
        MotivoDeCancelamento motivo,
        string? descricao,
        Guid? autorUsuarioId,
        Guid idDoEvento,
        DateTimeOffset agora)
    {
        ExcecaoDeDominio.LancarSe(!Enum.IsDefined(motivo), "motivo_invalido", "Motivo de cancelamento inválido.");

        var descricaoValida = TextoNormalizado.Opcional(
            descricao, TamanhoMaximoDaDescricaoDoCancelamento, "descricao_invalida", "A descrição do cancelamento");

        ExcecaoDeDominio.LancarSe(
            motivo == MotivoDeCancelamento.Outro && descricaoValida is null,
            "motivo_exige_descricao",
            "Descreva o motivo quando ele for \"Outro\".");

        if (Status == StatusDaEntrega.Cancelada)
        {
            return null;
        }

        if (!RegrasDaEntrega.PermiteCancelamento(Status))
        {
            throw ExcecaoDeDominio.Conflito(
                "cancelamento_nao_permitido",
                $"Entrega no status {Status} não pode ser cancelada.");
        }

        var anterior = Status;
        var instante = agora.ToUniversalTime();

        Status = StatusDaEntrega.Cancelada;
        MotivoDoCancelamento = motivo;
        DescricaoDoCancelamento = descricaoValida;
        CanceladaEm = instante;
        AtualizadaEm = instante;

        return RegistrarEvento(
            TipoDeEventoDaEntrega.Cancelada,
            new { statusAnterior = anterior.ToString(), motivo = motivo.ToString() },
            autorUsuarioId,
            idDoEvento,
            instante);
    }

    /// <summary>Inclui a entrega numa rota: <c>Criada</c> ou <c>Reagendada</c> → <c>Planejada</c>.</summary>
    public EventoDaEntrega Planejar(Guid rotaId, Guid? autorUsuarioId, Guid idDoEvento, DateTimeOffset agora)
    {
        ExcecaoDeDominio.LancarSe(rotaId == Guid.Empty, "rota_invalida", "Rota inválida.");

        if (!RegrasDaEntrega.PodeEntrarEmRota(Status))
        {
            throw ExcecaoDeDominio.Conflito(
                "entrega_inelegivel_para_rota",
                $"Entrega no status {Status} não pode entrar em rota.");
        }

        var anterior = Status;
        var instante = agora.ToUniversalTime();
        Status = StatusDaEntrega.Planejada;
        AtualizadaEm = instante;

        return RegistrarEvento(
            TipoDeEventoDaEntrega.Planejada,
            new { rotaId, statusAnterior = anterior.ToString() },
            autorUsuarioId,
            idDoEvento,
            instante);
    }

    /// <summary>
    /// Define o motorista da rota para a entrega: <c>Planejada</c> → <c>Atribuida</c>. Chamado de
    /// novo quando a rota troca de motorista — a reatribuição fica registrada na timeline.
    /// </summary>
    public EventoDaEntrega Atribuir(Guid rotaId, Guid motoristaId, Guid? autorUsuarioId, Guid idDoEvento, DateTimeOffset agora)
    {
        ExcecaoDeDominio.LancarSe(rotaId == Guid.Empty, "rota_invalida", "Rota inválida.");
        ExcecaoDeDominio.LancarSe(motoristaId == Guid.Empty, "motorista_invalido", "Motorista inválido.");

        if (!RegrasDaEntrega.EstaEmRotaNaoIniciada(Status))
        {
            throw ExcecaoDeDominio.Conflito(
                "entrega_nao_planejada",
                $"Entrega no status {Status} não recebe motorista: ela precisa estar planejada numa rota.");
        }

        var instante = agora.ToUniversalTime();
        Status = StatusDaEntrega.Atribuida;
        AtualizadaEm = instante;

        return RegistrarEvento(
            TipoDeEventoDaEntrega.Atribuida, new { rotaId, motoristaId }, autorUsuarioId, idDoEvento, instante);
    }

    /// <summary>Retira a entrega de uma rota que ainda não saiu: volta a <c>Criada</c>.</summary>
    public EventoDaEntrega RetirarDaRota(Guid rotaId, Guid? autorUsuarioId, Guid idDoEvento, DateTimeOffset agora)
    {
        ExcecaoDeDominio.LancarSe(rotaId == Guid.Empty, "rota_invalida", "Rota inválida.");

        if (!RegrasDaEntrega.EstaEmRotaNaoIniciada(Status))
        {
            throw ExcecaoDeDominio.Conflito(
                "entrega_fora_de_rota",
                $"Entrega no status {Status} não está numa rota que possa ser desfeita.");
        }

        var anterior = Status;
        var instante = agora.ToUniversalTime();
        Status = StatusDaEntrega.Criada;
        AtualizadaEm = instante;

        return RegistrarEvento(
            TipoDeEventoDaEntrega.RetiradaDaRota,
            new { rotaId, statusAnterior = anterior.ToString() },
            autorUsuarioId,
            idDoEvento,
            instante);
    }

    private static void ValidarReferencias(DadosDaEntrega dados)
    {
        ArgumentNullException.ThrowIfNull(dados.Endereco);
        ArgumentNullException.ThrowIfNull(dados.Janela);
        ExcecaoDeDominio.LancarSe(dados.ClienteId == Guid.Empty, "cliente_invalido", "Informe o cliente.");
        ExcecaoDeDominio.LancarSe(dados.DestinatarioId == Guid.Empty, "destinatario_invalido", "Informe o destinatário.");
    }

    private static void GarantirJanelaFutura(JanelaDeEntrega janela, DateTimeOffset agora) =>
        ExcecaoDeDominio.LancarSe(
            janela.Fim <= agora,
            "janela_no_passado",
            "A janela prometida precisa terminar no futuro.");

    private static string? ValidarObservacoes(string? observacoes) =>
        TextoNormalizado.Opcional(observacoes, TamanhoMaximoDasObservacoes, "observacoes_invalidas", "As observações");

    private static void AnotarSeMudou<T>(List<string> campos, string campo, T atual, T novo)
    {
        if (!EqualityComparer<T>.Default.Equals(atual, novo))
        {
            campos.Add(campo);
        }
    }

    private EventoDaEntrega RegistrarEvento(
        TipoDeEventoDaEntrega tipo,
        object detalhes,
        Guid? autorUsuarioId,
        Guid idDoEvento,
        DateTimeOffset instante)
    {
        UltimaSequenciaDeEvento++;

        return EventoDaEntrega.Registrar(
            idDoEvento,
            OrganizacaoId,
            Id,
            UltimaSequenciaDeEvento,
            tipo,
            Status,
            autorUsuarioId,
            JsonSerializer.Serialize(detalhes, OpcoesDeJson),
            instante);
    }
}
