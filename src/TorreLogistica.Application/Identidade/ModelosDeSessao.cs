using TorreLogistica.Domain.Identidade;

namespace TorreLogistica.Application.Identidade;

/// <summary>Credenciais de login.</summary>
/// <param name="Organizacao">Identificador público (slug) da organização.</param>
/// <param name="Email">E-mail da conta.</param>
/// <param name="Senha">Senha.</param>
/// <param name="Canal">Canal pelo qual o login foi pedido.</param>
public sealed record ComandoDeAutenticacao(
    string? Organizacao,
    string? Email,
    string? Senha,
    CanalDeAcesso Canal);

/// <summary>Conta autenticada, como devolvida ao cliente.</summary>
public sealed record UsuarioAutenticado(
    Guid Id,
    string Nome,
    string Email,
    Perfil Perfil,
    Guid OrganizacaoId,
    string OrganizacaoNome,
    string OrganizacaoSlug);

/// <summary>Resultado de login ou renovação bem-sucedidos.</summary>
/// <param name="Usuario">Conta autenticada.</param>
/// <param name="TokenDeAcesso">Token de curta duração.</param>
/// <param name="TokenDeAcessoExpiraEm">Validade do token de acesso.</param>
/// <param name="TokenDeRenovacao">Valor cru do token de renovação — só vai para o cookie.</param>
/// <param name="TokenDeRenovacaoExpiraEm">Validade do token de renovação.</param>
/// <param name="Canal">Canal da sessão.</param>
public sealed record SessaoEmitida(
    UsuarioAutenticado Usuario,
    string TokenDeAcesso,
    DateTimeOffset TokenDeAcessoExpiraEm,
    string TokenDeRenovacao,
    DateTimeOffset TokenDeRenovacaoExpiraEm,
    CanalDeAcesso Canal);
