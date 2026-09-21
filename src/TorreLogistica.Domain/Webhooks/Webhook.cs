using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Comum;

namespace TorreLogistica.Domain.Webhooks;

/// <summary>Eventos que a Torre publica para fora.</summary>
/// <remarks>
/// Vocabulário fechado e estável: o nome do evento é contrato público com o assinante, e renomear um
/// quebra integração de terceiro sem aviso. Evento novo entra como nome novo.
/// </remarks>
public static class TiposDeEventoDeWebhook
{
    /// <summary>Motorista saiu para a rota com a entrega.</summary>
    public const string EntregaIniciada = "delivery.started";

    /// <summary>Situação do SLA passou a risco ou atraso.</summary>
    public const string EntregaEmRisco = "delivery.at_risk";

    /// <summary>Tentativa sem sucesso registrada.</summary>
    public const string TentativaFrustrada = "delivery.failed_attempt";

    /// <summary>Entrega concluída.</summary>
    public const string EntregaConcluida = "delivery.completed";

    /// <summary>Todos os eventos publicáveis.</summary>
    public static readonly IReadOnlySet<string> Todos = new HashSet<string>(StringComparer.Ordinal)
    {
        EntregaIniciada,
        EntregaEmRisco,
        TentativaFrustrada,
        EntregaConcluida,
    };

    /// <summary>Indica se o nome é um evento conhecido.</summary>
    public static bool EhConhecido(string? tipo) => tipo is not null && Todos.Contains(tipo);
}

/// <summary>Estado de uma entrega de webhook.</summary>
public enum EstadoDaEntregaDeWebhook
{
    /// <summary>Esperando tentativa.</summary>
    Pendente = 1,

    /// <summary>O assinante confirmou o recebimento.</summary>
    Entregue = 2,

    /// <summary>Tentativas esgotadas. Só sai daqui por reenvio manual.</summary>
    Falhada = 3,
}

/// <summary>Limites e prazos da entrega de webhook.</summary>
/// <remarks>
/// O backoff cresce depressa de propósito: assinante fora do ar costuma demorar minutos ou horas para
/// voltar, e insistir de segundo em segundo transforma a indisponibilidade dele em carga nossa.
/// </remarks>
public static class PoliticaDeWebhook
{
    /// <summary>Tentativas antes de a entrega ir para o estado falhado.</summary>
    public const int MaximoDeTentativas = 6;

    /// <summary>Tamanho máximo da URL de destino.</summary>
    public const int TamanhoMaximoDaUrl = 500;

    /// <summary>Tamanho máximo do nome da assinatura.</summary>
    public const int TamanhoMaximoDoNome = 80;

    /// <summary>Bytes de entropia do segredo de assinatura.</summary>
    public const int BytesDoSegredo = 32;

    /// <summary>Tamanho máximo guardado de uma mensagem de erro.</summary>
    public const int TamanhoMaximoDoErro = 500;

    /// <summary>Tempo limite de cada tentativa.</summary>
    public static readonly TimeSpan TempoLimiteDaTentativa = TimeSpan.FromSeconds(10);

    /// <summary>Janela que o assinante deve aceitar entre o carimbo assinado e o recebimento.</summary>
    public static readonly TimeSpan ToleranciaDoCarimbo = TimeSpan.FromMinutes(5);

    /// <summary>Espera antes da tentativa de número informado. A primeira é imediata.</summary>
    public static TimeSpan EsperaAntesDaTentativa(int numeroDaTentativa) => numeroDaTentativa switch
    {
        <= 1 => TimeSpan.Zero,
        2 => TimeSpan.FromSeconds(30),
        3 => TimeSpan.FromMinutes(2),
        4 => TimeSpan.FromMinutes(8),
        5 => TimeSpan.FromMinutes(32),
        _ => TimeSpan.FromHours(2),
    };
}

/// <summary>
/// Evento gravado no mesmo commit do estado que o originou.
/// </summary>
/// <remarks>
/// É o que elimina o modo de falha desta fase: gravar a entrega como concluída e perder o aviso porque a
/// publicação acontecia fora da transação e o processo caiu no meio. Aqui o evento nasce junto com o fato;
/// publicar vira problema de outro processo, que pode falhar e tentar de novo sem nada se perder.
/// </remarks>
public sealed class MensagemDoOutbox
{
    private MensagemDoOutbox()
    {
        Tipo = string.Empty;
        Conteudo = "{}";
    }

    /// <summary>Identificador (UUIDv7). Vai no cabeçalho do webhook, para o assinante deduplicar.</summary>
    public Guid Id { get; private set; }

    /// <summary>Organização dona do fato.</summary>
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Nome do evento, do vocabulário de <see cref="TiposDeEventoDeWebhook"/>.</summary>
    public string Tipo { get; private set; }

    /// <summary>Entrega a que o evento se refere.</summary>
    public Guid EntregaId { get; private set; }

    /// <summary>Corpo do evento, em JSON.</summary>
    public string Conteudo { get; private set; }

    /// <summary>Instante do fato no domínio.</summary>
    public DateTimeOffset OcorridoEm { get; private set; }

    /// <summary>
    /// O <c>traceparent</c> da operação que produziu o evento, para ligar o que sai depois ao que
    /// aconteceu antes.
    /// </summary>
    /// <remarks>
    /// Sem ele, a entrega do webhook seria um rastro solto: ninguém ligaria "o cliente não recebeu o
    /// aviso" à conclusão da entrega que devia tê-lo produzido. É dado de diagnóstico, não de negócio —
    /// nulo é aceitável, e nada no domínio depende dele.
    /// </remarks>
    public string? Rastro { get; private set; }

    /// <summary>Instante da gravação.</summary>
    public DateTimeOffset CriadaEm { get; private set; }

    /// <summary>Quando o despachante transformou o evento em entregas.</summary>
    public DateTimeOffset? DespachadaEm { get; private set; }

    /// <summary>Tentativas de despacho que falharam.</summary>
    public int TentativasDeDespacho { get; private set; }

    /// <summary>Antes disto o despachante ignora a mensagem.</summary>
    public DateTimeOffset DisponivelEm { get; private set; }

    /// <summary>Último erro de despacho, para diagnóstico.</summary>
    public string? UltimoErro { get; private set; }

    /// <summary>Cria o evento.</summary>
    public static MensagemDoOutbox Criar(
        Guid id,
        Guid organizacaoId,
        string tipo,
        Guid entregaId,
        string conteudo,
        DateTimeOffset ocorridoEm,
        DateTimeOffset agora,
        string? rastro = null)
    {
        ExcecaoDeDominio.LancarSe(
            !TiposDeEventoDeWebhook.EhConhecido(tipo), "tipo_de_evento_invalido", "Tipo de evento desconhecido.");

        ExcecaoDeDominio.LancarSe(
            string.IsNullOrWhiteSpace(conteudo), "conteudo_invalido", "O conteúdo do evento é obrigatório.");

        return new MensagemDoOutbox
        {
            Id = id,
            OrganizacaoId = organizacaoId,
            Tipo = tipo,
            EntregaId = entregaId,
            Conteudo = conteudo,
            OcorridoEm = ocorridoEm,
            CriadaEm = agora,
            DisponivelEm = agora,
            Rastro = rastro,
        };
    }

    /// <summary>Marca a mensagem como já transformada em entregas.</summary>
    public void MarcarDespachada(DateTimeOffset agora) => DespachadaEm ??= agora;

    /// <summary>Adia o despacho depois de uma falha.</summary>
    public void AdiarPorFalha(string? erro, DateTimeOffset agora)
    {
        TentativasDeDespacho++;
        UltimoErro = TextoNormalizado.Opcional(erro, PoliticaDeWebhook.TamanhoMaximoDoErro, "erro_invalido", "O erro");
        DisponivelEm = agora + PoliticaDeWebhook.EsperaAntesDaTentativa(TentativasDeDespacho + 1);
    }
}

/// <summary>
/// Assinatura de um endereço externo que quer receber eventos.
/// </summary>
/// <remarks>
/// O segredo é guardado <b>cifrado</b>, e não como hash: diferente de senha, ele precisa ser recuperável
/// para assinar cada entrega. A proteção aqui é criptografia em repouso com chave de configuração, não
/// derivação lenta.
/// </remarks>
public sealed class AssinaturaDeWebhook
{
    private readonly List<string> _eventos = [];

    private AssinaturaDeWebhook()
    {
        Nome = string.Empty;
        Url = string.Empty;
        SegredoCifrado = [];
    }

    /// <summary>Identificador (UUIDv7).</summary>
    public Guid Id { get; private set; }

    /// <summary>Organização dona da assinatura.</summary>
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Nome, para o operador saber o que está revogando.</summary>
    public string Nome { get; private set; }

    /// <summary>Endereço que recebe o POST.</summary>
    public string Url { get; private set; }

    /// <summary>Segredo de assinatura, cifrado em repouso.</summary>
    public byte[] SegredoCifrado { get; private set; }

    /// <summary>Eventos assinados. Vazio significa todos.</summary>
    public IReadOnlyList<string> Eventos => _eventos;

    /// <summary>Instante da criação.</summary>
    public DateTimeOffset CriadaEm { get; private set; }

    /// <summary>Conta que criou.</summary>
    public Guid? AutorUsuarioId { get; private set; }

    /// <summary>Quando foi revogada, se foi.</summary>
    public DateTimeOffset? RevogadaEm { get; private set; }

    /// <summary>Assinatura revogada não recebe mais nada.</summary>
    public bool EstaAtiva => RevogadaEm is null;

    /// <summary>Cria a assinatura.</summary>
    public static AssinaturaDeWebhook Criar(
        Guid id,
        Guid organizacaoId,
        string? nome,
        string? url,
        byte[] segredoCifrado,
        IReadOnlyCollection<string>? eventos,
        Guid? autorUsuarioId,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(segredoCifrado);

        ExcecaoDeDominio.LancarSe(
            !Uri.TryCreate(url, UriKind.Absolute, out var endereco)
            || (endereco.Scheme != Uri.UriSchemeHttps && endereco.Scheme != Uri.UriSchemeHttp),
            "url_invalida",
            "Informe um endereço http ou https absoluto.");

        ExcecaoDeDominio.LancarSe(
            endereco!.AbsoluteUri.Length > PoliticaDeWebhook.TamanhoMaximoDaUrl,
            "url_invalida",
            $"O endereço passa de {PoliticaDeWebhook.TamanhoMaximoDaUrl} caracteres.");

        var assinados = (eventos ?? [])
            .Select(evento => (evento ?? string.Empty).Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var evento in assinados)
        {
            ExcecaoDeDominio.LancarSe(
                !TiposDeEventoDeWebhook.EhConhecido(evento),
                "tipo_de_evento_invalido",
                $"Evento desconhecido: {evento}. Conhecidos: {string.Join(", ", TiposDeEventoDeWebhook.Todos)}.");
        }

        var assinatura = new AssinaturaDeWebhook
        {
            Id = id,
            OrganizacaoId = organizacaoId,
            Nome = TextoNormalizado.Obrigatorio(nome, PoliticaDeWebhook.TamanhoMaximoDoNome, "nome_invalido", "O nome da assinatura"),
            Url = endereco.AbsoluteUri,
            SegredoCifrado = segredoCifrado,
            CriadaEm = agora,
            AutorUsuarioId = autorUsuarioId,
        };

        assinatura._eventos.AddRange(assinados);
        return assinatura;
    }

    /// <summary>Indica se esta assinatura quer o evento informado.</summary>
    public bool Assina(string tipo) => _eventos.Count == 0 || _eventos.Contains(tipo, StringComparer.Ordinal);

    /// <summary>Revoga a assinatura. Revogar de novo não muda a primeira data.</summary>
    public void Revogar(DateTimeOffset agora) => RevogadaEm ??= agora;
}

/// <summary>
/// Uma entrega pendente: um evento para um assinante.
/// </summary>
/// <remarks>
/// Única por (assinatura, mensagem) — é a dedução do consumidor. O despachante pode rodar duas vezes, ou
/// duas instâncias podem pegar a mesma mensagem: o índice garante que o assinante receba aquele evento uma
/// vez só, mesmo com a fila entregando a mensagem mais de uma vez.
/// </remarks>
public sealed class EntregaDeWebhook
{
    private EntregaDeWebhook()
    {
        Tipo = string.Empty;
        Conteudo = "{}";
        Url = string.Empty;
    }

    /// <summary>Identificador (UUIDv7).</summary>
    public Guid Id { get; private set; }

    /// <summary>Organização.</summary>
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Assinatura de destino.</summary>
    public Guid AssinaturaId { get; private set; }

    /// <summary>Mensagem de origem no outbox.</summary>
    public Guid MensagemId { get; private set; }

    /// <summary>Nome do evento.</summary>
    public string Tipo { get; private set; }

    /// <summary>Corpo enviado.</summary>
    public string Conteudo { get; private set; }

    /// <summary>Endereço no momento da criação: mudar a assinatura depois não reescreve o histórico.</summary>
    public string Url { get; private set; }

    /// <summary>Situação atual.</summary>
    public EstadoDaEntregaDeWebhook Estado { get; private set; }

    /// <summary>Tentativas já feitas.</summary>
    public int Tentativas { get; private set; }

    /// <summary>Antes disto a entrega não é tentada.</summary>
    public DateTimeOffset DisponivelEm { get; private set; }

    /// <summary>Instante da criação.</summary>
    public DateTimeOffset CriadaEm { get; private set; }

    /// <summary>O <c>traceparent</c> herdado da mensagem que originou esta entrega.</summary>
    public string? Rastro { get; private set; }

    /// <summary>Quando foi entregue ou desistiu.</summary>
    public DateTimeOffset? ConcluidaEm { get; private set; }

    /// <summary>Status HTTP da última tentativa.</summary>
    public int? UltimoStatus { get; private set; }

    /// <summary>Erro da última tentativa.</summary>
    public string? UltimoErro { get; private set; }

    /// <summary>Cria a entrega pendente.</summary>
    public static EntregaDeWebhook Criar(
        Guid id,
        Guid organizacaoId,
        AssinaturaDeWebhook assinatura,
        MensagemDoOutbox mensagem,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(assinatura);
        ArgumentNullException.ThrowIfNull(mensagem);

        return new EntregaDeWebhook
        {
            Id = id,
            OrganizacaoId = organizacaoId,
            AssinaturaId = assinatura.Id,
            MensagemId = mensagem.Id,
            Tipo = mensagem.Tipo,
            Conteudo = mensagem.Conteudo,
            Url = assinatura.Url,
            Estado = EstadoDaEntregaDeWebhook.Pendente,
            DisponivelEm = agora,
            CriadaEm = agora,
            Rastro = mensagem.Rastro,
        };
    }

    /// <summary>Registra uma tentativa bem-sucedida.</summary>
    public TentativaDeWebhook RegistrarSucesso(Guid idDaTentativa, int status, int duracaoEmMilissegundos, DateTimeOffset agora)
    {
        Tentativas++;
        Estado = EstadoDaEntregaDeWebhook.Entregue;
        ConcluidaEm = agora;
        UltimoStatus = status;
        UltimoErro = null;

        return TentativaDeWebhook.Registrar(idDaTentativa, OrganizacaoId, Id, Tentativas, status, duracaoEmMilissegundos, null, agora);
    }

    /// <summary>
    /// Registra uma tentativa que falhou, adiando a próxima — ou desistindo, esgotadas as tentativas.
    /// </summary>
    public TentativaDeWebhook RegistrarFalha(
        Guid idDaTentativa,
        int? status,
        string? erro,
        int duracaoEmMilissegundos,
        DateTimeOffset agora)
    {
        Tentativas++;
        UltimoStatus = status;
        UltimoErro = TextoNormalizado.Opcional(erro, PoliticaDeWebhook.TamanhoMaximoDoErro, "erro_invalido", "O erro");

        if (Tentativas >= PoliticaDeWebhook.MaximoDeTentativas)
        {
            // Não some: fica visível no estado falhado, que é a nossa carta na fila morta. Sair dali é
            // decisão de gente, pelo reenvio manual.
            Estado = EstadoDaEntregaDeWebhook.Falhada;
            ConcluidaEm = agora;
        }
        else
        {
            DisponivelEm = agora + PoliticaDeWebhook.EsperaAntesDaTentativa(Tentativas + 1);
        }

        return TentativaDeWebhook.Registrar(
            idDaTentativa, OrganizacaoId, Id, Tentativas, status, duracaoEmMilissegundos, UltimoErro, agora);
    }

    /// <summary>
    /// Empurra a disponibilidade para a frente enquanto uma rodada tenta entregar.
    /// </summary>
    /// <remarks>
    /// É o arrendamento que substitui manter a transação aberta durante o POST: outra instância não pega
    /// esta entrega no intervalo, e se o processo morrer no meio ela volta sozinha quando o prazo vencer.
    /// </remarks>
    public void AdiarPorArrendamento(DateTimeOffset agora, TimeSpan duracao) => DisponivelEm = agora + duracao;

    /// <summary>Recoloca uma entrega falhada na fila, por decisão do operador.</summary>
    public void Reenviar(DateTimeOffset agora)
    {
        ExcecaoDeDominio.LancarSe(
            Estado != EstadoDaEntregaDeWebhook.Falhada,
            "entrega_nao_reenviavel",
            "Só entrega falhada é reenviada: pendente já está na fila e entregue não se repete.");

        Estado = EstadoDaEntregaDeWebhook.Pendente;
        Tentativas = 0;
        ConcluidaEm = null;
        DisponivelEm = agora;
    }
}

/// <summary>Uma tentativa de entrega, guardada para sempre.</summary>
/// <remarks>
/// Somente-inserção: é o histórico que responde "tentamos avisar?" numa discussão com o assinante, e
/// histórico que pode ser reescrito não responde nada.
/// </remarks>
public sealed class TentativaDeWebhook
{
    private TentativaDeWebhook()
    {
    }

    /// <summary>Identificador (UUIDv7).</summary>
    public Guid Id { get; private set; }

    /// <summary>Organização.</summary>
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Entrega tentada.</summary>
    public Guid EntregaDeWebhookId { get; private set; }

    /// <summary>Número da tentativa, a partir de 1.</summary>
    public int Numero { get; private set; }

    /// <summary>Status HTTP recebido, quando houve resposta.</summary>
    public int? Status { get; private set; }

    /// <summary>Duração da tentativa.</summary>
    public int DuracaoEmMilissegundos { get; private set; }

    /// <summary>Erro, quando não houve resposta utilizável.</summary>
    public string? Erro { get; private set; }

    /// <summary>Instante da tentativa.</summary>
    public DateTimeOffset TentadaEm { get; private set; }

    internal static TentativaDeWebhook Registrar(
        Guid id,
        Guid organizacaoId,
        Guid entregaDeWebhookId,
        int numero,
        int? status,
        int duracaoEmMilissegundos,
        string? erro,
        DateTimeOffset agora) => new()
        {
            Id = id,
            OrganizacaoId = organizacaoId,
            EntregaDeWebhookId = entregaDeWebhookId,
            Numero = numero,
            Status = status,
            DuracaoEmMilissegundos = duracaoEmMilissegundos,
            Erro = erro,
            TentadaEm = agora,
        };
}
