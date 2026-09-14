using System.Globalization;
using System.Text.RegularExpressions;

namespace TorreLogistica.Domain.Rotas;

/// <summary>
/// Código humano da rota: <c>ROT-2026-0092</c>.
/// </summary>
/// <remarks>
/// Mesmo mecanismo do código da entrega (ADR 0012): contador por organização e ano UTC,
/// reservado na transação que grava a rota. Quatro dígitos no mínimo: rotas são dezenas por
/// dia, não milhares.
/// </remarks>
public static partial class CodigoDaRota
{
    /// <summary>Série do contador de códigos.</summary>
    public const string Serie = "rota";

    /// <summary>Prefixo do código.</summary>
    public const string Prefixo = "ROT";

    /// <summary>Tamanho máximo do código.</summary>
    public const int TamanhoMaximo = 24;

    /// <summary>Monta o código a partir do ano e do número reservado.</summary>
    public static string Gerar(int ano, long numero)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ano, 2000);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ano, 9999);
        ArgumentOutOfRangeException.ThrowIfLessThan(numero, 1);

        return string.Create(CultureInfo.InvariantCulture, $"{Prefixo}-{ano:D4}-{numero:D4}");
    }

    /// <summary>Indica se o texto tem o formato de um código de rota.</summary>
    public static bool EhValido(string? codigo) => codigo is not null && Formato().IsMatch(codigo);

    // \z, e não $: $ casa antes de uma quebra de linha final.
    [GeneratedRegex(@"^ROT-\d{4}-\d{4,18}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Formato();
}
