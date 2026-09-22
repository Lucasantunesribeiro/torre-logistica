using System.Globalization;
using System.Security.Cryptography;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TorreLogistica.Application.Abstracoes.Armazenamento;
using TorreLogistica.Domain.Abstracoes.Tempo;

namespace TorreLogistica.Infrastructure.Armazenamento;

/// <summary>
/// Storage de objetos no Azure Blob Storage, com contêiner privado e SAS de curta duração.
/// </summary>
/// <remarks>
/// <para>
/// A fronteira Azure termina nesta classe: nenhum tipo do SDK atravessa <see cref="IObjectStorage"/>, e
/// nem o domínio nem a aplicação sabem que ela existe. Trocar por outro fornecedor é escrever outro
/// adaptador.
/// </para>
/// <para>
/// <b>Nada é público.</b> O contêiner não tem acesso anônimo, e o banco guarda apenas a chave lógica do
/// objeto — nunca uma URL. Toda URL é gerada na hora, com assinatura que expira em minutos, e só depois
/// de a aplicação ter conferido autorização e tenant.
/// </para>
/// <para>
/// <b>Como a assinatura é feita.</b> Com identidade gerenciada não existe segredo para assinar: o
/// adaptador pede ao Azure uma <i>chave de delegação de usuário</i>, válida por pouco tempo, e assina a
/// SAS com ela. A SAS resultante carrega os poderes da identidade, nunca mais que isso, e o Azure a
/// invalida quando a delegação vence. A chave de delegação é cacheada em memória e renovada antes de
/// expirar, porque pedi-la a cada autorização seria uma ida de rede por foto.
/// </para>
/// <para>
/// <b>Diferença honesta em relação ao adaptador local.</b> No local, o envio passa pela própria API, que
/// confere tipo e corta no tamanho máximo enquanto grava. Uma SAS não consegue prometer nenhum dos dois:
/// o Azure Blob não tem cláusula de tipo nem de tamanho na assinatura — conferido contra o emulador, que
/// aceita com 201 um envio de tipo diferente do assinado. As duas regras continuam valendo, só que um
/// passo depois: <see cref="ObterAsync"/> devolve o tipo e o tamanho reais do que foi gravado, e o
/// domínio recusa o registro do comprovante fora da política. O objeto rejeitado fica órfão, que é a
/// inconsistência já prevista no contrato (objeto sem metadado é lixo recolhível).
/// </para>
/// </remarks>
public sealed class ArmazenamentoBlobDeObjetos : IObjectStorage, IDisposable
{
    /// <summary>Nome do metadado onde o SHA-256 calculado é guardado, para não recalcular.</summary>
    internal const string MetadadoDoHash = "sha256";

    private readonly OpcoesDeArmazenamento _opcoes;
    private readonly OpcoesDoBlob _blob;
    private readonly IRelogio _relogio;
    private readonly ILogger<ArmazenamentoBlobDeObjetos> _log;
    private readonly BlobServiceClient _servico;
    private readonly BlobContainerClient _contedor;
    private readonly StorageSharedKeyCredential? _chaveDaConta;
    private readonly SemaphoreSlim _travaDaDelegacao = new(1, 1);

    private UserDelegationKey? _delegacao;
    private DateTimeOffset _delegacaoValidaAte;

    /// <summary>Monta o cliente a partir da configuração já validada na subida.</summary>
    public ArmazenamentoBlobDeObjetos(
        IOptions<OpcoesDeArmazenamento> opcoes,
        IRelogio relogio,
        ILogger<ArmazenamentoBlobDeObjetos> log)
    {
        ArgumentNullException.ThrowIfNull(opcoes);

        _opcoes = opcoes.Value;
        _blob = _opcoes.Blob;
        _relogio = relogio ?? throw new ArgumentNullException(nameof(relogio));
        _log = log ?? throw new ArgumentNullException(nameof(log));

        var servico = EnderecoDoServico(_blob);
        var clienteOpcoes = OpcoesDoCliente(_blob);

        if (UsaChaveDaConta)
        {
            _chaveDaConta = new StorageSharedKeyCredential(_blob.Conta, _blob.ChaveDaConta);
            _servico = new BlobServiceClient(servico, _chaveDaConta, clienteOpcoes);
        }
        else
        {
            // DefaultAzureCredential resolve a identidade gerenciada da Container App em produção e a
            // credencial do desenvolvedor na máquina. Nenhum segredo é lido de configuração.
            _servico = new BlobServiceClient(servico, new DefaultAzureCredential(), clienteOpcoes);
        }

        _contedor = _servico.GetBlobContainerClient(_blob.Contedor);
    }

    private bool UsaChaveDaConta =>
        string.Equals(_blob.Autenticacao, OpcoesDoBlob.AutenticacaoPorChave, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Opções do cliente, com a versão da API fixada quando a configuração pedir.
    /// </summary>
    /// <remarks>
    /// Contra o Azure real a versão fica no padrão do SDK. Contra o emulador ela é fixada, porque o
    /// emulador recusa versão que não conhece — e a mensagem que ele devolve nem sempre diz isso.
    /// </remarks>
    internal static BlobClientOptions OpcoesDoCliente(OpcoesDoBlob blob)
    {
        ArgumentNullException.ThrowIfNull(blob);

        return string.IsNullOrWhiteSpace(blob.VersaoDoServico)
            ? new BlobClientOptions()
            : new BlobClientOptions(Enum.Parse<BlobClientOptions.ServiceVersion>(blob.VersaoDoServico));
    }

    /// <summary>Endereço do serviço de blob: o configurado, ou o do domínio público derivado da conta.</summary>
    internal static Uri EnderecoDoServico(OpcoesDoBlob blob)
    {
        ArgumentNullException.ThrowIfNull(blob);

        return string.IsNullOrWhiteSpace(blob.EndpointDoServico)
            ? new Uri($"https://{blob.Conta}.blob.core.windows.net", UriKind.Absolute)
            : new Uri(blob.EndpointDoServico, UriKind.Absolute);
    }

    /// <inheritdoc />
    public async Task<UrlAssinada> AutorizarEnvioAsync(
        string chave,
        string tipoDeConteudo,
        long tamanhoMaximoEmBytes,
        CancellationToken cancelamento)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chave);
        ArgumentException.ThrowIfNullOrWhiteSpace(tipoDeConteudo);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(tamanhoMaximoEmBytes, 0);

        var expira = _relogio.AgoraUtc.Add(_opcoes.ValidadeDaUrlDeEnvio);

        // Create e Write, e nada além: a autorização de envio não pode ler nem apagar o que já existe.
        //
        // O tipo NÃO é prendido pela assinatura, e isso foi conferido contra o emulador: subir
        // text/plain com uma SAS emitida para image/jpeg é aceito com 201. O Azure Blob simplesmente não
        // tem cláusula de tipo nem de tamanho na SAS — o campo ContentType do construtor é override da
        // resposta de LEITURA, não regra de escrita.
        //
        // Quem barra é o passo seguinte: ObterAsync devolve o tipo e o tamanho REAIS do que foi gravado,
        // e ArquivoDoComprovante.Criar recusa o registro fora da política. O objeto rejeitado fica órfão,
        // que é a inconsistência já prevista no contrato — objeto sem metadado é lixo recolhível.
        var construtor = Construtor(chave, expira);
        construtor.SetPermissions(BlobSasPermissions.Create | BlobSasPermissions.Write);

        var url = await AssinarAsync(chave, construtor, cancelamento).ConfigureAwait(false);

        return new UrlAssinada(
            url,
            expira,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Content-Type"] = tipoDeConteudo,
                // Exigido pelo Azure em PUT de bloco único; sem ele a requisição é recusada.
                ["x-ms-blob-type"] = "BlockBlob",
            });
    }

    /// <inheritdoc />
    public async Task<UrlAssinada> AutorizarLeituraAsync(string chave, CancellationToken cancelamento)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chave);

        var expira = _relogio.AgoraUtc.Add(_opcoes.ValidadeDaUrlDeLeitura);

        var construtor = Construtor(chave, expira);
        construtor.SetPermissions(BlobSasPermissions.Read);

        var url = await AssinarAsync(chave, construtor, cancelamento).ConfigureAwait(false);

        return new UrlAssinada(url, expira, new Dictionary<string, string>(StringComparer.Ordinal));
    }

    /// <inheritdoc />
    public async Task<ObjetoArmazenado?> ObterAsync(string chave, CancellationToken cancelamento)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chave);

        var blob = _contedor.GetBlobClient(chave);

        BlobProperties propriedades;
        try
        {
            propriedades = await blob.GetPropertiesAsync(cancellationToken: cancelamento).ConfigureAwait(false);
        }
        catch (RequestFailedException excecao) when (excecao.Status == 404)
        {
            // Objeto que não existe não é erro: é o caso "ainda não enviaram", que o chamador trata.
            return null;
        }

        var hash = propriedades.Metadata.TryGetValue(MetadadoDoHash, out var guardado) && !string.IsNullOrWhiteSpace(guardado)
            ? guardado
            : await CalcularEGuardarHashAsync(blob, propriedades, cancelamento).ConfigureAwait(false);

        return new ObjetoArmazenado(
            chave,
            propriedades.ContentLength,
            propriedades.ContentType ?? "application/octet-stream",
            hash,
            propriedades.CreatedOn);
    }

    /// <summary>
    /// Calcula o SHA-256 baixando o objeto uma vez, e o guarda como metadado.
    /// </summary>
    /// <remarks>
    /// O envio vai direto do aparelho para o Storage — a API nunca vê os bytes e não tem como calcular o
    /// resumo no caminho. E o resumo não pode vir do cliente: quem manda o arquivo é justamente quem não
    /// pode atestar o que mandou. Por isso o adaptador lê o objeto uma vez, no registro do comprovante, e
    /// grava o resultado como metadado — as leituras seguintes não baixam nada.
    /// </remarks>
    private async Task<string> CalcularEGuardarHashAsync(
        BlobClient blob,
        BlobProperties propriedades,
        CancellationToken cancelamento)
    {
        await using var conteudo = await blob.OpenReadAsync(cancellationToken: cancelamento).ConfigureAwait(false);
        var resumo = await SHA256.HashDataAsync(conteudo, cancelamento).ConfigureAwait(false);
        var hash = Convert.ToHexString(resumo).ToLowerInvariant();

        var metadados = new Dictionary<string, string>(propriedades.Metadata, StringComparer.Ordinal)
        {
            [MetadadoDoHash] = hash,
        };

        try
        {
            await blob.SetMetadataAsync(
                metadados,
                new BlobRequestConditions { IfMatch = propriedades.ETag },
                cancelamento).ConfigureAwait(false);
        }
        catch (RequestFailedException excecao) when (excecao.Status is 412 or 404)
        {
            // Alguém trocou o objeto entre a leitura e a gravação do metadado. O resumo devolvido ainda é
            // o do conteúdo que foi lido; a próxima chamada recalcula sobre o conteúdo novo.
            _log.LogWarning(
                "Objeto {Chave} mudou enquanto o resumo era calculado; o metadado não foi gravado.", blob.Name);
        }

        return hash;
    }

    /// <summary>
    /// Versão a declarar na própria assinatura, no formato de fio (<c>2025-07-05</c>).
    /// </summary>
    /// <remarks>
    /// O <c>BlobSasBuilder</c> tem versão própria e NÃO herda a do cliente: sem isto, a URL sai com a
    /// versão mais nova do SDK mesmo com o cliente fixado numa antiga, e o emulador recusa a assinatura
    /// com 403 — erro que acusa credencial errada quando o problema é outro. Conferido contra o Azurite.
    /// </remarks>
    private string? VersaoDaAssinatura =>
        string.IsNullOrWhiteSpace(_blob.VersaoDoServico)
            ? null
            : _blob.VersaoDoServico.TrimStart('V', 'v').Replace('_', '-');

    private BlobSasBuilder ConstrutorBase(string chave, DateTimeOffset expira) => new()
    {
        BlobContainerName = _contedor.Name,
        BlobName = chave,
        Resource = "b",
        // Cinco minutos de folga para trás absorvem relógio dessincronizado entre a aplicação e o Azure,
        // que faria a assinatura nascer inválida.
        StartsOn = _relogio.AgoraUtc.AddMinutes(-5),
        ExpiresOn = expira,
        // Exige HTTPS quando o serviço é HTTPS. O emulador fala HTTP, e prender o protocolo ali
        // produziria uma assinatura que o próprio emulador recusa.
        Protocol = _contedor.Uri.Scheme == Uri.UriSchemeHttps ? SasProtocol.Https : SasProtocol.None,
    };

    private BlobSasBuilder Construtor(string chave, DateTimeOffset expira)
    {
        var construtor = ConstrutorBase(chave, expira);

        if (VersaoDaAssinatura is { } versao)
        {
            construtor.Version = versao;
        }

        return construtor;
    }

    private async Task<Uri> AssinarAsync(string chave, BlobSasBuilder construtor, CancellationToken cancelamento)
    {
        var blob = _contedor.GetBlobClient(chave);

        if (_chaveDaConta is not null)
        {
            // Só chega aqui com o emulador: ele não implementa chave de delegação de usuário.
            return new UriBuilder(blob.Uri)
            {
                Query = construtor.ToSasQueryParameters(_chaveDaConta).ToString(),
            }.Uri;
        }

        var delegacao = await ObterDelegacaoAsync(cancelamento).ConfigureAwait(false);

        return new UriBuilder(blob.Uri)
        {
            Query = construtor.ToSasQueryParameters(delegacao, _blob.Conta).ToString(),
        }.Uri;
    }

    /// <summary>A chave de delegação em vigor, pedida ao Azure e renovada antes de vencer.</summary>
    private async Task<UserDelegationKey> ObterDelegacaoAsync(CancellationToken cancelamento)
    {
        // Renova com folga: uma chave que vence no meio da emissão produziria SAS nascida morta.
        var folga = TimeSpan.FromMinutes(5);

        if (_delegacao is not null && _relogio.AgoraUtc + folga < _delegacaoValidaAte)
        {
            return _delegacao;
        }

        await _travaDaDelegacao.WaitAsync(cancelamento).ConfigureAwait(false);
        try
        {
            if (_delegacao is not null && _relogio.AgoraUtc + folga < _delegacaoValidaAte)
            {
                return _delegacao;
            }

            var inicio = _relogio.AgoraUtc.AddMinutes(-5);
            var fim = _relogio.AgoraUtc.Add(_blob.ValidadeDaChaveDeDelegacao);

            var chave = await _servico.GetUserDelegationKeyAsync(inicio, fim, cancelamento).ConfigureAwait(false);

            _delegacao = chave.Value;
            _delegacaoValidaAte = fim;

            _log.LogInformation(
                "Chave de delegação do Storage renovada; válida até {ValidaAte:o}.", fim);

            return _delegacao;
        }
        finally
        {
            _travaDaDelegacao.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _travaDaDelegacao.Dispose();

    /// <summary>Descrição do destino, para o log de subida. Nunca inclui segredo.</summary>
    internal string Descricao() => string.Create(
        CultureInfo.InvariantCulture,
        $"conta '{_blob.Conta}', contêiner '{_blob.Contedor}', autenticação '{_blob.Autenticacao}'");
}

/// <summary>
/// Diz na subida qual adaptador de storage está atendendo.
/// </summary>
/// <remarks>
/// Serviço de fundo que não roda laço nenhum: ele existe para que a resolução de
/// <see cref="IObjectStorage"/> aconteça no arranque, e não na primeira foto de comprovante. Assim o
/// log responde "onde está o comprovante?" sem depender de ter havido um comprovante, e um cliente mal
/// configurado falha enquanto ainda há quem esteja olhando.
/// </remarks>
public sealed class AnuncioDoArmazenamento(
    IObjectStorage armazenamento,
    IOptions<OpcoesDeArmazenamento> opcoes,
    ILogger<AnuncioDoArmazenamento> log) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(opcoes);

        if (armazenamento is ArmazenamentoBlobDeObjetos blob)
        {
            log.LogInformation("Armazenamento de comprovantes: Azure Blob Storage — {Destino}.", blob.Descricao());
        }
        else
        {
            log.LogInformation(
                "Armazenamento de comprovantes: disco local em {Diretorio}.", opcoes.Value.DiretorioEfetivo);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
