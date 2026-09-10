using System.Xml.Linq;

namespace TorreLogistica.ArchitectureTests;

/// <summary>
/// Acesso aos arquivos do repositório a partir do diretório de execução do teste.
/// </summary>
/// <remarks>
/// Os testes de arquitetura leem os próprios <c>.csproj</c> e o código-fonte porque
/// é o único jeito de pegar uma referência proibida que ainda não foi usada: uma
/// referência de projeto declarada e não exercitada não aparece nos metadados do
/// assembly compilado, e passaria despercebida por uma checagem só em tempo de execução.
/// </remarks>
public static class Repositorio
{
    private const string ArquivoDeSolucao = "TorreLogistica.slnx";

    private static readonly Lazy<DirectoryInfo> RaizCalculada = new(LocalizarRaiz);

    /// <summary>Diretório raiz do repositório.</summary>
    public static DirectoryInfo Raiz => RaizCalculada.Value;

    /// <summary>Caminho do arquivo de projeto de um componente em <c>src/</c>.</summary>
    public static string CaminhoDoProjeto(string nomeDoProjeto) =>
        Path.Combine(Raiz.FullName, "src", nomeDoProjeto, $"{nomeDoProjeto}.csproj");

    /// <summary>Diretório de código-fonte de um componente em <c>src/</c>.</summary>
    public static string DiretorioDoProjeto(string nomeDoProjeto) =>
        Path.Combine(Raiz.FullName, "src", nomeDoProjeto);

    /// <summary>Nomes dos projetos referenciados por um <c>.csproj</c>.</summary>
    public static IReadOnlyList<string> ReferenciasDeProjeto(string nomeDoProjeto) =>
        [.. LerItens(nomeDoProjeto, "ProjectReference")
            .Select(caminho => Path.GetFileNameWithoutExtension(
                caminho.Replace('\\', Path.DirectorySeparatorChar)))
            .Where(nome => !string.IsNullOrEmpty(nome))];

    /// <summary>Nomes dos pacotes NuGet referenciados por um <c>.csproj</c>.</summary>
    public static IReadOnlyList<string> ReferenciasDePacote(string nomeDoProjeto) =>
        [.. LerItens(nomeDoProjeto, "PackageReference")];

    /// <summary>Arquivos <c>.cs</c> escritos à mão de um componente em <c>src/</c>.</summary>
    public static IReadOnlyList<string> ArquivosDeCodigo(string nomeDoProjeto) =>
        [.. Directory
            .EnumerateFiles(DiretorioDoProjeto(nomeDoProjeto), "*.cs", SearchOption.AllDirectories)
            .Where(caminho => !CaminhoEhGerado(caminho))];

    private static bool CaminhoEhGerado(string caminho)
    {
        var normalizado = caminho.Replace('\\', '/');

        return normalizado.Contains("/bin/", StringComparison.Ordinal)
            || normalizado.Contains("/obj/", StringComparison.Ordinal)
            || normalizado.Contains("/Migrations/", StringComparison.Ordinal);
    }

    private static IEnumerable<string> LerItens(string nomeDoProjeto, string nomeDoItem)
    {
        var projeto = XDocument.Load(CaminhoDoProjeto(nomeDoProjeto));

        return projeto
            .Descendants(nomeDoItem)
            .Select(item => item.Attribute("Include")?.Value)
            .Where(valor => !string.IsNullOrWhiteSpace(valor))
            .Select(valor => valor!);
    }

    private static DirectoryInfo LocalizarRaiz()
    {
        var diretorio = new DirectoryInfo(AppContext.BaseDirectory);

        while (diretorio is not null)
        {
            if (File.Exists(Path.Combine(diretorio.FullName, ArquivoDeSolucao)))
            {
                return diretorio;
            }

            diretorio = diretorio.Parent;
        }

        throw new InvalidOperationException(
            $"Não foi possível localizar {ArquivoDeSolucao} a partir de {AppContext.BaseDirectory}.");
    }
}
