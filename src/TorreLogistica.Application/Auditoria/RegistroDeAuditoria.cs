using System.Text.Json;
using TorreLogistica.Domain.Abstracoes.Identificadores;
using TorreLogistica.Domain.Auditoria;

namespace TorreLogistica.Application.Auditoria;

/// <summary>Monta eventos de auditoria com detalhes serializados de forma consistente.</summary>
public static class RegistroDeAuditoria
{
    private static readonly JsonSerializerOptions Serializacao = new(JsonSerializerDefaults.Web);

    /// <summary>Cria um evento pronto para ser adicionado ao contexto.</summary>
    /// <param name="identificadores">Gerador de identificador.</param>
    /// <param name="agora">Instante do evento.</param>
    /// <param name="organizacaoId">Organização.</param>
    /// <param name="tipo">Tipo do evento.</param>
    /// <param name="autorUsuarioId">Autor, ou <see langword="null"/> para ação do sistema.</param>
    /// <param name="alvoTipo">Tipo do recurso afetado.</param>
    /// <param name="alvoId">Recurso afetado.</param>
    /// <param name="dados">Objeto com os detalhes. Nunca inclua senha, hash ou token.</param>
    public static EventoDeAuditoria Criar(
        IGeradorDeIdentificador identificadores,
        DateTimeOffset agora,
        Guid organizacaoId,
        string tipo,
        Guid? autorUsuarioId,
        string alvoTipo,
        Guid alvoId,
        object dados)
    {
        ArgumentNullException.ThrowIfNull(identificadores);

        return EventoDeAuditoria.Registrar(
            identificadores.Novo(),
            organizacaoId,
            tipo,
            autorUsuarioId,
            alvoTipo,
            alvoId,
            JsonSerializer.Serialize(dados, Serializacao),
            agora);
    }
}
