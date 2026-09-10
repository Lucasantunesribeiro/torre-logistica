using System.Text.RegularExpressions;

namespace TorreLogistica.ArchitectureTests;

/// <summary>
/// Guarda a regra de relógio injetável do CLAUDE.md, seção 48.
/// </summary>
/// <remarks>
/// SLA, ETA, motorista offline, timeline e simulador dependem de tempo controlável.
/// Uma única leitura direta do relógio escondida numa regra torna o comportamento
/// impossível de testar e o cenário de demonstração impossível de reproduzir.
/// </remarks>
public sealed partial class UsoDoRelogioTestes
{
    /// <summary>
    /// Único lugar autorizado a falar com o relógio real: é a implementação da
    /// abstração, e ela delega ao <c>TimeProvider</c> da plataforma.
    /// </summary>
    private static readonly string[] ArquivosAutorizados =
    [
        "RelogioDoSistema.cs",
    ];

    [Theory]
    [InlineData("TorreLogistica.Domain")]
    [InlineData("TorreLogistica.Application")]
    [InlineData("TorreLogistica.Infrastructure")]
    [InlineData("TorreLogistica.Api")]
    [InlineData("TorreLogistica.Workers")]
    public void NenhumCodigoLeORelogioDoSistemaDiretamente(string nomeDoProjeto)
    {
        var infratores = new List<string>();

        foreach (var arquivo in Repositorio.ArquivosDeCodigo(nomeDoProjeto))
        {
            if (ArquivosAutorizados.Contains(Path.GetFileName(arquivo), StringComparer.Ordinal))
            {
                continue;
            }

            // Comentário e documentação citam a regra de propósito — inclusive o
            // próprio IRelogio, que explica o que não se deve chamar. Aqui só
            // interessa o código executável.
            var conteudo = RemoverComentarios(File.ReadAllText(arquivo));

            foreach (Match ocorrencia in LeituraDiretaDeRelogio().Matches(conteudo))
            {
                var linha = conteudo.Take(ocorrencia.Index).Count(caractere => caractere == '\n') + 1;
                infratores.Add($"{Path.GetFileName(arquivo)}:{linha} → {ocorrencia.Value}");
            }
        }

        Assert.True(
            infratores.Count == 0,
            "Leitura direta do relógio do sistema fora da abstração IRelogio:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, infratores));
    }

    /// <summary>
    /// Apaga comentário de bloco e de linha, preservando a contagem de linhas para
    /// que a posição relatada continue apontando para o lugar certo do arquivo.
    /// </summary>
    private static string RemoverComentarios(string conteudo)
    {
        var semBloco = ComentarioDeBloco().Replace(conteudo, encontrado =>
            new string('\n', encontrado.Value.Count(caractere => caractere == '\n')));

        var linhas = semBloco.Split('\n');

        for (var indice = 0; indice < linhas.Length; indice++)
        {
            var linha = linhas[indice];

            if (linha.TrimStart().StartsWith("//", StringComparison.Ordinal))
            {
                linhas[indice] = string.Empty;
                continue;
            }

            // Comentário no fim de uma linha de código. A checagem de aspas evita
            // cortar o meio de um literal de texto que contenha "//", como uma URL.
            var inicioDoComentario = linha.IndexOf("//", StringComparison.Ordinal);
            if (inicioDoComentario >= 0 && !linha.Contains('"', StringComparison.Ordinal))
            {
                linhas[indice] = linha[..inicioDoComentario];
            }
        }

        return string.Join('\n', linhas);
    }

    [GeneratedRegex(
        @"\b(?:DateTime|DateTimeOffset)\s*\.\s*(?:UtcNow|Now|Today)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex LeituraDiretaDeRelogio();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ComentarioDeBloco();
}
