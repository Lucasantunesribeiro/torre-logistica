namespace TorreLogistica.Domain.Identidade;

/// <summary>
/// Por onde uma sessão entra no sistema.
/// </summary>
/// <remarks>
/// Cada canal tem login, token, cookie de renovação e esquema de autenticação próprios.
/// Um token do canal <see cref="Motorista"/> não é credencial para o console — não é
/// que ele "não tenha permissão": para o console ele simplesmente não é um token válido.
/// </remarks>
public enum CanalDeAcesso
{
    /// <summary>Console operacional: administrador, supervisor e operador.</summary>
    Operacao = 1,

    /// <summary>PWA do motorista.</summary>
    Motorista = 2,
}
