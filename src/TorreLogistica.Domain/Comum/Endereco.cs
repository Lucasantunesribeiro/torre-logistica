using TorreLogistica.Domain.Abstracoes.Erros;

namespace TorreLogistica.Domain.Comum;

/// <summary>
/// Endereço postal brasileiro.
/// </summary>
/// <remarks>
/// A v1 atende operação no Brasil: CEP de 8 dígitos e UF entre as 27 unidades federativas.
/// Endereço de outro país exigirá outro tipo, não um afrouxamento deste — validação que
/// aceita qualquer coisa não valida nada. Decisão registrada no ADR 0011.
/// </remarks>
public sealed record Endereco
{
    /// <summary>Tamanho máximo do logradouro.</summary>
    public const int TamanhoMaximoDoLogradouro = 150;

    /// <summary>Tamanho máximo do número.</summary>
    public const int TamanhoMaximoDoNumero = 20;

    /// <summary>Tamanho máximo do complemento, do bairro e da cidade.</summary>
    public const int TamanhoMaximoDeTextoCurto = 80;

    private static readonly HashSet<string> UnidadesFederativas = new(StringComparer.Ordinal)
    {
        "AC", "AL", "AM", "AP", "BA", "CE", "DF", "ES", "GO", "MA", "MG", "MS", "MT", "PA",
        "PB", "PE", "PI", "PR", "RJ", "RN", "RO", "RR", "RS", "SC", "SE", "SP", "TO",
    };

    // Usado pelo EF Core ao materializar o tipo complexo.
    private Endereco()
    {
        Logradouro = string.Empty;
        Numero = string.Empty;
        Bairro = string.Empty;
        Cidade = string.Empty;
        Uf = string.Empty;
        Cep = string.Empty;
    }

    /// <summary>Rua, avenida, estrada.</summary>
    public string Logradouro { get; private set; }

    /// <summary>Número, ou "S/N".</summary>
    public string Numero { get; private set; }

    /// <summary>Complemento opcional.</summary>
    public string? Complemento { get; private set; }

    /// <summary>Bairro.</summary>
    public string Bairro { get; private set; }

    /// <summary>Cidade.</summary>
    public string Cidade { get; private set; }

    /// <summary>Sigla da unidade federativa, em maiúsculas.</summary>
    public string Uf { get; private set; }

    /// <summary>CEP com 8 dígitos, sem pontuação.</summary>
    public string Cep { get; private set; }

    /// <summary>Valida e cria um endereço.</summary>
    public static Endereco Criar(
        string? logradouro,
        string? numero,
        string? complemento,
        string? bairro,
        string? cidade,
        string? uf,
        string? cep)
    {
        var ufNormalizada = (uf ?? string.Empty).Trim().ToUpperInvariant();
        ExcecaoDeDominio.LancarSe(
            !UnidadesFederativas.Contains(ufNormalizada),
            "uf_invalida",
            "UF inválida: informe a sigla de uma das 27 unidades federativas.");

        var cepNormalizado = new string((cep ?? string.Empty).Where(caractere => caractere is not ('-' or '.' or ' ')).ToArray());
        ExcecaoDeDominio.LancarSe(
            cepNormalizado.Length != 8 || !cepNormalizado.All(char.IsAsciiDigit) || cepNormalizado == "00000000",
            "cep_invalido",
            "CEP inválido: informe 8 dígitos.");

        return new Endereco
        {
            Logradouro = TextoNormalizado.Obrigatorio(logradouro, TamanhoMaximoDoLogradouro, "endereco_incompleto", "O logradouro"),
            Numero = TextoNormalizado.Obrigatorio(numero, TamanhoMaximoDoNumero, "endereco_incompleto", "O número"),
            Complemento = TextoNormalizado.Opcional(complemento, TamanhoMaximoDeTextoCurto, "endereco_incompleto", "O complemento"),
            Bairro = TextoNormalizado.Obrigatorio(bairro, TamanhoMaximoDeTextoCurto, "endereco_incompleto", "O bairro"),
            Cidade = TextoNormalizado.Obrigatorio(cidade, TamanhoMaximoDeTextoCurto, "endereco_incompleto", "A cidade"),
            Uf = ufNormalizada,
            Cep = cepNormalizado,
        };
    }
}
