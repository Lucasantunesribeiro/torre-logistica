namespace TorreLogistica.Api.Comum;

/// <summary>Leitura de filtro de status repetido na query string (<c>?status=A&amp;status=B</c>).</summary>
public static class LeituraDeStatus
{
    /// <summary>
    /// Converte os nomes em valores do enum. Só o nome exato: <c>1</c> ou caixa diferente não
    /// passam, para o contrato ter uma forma só.
    /// </summary>
    /// <returns>Os valores, sem repetição, ou <see langword="null"/> se algum for inválido.</returns>
    public static List<TEnum>? Ler<TEnum>(string[]? valores, int quantidadeMaxima)
        where TEnum : struct, Enum
    {
        if (valores is { Length: var quantidade } && quantidade > quantidadeMaxima)
        {
            return null;
        }

        var lidos = new List<TEnum>();
        foreach (var valor in valores ?? [])
        {
            if (string.IsNullOrEmpty(valor)
                || !valor.All(char.IsAsciiLetter)
                || !Enum.TryParse<TEnum>(valor, ignoreCase: false, out var convertido)
                || !Enum.IsDefined(convertido))
            {
                return null;
            }

            if (!lidos.Contains(convertido))
            {
                lidos.Add(convertido);
            }
        }

        return lidos;
    }
}
