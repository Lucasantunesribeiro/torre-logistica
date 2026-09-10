using TorreLogistica.Domain.Abstracoes.Erros;

namespace TorreLogistica.Api.Erros;

/// <summary>
/// Traduz a categoria de um erro de domínio para o contrato HTTP da API.
/// </summary>
/// <remarks>
/// A tradução vive aqui, na borda, e em nenhum outro lugar: o domínio não conhece
/// status HTTP e cada caso de uso não pode inventar o seu. É função pura, por isso
/// coberta diretamente por teste de unidade.
/// </remarks>
public static class MapeamentoDeErrosDeDominio
{
    /// <summary>Prefixo dos identificadores de tipo de problema da API.</summary>
    public const string PrefixoDoTipo = "urn:torre-logistica:erro:";

    /// <summary>Status HTTP correspondente à categoria do erro.</summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><c>422</c> para regra violada: a requisição foi entendida,
    /// mas o negócio a recusou. Entrada malformada continua sendo <c>400</c>, tratada
    /// pela validação de modelo antes de chegar ao domínio.</description></item>
    /// <item><description><c>409</c> para conflito de estado ou corrida perdida.</description></item>
    /// <item><description><c>404</c> para recurso inexistente no contexto autorizado —
    /// inclusive recurso de outro tenant, que não deve ter sua existência revelada.</description></item>
    /// </list>
    /// </remarks>
    public static int ParaStatusHttp(CategoriaDeErroDeDominio categoria) => categoria switch
    {
        CategoriaDeErroDeDominio.RegraViolada => StatusCodes.Status422UnprocessableEntity,
        CategoriaDeErroDeDominio.Conflito => StatusCodes.Status409Conflict,
        CategoriaDeErroDeDominio.NaoEncontrado => StatusCodes.Status404NotFound,
        _ => StatusCodes.Status500InternalServerError,
    };

    /// <summary>Título curto e estável exibido no ProblemDetails.</summary>
    public static string ParaTitulo(CategoriaDeErroDeDominio categoria) => categoria switch
    {
        CategoriaDeErroDeDominio.RegraViolada => "Regra de negócio violada",
        CategoriaDeErroDeDominio.Conflito => "Conflito com o estado atual",
        CategoriaDeErroDeDominio.NaoEncontrado => "Recurso não encontrado",
        _ => "Erro inesperado",
    };

    /// <summary>Identificador do tipo de problema, derivado do código do erro.</summary>
    public static string ParaTipo(string codigo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codigo);
        return PrefixoDoTipo + codigo;
    }
}
