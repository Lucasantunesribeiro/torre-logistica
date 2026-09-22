using Testcontainers.Azurite;

namespace TorreLogistica.IntegrationTests.Infra;

/// <summary>
/// Azurite — o emulador oficial do Azure Storage — subido por teste que precisa do provedor <c>blob</c>.
/// </summary>
/// <remarks>
/// <para>
/// A suíte não toca em conta real do Azure: nada aqui depende de assinatura, credencial ou rede externa.
/// O emulador fala o mesmo protocolo do serviço, então upload, leitura, metadados, recusa por assinatura
/// inválida e recusa por assinatura vencida são exercitados de verdade.
/// </para>
/// <para>
/// <b>Duas coisas o emulador não faz</b>, e por isso não são provadas aqui:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>Chave de delegação de usuário</b> (<c>GetUserDelegationKey</c>), que é como a produção assina a SAS
/// com identidade gerenciada. Por isso os testes autenticam por chave de conta — a do
/// <c>devstoreaccount1</c>, publicada pela Microsoft na documentação do emulador e sem valor fora dele.
/// A escolha entre os dois caminhos é coberta por teste de unidade, e o caminho de delegação só pode ser
/// confirmado depois do provisionamento real.
/// </description></item>
/// <item><description>
/// <b>RBAC do Azure</b>: o emulador não avalia papel nenhum. Que <c>Storage Blob Data Contributor</c> e
/// <c>Storage Blob Delegator</c> bastem para as operações usadas é afirmação a conferir no ambiente real.
/// </description></item>
/// </list>
/// </remarks>
public sealed class ContainerAzurite : IAsyncLifetime
{
    /// <summary>Imagem do emulador.</summary>
    public const string Imagem = "mcr.microsoft.com/azure-storage/azurite:3.35.0";

    /// <summary>Conta do emulador, fixa e conhecida.</summary>
    public const string Conta = "devstoreaccount1";

    /// <summary>
    /// Chave do emulador.
    /// </summary>
    /// <remarks>
    /// Não é segredo: é constante publicada pela Microsoft, igual em toda instalação do Azurite, e não
    /// dá acesso a nada além deste contêiner descartável. A varredura de segredos do repositório a
    /// reconhece por este comentário.
    /// </remarks>
    public const string Chave =
        "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    /// <summary>Contêiner dos comprovantes dentro do emulador.</summary>
    public const string Contedor = "comprovantes";

    /// <summary>
    /// Versão da API que o emulador entende.
    /// </summary>
    /// <remarks>
    /// O SDK declara uma versão mais nova que a do Azurite, e o emulador recusa com 400 e a mensagem
    /// "The API version ... is not supported by Azurite". Fixar aqui mantém o teste independente do
    /// ritmo de lançamento do SDK; contra o Azure real a versão fica no padrão.
    /// </remarks>
    public const string VersaoDoServico = "V2025_07_05";

    // O comando repete o padrão da imagem e acrescenta --skipApiVersionCheck: o SDK do Azure costuma
    // declarar uma versão de API mais nova que a do emulador, e sem o parâmetro o Azurite recusa com
    // 400 sem corpo — erro que não diz o que é.
    private readonly AzuriteContainer _azurite = new AzuriteBuilder(Imagem)
        .WithCleanUp(true)
        .Build();

    /// <summary>Endereço do serviço de blob do emulador, já com a conta no caminho.</summary>
    public string EndpointDoServico { get; private set; } = string.Empty;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        await _azurite.StartAsync().ConfigureAwait(false);

        // GetBlobEndpoint já devolve o endereço COM a conta no caminho
        // (http://host:porta/devstoreaccount1). Acrescentar a conta de novo produz
        // ".../devstoreaccount1/devstoreaccount1", que o emulador lê como contêiner e recusa.
        EndpointDoServico = _azurite.GetBlobEndpoint().TrimEnd('/');

        // O contêiner é criado aqui, e não pela aplicação: em produção quem o cria é o Bicep, e a
        // aplicação nunca tem permissão para criar contêiner. O teste reproduz essa divisão.
        var servico = ServicoDeBlob();

        try
        {
            await servico.GetBlobContainerClient(Contedor)
                .CreateIfNotExistsAsync(Azure.Storage.Blobs.Models.PublicAccessType.None)
                .ConfigureAwait(false);
        }
        catch (Exception excecao)
        {
            // Sem isto, a falha aparece como "retry failed" sem dizer contra qual endereço.
            throw new InvalidOperationException(
                $"Não foi possível preparar o contêiner '{Contedor}' em '{EndpointDoServico}' "
                + $"(bruto: '{_azurite.GetBlobEndpoint()}').", excecao);
        }
    }

    /// <summary>Configuração que aponta a API para este emulador.</summary>
    public IReadOnlyDictionary<string, string?> Configuracao(string? contedor = null) =>
        new Dictionary<string, string?>
        {
            ["Torre:Armazenamento:Provedor"] = "blob",
            ["Torre:Armazenamento:Blob:Conta"] = Conta,
            ["Torre:Armazenamento:Blob:Contedor"] = contedor ?? Contedor,
            ["Torre:Armazenamento:Blob:EndpointDoServico"] = EndpointDoServico,
            ["Torre:Armazenamento:Blob:Autenticacao"] = "chave-de-conta",
            ["Torre:Armazenamento:Blob:ChaveDaConta"] = Chave,
            ["Torre:Armazenamento:Blob:VersaoDoServico"] = VersaoDoServico,
        };

    /// <summary>Cliente direto no emulador, para o teste conferir o que a API gravou.</summary>
    public Azure.Storage.Blobs.BlobContainerClient ClienteDoContedor(string? contedor = null) =>
        ServicoDeBlob().GetBlobContainerClient(contedor ?? Contedor);

    private Azure.Storage.Blobs.BlobServiceClient ServicoDeBlob() =>
        new(
            new Uri(EndpointDoServico),
            new Azure.Storage.StorageSharedKeyCredential(Conta, Chave),
            new Azure.Storage.Blobs.BlobClientOptions(
                Enum.Parse<Azure.Storage.Blobs.BlobClientOptions.ServiceVersion>(VersaoDoServico)));

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _azurite.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
