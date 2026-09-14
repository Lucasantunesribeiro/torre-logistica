namespace TorreLogistica.Application.Abstracoes.Seguranca;

/// <summary>Resultado da conferência de uma senha.</summary>
public enum ResultadoDaVerificacaoDeSenha
{
    /// <summary>Senha incorreta.</summary>
    Invalida = 0,

    /// <summary>Senha correta.</summary>
    Valida = 1,

    /// <summary>Senha correta, mas o hash usa parâmetros antigos e deve ser recalculado.</summary>
    ValidaComRecalculo = 2,
}

/// <summary>Cálculo e conferência de hash de senha.</summary>
public interface IHasherDeSenha
{
    /// <summary>Gera o hash de uma senha.</summary>
    string GerarHash(string senha);

    /// <summary>Confere uma senha contra um hash.</summary>
    ResultadoDaVerificacaoDeSenha Verificar(string hash, string senha);

    /// <summary>
    /// Gasta o mesmo tempo de uma conferência real, contra um hash que não pertence a
    /// ninguém.
    /// </summary>
    /// <remarks>
    /// Sem isto, "organização ou e-mail inexistente" responde em microssegundos e "senha
    /// errada" em dezenas de milissegundos — e o tempo de resposta vira um oráculo para
    /// descobrir quais contas existem.
    /// </remarks>
    void VerificarContraHashFicticio(string senha);
}
