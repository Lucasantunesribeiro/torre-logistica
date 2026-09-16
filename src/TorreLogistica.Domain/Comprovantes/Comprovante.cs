using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Comum;

namespace TorreLogistica.Domain.Comprovantes;

/// <summary>O que o arquivo prova.</summary>
public enum TipoDeArquivoDoComprovante
{
    /// <summary>Foto do produto entregue, do local ou do protocolo assinado.</summary>
    Foto = 1,

    /// <summary>Assinatura de quem recebeu, capturada na tela.</summary>
    Assinatura = 2,
}

/// <summary>Limites do comprovante — os mesmos no domínio, na API e no aparelho.</summary>
public static class PoliticaDeComprovante
{
    /// <summary>Tamanho máximo de um arquivo.</summary>
    public const long TamanhoMaximoEmBytes = 5 * 1024 * 1024;

    /// <summary>Quantidade máxima de arquivos num comprovante.</summary>
    public const int QuantidadeMaximaDeArquivos = 4;

    /// <summary>Tamanho máximo do nome de quem recebeu.</summary>
    public const int TamanhoMaximoDoRecebedor = 120;

    /// <summary>Tamanho máximo da observação.</summary>
    public const int TamanhoMaximoDaObservacao = 500;

    /// <summary>Tamanho máximo da chave do objeto no storage.</summary>
    public const int TamanhoMaximoDaChave = 200;

    /// <summary>
    /// Tipos aceitos. Lista fechada: o que o navegador declara não é prova, e o storage confere o conteúdo
    /// real na gravação.
    /// </summary>
    public static readonly IReadOnlyList<string> TiposDeConteudoAceitos = ["image/jpeg", "image/png", "image/webp"];

    /// <summary>O tipo de conteúdo está na lista aceita.</summary>
    public static bool TipoAceito(string? tipoDeConteudo) =>
        tipoDeConteudo is not null && TiposDeConteudoAceitos.Contains(tipoDeConteudo, StringComparer.Ordinal);

    /// <summary>Extensão de arquivo do tipo aceito.</summary>
    public static string ExtensaoDe(string tipoDeConteudo) => tipoDeConteudo switch
    {
        "image/jpeg" => "jpg",
        "image/png" => "png",
        "image/webp" => "webp",
        _ => throw ExcecaoDeDominio.RegraViolada("tipo_de_arquivo_nao_aceito", "Tipo de arquivo não aceito."),
    };
}

/// <summary>
/// Um arquivo do comprovante: o binário vive no storage, e aqui ficam só os metadados (ADR 0007).
/// </summary>
public sealed class ArquivoDoComprovante
{
    private ArquivoDoComprovante()
    {
        Chave = string.Empty;
        TipoDeConteudo = string.Empty;
        HashSha256 = string.Empty;
    }

    /// <summary>Identificador (UUIDv7).</summary>
    public Guid Id { get; private set; }

    /// <summary>Organização.</summary>
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Comprovante a que pertence.</summary>
    public Guid ComprovanteId { get; private set; }

    /// <summary>O que o arquivo prova.</summary>
    public TipoDeArquivoDoComprovante Tipo { get; private set; }

    /// <summary>Chave do objeto no storage.</summary>
    public string Chave { get; private set; }

    /// <summary>Tipo de conteúdo real, conferido na gravação.</summary>
    public string TipoDeConteudo { get; private set; }

    /// <summary>Tamanho real, em bytes.</summary>
    public long TamanhoEmBytes { get; private set; }

    /// <summary>SHA-256 do conteúdo, em hexadecimal minúsculo.</summary>
    public string HashSha256 { get; private set; }

    /// <summary>Instante em que o objeto foi gravado no storage.</summary>
    public DateTimeOffset EnviadoEm { get; private set; }

    internal static ArquivoDoComprovante Registrar(
        Guid id,
        Guid organizacaoId,
        Guid comprovanteId,
        TipoDeArquivoDoComprovante tipo,
        string chave,
        string tipoDeConteudo,
        long tamanhoEmBytes,
        string hashSha256,
        DateTimeOffset enviadoEm)
    {
        ExcecaoDeDominio.LancarSe(id == Guid.Empty, "identificador_invalido", "Identificador inválido.");
        ExcecaoDeDominio.LancarSe(!Enum.IsDefined(tipo), "tipo_de_arquivo_invalido", "Tipo de arquivo de comprovante desconhecido.");
        ExcecaoDeDominio.LancarSe(
            !PoliticaDeComprovante.TipoAceito(tipoDeConteudo),
            "tipo_de_arquivo_nao_aceito",
            $"Tipo de arquivo não aceito. Aceitos: {string.Join(", ", PoliticaDeComprovante.TiposDeConteudoAceitos)}.");
        ExcecaoDeDominio.LancarSe(
            tamanhoEmBytes <= 0 || tamanhoEmBytes > PoliticaDeComprovante.TamanhoMaximoEmBytes,
            "arquivo_grande_demais",
            $"Cada arquivo do comprovante vai de 1 byte a {PoliticaDeComprovante.TamanhoMaximoEmBytes / (1024 * 1024)} MB.");

        var chaveValida = TextoNormalizado.Obrigatorio(
            chave, PoliticaDeComprovante.TamanhoMaximoDaChave, "chave_invalida", "A chave do arquivo");
        var hash = TextoNormalizado.Obrigatorio(hashSha256, 64, "hash_invalido", "O hash do arquivo");
        ExcecaoDeDominio.LancarSe(
            hash.Length != 64 || !hash.All(char.IsAsciiHexDigitLower),
            "hash_invalido",
            "O hash do arquivo precisa ser um SHA-256 em hexadecimal minúsculo.");

        return new ArquivoDoComprovante
        {
            Id = id,
            OrganizacaoId = organizacaoId,
            ComprovanteId = comprovanteId,
            Tipo = tipo,
            Chave = chaveValida,
            TipoDeConteudo = tipoDeConteudo,
            TamanhoEmBytes = tamanhoEmBytes,
            HashSha256 = hash,
            EnviadoEm = enviadoEm.ToUniversalTime(),
        };
    }
}

/// <summary>
/// Um arquivo já enviado ao storage, como a aplicação o confirma.
/// </summary>
/// <remarks>
/// Classe com propriedades somente-leitura, e não <c>record</c> posicional: o posicional gera <c>init</c>
/// público, e o domínio não expõe setter (teste de arquitetura).
/// </remarks>
/// <param name="tipo">O que o arquivo prova.</param>
/// <param name="chave">Chave do objeto no storage.</param>
/// <param name="tipoDeConteudo">Tipo de conteúdo real.</param>
/// <param name="tamanhoEmBytes">Tamanho real.</param>
/// <param name="hashSha256">SHA-256 do conteúdo.</param>
/// <param name="enviadoEm">Instante da gravação no storage.</param>
public sealed class ArquivoConfirmado(
    TipoDeArquivoDoComprovante tipo,
    string chave,
    string tipoDeConteudo,
    long tamanhoEmBytes,
    string hashSha256,
    DateTimeOffset enviadoEm)
{
    /// <summary>O que o arquivo prova.</summary>
    public TipoDeArquivoDoComprovante Tipo { get; } = tipo;

    /// <summary>Chave do objeto no storage.</summary>
    public string Chave { get; } = chave;

    /// <summary>Tipo de conteúdo real.</summary>
    public string TipoDeConteudo { get; } = tipoDeConteudo;

    /// <summary>Tamanho real, em bytes.</summary>
    public long TamanhoEmBytes { get; } = tamanhoEmBytes;

    /// <summary>SHA-256 do conteúdo.</summary>
    public string HashSha256 { get; } = hashSha256;

    /// <summary>Instante da gravação no storage.</summary>
    public DateTimeOffset EnviadoEm { get; } = enviadoEm;
}

/// <summary>
/// A prova de que a entrega aconteceu: quem recebeu, quando, onde, com que evidência e registrada por quem.
/// </summary>
/// <remarks>
/// <para>
/// Somente-inserção e único por entrega: o comprovante é o fato que sustenta a conclusão, e conclusão não se
/// reescreve. Repetir a conclusão não cria outro comprovante.
/// </para>
/// <para>
/// Os binários ficam no storage privado; aqui moram os metadados que permitem localizá-los, conferir
/// integridade (hash) e decidir quem pode lê-los.
/// </para>
/// </remarks>
public sealed class Comprovante
{
    private readonly List<ArquivoDoComprovante> _arquivos = [];

    private Comprovante()
    {
        RecebidoPor = string.Empty;
    }

    /// <summary>Identificador (UUIDv7).</summary>
    public Guid Id { get; private set; }

    /// <summary>Organização.</summary>
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Entrega provada.</summary>
    public Guid EntregaId { get; private set; }

    /// <summary>Rota em que a entrega foi feita.</summary>
    public Guid? RotaId { get; private set; }

    /// <summary>Motorista que entregou.</summary>
    public Guid? MotoristaId { get; private set; }

    /// <summary>Nome de quem recebeu.</summary>
    public string RecebidoPor { get; private set; }

    /// <summary>Observação de quem registrou.</summary>
    public string? Observacao { get; private set; }

    /// <summary>Onde a entrega foi concluída, quando o aparelho informou.</summary>
    public CoordenadaGeografica? Localizacao { get; private set; }

    /// <summary>Instante do registro.</summary>
    public DateTimeOffset RegistradoEm { get; private set; }

    /// <summary>Conta que registrou.</summary>
    public Guid AutorUsuarioId { get; private set; }

    /// <summary>Arquivos que provam a entrega.</summary>
    public IReadOnlyList<ArquivoDoComprovante> Arquivos => _arquivos;

    /// <summary>Registra o comprovante com os arquivos já confirmados no storage.</summary>
    public static Comprovante Registrar(
        Guid id,
        Guid organizacaoId,
        Guid entregaId,
        Guid? rotaId,
        Guid? motoristaId,
        string? recebidoPor,
        string? observacao,
        CoordenadaGeografica? localizacao,
        IReadOnlyList<ArquivoConfirmado> arquivos,
        Guid autorUsuarioId,
        Func<Guid> novoIdentificador,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(arquivos);
        ArgumentNullException.ThrowIfNull(novoIdentificador);

        ExcecaoDeDominio.LancarSe(
            id == Guid.Empty || organizacaoId == Guid.Empty || entregaId == Guid.Empty || autorUsuarioId == Guid.Empty,
            "identificador_invalido",
            "Identificador inválido.");

        var recebedor = TextoNormalizado.Obrigatorio(
            recebidoPor, PoliticaDeComprovante.TamanhoMaximoDoRecebedor, "recebedor_invalido", "O nome de quem recebeu");
        var texto = TextoNormalizado.Opcional(
            observacao, PoliticaDeComprovante.TamanhoMaximoDaObservacao, "observacao_invalida", "A observação");

        ExcecaoDeDominio.LancarSe(
            arquivos.Count > PoliticaDeComprovante.QuantidadeMaximaDeArquivos,
            "arquivos_demais",
            $"O comprovante aceita no máximo {PoliticaDeComprovante.QuantidadeMaximaDeArquivos} arquivos.");
        ExcecaoDeDominio.LancarSe(
            arquivos.Select(arquivo => arquivo.Chave).Distinct(StringComparer.Ordinal).Count() != arquivos.Count,
            "arquivo_repetido",
            "O mesmo arquivo foi informado duas vezes.");

        var instante = agora.ToUniversalTime();
        var comprovante = new Comprovante
        {
            Id = id,
            OrganizacaoId = organizacaoId,
            EntregaId = entregaId,
            RotaId = rotaId,
            MotoristaId = motoristaId,
            RecebidoPor = recebedor,
            Observacao = texto,
            Localizacao = localizacao,
            RegistradoEm = instante,
            AutorUsuarioId = autorUsuarioId,
        };

        foreach (var arquivo in arquivos)
        {
            comprovante._arquivos.Add(ArquivoDoComprovante.Registrar(
                novoIdentificador(),
                organizacaoId,
                id,
                arquivo.Tipo,
                arquivo.Chave,
                arquivo.TipoDeConteudo,
                arquivo.TamanhoEmBytes,
                arquivo.HashSha256,
                arquivo.EnviadoEm));
        }

        return comprovante;
    }

    /// <summary>Tem ao menos um arquivo — foto ou assinatura.</summary>
    public bool TemEvidencia => _arquivos.Count > 0;
}
