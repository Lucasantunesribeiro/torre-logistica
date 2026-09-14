namespace TorreLogistica.Domain.Identidade;

/// <summary>
/// Perfil de acesso de uma conta.
/// </summary>
/// <remarks>
/// Os três primeiros operam o console. <see cref="Motorista"/> não é "mais um papel" ao
/// lado deles: pertence a outro canal de acesso, com outra sessão e outro token, e não
/// herda permissão administrativa alguma. A fronteira é <see cref="CanalDeAcesso"/>.
/// </remarks>
public enum Perfil
{
    /// <summary>Administra a organização: usuários, perfis e configurações.</summary>
    Administrador = 1,

    /// <summary>Supervisiona a operação e consulta a equipe.</summary>
    Supervisor = 2,

    /// <summary>Opera o dia a dia: entregas, rotas e ocorrências.</summary>
    Operador = 3,

    /// <summary>Executa entregas pela PWA. Canal próprio, sem acesso ao console.</summary>
    Motorista = 4,
}

/// <summary>Regras que ligam perfil e canal de acesso.</summary>
public static class RegrasDePerfil
{
    /// <summary>Canal pelo qual uma conta com este perfil pode se autenticar.</summary>
    public static CanalDeAcesso Canal(this Perfil perfil) => perfil switch
    {
        Perfil.Administrador or Perfil.Supervisor or Perfil.Operador => CanalDeAcesso.Operacao,
        Perfil.Motorista => CanalDeAcesso.Motorista,
        _ => throw new ArgumentOutOfRangeException(nameof(perfil), perfil, "Perfil desconhecido."),
    };

    /// <summary>Indica se o valor corresponde a um perfil declarado.</summary>
    public static bool EhDefinido(this Perfil perfil) => Enum.IsDefined(perfil);
}
