namespace TorreLogistica.Domain.Abstracoes.Erros;

/// <summary>
/// Natureza de uma falha originada no domínio.
/// </summary>
/// <remarks>
/// Só constam aqui as categorias que o domínio realmente sabe produzir.
/// Autenticação, autorização e rate limit são decisões de borda HTTP e não
/// aparecem nesta lista de propósito — o domínio não conhece requisição.
/// </remarks>
public enum CategoriaDeErroDeDominio
{
    /// <summary>Entrada ou estado viola uma regra/invariante do negócio.</summary>
    RegraViolada = 1,

    /// <summary>A operação perdeu uma corrida ou colide com o estado atual.</summary>
    Conflito = 2,

    /// <summary>O recurso pedido não existe no contexto autorizado.</summary>
    NaoEncontrado = 3,
}
