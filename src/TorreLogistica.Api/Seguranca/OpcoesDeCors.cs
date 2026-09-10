namespace TorreLogistica.Api.Seguranca;

/// <summary>
/// Origens autorizadas a chamar a API a partir de um navegador.
/// </summary>
/// <remarks>
/// A lista nasce vazia de propósito: sem configuração explícita para o ambiente,
/// nenhuma origem é liberada. A topologia real (console, PWA e rastreamento público
/// em domínios próprios) é declarada por ambiente, nunca deduzida em tempo de execução.
/// </remarks>
public sealed class OpcoesDeCors
{
    /// <summary>Seção correspondente na configuração.</summary>
    public const string Secao = "Torre:Cors";

    /// <summary>Nome da política aplicada aos endpoints da API.</summary>
    public const string NomeDaPolitica = "torre-origens-conhecidas";

    /// <summary>Origens completas e exatas, com esquema e porta.</summary>
    public IReadOnlyList<string> OrigensPermitidas { get; set; } = [];
}
