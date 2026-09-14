using System.Security.Cryptography;
using TorreLogistica.Domain.Identidade;

namespace TorreLogistica.UnitTests.Dominio;

/// <summary>Construtores de objetos de domínio válidos para teste.</summary>
internal static class Fabrica
{
    public static readonly DateTimeOffset Agora = new(2026, 9, 14, 8, 0, 0, TimeSpan.Zero);

    public static Usuario Usuario(Perfil perfil, Guid? organizacaoId = null) =>
        Domain.Identidade.Usuario.Criar(
            Guid.CreateVersion7(),
            organizacaoId ?? Guid.CreateVersion7(),
            "Conta de Teste",
            EnderecoDeEmail.Criar($"conta.{Guid.NewGuid():n}@exemplo.test"),
            "hash-qualquer",
            perfil,
            Agora);

    public static byte[] Hash() => RandomNumberGenerator.GetBytes(32);
}
