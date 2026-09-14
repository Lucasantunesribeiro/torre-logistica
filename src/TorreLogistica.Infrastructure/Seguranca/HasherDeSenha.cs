using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TorreLogistica.Application.Abstracoes.Seguranca;

namespace TorreLogistica.Infrastructure.Seguranca;

/// <summary>
/// Hash de senha com PBKDF2-HMAC-SHA512, pela implementação do ASP.NET Core Identity.
/// </summary>
/// <remarks>
/// <para>
/// Usa a implementação mantida e auditada do Identity em vez de montar PBKDF2 à mão: o
/// formato já carrega algoritmo, iterações e sal, e a conferência já é em tempo constante.
/// Só o pacote <c>Microsoft.Extensions.Identity.Core</c> entra — nada de ASP.NET Core.
/// </para>
/// <para>
/// 210.000 iterações seguem a recomendação da OWASP para PBKDF2-HMAC-SHA512. Hash
/// gravado com menos iterações é reconhecido na conferência e recalculado no login.
/// </para>
/// </remarks>
public sealed class HasherDeSenha : IHasherDeSenha
{
    /// <summary>Iterações de PBKDF2.</summary>
    public const int Iteracoes = 210_000;

    // PasswordHasher exige um "usuário", mas a implementação padrão não o usa.
    private static readonly object Sentinela = new();

    private readonly PasswordHasher<object> _hasher = new(Options.Create(new PasswordHasherOptions
    {
        CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
        IterationCount = Iteracoes,
    }));

    private readonly Lazy<string> _hashFicticio;
    private readonly ILogger _log;

    /// <summary>Cria o hasher.</summary>
    public HasherDeSenha(ILogger<HasherDeSenha>? log = null)
    {
        _log = (ILogger?)log ?? NullLogger.Instance;

        // Calculado uma vez, com valor aleatório que ninguém conhece.
        _hashFicticio = new Lazy<string>(
            () => _hasher.HashPassword(Sentinela, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public string GerarHash(string senha)
    {
        ArgumentNullException.ThrowIfNull(senha);
        return _hasher.HashPassword(Sentinela, senha);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Hash corrompido no banco não pode derrubar o login com erro 500: além de indisponível
    /// para aquela conta, a resposta diferente denunciaria que a conta existe — contas
    /// inexistentes nunca chegam aqui. A conferência é recusada, o tempo de uma
    /// conferência real é gasto mesmo assim, e o problema vai para o log.
    /// </remarks>
    public ResultadoDaVerificacaoDeSenha Verificar(string hash, string senha)
    {
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(senha);

        try
        {
            return _hasher.VerifyHashedPassword(Sentinela, hash, senha) switch
            {
                PasswordVerificationResult.Success => ResultadoDaVerificacaoDeSenha.Valida,
                PasswordVerificationResult.SuccessRehashNeeded => ResultadoDaVerificacaoDeSenha.ValidaComRecalculo,
                _ => ResultadoDaVerificacaoDeSenha.Invalida,
            };
        }
        catch (FormatException)
        {
            // O valor do hash não entra no log.
            _log.LogError("Hash de senha armazenado em formato inválido; conferência recusada.");
            VerificarContraHashFicticio(senha);
            return ResultadoDaVerificacaoDeSenha.Invalida;
        }
    }

    /// <inheritdoc />
    public void VerificarContraHashFicticio(string senha)
    {
        ArgumentNullException.ThrowIfNull(senha);
        _ = _hasher.VerifyHashedPassword(Sentinela, _hashFicticio.Value, senha);
    }
}
