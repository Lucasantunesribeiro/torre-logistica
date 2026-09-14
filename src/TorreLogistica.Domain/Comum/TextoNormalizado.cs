using System.Collections.Frozen;
using System.Text;
using TorreLogistica.Domain.Abstracoes.Erros;

namespace TorreLogistica.Domain.Comum;

/// <summary>
/// Regras de texto livre digitado por gente: aparar, recusar controle, normalizar para busca.
/// </summary>
public static class TextoNormalizado
{
    /// <summary>
    /// Letra acentuada minúscula → letra base.
    /// </summary>
    /// <remarks>
    /// Tabela explícita, e não <c>Normalize(FormD)</c>: a solução roda com
    /// <c>InvariantGlobalization</c>, em que a decomposição Unicode não remove acento. Uma
    /// tabela também deixa a forma de busca idêntica em qualquer sistema operacional — o que
    /// importa, porque ela é gravada e sustenta índice único.
    /// </remarks>
    private static readonly FrozenDictionary<char, char> LetraBase = new Dictionary<char, char>
    {
        ['à'] = 'a',
        ['á'] = 'a',
        ['â'] = 'a',
        ['ã'] = 'a',
        ['ä'] = 'a',
        ['å'] = 'a',
        ['ā'] = 'a',
        ['ă'] = 'a',
        ['ą'] = 'a',
        ['ç'] = 'c',
        ['ć'] = 'c',
        ['č'] = 'c',
        ['ď'] = 'd',
        ['è'] = 'e',
        ['é'] = 'e',
        ['ê'] = 'e',
        ['ë'] = 'e',
        ['ē'] = 'e',
        ['ė'] = 'e',
        ['ę'] = 'e',
        ['ě'] = 'e',
        ['ğ'] = 'g',
        ['ì'] = 'i',
        ['í'] = 'i',
        ['î'] = 'i',
        ['ï'] = 'i',
        ['ī'] = 'i',
        ['į'] = 'i',
        ['ł'] = 'l',
        ['ľ'] = 'l',
        ['ñ'] = 'n',
        ['ń'] = 'n',
        ['ň'] = 'n',
        ['ò'] = 'o',
        ['ó'] = 'o',
        ['ô'] = 'o',
        ['õ'] = 'o',
        ['ö'] = 'o',
        ['ø'] = 'o',
        ['ō'] = 'o',
        ['ő'] = 'o',
        ['ř'] = 'r',
        ['ś'] = 's',
        ['š'] = 's',
        ['ş'] = 's',
        ['ť'] = 't',
        ['ţ'] = 't',
        ['ù'] = 'u',
        ['ú'] = 'u',
        ['û'] = 'u',
        ['ü'] = 'u',
        ['ū'] = 'u',
        ['ů'] = 'u',
        ['ű'] = 'u',
        ['ý'] = 'y',
        ['ÿ'] = 'y',
        ['ź'] = 'z',
        ['ż'] = 'z',
        ['ž'] = 'z',
    }.ToFrozenDictionary();

    /// <summary>
    /// Forma usada em busca e unicidade: sem acento, sem diferença de caixa e com espaços
    /// colapsados. "Hub  São José" e "hub sao jose" são o mesmo nome.
    /// </summary>
    public static string ParaBusca(string? texto)
    {
        var minusculo = Aparar(texto).ToLowerInvariant();
        var construtor = new StringBuilder(minusculo.Length);

        foreach (var caractere in minusculo)
        {
            construtor.Append(LetraBase.TryGetValue(caractere, out var basica) ? basica : caractere);
        }

        return construtor.ToString();
    }

    /// <summary>Remove espaços das pontas e colapsa espaços internos repetidos.</summary>
    public static string Aparar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return string.Empty;
        }

        var construtor = new StringBuilder(texto.Length);
        var ultimoFoiEspaco = false;

        foreach (var caractere in texto.Trim())
        {
            if (char.IsWhiteSpace(caractere))
            {
                if (!ultimoFoiEspaco)
                {
                    construtor.Append(' ');
                }

                ultimoFoiEspaco = true;
                continue;
            }

            construtor.Append(caractere);
            ultimoFoiEspaco = false;
        }

        return construtor.ToString();
    }

    /// <summary>
    /// Texto obrigatório, aparado, com teto de tamanho e sem caractere de controle.
    /// </summary>
    /// <remarks>
    /// Caractere de controle em nome de cadastro não tem uso legítimo e é o veículo de
    /// quebra de linha forjada em log e em arquivo exportado. A checagem olha o texto
    /// recebido, antes de aparar — aparar transformaria a quebra de linha em espaço e a
    /// esconderia. A exceção é a tabulação, que chega de planilha colada e vira espaço.
    /// </remarks>
    public static string Obrigatorio(string? texto, int tamanhoMaximo, string codigo, string rotulo)
    {
        var aparado = Aparar(texto);

        ExcecaoDeDominio.LancarSe(
            aparado.Length == 0 || aparado.Length > tamanhoMaximo || texto!.Any(EhControleProibido),
            codigo,
            $"{rotulo} deve ter entre 1 e {tamanhoMaximo} caracteres, sem caracteres de controle.");

        return aparado;
    }

    private static bool EhControleProibido(char caractere) => char.IsControl(caractere) && caractere != '\t';

    /// <summary>Como <see cref="Obrigatorio"/>, mas vazio vira <see langword="null"/>.</summary>
    public static string? Opcional(string? texto, int tamanhoMaximo, string codigo, string rotulo) =>
        string.IsNullOrWhiteSpace(texto) ? null : Obrigatorio(texto, tamanhoMaximo, codigo, rotulo);
}
