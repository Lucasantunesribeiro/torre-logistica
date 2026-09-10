using System.Text.RegularExpressions;

namespace TorreLogistica.Api.Correlacao;

/// <summary>
/// Constantes e validação do identificador de correlação transportado por HTTP.
/// </summary>
public static partial class CorrelacaoHttp
{
    /// <summary>Cabeçalho usado para receber e devolver o identificador.</summary>
    public const string NomeDoCabecalho = "X-Correlation-Id";

    /// <summary>Chave usada em <c>HttpContext.Items</c>.</summary>
    public const string ChaveNoContexto = "TorreLogistica:IdDeCorrelacao";

    /// <summary>Tamanho máximo aceito para um identificador vindo do cliente.</summary>
    public const int TamanhoMaximo = 64;

    /// <summary>
    /// Indica se um identificador recebido do cliente pode ser reaproveitado.
    /// </summary>
    /// <remarks>
    /// O valor volta no cabeçalho da resposta e entra em toda linha de log da
    /// requisição. Aceitar texto arbitrário abriria injeção de cabeçalho e poluição
    /// de log, então só passa o que couber em um formato estreito e curto.
    /// </remarks>
    public static bool EhIdentificadorAceitavel(string? valor) =>
        !string.IsNullOrWhiteSpace(valor)
        && valor.Length <= TamanhoMaximo
        && FormatoAceito().IsMatch(valor);

    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex FormatoAceito();
}
