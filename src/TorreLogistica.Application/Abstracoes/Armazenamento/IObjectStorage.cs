namespace TorreLogistica.Application.Abstracoes.Armazenamento;

/// <summary>Uma URL assinada de curta duração, com os cabeçalhos que o cliente precisa enviar.</summary>
/// <param name="Url">Endereço completo, já assinado.</param>
/// <param name="ExpiraEm">Fim da validade da assinatura.</param>
/// <param name="CabecalhosObrigatorios">Cabeçalhos que fazem parte do que foi autorizado.</param>
public sealed record UrlAssinada(
    Uri Url,
    DateTimeOffset ExpiraEm,
    IReadOnlyDictionary<string, string> CabecalhosObrigatorios);

/// <summary>Metadados de um objeto já gravado no storage.</summary>
/// <param name="Chave">Chave do objeto.</param>
/// <param name="TamanhoEmBytes">Tamanho real do que foi gravado.</param>
/// <param name="TipoDeConteudo">Tipo de conteúdo real, conferido na gravação.</param>
/// <param name="HashSha256">SHA-256 do conteúdo, em hexadecimal minúsculo.</param>
/// <param name="GravadoEm">Instante da gravação.</param>
public sealed record ObjetoArmazenado(
    string Chave,
    long TamanhoEmBytes,
    string TipoDeConteudo,
    string HashSha256,
    DateTimeOffset GravadoEm);

/// <summary>
/// Storage de objeto para arquivos binários (ADR 0007).
/// </summary>
/// <remarks>
/// <para>
/// Arquivo não entra no PostgreSQL e não passa pela API: o cliente pede autorização, recebe uma URL
/// assinada curta e envia direto ao storage. O banco guarda só os metadados, e só <b>depois</b> de o objeto
/// existir — metadado sem objeto é inconsistência detectável, objeto sem metadado é lixo recolhível.
/// </para>
/// <para>
/// Nenhuma leitura é pública: quem pode ler recebe outra URL assinada curta, emitida depois de conferir
/// autorização e tenant. A implementação de produção (S3 ou compatível) é decisão da Fase 25; a abstração
/// existe para que essa escolha não alcance o domínio.
/// </para>
/// </remarks>
public interface IObjectStorage
{
    /// <summary>
    /// Autoriza o envio de um objeto: a assinatura vale para esta chave, este tipo de conteúdo e até este
    /// tamanho. Enviar outra coisa com a mesma URL é recusado pelo storage.
    /// </summary>
    Task<UrlAssinada> AutorizarEnvioAsync(
        string chave,
        string tipoDeConteudo,
        long tamanhoMaximoEmBytes,
        CancellationToken cancelamento);

    /// <summary>Autoriza a leitura de um objeto por tempo curto.</summary>
    Task<UrlAssinada> AutorizarLeituraAsync(string chave, CancellationToken cancelamento);

    /// <summary>Metadados do objeto, ou <see langword="null"/> se ele não existe.</summary>
    Task<ObjetoArmazenado?> ObterAsync(string chave, CancellationToken cancelamento);
}
