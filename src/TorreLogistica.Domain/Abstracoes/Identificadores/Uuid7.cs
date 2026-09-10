namespace TorreLogistica.Domain.Abstracoes.Identificadores;

/// <summary>
/// Leitura dos campos de um identificador UUID versão 7, conforme a RFC 9562.
/// </summary>
/// <remarks>
/// Um UUIDv7 carrega, nos 48 bits mais significativos, o instante de criação em
/// milissegundos desde a época Unix. Isso dá ordenação temporal natural em índice
/// de banco — a razão de o projeto tê-lo adotado como identificador padrão.
/// Este tipo apenas interpreta bytes: não gera identificador e não depende de nada.
/// </remarks>
public static class Uuid7
{
    private const int VersaoEsperada = 7;
    private const int VarianteRfc9562 = 0b10;

    /// <summary>Número da versão gravado no identificador (4 bits).</summary>
    public static int Versao(Guid identificador)
    {
        Span<byte> bytes = stackalloc byte[16];
        EscreverBigEndian(identificador, bytes);
        return bytes[6] >> 4;
    }

    /// <summary>Indica se o identificador é um UUIDv7 com a variante da RFC 9562.</summary>
    public static bool EhUuidV7(Guid identificador)
    {
        Span<byte> bytes = stackalloc byte[16];
        EscreverBigEndian(identificador, bytes);
        return (bytes[6] >> 4) == VersaoEsperada && (bytes[8] >> 6) == VarianteRfc9562;
    }

    /// <summary>
    /// Instante de criação embutido no identificador, com precisão de milissegundo.
    /// </summary>
    /// <exception cref="ArgumentException">Se o identificador não for UUIDv7.</exception>
    public static DateTimeOffset ExtrairInstanteDeCriacao(Guid identificador)
    {
        if (!EhUuidV7(identificador))
        {
            throw new ArgumentException(
                "O identificador informado não é um UUID versão 7.",
                nameof(identificador));
        }

        Span<byte> bytes = stackalloc byte[16];
        EscreverBigEndian(identificador, bytes);

        long milissegundos = 0;
        for (var i = 0; i < 6; i++)
        {
            milissegundos = (milissegundos << 8) | bytes[i];
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(milissegundos);
    }

    private static void EscreverBigEndian(Guid identificador, Span<byte> destino)
    {
        if (!identificador.TryWriteBytes(destino, bigEndian: true, out _))
        {
            throw new ArgumentException(
                "Não foi possível ler os bytes do identificador.",
                nameof(identificador));
        }
    }
}
