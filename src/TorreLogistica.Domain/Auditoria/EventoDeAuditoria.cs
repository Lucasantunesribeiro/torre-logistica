using TorreLogistica.Domain.Abstracoes.Erros;

namespace TorreLogistica.Domain.Auditoria;

/// <summary>Tipos de evento administrativo registrados na trilha de auditoria.</summary>
public static class TiposDeEventoDeAuditoria
{
    /// <summary>Conta criada por um administrador.</summary>
    public const string UsuarioCriado = "usuario_criado";

    /// <summary>Perfil de uma conta alterado.</summary>
    public const string PerfilAlterado = "perfil_alterado";

    /// <summary>Conta desativada.</summary>
    public const string UsuarioDesativado = "usuario_desativado";

    /// <summary>Token de renovação reapresentado; família de sessão revogada.</summary>
    public const string ReusoDeTokenDetectado = "reuso_de_token_detectado";

    /// <summary>
    /// Tipo de evento de cadastro operacional, no formato <c>recurso_acao</c> —
    /// por exemplo <c>veiculo_alterado</c> ou <c>motorista_inativado</c>.
    /// </summary>
    public static string DeCadastro(string recurso, string acao)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recurso);
        ArgumentException.ThrowIfNullOrWhiteSpace(acao);
        return $"{recurso}_{acao}";
    }
}

/// <summary>
/// Registro imutável de uma ação administrativa ou de segurança.
/// </summary>
/// <remarks>
/// A trilha é somente-inserção. Não basta a aplicação "não editar": o banco recusa
/// <c>UPDATE</c> e <c>DELETE</c> nesta tabela por trigger, então nem um defeito futuro
/// nem um acesso manual consegue reescrever o passado sem deixar a operação falhar.
/// </remarks>
public sealed class EventoDeAuditoria
{
    private EventoDeAuditoria()
    {
        Tipo = string.Empty;
        AlvoTipo = string.Empty;
        Dados = "{}";
    }

    /// <summary>Identificador (UUIDv7).</summary>
    public Guid Id { get; private set; }

    /// <summary>Organização em que o evento ocorreu.</summary>
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Instante do evento.</summary>
    public DateTimeOffset OcorridoEm { get; private set; }

    /// <summary>Tipo, um dos valores de <see cref="TiposDeEventoDeAuditoria"/>.</summary>
    public string Tipo { get; private set; }

    /// <summary>Conta que executou a ação; vazio quando o sistema agiu sozinho.</summary>
    public Guid? AutorUsuarioId { get; private set; }

    /// <summary>Tipo do recurso afetado.</summary>
    public string AlvoTipo { get; private set; }

    /// <summary>Identificador do recurso afetado.</summary>
    public Guid AlvoId { get; private set; }

    /// <summary>Detalhes em JSON. Nunca contém senha, hash ou token.</summary>
    public string Dados { get; private set; }

    /// <summary>Registra um evento.</summary>
    public static EventoDeAuditoria Registrar(
        Guid id,
        Guid organizacaoId,
        string tipo,
        Guid? autorUsuarioId,
        string alvoTipo,
        Guid alvoId,
        string dadosEmJson,
        DateTimeOffset agora)
    {
        ExcecaoDeDominio.LancarSe(id == Guid.Empty, "identificador_invalido", "Identificador inválido.");
        ExcecaoDeDominio.LancarSe(
            organizacaoId == Guid.Empty, "organizacao_invalida", "Organização inválida.");
        ArgumentException.ThrowIfNullOrWhiteSpace(tipo);
        ArgumentException.ThrowIfNullOrWhiteSpace(alvoTipo);

        return new EventoDeAuditoria
        {
            Id = id,
            OrganizacaoId = organizacaoId,
            Tipo = tipo,
            AutorUsuarioId = autorUsuarioId,
            AlvoTipo = alvoTipo,
            AlvoId = alvoId,
            Dados = string.IsNullOrWhiteSpace(dadosEmJson) ? "{}" : dadosEmJson,
            OcorridoEm = agora.ToUniversalTime(),
        };
    }
}
