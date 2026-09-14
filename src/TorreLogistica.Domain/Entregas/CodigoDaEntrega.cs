using System.Globalization;
using System.Text.RegularExpressions;

namespace TorreLogistica.Domain.Entregas;

/// <summary>
/// Código humano da entrega: <c>ENT-2026-004821</c>.
/// </summary>
/// <remarks>
/// <para>
/// Serve para conversa — telefone, etiqueta, planilha. Nunca é chave primária
/// (CLAUDE.md, seção 12): a chave é o UUIDv7.
/// </para>
/// <para>
/// O número é sequencial por organização e por ano, reservado no banco na mesma transação
/// que grava a entrega. O ano é o do instante UTC da criação: o fuso da organização é
/// configuração de apresentação, e o código não pode depender dela.
/// </para>
/// </remarks>
public static partial class CodigoDaEntrega
{
    /// <summary>Série do contador de códigos.</summary>
    public const string Serie = "entrega";

    /// <summary>Prefixo do código.</summary>
    public const string Prefixo = "ENT";

    /// <summary>Tamanho máximo do código.</summary>
    public const int TamanhoMaximo = 24;

    /// <summary>Monta o código a partir do ano e do número reservado.</summary>
    public static string Gerar(int ano, long numero)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ano, 2000);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ano, 9999);
        ArgumentOutOfRangeException.ThrowIfLessThan(numero, 1);

        return string.Create(CultureInfo.InvariantCulture, $"{Prefixo}-{ano:D4}-{numero:D6}");
    }

    /// <summary>Indica se o texto tem o formato de um código de entrega.</summary>
    public static bool EhValido(string? codigo) => codigo is not null && Formato().IsMatch(codigo);

    // \z, e não $: no .NET, $ também casa antes de uma quebra de linha final, e
    // "ENT-2026-000001\n" passaria como código válido.
    [GeneratedRegex(@"^ENT-\d{4}-\d{6,18}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Formato();
}
