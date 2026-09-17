using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using TorreLogistica.Domain.Integracoes;

namespace TorreLogistica.Application.Integracoes;

/// <summary>
/// Geração e conferência da chave de API de uma integração.
/// </summary>
/// <remarks>
/// <para>
/// Formato: <c>tlog_&lt;identificador&gt;_&lt;segredo&gt;</c>. O identificador é público e serve só para
/// achar a linha; o segredo é conferido contra o SHA-256 guardado, em tempo constante. Sem o
/// identificador, conferir uma chave exigiria varrer a tabela calculando hash — e essa varredura seria,
/// ela mesma, o ataque.
/// </para>
/// <para>
/// O prefixo fixo existe para que a chave seja reconhecível: varredura de segredos em repositório,
/// log e histórico consegue casar <c>tlog_</c> e apontar o vazamento.
/// </para>
/// </remarks>
public static class SegredosDeIntegracao
{
    /// <summary>Chave recém-emitida, com as partes já separadas.</summary>
    /// <param name="Chave">O que vai para o sistema externo. Não é recuperável depois.</param>
    /// <param name="IdentificadorPublico">Parte que identifica a linha.</param>
    /// <param name="HashDoSegredo">SHA-256 do segredo, para persistir.</param>
    public sealed record ChaveEmitida(string Chave, string IdentificadorPublico, byte[] HashDoSegredo);

    /// <summary>Sorteia uma chave nova.</summary>
    public static ChaveEmitida Gerar()
    {
        var identificador = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(PoliticaDeIntegracao.BytesDoIdentificador));
        var segredo = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(PoliticaDeIntegracao.BytesDoSegredo));

        var chave = string.Concat(
            PoliticaDeIntegracao.PrefixoDaChave,
            PoliticaDeIntegracao.SeparadorDaChave,
            identificador,
            PoliticaDeIntegracao.SeparadorDaChave,
            segredo);

        return new ChaveEmitida(chave, identificador, CalcularHash(segredo));
    }

    /// <summary>
    /// Separa uma chave apresentada, sem consultar nada.
    /// </summary>
    /// <remarks>
    /// Rejeitar aqui o que nem tem forma de chave evita gastar banco e tempo de hash com lixo — e é o
    /// primeiro degrau contra tentativa em massa.
    /// </remarks>
    public static bool TentarAnalisar(string? chave, out string identificadorPublico, out string segredo)
    {
        identificadorPublico = string.Empty;
        segredo = string.Empty;

        if (string.IsNullOrEmpty(chave))
        {
            return false;
        }

        var partes = chave.Split(PoliticaDeIntegracao.SeparadorDaChave);

        if (partes.Length != 3
            || !string.Equals(partes[0], PoliticaDeIntegracao.PrefixoDaChave, StringComparison.Ordinal)
            || partes[1].Length != PoliticaDeIntegracao.TamanhoDoIdentificador
            || partes[2].Length != PoliticaDeIntegracao.TamanhoDoSegredo
            || !Base64Url.IsValid(partes[1])
            || !Base64Url.IsValid(partes[2]))
        {
            return false;
        }

        identificadorPublico = partes[1];
        segredo = partes[2];
        return true;
    }

    /// <summary>SHA-256 do segredo.</summary>
    public static byte[] CalcularHash(string segredo)
    {
        ArgumentNullException.ThrowIfNull(segredo);
        return SHA256.HashData(Encoding.UTF8.GetBytes(segredo));
    }

    /// <summary>Confere o segredo contra o hash guardado, em tempo constante.</summary>
    public static bool Confere(byte[]? hashGravado, string segredo) =>
        hashGravado is not null && CryptographicOperations.FixedTimeEquals(hashGravado, CalcularHash(segredo));

    /// <summary>SHA-256 de um corpo recebido, para comparar reenvios da mesma chave de idempotência.</summary>
    public static byte[] CalcularHashDoCorpo(ReadOnlySpan<byte> corpo) => SHA256.HashData(corpo);
}
