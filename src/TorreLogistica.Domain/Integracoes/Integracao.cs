using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Comum;

namespace TorreLogistica.Domain.Integracoes;

/// <summary>Limites e formato das credenciais de integração.</summary>
/// <remarks>
/// <para>
/// A chave entregue ao sistema externo tem duas partes: um <b>identificador público</b>, que só serve para
/// achar a linha, e um <b>segredo</b>, que é conferido por hash. Sem o identificador, conferir uma chave
/// exigiria varrer a tabela inteira calculando hash — e o custo dessa varredura viraria o próprio ataque.
/// </para>
/// <para>
/// O segredo é guardado como SHA-256, e não com o hasher de senha. Senha humana é curta e adivinhável, e
/// por isso precisa de derivação lenta; um segredo de 32 bytes sorteados não tem dicionário que o alcance,
/// e derivação lenta aqui só puniria o sistema externo legítimo, que apresenta a chave a cada requisição.
/// </para>
/// </remarks>
public static class PoliticaDeIntegracao
{
    /// <summary>Prefixo da chave, para identificar o segredo em vazamento e em varredura de repositório.</summary>
    public const string PrefixoDaChave = "tlog";

    /// <summary>
    /// Separador entre as partes da chave.
    /// </summary>
    /// <remarks>
    /// Ponto, e não sublinhado: o alfabeto Base64Url inclui <c>-</c> e <c>_</c>, e usar um deles como
    /// separador quebraria exatamente as chaves cujo segredo sorteasse aquele caractere — uma falha
    /// intermitente, que aparece só para parte dos integradores.
    /// </remarks>
    public const char SeparadorDaChave = '.';

    /// <summary>Bytes de entropia do identificador público.</summary>
    public const int BytesDoIdentificador = 12;

    /// <summary>Bytes de entropia do segredo.</summary>
    public const int BytesDoSegredo = 32;

    /// <summary>Tamanho do identificador codificado em Base64Url sem preenchimento.</summary>
    public const int TamanhoDoIdentificador = 16;

    /// <summary>Tamanho do segredo codificado em Base64Url sem preenchimento.</summary>
    public const int TamanhoDoSegredo = 43;

    /// <summary>Tamanho do SHA-256 guardado.</summary>
    public const int TamanhoDoHashEmBytes = 32;

    /// <summary>Tamanho máximo do nome da integração.</summary>
    public const int TamanhoMaximoDoNome = 80;

    /// <summary>Tamanho máximo da chave de idempotência aceita do cliente.</summary>
    public const int TamanhoMaximoDaChaveDeIdempotencia = 120;

    /// <summary>Tamanho máximo do identificador da entrega no sistema de origem.</summary>
    public const int TamanhoMaximoDoIdentificadorExterno = 120;
}

/// <summary>
/// Credencial de um sistema externo, separada da identidade humana.
/// </summary>
/// <remarks>
/// Integração não é usuário: não tem perfil, não entra no console e não pode ser dona de uma sessão. O que
/// ela faz é registrado com autor vazio na timeline — "foi o sistema", e não "foi alguém" —, e a própria
/// integração fica identificada na trilha de auditoria.
/// </remarks>
public sealed class Integracao
{
    private Integracao()
    {
        Nome = string.Empty;
        IdentificadorPublico = string.Empty;
        HashDoSegredo = [];
    }

    /// <summary>Identificador (UUIDv7).</summary>
    public Guid Id { get; private set; }

    /// <summary>Organização dona da credencial.</summary>
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Nome de quem integra, para o operador saber o que está revogando.</summary>
    public string Nome { get; private set; }

    /// <summary>Parte pública da chave: acha a linha, não autoriza nada.</summary>
    public string IdentificadorPublico { get; private set; }

    /// <summary>SHA-256 do segredo entregue ao sistema externo.</summary>
    public byte[] HashDoSegredo { get; private set; }

    /// <summary>Instante da emissão.</summary>
    public DateTimeOffset CriadaEm { get; private set; }

    /// <summary>Conta que emitiu.</summary>
    public Guid? AutorUsuarioId { get; private set; }

    /// <summary>Quando foi revogada, se foi.</summary>
    public DateTimeOffset? RevogadaEm { get; private set; }

    /// <summary>Conta que revogou.</summary>
    public Guid? RevogadaPorUsuarioId { get; private set; }

    /// <summary>Último uso conhecido, para o operador identificar credencial esquecida.</summary>
    public DateTimeOffset? UltimoUsoEm { get; private set; }

    /// <summary>Emite uma credencial.</summary>
    public static Integracao Emitir(
        Guid id,
        Guid organizacaoId,
        string? nome,
        string identificadorPublico,
        byte[] hashDoSegredo,
        Guid? autorUsuarioId,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(hashDoSegredo);

        ExcecaoDeDominio.LancarSe(
            hashDoSegredo.Length != PoliticaDeIntegracao.TamanhoDoHashEmBytes,
            "hash_do_segredo_invalido",
            "O hash do segredo da integração precisa ter 32 bytes.");

        ExcecaoDeDominio.LancarSe(
            string.IsNullOrWhiteSpace(identificadorPublico)
            || identificadorPublico.Length != PoliticaDeIntegracao.TamanhoDoIdentificador,
            "identificador_publico_invalido",
            "Identificador público da integração inválido.");

        return new Integracao
        {
            Id = id,
            OrganizacaoId = organizacaoId,
            Nome = TextoNormalizado.Obrigatorio(
                nome, PoliticaDeIntegracao.TamanhoMaximoDoNome, "nome_invalido", "O nome da integração"),
            IdentificadorPublico = identificadorPublico,
            HashDoSegredo = hashDoSegredo,
            CriadaEm = agora,
            AutorUsuarioId = autorUsuarioId,
        };
    }

    /// <summary>Revoga a credencial. Revogar de novo não muda a primeira data.</summary>
    public void Revogar(Guid? autorUsuarioId, DateTimeOffset agora)
    {
        if (RevogadaEm is not null)
        {
            return;
        }

        RevogadaEm = agora;
        RevogadaPorUsuarioId = autorUsuarioId;
    }

    /// <summary>Indica se a credencial ainda autentica.</summary>
    public bool EstaAtiva => RevogadaEm is null;

    /// <summary>
    /// Anota o uso, com granularidade de minuto.
    /// </summary>
    /// <remarks>
    /// Gravar a cada requisição transformaria uma linha em ponto de contenção para todo o tráfego da
    /// integração. O minuto basta para responder "esta credencial ainda é usada?".
    /// </remarks>
    /// <returns><see langword="true"/> quando o valor mudou e vale gravar.</returns>
    public bool RegistrarUso(DateTimeOffset agora)
    {
        if (UltimoUsoEm is { } ultimo && agora - ultimo < TimeSpan.FromMinutes(1))
        {
            return false;
        }

        UltimoUsoEm = agora;
        return true;
    }
}

/// <summary>
/// Requisição já processada de uma integração, guardada para que o reenvio não repita o efeito.
/// </summary>
/// <remarks>
/// <para>
/// Guarda o hash do corpo recebido junto da chave de idempotência. Reenviar a mesma chave com o mesmo
/// corpo devolve o mesmo resultado; reenviar a mesma chave com corpo <b>diferente</b> é conflito — o
/// cliente reaproveitou uma chave, e responder o resultado antigo esconderia o defeito dele.
/// </para>
/// <para>
/// Somente-inserção: o registro nasce junto do efeito, na mesma transação, e nunca é alterado.
/// </para>
/// </remarks>
public sealed class RequisicaoDeIntegracao
{
    private RequisicaoDeIntegracao()
    {
        Chave = string.Empty;
        HashDaRequisicao = [];
        Recurso = string.Empty;
    }

    /// <summary>Identificador (UUIDv7).</summary>
    public Guid Id { get; private set; }

    /// <summary>Organização.</summary>
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Integração que enviou.</summary>
    public Guid IntegracaoId { get; private set; }

    /// <summary>Valor do cabeçalho <c>Idempotency-Key</c>.</summary>
    public string Chave { get; private set; }

    /// <summary>SHA-256 do corpo recebido.</summary>
    public byte[] HashDaRequisicao { get; private set; }

    /// <summary>Recurso afetado, para leitura da trilha.</summary>
    public string Recurso { get; private set; }

    /// <summary>Recurso criado pela requisição.</summary>
    public Guid RecursoId { get; private set; }

    /// <summary>Instante do processamento.</summary>
    public DateTimeOffset ProcessadaEm { get; private set; }

    /// <summary>Registra o processamento.</summary>
    public static RequisicaoDeIntegracao Registrar(
        Guid id,
        Guid organizacaoId,
        Guid integracaoId,
        string? chave,
        byte[] hashDaRequisicao,
        string recurso,
        Guid recursoId,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(hashDaRequisicao);

        ExcecaoDeDominio.LancarSe(
            hashDaRequisicao.Length != PoliticaDeIntegracao.TamanhoDoHashEmBytes,
            "hash_da_requisicao_invalido",
            "O hash da requisição precisa ter 32 bytes.");

        return new RequisicaoDeIntegracao
        {
            Id = id,
            OrganizacaoId = organizacaoId,
            IntegracaoId = integracaoId,
            Chave = TextoNormalizado.Obrigatorio(
                chave,
                PoliticaDeIntegracao.TamanhoMaximoDaChaveDeIdempotencia,
                "chave_de_idempotencia_invalida",
                "A chave de idempotência"),
            HashDaRequisicao = hashDaRequisicao,
            Recurso = recurso,
            RecursoId = recursoId,
            ProcessadaEm = agora,
        };
    }
}

/// <summary>
/// Vínculo entre o identificador da entrega no sistema de origem e a entrega da Torre.
/// </summary>
/// <remarks>
/// Fica em tabela própria, e não numa coluna de <c>entregas</c>: o identificador pertence à relação com
/// <b>aquela</b> integração, não ao agregado da entrega. Assim duas integrações da mesma organização podem
/// usar numerações independentes sem disputar o mesmo campo.
/// </remarks>
public sealed class ReferenciaExternaDaEntrega
{
    private ReferenciaExternaDaEntrega()
    {
        IdentificadorExterno = string.Empty;
    }

    /// <summary>Identificador (UUIDv7).</summary>
    public Guid Id { get; private set; }

    /// <summary>Organização.</summary>
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Integração que criou a entrega.</summary>
    public Guid IntegracaoId { get; private set; }

    /// <summary>Entrega da Torre.</summary>
    public Guid EntregaId { get; private set; }

    /// <summary>Identificador no sistema de origem.</summary>
    public string IdentificadorExterno { get; private set; }

    /// <summary>Instante do vínculo.</summary>
    public DateTimeOffset CriadaEm { get; private set; }

    /// <summary>Registra o vínculo.</summary>
    public static ReferenciaExternaDaEntrega Registrar(
        Guid id,
        Guid organizacaoId,
        Guid integracaoId,
        Guid entregaId,
        string? identificadorExterno,
        DateTimeOffset agora) => new()
        {
            Id = id,
            OrganizacaoId = organizacaoId,
            IntegracaoId = integracaoId,
            EntregaId = entregaId,
            IdentificadorExterno = TextoNormalizado.Obrigatorio(
                identificadorExterno,
                PoliticaDeIntegracao.TamanhoMaximoDoIdentificadorExterno,
                "identificador_externo_invalido",
                "O identificador da entrega no sistema de origem"),
            CriadaEm = agora,
        };
}
