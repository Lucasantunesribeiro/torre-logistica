namespace TorreLogistica.Application.Comum;

/// <summary>Uma página de uma coleção.</summary>
/// <param name="Itens">Itens da página.</param>
/// <param name="Pagina">Número da página, a partir de 1.</param>
/// <param name="TamanhoDaPagina">Tamanho pedido.</param>
/// <param name="Total">Total de itens na coleção.</param>
public sealed record PaginaDeResultados<T>(
    IReadOnlyList<T> Itens,
    int Pagina,
    int TamanhoDaPagina,
    int Total);

/// <summary>Limites de paginação aceitos pela aplicação.</summary>
public static class LimitesDePaginacao
{
    /// <summary>Tamanho de página quando o cliente não informa.</summary>
    public const int TamanhoPadrao = 25;

    /// <summary>Maior página aceita. Coleção sem teto é convite a consulta que derruba o banco.</summary>
    public const int TamanhoMaximo = 100;
}
