using TorreLogistica.Domain.Abstracoes.Erros;

namespace TorreLogistica.Domain.Comum;

/// <summary>
/// Telefone em formato E.164 (<c>+5511987654321</c>).
/// </summary>
/// <remarks>
/// <para>
/// Número sem código de país, com 10 ou 11 dígitos, é tratado como brasileiro. É uma
/// suposição de produto da v1, registrada aqui; número de outro país precisa vir com <c>+</c>.
/// </para>
/// <para>
/// Telefone é dado pessoal: nunca entra em log nem em trilha de auditoria.
/// </para>
/// </remarks>
public sealed record Telefone
{
    private Telefone(string valor) => Valor = valor;

    /// <summary>Número normalizado em E.164.</summary>
    public string Valor { get; }

    /// <summary>Tenta interpretar o texto como telefone.</summary>
    public static bool TentarCriar(string? texto, out Telefone? telefone)
    {
        telefone = null;

        if (string.IsNullOrWhiteSpace(texto) || texto.Length > 32)
        {
            return false;
        }

        var aparado = texto.Trim();
        var internacional = aparado.StartsWith('+');

        // Só dígitos e os separadores que as pessoas realmente digitam.
        if (aparado.Skip(internacional ? 1 : 0).Any(caractere =>
                !char.IsAsciiDigit(caractere) && caractere is not (' ' or '-' or '(' or ')' or '.')))
        {
            return false;
        }

        var digitos = new string(aparado.Where(char.IsAsciiDigit).ToArray());

        if (!internacional)
        {
            if (digitos.Length is not (10 or 11))
            {
                return false;
            }

            digitos = "55" + digitos;
        }

        if (digitos.Length is < 8 or > 15 || digitos[0] == '0')
        {
            return false;
        }

        if (digitos.StartsWith("55", StringComparison.Ordinal) && !NacionalValido(digitos[2..]))
        {
            return false;
        }

        telefone = new Telefone("+" + digitos);
        return true;
    }

    /// <summary>Cria o telefone ou lança erro de regra.</summary>
    public static Telefone Criar(string? texto) =>
        TentarCriar(texto, out var telefone)
            ? telefone!
            : throw ExcecaoDeDominio.RegraViolada("telefone_invalido", "Telefone inválido.");

    /// <summary>Telefone opcional: vazio vira <see langword="null"/>.</summary>
    public static Telefone? CriarOpcional(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : Criar(texto);

    // DDD de 11 a 99 sem zero, e celular de 11 dígitos sempre começando com 9.
    private static bool NacionalValido(string nacional) =>
        nacional.Length is 10 or 11
        && nacional[0] != '0'
        && nacional[1] != '0'
        && (nacional.Length == 10 || nacional[2] == '9');
}
