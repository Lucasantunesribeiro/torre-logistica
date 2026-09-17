using System.Globalization;
using System.Text;
using TorreLogistica.Application.Cadastros;
using TorreLogistica.Application.Entregas;
using TorreLogistica.Domain.Abstracoes.Erros;

namespace TorreLogistica.Application.Integracoes;

/// <summary>Problema em uma linha do arquivo.</summary>
/// <param name="Linha">Número da linha no arquivo, contando o cabeçalho como 1.</param>
public sealed record ErroDaLinha(int Linha, string Campo, string Codigo, string Mensagem);

/// <summary>Conferência do arquivo, sem gravar nada.</summary>
public sealed record PreviaDaImportacao(
    string HashDoArquivo,
    int TotalDeLinhas,
    int LinhasValidas,
    IReadOnlyList<ErroDaLinha> Erros);

/// <summary>Linha que virou entrega.</summary>
public sealed record LinhaImportada(int Linha, string IdentificadorExterno, Guid EntregaId, string Codigo, bool JaExistia);

/// <summary>Resultado da importação confirmada.</summary>
public sealed record ResultadoDaImportacao(
    string HashDoArquivo,
    int TotalDeLinhas,
    IReadOnlyList<LinhaImportada> Importadas,
    IReadOnlyList<ErroDaLinha> Erros);

/// <summary>
/// Leitor de CSV conforme o subconjunto do RFC 4180 que a importação aceita.
/// </summary>
/// <remarks>
/// Escrito à mão, e não trazido de biblioteca, porque o formato aceito aqui é definido por nós: cabeçalho
/// fixo, sem campos multilinha, e um punhado de colunas conhecidas. O que precisa estar certo — vírgula
/// dentro de aspas e aspas escapadas — cabe em poucas linhas e fica coberto por teste, enquanto uma
/// dependência nova custaria revisão de segurança e manutenção (CLAUDE.md, seção 71).
/// </remarks>
public static class LeitorDeCsv
{
    /// <summary>Separa o conteúdo em linhas de campos.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> Ler(string? conteudo)
    {
        var linhas = new List<IReadOnlyList<string>>();

        if (string.IsNullOrWhiteSpace(conteudo))
        {
            return linhas;
        }

        // BOM de planilha do Windows: sem isto, a primeira coluna do cabeçalho nunca casa.
        var texto = conteudo.TrimStart('﻿');

        foreach (var bruta in texto.Split('\n'))
        {
            var linha = bruta.TrimEnd('\r');

            if (linha.Length == 0)
            {
                continue;
            }

            linhas.Add(SepararCampos(linha));
        }

        return linhas;
    }

    private static List<string> SepararCampos(string linha)
    {
        var campos = new List<string>();
        var atual = new StringBuilder();
        var entreAspas = false;

        for (var posicao = 0; posicao < linha.Length; posicao++)
        {
            var caractere = linha[posicao];

            if (entreAspas)
            {
                if (caractere != '"')
                {
                    atual.Append(caractere);
                    continue;
                }

                // Aspas dobradas dentro do campo são uma aspa literal.
                if (posicao + 1 < linha.Length && linha[posicao + 1] == '"')
                {
                    atual.Append('"');
                    posicao++;
                    continue;
                }

                entreAspas = false;
                continue;
            }

            switch (caractere)
            {
                case '"':
                    entreAspas = true;
                    break;
                case ',':
                    campos.Add(atual.ToString().Trim());
                    atual.Clear();
                    break;
                default:
                    atual.Append(caractere);
                    break;
            }
        }

        campos.Add(atual.ToString().Trim());
        return campos;
    }
}

/// <summary>
/// Importação de entregas por arquivo CSV.
/// </summary>
/// <remarks>
/// <para>
/// Duas etapas de propósito. A <b>prévia</b> confere o arquivo inteiro sem gravar nada; a
/// <b>confirmação</b> recusa o lote antes de começar se houver um só erro de formato. Assim não existe
/// "metade importada" por dado inválido — o caso que o operador descobriria só depois.
/// </para>
/// <para>
/// Cada linha entra com chave de idempotência derivada do <b>hash do arquivo</b> e do número da linha.
/// Reenviar o mesmo arquivo é seguro por construção: cada linha reencontra a própria chave e o
/// identificador de origem, e nada é duplicado — inclusive depois de uma queda no meio da importação.
/// </para>
/// </remarks>
public sealed class ImportacaoDeEntregas(RecepcaoDeEntregasExternas recepcao)
{
    /// <summary>Linhas de dados aceitas num arquivo.</summary>
    public const int LimiteDeLinhas = 500;

    /// <summary>Colunas esperadas, nesta ordem.</summary>
    public static readonly string[] Cabecalho =
    [
        "identificador_externo",
        "cliente_id",
        "destinatario_id",
        "prometida_de",
        "prometida_ate",
        "logradouro",
        "numero",
        "complemento",
        "bairro",
        "cidade",
        "uf",
        "cep",
        "latitude",
        "longitude",
        "observacoes",
    ];

    /// <summary>Confere o arquivo sem gravar nada.</summary>
    public static PreviaDaImportacao Analisar(string? conteudo)
    {
        var (hash, linhas, erros) = Interpretar(conteudo);

        return new PreviaDaImportacao(
            hash,
            linhas.Count,
            linhas.Count(linha => erros.All(erro => erro.Linha != linha.Numero)),
            erros);
    }

    /// <summary>Importa o arquivo. Recusa o lote inteiro se a conferência achar qualquer erro.</summary>
    public async Task<ResultadoDaImportacao> ImportarAsync(
        Guid integracaoId,
        string? conteudo,
        CancellationToken cancelamento)
    {
        var (hash, linhas, erros) = Interpretar(conteudo);

        if (erros.Count > 0)
        {
            return new ResultadoDaImportacao(hash, linhas.Count, [], erros);
        }

        var importadas = new List<LinhaImportada>(linhas.Count);
        var recusadas = new List<ErroDaLinha>();

        foreach (var linha in linhas)
        {
            try
            {
                var recebida = await recepcao
                    .ReceberAsync(
                        integracaoId,
                        $"csv:{hash}:{linha.Numero}",
                        SegredosDeIntegracao.CalcularHashDoCorpo(Encoding.UTF8.GetBytes(linha.Original)),
                        linha.IdentificadorExterno,
                        linha.Dados,
                        cancelamento)
                    .ConfigureAwait(false);

                importadas.Add(new LinhaImportada(
                    linha.Numero,
                    linha.IdentificadorExterno,
                    recebida.Entrega.Id,
                    recebida.Entrega.Codigo,
                    recebida.JaExistia));
            }
            catch (ExcecaoDeDominio excecao)
            {
                // Regra de negócio só reprova na gravação — cliente inexistente, janela no passado,
                // destinatário de outra organização. A linha é relatada e as demais seguem: parar aqui
                // deixaria o lote pela metade sem que ninguém soubesse o porquê.
                recusadas.Add(new ErroDaLinha(linha.Numero, "linha", excecao.Codigo, excecao.Message));
            }
        }

        return new ResultadoDaImportacao(hash, linhas.Count, importadas, recusadas);
    }

    private static (string Hash, List<LinhaDeImportacao> Linhas, List<ErroDaLinha> Erros) Interpretar(string? conteudo)
    {
        var hash = Convert.ToHexString(
            SegredosDeIntegracao.CalcularHashDoCorpo(Encoding.UTF8.GetBytes(conteudo ?? string.Empty))).ToLowerInvariant();

        var linhas = new List<LinhaDeImportacao>();
        var erros = new List<ErroDaLinha>();
        var registros = LeitorDeCsv.Ler(conteudo);

        if (registros.Count == 0)
        {
            erros.Add(new ErroDaLinha(0, "arquivo", "arquivo_vazio", "O arquivo não tem linhas."));
            return (hash, linhas, erros);
        }

        if (!registros[0].SequenceEqual(Cabecalho, StringComparer.OrdinalIgnoreCase))
        {
            erros.Add(new ErroDaLinha(
                1,
                "cabecalho",
                "cabecalho_invalido",
                $"O cabeçalho precisa ser exatamente: {string.Join(", ", Cabecalho)}."));

            return (hash, linhas, erros);
        }

        if (registros.Count - 1 > LimiteDeLinhas)
        {
            erros.Add(new ErroDaLinha(
                0, "arquivo", "arquivo_grande_demais", $"O arquivo passa de {LimiteDeLinhas} linhas de dados."));

            return (hash, linhas, erros);
        }

        var identificadores = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var indice = 1; indice < registros.Count; indice++)
        {
            var numero = indice + 1;
            var campos = registros[indice];

            if (campos.Count != Cabecalho.Length)
            {
                erros.Add(new ErroDaLinha(
                    numero, "linha", "colunas_invalidas", $"A linha tem {campos.Count} colunas; esperadas {Cabecalho.Length}."));
                continue;
            }

            var antes = erros.Count;
            var identificador = Texto(campos[0]);

            if (identificador is null)
            {
                erros.Add(new ErroDaLinha(numero, "identificador_externo", "campo_obrigatorio", "Informe o identificador de origem."));
            }
            else if (!identificadores.Add(identificador))
            {
                // Duplicata dentro do próprio arquivo: o índice único pegaria na gravação, mas avisar
                // agora evita importar metade e descobrir depois.
                erros.Add(new ErroDaLinha(
                    numero, "identificador_externo", "identificador_repetido", "Este identificador aparece mais de uma vez no arquivo."));
            }

            var cliente = Identificador(campos[1], numero, "cliente_id", erros);
            var destinatario = Identificador(campos[2], numero, "destinatario_id", erros);
            var de = Instante(campos[3], numero, "prometida_de", erros);
            var ate = Instante(campos[4], numero, "prometida_ate", erros);
            var latitude = Numero(campos[12], numero, "latitude", erros);
            var longitude = Numero(campos[13], numero, "longitude", erros);

            if (erros.Count != antes)
            {
                continue;
            }

            var endereco = Texto(campos[5]) is null
                ? null
                : new DadosDeEndereco(
                    campos[5], campos[6], Texto(campos[7]), campos[8], campos[9], campos[10], campos[11]);

            linhas.Add(new LinhaDeImportacao(
                numero,
                identificador!,
                string.Join(',', campos),
                new DadosDeEntrega(
                    cliente!.Value, destinatario!.Value, endereco, latitude, longitude, de!.Value, ate!.Value, Texto(campos[14]))));
        }

        return (hash, linhas, erros);
    }

    private static string? Texto(string valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static Guid? Identificador(string valor, int linha, string campo, List<ErroDaLinha> erros)
    {
        if (Guid.TryParse(valor, out var identificador))
        {
            return identificador;
        }

        erros.Add(new ErroDaLinha(linha, campo, "identificador_invalido", "Informe um identificador válido."));
        return null;
    }

    private static DateTimeOffset? Instante(string valor, int linha, string campo, List<ErroDaLinha> erros)
    {
        if (DateTimeOffset.TryParse(valor, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var instante))
        {
            return instante;
        }

        erros.Add(new ErroDaLinha(linha, campo, "data_invalida", "Informe data e hora com fuso, no formato ISO 8601."));
        return null;
    }

    private static double? Numero(string valor, int linha, string campo, List<ErroDaLinha> erros)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        if (double.TryParse(valor, NumberStyles.Float, CultureInfo.InvariantCulture, out var numero))
        {
            return numero;
        }

        erros.Add(new ErroDaLinha(linha, campo, "numero_invalido", "Informe um número com ponto decimal."));
        return null;
    }

    private sealed record LinhaDeImportacao(int Numero, string IdentificadorExterno, string Original, DadosDeEntrega Dados);
}
