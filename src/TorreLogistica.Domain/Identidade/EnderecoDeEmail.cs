using System.Net.Mail;
using TorreLogistica.Domain.Abstracoes.Erros;

namespace TorreLogistica.Domain.Identidade;

/// <summary>
/// Endereço de e-mail validado, com a forma normalizada usada para comparação.
/// </summary>
/// <remarks>
/// A normalização (espaços removidos, caixa baixa invariante) existe para que
/// <c>Ana@Empresa.com</c> e <c>ana@empresa.com </c> sejam a mesma conta. O texto
/// original é preservado para exibição; a unicidade usa sempre o normalizado.
/// </remarks>
public sealed record EnderecoDeEmail
{
    /// <summary>Tamanho máximo aceito, conforme RFC 5321.</summary>
    public const int TamanhoMaximo = 254;

    private EnderecoDeEmail(string valor, string normalizado)
    {
        Valor = valor;
        Normalizado = normalizado;
    }

    /// <summary>Endereço como informado, sem espaços nas pontas.</summary>
    public string Valor { get; }

    /// <summary>Forma usada em comparação e índice de unicidade.</summary>
    public string Normalizado { get; }

    /// <summary>Normaliza um texto sem validar. Útil para buscar por e-mail recebido.</summary>
    public static string Normalizar(string? texto) =>
        (texto ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Tenta criar um endereço válido.</summary>
    public static bool TentarCriar(string? texto, out EnderecoDeEmail? endereco)
    {
        endereco = null;
        var aparado = (texto ?? string.Empty).Trim();

        if (aparado.Length is 0 or > TamanhoMaximo
            || aparado.Contains(' ', StringComparison.Ordinal)
            || !MailAddress.TryCreate(aparado, out var analisado)
            // MailAddress aceita "Nome <a@b.c>"; aqui só entra o endereço puro.
            || !string.Equals(analisado.Address, aparado, StringComparison.Ordinal)
            || !analisado.Host.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        endereco = new EnderecoDeEmail(aparado, Normalizar(aparado));
        return true;
    }

    /// <summary>Cria um endereço ou lança erro de regra.</summary>
    public static EnderecoDeEmail Criar(string? texto) =>
        TentarCriar(texto, out var endereco)
            ? endereco!
            : throw ExcecaoDeDominio.RegraViolada("email_invalido", "E-mail inválido.");
}
