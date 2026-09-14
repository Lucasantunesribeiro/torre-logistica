using TorreLogistica.Domain.Identidade;

namespace TorreLogistica.Application.Abstracoes.Identidade;

/// <summary>
/// Organização efetiva da operação em curso.
/// </summary>
/// <remarks>
/// Deriva exclusivamente de uma autoridade autenticada — hoje, a sessão. Nunca de campo
/// de requisição. Quando não há autoridade, o valor é <see langword="null"/> e os filtros
/// de tenant passam a não enxergar dado algum: falha fechada.
/// </remarks>
public interface IContextoDoTenant
{
    /// <summary>Organização da sessão autenticada, ou <see langword="null"/>.</summary>
    Guid? OrganizacaoId { get; }
}

/// <summary>Conta autenticada que executa a operação em curso.</summary>
public interface IContextoDoUsuario : IContextoDoTenant
{
    /// <summary>Há sessão autenticada.</summary>
    bool EstaAutenticado { get; }

    /// <summary>Conta autenticada. Lança se não houver sessão.</summary>
    Guid UsuarioId { get; }

    /// <summary>Organização da sessão. Lança se não houver sessão.</summary>
    Guid OrganizacaoIdAutenticada { get; }

    /// <summary>Perfil carregado no token de acesso.</summary>
    Perfil Perfil { get; }

    /// <summary>Sessão corrente.</summary>
    Guid SessaoId { get; }

    /// <summary>Canal da sessão.</summary>
    CanalDeAcesso Canal { get; }
}
