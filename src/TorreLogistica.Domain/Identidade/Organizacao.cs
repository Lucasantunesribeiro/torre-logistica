using System.Text.RegularExpressions;
using TorreLogistica.Domain.Abstracoes.Erros;

namespace TorreLogistica.Domain.Identidade;

/// <summary>
/// Organização cliente: a fronteira de isolamento de todos os dados de negócio.
/// </summary>
/// <remarks>
/// O <see cref="Slug"/> é o identificador público usado no login. Ele identifica, mas
/// não autoriza: quem define a organização efetiva de uma requisição é a sessão
/// autenticada, nunca um campo enviado pelo cliente.
/// </remarks>
public sealed partial class Organizacao
{
    /// <summary>Tamanho mínimo do identificador público.</summary>
    public const int TamanhoMinimoDoSlug = 3;

    /// <summary>Tamanho máximo do identificador público.</summary>
    public const int TamanhoMaximoDoSlug = 63;

    /// <summary>Tamanho máximo do nome.</summary>
    public const int TamanhoMaximoDoNome = 120;

    private Organizacao()
    {
        Nome = string.Empty;
        Slug = string.Empty;
    }

    /// <summary>Identificador interno (UUIDv7).</summary>
    public Guid Id { get; private set; }

    /// <summary>Nome de exibição.</summary>
    public string Nome { get; private set; }

    /// <summary>Identificador público, em minúsculas, usado no login.</summary>
    public string Slug { get; private set; }

    /// <summary>Organização inativa não autentica ninguém.</summary>
    public bool Ativa { get; private set; }

    /// <summary>Instante de criação, em UTC.</summary>
    public DateTimeOffset CriadaEm { get; private set; }

    /// <summary>Cria uma organização ativa.</summary>
    public static Organizacao Criar(Guid id, string? nome, string? slug, DateTimeOffset agora)
    {
        ExcecaoDeDominio.LancarSe(id == Guid.Empty, "identificador_invalido", "Identificador inválido.");

        var nomeAparado = (nome ?? string.Empty).Trim();
        ExcecaoDeDominio.LancarSe(
            nomeAparado.Length is 0 or > TamanhoMaximoDoNome,
            "nome_da_organizacao_invalido",
            $"O nome da organização deve ter entre 1 e {TamanhoMaximoDoNome} caracteres.");

        var slugNormalizado = NormalizarSlug(slug);
        ExcecaoDeDominio.LancarSe(
            !SlugEhValido(slugNormalizado),
            "slug_invalido",
            "O identificador da organização deve ter de 3 a 63 caracteres: letras minúsculas, "
            + "números e hífens, sem hífen nas pontas nem repetido.");

        return new Organizacao
        {
            Id = id,
            Nome = nomeAparado,
            Slug = slugNormalizado,
            Ativa = true,
            CriadaEm = agora.ToUniversalTime(),
        };
    }

    /// <summary>Normaliza o texto recebido no login para comparação.</summary>
    public static string NormalizarSlug(string? slug) =>
        (slug ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Indica se um slug já normalizado respeita o formato.</summary>
    public static bool SlugEhValido(string slugNormalizado) =>
        slugNormalizado.Length is >= TamanhoMinimoDoSlug and <= TamanhoMaximoDoSlug
        && FormatoDoSlug().IsMatch(slugNormalizado);

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex FormatoDoSlug();
}
