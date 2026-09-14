using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace TorreLogistica.Application.Identidade;

/// <summary>
/// Geração e hash dos tokens opacos de renovação.
/// </summary>
/// <remarks>
/// 32 bytes de gerador criptográfico (256 bits) — impossível de adivinhar, então não há
/// necessidade do hash lento usado para senhas — SHA-256 basta para que o banco não guarde
/// nada utilizável. Senha precisa de hash lento porque é escolhida por gente e tem pouca
/// entropia; este token não.
/// </remarks>
public static class SegredosDeRenovacao
{
    private const int BytesDeEntropia = 32;

    /// <summary>Tamanho do token codificado em Base64Url sem preenchimento.</summary>
    public const int TamanhoCodificado = 43;

    /// <summary>Gera um token novo e o hash que será persistido.</summary>
    public static (string TokenCru, byte[] Hash) Gerar()
    {
        var bytes = RandomNumberGenerator.GetBytes(BytesDeEntropia);
        var cru = Base64Url.EncodeToString(bytes);
        return (cru, CalcularHash(cru));
    }

    /// <summary>Hash SHA-256 do valor recebido do cliente.</summary>
    public static byte[] CalcularHash(string tokenCru)
    {
        ArgumentNullException.ThrowIfNull(tokenCru);
        return SHA256.HashData(Encoding.UTF8.GetBytes(tokenCru));
    }

    /// <summary>
    /// Descarta de cara o que nem tem formato de token, sem consultar o banco.
    /// </summary>
    public static bool FormatoEhPlausivel(string? tokenCru) =>
        tokenCru is { Length: TamanhoCodificado } && Base64Url.IsValid(tokenCru);
}
