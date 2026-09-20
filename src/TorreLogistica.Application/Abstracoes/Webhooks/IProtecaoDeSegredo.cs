namespace TorreLogistica.Application.Abstracoes.Webhooks;

/// <summary>
/// Cifra e decifra segredos que precisam ser recuperados, como o de assinatura de webhook.
/// </summary>
/// <remarks>
/// <para>
/// Diferente de senha e de chave de API, este segredo <b>não</b> pode ser hash: cada entrega precisa dele
/// em claro para calcular o HMAC. A proteção possível é criptografia em repouso — quem lê o banco sem a
/// chave de configuração não consegue assinar nada em nosso nome.
/// </para>
/// <para>
/// A implementação vive na infraestrutura porque a escolha do algoritmo e a origem da chave são detalhes
/// de implantação, não regra de negócio.
/// </para>
/// </remarks>
public interface IProtecaoDeSegredo
{
    /// <summary>Cifra o segredo.</summary>
    byte[] Cifrar(string segredo);

    /// <summary>Decifra o segredo.</summary>
    /// <exception cref="InvalidOperationException">Quando o conteúdo não abre com a chave atual.</exception>
    string Decifrar(byte[] cifrado);
}

/// <summary>Resultado de uma tentativa de entrega HTTP.</summary>
/// <param name="Sucesso">O assinante respondeu com 2xx.</param>
/// <param name="Status">Status HTTP, quando houve resposta.</param>
/// <param name="Erro">Motivo, quando não houve resposta utilizável.</param>
/// <param name="DuracaoEmMilissegundos">Quanto demorou.</param>
public sealed record ResultadoDaEntregaDeWebhook(bool Sucesso, int? Status, string? Erro, int DuracaoEmMilissegundos);

/// <summary>Envia o POST assinado ao assinante.</summary>
/// <remarks>
/// Fronteira externa de verdade: a implementação fala HTTP com endereço que o cliente cadastrou, com tempo
/// limite curto e sem seguir redirecionamento.
/// </remarks>
public interface IClienteDeWebhook
{
    /// <summary>Entrega o evento e devolve o que aconteceu, sem lançar por falha do assinante.</summary>
    Task<ResultadoDaEntregaDeWebhook> EntregarAsync(
        string url,
        string tipo,
        Guid mensagemId,
        string conteudo,
        string segredo,
        CancellationToken cancelamento);
}
