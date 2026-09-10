namespace TorreLogistica.Domain.Abstracoes.Identificadores;

/// <summary>
/// Gera o identificador interno padrão das entidades do sistema (UUIDv7).
/// </summary>
/// <remarks>
/// Existe como abstração — e não como chamada estática — porque o simulador e os
/// testes determinísticos precisam controlar a sequência de identificadores gerada.
/// Ver <c>docs/adr/0004-uuidv7-como-identificador.md</c>.
/// </remarks>
public interface IGeradorDeIdentificador
{
    /// <summary>Cria um novo identificador UUIDv7.</summary>
    Guid Novo();
}
