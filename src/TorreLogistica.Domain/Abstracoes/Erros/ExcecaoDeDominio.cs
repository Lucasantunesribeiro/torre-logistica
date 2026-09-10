using System.Diagnostics.CodeAnalysis;

namespace TorreLogistica.Domain.Abstracoes.Erros;

/// <summary>
/// Falha prevista pelo domínio, com código estável e categoria conhecida.
/// </summary>
/// <remarks>
/// Erro de domínio não é acidente: é resposta esperada de uma regra. Por isso ele
/// carrega um <see cref="Codigo"/> textual estável — o cliente pode programar em
/// cima dele — e uma <see cref="Categoria"/>, que a borda HTTP traduz em status.
/// Mensagem é para humano; código é para máquina.
/// </remarks>
public class ExcecaoDeDominio : Exception
{
    /// <summary>Cria uma falha de domínio.</summary>
    /// <param name="codigo">Código estável, em <c>caixa_baixa_com_underscore</c>.</param>
    /// <param name="mensagem">Mensagem legível, sem dado sensível.</param>
    /// <param name="categoria">Natureza da falha.</param>
    /// <param name="excecaoInterna">Causa original, quando houver.</param>
    public ExcecaoDeDominio(
        string codigo,
        string mensagem,
        CategoriaDeErroDeDominio categoria = CategoriaDeErroDeDominio.RegraViolada,
        Exception? excecaoInterna = null)
        : base(mensagem, excecaoInterna)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codigo);
        ArgumentException.ThrowIfNullOrWhiteSpace(mensagem);

        Codigo = codigo;
        Categoria = categoria;
    }

    /// <summary>Código estável da falha, seguro para expor ao cliente.</summary>
    public string Codigo { get; }

    /// <summary>Natureza da falha, usada para escolher o status HTTP.</summary>
    public CategoriaDeErroDeDominio Categoria { get; }

    /// <summary>Cria uma falha de regra/invariante violada.</summary>
    public static ExcecaoDeDominio RegraViolada(string codigo, string mensagem) =>
        new(codigo, mensagem, CategoriaDeErroDeDominio.RegraViolada);

    /// <summary>Cria uma falha de conflito com o estado atual.</summary>
    public static ExcecaoDeDominio Conflito(string codigo, string mensagem) =>
        new(codigo, mensagem, CategoriaDeErroDeDominio.Conflito);

    /// <summary>Cria uma falha de recurso inexistente no contexto autorizado.</summary>
    public static ExcecaoDeDominio NaoEncontrado(string codigo, string mensagem) =>
        new(codigo, mensagem, CategoriaDeErroDeDominio.NaoEncontrado);

    /// <summary>Lança <see cref="RegraViolada"/> quando a condição for verdadeira.</summary>
    public static void LancarSe(
        [DoesNotReturnIf(true)] bool condicao,
        string codigo,
        string mensagem)
    {
        if (condicao)
        {
            throw RegraViolada(codigo, mensagem);
        }
    }
}
