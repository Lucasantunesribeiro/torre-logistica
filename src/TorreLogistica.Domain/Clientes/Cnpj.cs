using TorreLogistica.Domain.Abstracoes.Erros;

namespace TorreLogistica.Domain.Clientes;

/// <summary>
/// CNPJ, nos formatos numérico e alfanumérico.
/// </summary>
/// <remarks>
/// <para>
/// Desde julho de 2026 a Receita Federal emite CNPJ com as 12 primeiras posições
/// alfanuméricas. Os dois dígitos verificadores continuam numéricos e usam o mesmo cálculo
/// de módulo 11, com cada caractere valendo seu código ASCII menos 48 — o que faz o
/// algoritmo novo dar exatamente o resultado antigo para CNPJ só com números.
/// </para>
/// <para>
/// CNPJ identifica empresa, não pessoa: é dado público, e serve de chave natural para
/// integração com o sistema do cliente.
/// </para>
/// </remarks>
public sealed record Cnpj
{
    private static readonly int[] PesosDoPrimeiroDigito = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    private static readonly int[] PesosDoSegundoDigito = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

    private Cnpj(string valor) => Valor = valor;

    /// <summary>14 caracteres, maiúsculos, sem pontuação.</summary>
    public string Valor { get; }

    /// <summary>Forma de exibição: <c>12.ABC.345/01DE-35</c>.</summary>
    public string Formatado => $"{Valor[..2]}.{Valor[2..5]}.{Valor[5..8]}/{Valor[8..12]}-{Valor[12..]}";

    /// <summary>Tenta interpretar o texto como CNPJ válido.</summary>
    public static bool TentarCriar(string? texto, out Cnpj? cnpj)
    {
        cnpj = null;

        if (string.IsNullOrWhiteSpace(texto) || texto.Length > 32)
        {
            return false;
        }

        var normalizado = new string(texto
            .Where(caractere => caractere is not ('.' or '/' or '-' or ' '))
            .Select(char.ToUpperInvariant)
            .ToArray());

        if (normalizado.Length != 14
            || !normalizado[..12].All(caractere => char.IsAsciiDigit(caractere) || char.IsAsciiLetterUpper(caractere))
            || !normalizado[12..].All(char.IsAsciiDigit)
            // Sequência repetida passa no cálculo, mas não existe.
            || normalizado.All(caractere => caractere == normalizado[0]))
        {
            return false;
        }

        var primeiro = DigitoVerificador(normalizado[..12], PesosDoPrimeiroDigito);
        var segundo = DigitoVerificador(normalizado[..12] + primeiro, PesosDoSegundoDigito);

        if (normalizado[12] - '0' != primeiro || normalizado[13] - '0' != segundo)
        {
            return false;
        }

        cnpj = new Cnpj(normalizado);
        return true;
    }

    /// <summary>Cria o CNPJ ou lança erro de regra.</summary>
    public static Cnpj Criar(string? texto) =>
        TentarCriar(texto, out var cnpj)
            ? cnpj!
            : throw ExcecaoDeDominio.RegraViolada("cnpj_invalido", "CNPJ inválido.");

    /// <summary>CNPJ opcional: vazio vira <see langword="null"/>.</summary>
    public static Cnpj? CriarOpcional(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : Criar(texto);

    private static int DigitoVerificador(string base_, int[] pesos)
    {
        var soma = 0;
        for (var indice = 0; indice < pesos.Length; indice++)
        {
            soma += (base_[indice] - '0') * pesos[indice];
        }

        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }
}
