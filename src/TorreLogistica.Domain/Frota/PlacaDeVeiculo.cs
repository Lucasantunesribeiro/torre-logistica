using TorreLogistica.Domain.Abstracoes.Erros;

namespace TorreLogistica.Domain.Frota;

/// <summary>
/// Placa brasileira, no padrão antigo (<c>ABC1234</c>) ou Mercosul (<c>ABC1D23</c>).
/// </summary>
/// <remarks>
/// Guardada sem hífen e em maiúsculas, para que <c>abc-1234</c> e <c>ABC1234</c> sejam a
/// mesma placa na regra de unicidade.
/// </remarks>
public sealed record PlacaDeVeiculo
{
    private PlacaDeVeiculo(string valor) => Valor = valor;

    /// <summary>Sete caracteres, maiúsculos, sem separador.</summary>
    public string Valor { get; }

    /// <summary>Indica se é placa do padrão Mercosul.</summary>
    public bool EhMercosul => char.IsAsciiLetter(Valor[4]);

    /// <summary>Tenta interpretar o texto como placa.</summary>
    public static bool TentarCriar(string? texto, out PlacaDeVeiculo? placa)
    {
        placa = null;

        if (string.IsNullOrWhiteSpace(texto) || texto.Length > 16)
        {
            return false;
        }

        var valor = new string(texto
            .Where(caractere => caractere is not ('-' or ' '))
            .Select(char.ToUpperInvariant)
            .ToArray());

        var valida = valor.Length == 7
            && valor[..3].All(char.IsAsciiLetterUpper)
            && char.IsAsciiDigit(valor[3])
            && (char.IsAsciiDigit(valor[4]) || char.IsAsciiLetterUpper(valor[4]))
            && valor[5..].All(char.IsAsciiDigit);

        if (!valida)
        {
            return false;
        }

        placa = new PlacaDeVeiculo(valor);
        return true;
    }

    /// <summary>Cria a placa ou lança erro de regra.</summary>
    public static PlacaDeVeiculo Criar(string? texto) =>
        TentarCriar(texto, out var placa)
            ? placa!
            : throw ExcecaoDeDominio.RegraViolada(
                "placa_invalida", "Placa inválida: use o padrão ABC1234 ou o Mercosul ABC1D23.");
}
