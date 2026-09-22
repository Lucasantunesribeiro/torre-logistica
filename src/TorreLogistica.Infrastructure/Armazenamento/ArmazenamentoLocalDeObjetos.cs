using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TorreLogistica.Application.Abstracoes.Armazenamento;
using TorreLogistica.Domain.Abstracoes.Tempo;

namespace TorreLogistica.Infrastructure.Armazenamento;

/// <summary>Configuração do storage de objetos.</summary>
public sealed class OpcoesDeArmazenamento
{
    /// <summary>Seção correspondente na configuração.</summary>
    public const string Secao = "Torre:Armazenamento";

    /// <summary>Provedor de disco local, com URL assinada servida pela própria API.</summary>
    public const string ProvedorLocal = "local";

    /// <summary>Azure Blob Storage, contêiner privado com URL assinada de curta duração (ADR 0007).</summary>
    public const string ProvedorBlob = "blob";

    /// <summary>
    /// Qual adaptador de <c>IObjectStorage</c> usar: <c>local</c> ou <c>blob</c>.
    /// </summary>
    /// <remarks>
    /// Explícito de propósito. Antes não existia escolha — o disco local era o único caminho e ninguém
    /// declarava nada, então subir em produção com armazenamento efêmero era o comportamento padrão e
    /// silencioso. Agora quem quer disco local em produção precisa dizer isso em voz alta, em
    /// <see cref="PermitirLocalForaDeDesenvolvimento"/>.
    /// </remarks>
    public string Provedor { get; set; } = ProvedorLocal;

    /// <summary>Diretório do storage local. Vazio usa uma pasta sob o diretório de dados do processo.</summary>
    public string? Diretorio { get; set; }

    /// <summary>
    /// Autoriza o provedor local fora de desenvolvimento e teste.
    /// </summary>
    /// <remarks>
    /// Sem isto, o processo **recusa subir**: comprovante em disco de contêiner some no primeiro
    /// reinício, e perder prova de entrega em silêncio é pior que não subir. Ligar é uma decisão
    /// consciente de quem aceita esse custo — num ambiente de demonstração, por exemplo.
    /// </remarks>
    public bool PermitirLocalForaDeDesenvolvimento { get; set; }

    /// <summary>
    /// Diretório efetivo do provedor local, com o padrão aplicado.
    /// </summary>
    /// <remarks>
    /// Mora aqui, e não no construtor do adaptador, porque a validação de subida precisa do mesmo
    /// caminho que o adaptador vai usar. Duas cópias da regra dariam um processo que valida um diretório
    /// e escreve em outro.
    /// </remarks>
    public string DiretorioEfetivo => string.IsNullOrWhiteSpace(Diretorio)
        ? Path.Combine(AppContext.BaseDirectory, "armazenamento")
        : Diretorio;

    /// <summary>Endereço base público da API, usado para montar as URLs assinadas.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Informe o endereço base da API para as URLs assinadas.")]
    public string EnderecoBase { get; set; } = "http://localhost:5080";

    /// <summary>
    /// Chave HMAC em Base64, com ao menos 32 bytes, que assina as URLs. Segredo: vem do gerenciador de
    /// segredos em produção.
    /// </summary>
    public string? ChaveDeAssinatura { get; set; }

    /// <summary>Validade da URL de envio.</summary>
    public TimeSpan ValidadeDaUrlDeEnvio { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Validade da URL de leitura. Curta de propósito: URL longa é link público com prazo.</summary>
    public TimeSpan ValidadeDaUrlDeLeitura { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Configuração do provedor <c>blob</c>. Só é lida quando ele é o escolhido.</summary>
    public OpcoesDoBlob Blob { get; set; } = new();
}

/// <summary>Configuração do Azure Blob Storage — seção <c>Torre:Armazenamento:Blob</c>.</summary>
public sealed class OpcoesDoBlob
{
    /// <summary>Identidade gerenciada da própria aplicação. Sem segredo em lugar nenhum.</summary>
    public const string AutenticacaoPorIdentidade = "identidade-gerenciada";

    /// <summary>Chave da conta. Só para emulador local; recusada em produção.</summary>
    public const string AutenticacaoPorChave = "chave-de-conta";

    /// <summary>Nome da conta de armazenamento.</summary>
    public string? Conta { get; set; }

    /// <summary>Contêiner dos comprovantes. Privado — nunca com acesso anônimo.</summary>
    public string? Contedor { get; set; }

    /// <summary>
    /// Endereço do serviço de blob. Vazio deriva de <see cref="Conta"/> no domínio público do Azure.
    /// </summary>
    /// <remarks>
    /// Existe para o emulador, cujo endereço é <c>http://host:porta/conta</c> em vez de
    /// <c>https://conta.blob.core.windows.net</c>.
    /// </remarks>
    public string? EndpointDoServico { get; set; }

    /// <summary>Como a aplicação se autentica no Storage.</summary>
    public string Autenticacao { get; set; } = AutenticacaoPorIdentidade;

    /// <summary>
    /// Chave da conta, usada apenas com <see cref="AutenticacaoPorChave"/>.
    /// </summary>
    /// <remarks>
    /// Existe por um motivo só: o emulador não implementa a chave de delegação de usuário, então os
    /// testes de integração precisam assinar com chave de conta. Em produção este caminho é **recusado
    /// na subida** — chave de conta é segredo permanente que dá acesso total, e identidade gerenciada
    /// resolve o caso sem guardar nada.
    /// </remarks>
    public string? ChaveDaConta { get; set; }

    /// <summary>
    /// Validade da chave de delegação pedida ao Azure, que é o teto de qualquer SAS emitida com ela.
    /// </summary>
    public TimeSpan ValidadeDaChaveDeDelegacao { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Versão da API do Storage a declarar, no formato <c>V2025_11_05</c>. Vazio usa a mais nova do SDK.
    /// </summary>
    /// <remarks>
    /// Existe por causa do emulador, que fica para trás do SDK e recusa versão que não conhece. Contra o
    /// serviço real não precisa ser configurada: a versão mais nova do SDK é sempre aceita.
    /// </remarks>
    public string? VersaoDoServico { get; set; }
}

/// <summary>
/// Validação das opções na subida.
/// </summary>
/// <remarks>
/// Roda por <c>ValidateOnStart</c>, e é isso que muda o momento da falha. O adaptador local é um
/// singleton resolvido sob demanda: enquanto a checagem do diretório morava no construtor dele, um
/// diretório sem permissão só aparecia como <c>500</c> na primeira entrega com comprovante — horas depois
/// da implantação, para um motorista na rua. Aqui, o processo nem sobe.
/// </remarks>
public sealed class ValidacaoDeOpcoesDeArmazenamento(IHostEnvironment ambiente) : IValidateOptions<OpcoesDeArmazenamento>
{
    private readonly IHostEnvironment _ambiente = ambiente ?? throw new ArgumentNullException(nameof(ambiente));

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, OpcoesDeArmazenamento options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var falhas = new List<string>();

        ValidarProvedor(options, falhas);

        if (!Uri.TryCreate(options.EnderecoBase, UriKind.Absolute, out _))
        {
            falhas.Add($"{nameof(options.EnderecoBase)} precisa ser uma URL absoluta.");
        }

        if (options.ValidadeDaUrlDeEnvio < TimeSpan.FromMinutes(1) || options.ValidadeDaUrlDeEnvio > TimeSpan.FromHours(1))
        {
            falhas.Add($"{nameof(options.ValidadeDaUrlDeEnvio)} vai de 1 minuto a 1 hora.");
        }

        if (options.ValidadeDaUrlDeLeitura < TimeSpan.FromMinutes(1) || options.ValidadeDaUrlDeLeitura > TimeSpan.FromMinutes(30))
        {
            falhas.Add($"{nameof(options.ValidadeDaUrlDeLeitura)} vai de 1 a 30 minutos.");
        }

        return falhas.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(falhas);
    }

    private void ValidarProvedor(OpcoesDeArmazenamento options, List<string> falhas)
    {
        var provedor = (options.Provedor ?? string.Empty).Trim();

        if (string.Equals(provedor, OpcoesDeArmazenamento.ProvedorBlob, StringComparison.OrdinalIgnoreCase))
        {
            ValidarBlob(options.Blob, falhas);
            return;
        }

        if (!string.Equals(provedor, OpcoesDeArmazenamento.ProvedorLocal, StringComparison.OrdinalIgnoreCase))
        {
            falhas.Add(
                $"{OpcoesDeArmazenamento.Secao}:Provedor='{provedor}' não existe. Use "
                + $"'{OpcoesDeArmazenamento.ProvedorLocal}' ou '{OpcoesDeArmazenamento.ProvedorBlob}'.");
            return;
        }

        var ehAmbienteDeDesenvolvimento = _ambiente.IsDevelopment() || _ambiente.IsEnvironment("Testing");

        if (!ehAmbienteDeDesenvolvimento && !options.PermitirLocalForaDeDesenvolvimento)
        {
            falhas.Add(
                $"{OpcoesDeArmazenamento.Secao}:Provedor='{OpcoesDeArmazenamento.ProvedorLocal}' no ambiente "
                + $"{_ambiente.EnvironmentName} guarda comprovante em disco do processo, que some no reinício. "
                + $"Para aceitar isso conscientemente, ligue {OpcoesDeArmazenamento.Secao}:"
                + $"{nameof(OpcoesDeArmazenamento.PermitirLocalForaDeDesenvolvimento)}.");
            return;
        }

        ValidarDiretorio(options.DiretorioEfetivo, falhas);
    }

    private void ValidarBlob(OpcoesDoBlob blob, List<string> falhas)
    {
        const string Prefixo = OpcoesDeArmazenamento.Secao + ":Blob";

        if (string.IsNullOrWhiteSpace(blob.Conta))
        {
            falhas.Add($"{Prefixo}:Conta é obrigatória com o provedor '{OpcoesDeArmazenamento.ProvedorBlob}'.");
        }

        if (string.IsNullOrWhiteSpace(blob.Contedor))
        {
            falhas.Add($"{Prefixo}:Contedor é obrigatório com o provedor '{OpcoesDeArmazenamento.ProvedorBlob}'.");
        }
        else if (!NomeDeContedorValido(blob.Contedor))
        {
            falhas.Add(
                $"{Prefixo}:Contedor='{blob.Contedor}' não é um nome válido: de 3 a 63 caracteres, "
                + "minúsculas, números e hífen, começando e terminando por letra ou número.");
        }

        if (!string.IsNullOrWhiteSpace(blob.EndpointDoServico)
            && !Uri.TryCreate(blob.EndpointDoServico, UriKind.Absolute, out _))
        {
            falhas.Add($"{Prefixo}:EndpointDoServico precisa ser uma URL absoluta.");
        }

        if (!string.IsNullOrWhiteSpace(blob.VersaoDoServico)
            && !Enum.TryParse<Azure.Storage.Blobs.BlobClientOptions.ServiceVersion>(blob.VersaoDoServico, out _))
        {
            falhas.Add(
                $"{Prefixo}:VersaoDoServico='{blob.VersaoDoServico}' não é uma versão conhecida do SDK. "
                + "Use o formato V2025_11_05, ou deixe vazio para a mais nova.");
        }

        if (blob.ValidadeDaChaveDeDelegacao < TimeSpan.FromMinutes(5)
            || blob.ValidadeDaChaveDeDelegacao > TimeSpan.FromDays(7))
        {
            falhas.Add($"{Prefixo}:ValidadeDaChaveDeDelegacao vai de 5 minutos a 7 dias.");
        }

        var autenticacao = (blob.Autenticacao ?? string.Empty).Trim();
        var porChave = string.Equals(autenticacao, OpcoesDoBlob.AutenticacaoPorChave, StringComparison.OrdinalIgnoreCase);
        var porIdentidade = string.Equals(autenticacao, OpcoesDoBlob.AutenticacaoPorIdentidade, StringComparison.OrdinalIgnoreCase);

        if (!porChave && !porIdentidade)
        {
            falhas.Add(
                $"{Prefixo}:Autenticacao='{autenticacao}' não existe. Use "
                + $"'{OpcoesDoBlob.AutenticacaoPorIdentidade}' ou '{OpcoesDoBlob.AutenticacaoPorChave}'.");
            return;
        }

        if (!porChave)
        {
            return;
        }

        // Chave de conta é segredo permanente e dá acesso total à conta inteira. Ela existe aqui só
        // porque o emulador não implementa chave de delegação de usuário. Em produção, recusa.
        if (!_ambiente.IsDevelopment() && !_ambiente.IsEnvironment("Testing"))
        {
            falhas.Add(
                $"{Prefixo}:Autenticacao='{OpcoesDoBlob.AutenticacaoPorChave}' não é aceito no ambiente "
                + $"{_ambiente.EnvironmentName}. Use '{OpcoesDoBlob.AutenticacaoPorIdentidade}': chave de conta é "
                + "segredo permanente com acesso total, e a identidade gerenciada resolve o caso sem guardar nada.");
        }

        if (string.IsNullOrWhiteSpace(blob.ChaveDaConta))
        {
            falhas.Add($"{Prefixo}:ChaveDaConta é obrigatória com Autenticacao='{OpcoesDoBlob.AutenticacaoPorChave}'.");
        }
    }

    /// <summary>Regra de nome de contêiner do Azure, conferida aqui para falhar na subida e não no primeiro uso.</summary>
    private static bool NomeDeContedorValido(string nome) =>
        nome.Length is >= 3 and <= 63
        && nome.All(caractere => char.IsAsciiLetterLower(caractere) || char.IsAsciiDigit(caractere) || caractere == '-')
        && char.IsAsciiLetterOrDigit(nome[0])
        && char.IsAsciiLetterOrDigit(nome[^1])
        && !nome.Contains("--", StringComparison.Ordinal);

    private static void ValidarDiretorio(string diretorio, List<string> falhas)
    {
        // Criar e escrever de verdade, não só perguntar se existe: no contêiner, o processo roda sem
        // privilégio e a pasta de instalação pertence ao root. Só a escrita revela isso.
        try
        {
            Directory.CreateDirectory(diretorio);

            var sonda = Path.Combine(diretorio, $".escrita-{Environment.ProcessId}");
            File.WriteAllBytes(sonda, []);
            File.Delete(sonda);
        }
        catch (Exception excecao) when (excecao is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            falhas.Add(
                $"{OpcoesDeArmazenamento.Secao}:Diretorio não é gravável em '{diretorio}': {excecao.GetType().Name}. "
                + "Aponte para um caminho que o usuário do processo possa escrever.");
        }
    }
}

/// <summary>Assinatura das URLs do storage: HMAC-SHA256 sobre o que foi autorizado.</summary>
/// <remarks>
/// A assinatura cobre operação, chave, tipo de conteúdo, tamanho máximo e expiração. Trocar qualquer um
/// desses campos invalida a URL — é o que impede subir outro arquivo, maior ou de outro tipo, com uma
/// autorização emitida para outra coisa.
/// </remarks>
public sealed class AssinaturaDeUrlDeArmazenamento
{
    /// <summary>Tamanho mínimo da chave, em bytes.</summary>
    public const int TamanhoMinimoEmBytes = 32;

    private readonly byte[] _chave;

    /// <summary>Resolve a chave a partir da configuração e do ambiente.</summary>
    public AssinaturaDeUrlDeArmazenamento(
        IOptions<OpcoesDeArmazenamento> opcoes,
        IHostEnvironment ambiente,
        ILogger<AssinaturaDeUrlDeArmazenamento> log)
    {
        ArgumentNullException.ThrowIfNull(opcoes);
        ArgumentNullException.ThrowIfNull(ambiente);
        ArgumentNullException.ThrowIfNull(log);

        var configurada = opcoes.Value.ChaveDeAssinatura;

        if (!string.IsNullOrWhiteSpace(configurada))
        {
            try
            {
                _chave = Convert.FromBase64String(configurada);
            }
            catch (FormatException excecao)
            {
                throw new InvalidOperationException($"{OpcoesDeArmazenamento.Secao}:ChaveDeAssinatura não está em Base64.", excecao);
            }

            if (_chave.Length < TamanhoMinimoEmBytes)
            {
                throw new InvalidOperationException(
                    $"{OpcoesDeArmazenamento.Secao}:ChaveDeAssinatura precisa ter ao menos {TamanhoMinimoEmBytes} bytes.");
            }
        }
        else if (ambiente.IsDevelopment() || ambiente.IsEnvironment("Testing"))
        {
            _chave = RandomNumberGenerator.GetBytes(64);
            log.LogWarning(
                "Nenhuma chave de assinatura de storage configurada; usando chave efêmera no ambiente {Ambiente}. "
                + "URLs assinadas não sobrevivem a reinício.",
                ambiente.EnvironmentName);
        }
        else
        {
            throw new InvalidOperationException(
                $"{OpcoesDeArmazenamento.Secao}:ChaveDeAssinatura é obrigatória no ambiente {ambiente.EnvironmentName}.");
        }
    }

    /// <summary>Assina os campos autorizados.</summary>
    public string Assinar(string operacao, string chave, string tipoDeConteudo, long tamanhoMaximo, long expiraEm)
    {
        var conteudo = string.Create(
            CultureInfo.InvariantCulture,
            $"{operacao}\n{chave}\n{tipoDeConteudo}\n{tamanhoMaximo}\n{expiraEm}");

        return Convert.ToHexString(HMACSHA256.HashData(_chave, Encoding.UTF8.GetBytes(conteudo))).ToLowerInvariant();
    }

    /// <summary>Confere a assinatura em tempo constante.</summary>
    public bool Confere(string assinatura, string operacao, string chave, string tipoDeConteudo, long tamanhoMaximo, long expiraEm)
    {
        var esperada = Assinar(operacao, chave, tipoDeConteudo, tamanhoMaximo, expiraEm);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(esperada),
            Encoding.UTF8.GetBytes(assinatura ?? string.Empty));
    }
}

/// <summary>
/// Storage de objetos em disco local, com URL assinada servida pela própria API.
/// </summary>
/// <remarks>
/// <para>
/// É o adaptador desta fase: roda em desenvolvimento, em teste e numa instância única, sem serviço externo e
/// sem custo. O ADR 0007 já registra que a escolha de S3 ou compatível é da Fase 25 — e é exatamente para
/// essa troca que <see cref="IObjectStorage"/> existe.
/// </para>
/// <para>
/// A gravação confere o que chegou contra o que foi autorizado — tipo e tamanho — e calcula o SHA-256 do
/// conteúdo. Nenhum arquivo é servido sem assinatura válida e dentro do prazo.
/// </para>
/// </remarks>
public sealed class ArmazenamentoLocalDeObjetos : IObjectStorage
{
    /// <summary>Operação de envio, como aparece na assinatura.</summary>
    public const string OperacaoDeEnvio = "PUT";

    /// <summary>Operação de leitura, como aparece na assinatura.</summary>
    public const string OperacaoDeLeitura = "GET";

    /// <summary>Caminho dos endpoints de arquivo servidos pela API.</summary>
    public const string CaminhoBase = "/api/arquivos";

    private readonly OpcoesDeArmazenamento _opcoes;
    private readonly AssinaturaDeUrlDeArmazenamento _assinatura;
    private readonly IRelogio _relogio;
    private readonly string _diretorio;

    /// <summary>Cria o adaptador e garante o diretório.</summary>
    public ArmazenamentoLocalDeObjetos(
        IOptions<OpcoesDeArmazenamento> opcoes,
        AssinaturaDeUrlDeArmazenamento assinatura,
        IRelogio relogio)
    {
        ArgumentNullException.ThrowIfNull(opcoes);

        _opcoes = opcoes.Value;
        _assinatura = assinatura ?? throw new ArgumentNullException(nameof(assinatura));
        _relogio = relogio ?? throw new ArgumentNullException(nameof(relogio));

        // O mesmo caminho que ValidacaoDeOpcoesDeArmazenamento já provou gravável na subida. Aqui o
        // CreateDirectory é idempotente e barato; quem falha cedo é a validação, não este construtor.
        _diretorio = _opcoes.DiretorioEfetivo;

        Directory.CreateDirectory(_diretorio);
    }

    /// <inheritdoc />
    public Task<UrlAssinada> AutorizarEnvioAsync(string chave, string tipoDeConteudo, long tamanhoMaximoEmBytes, CancellationToken cancelamento)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chave);
        ArgumentException.ThrowIfNullOrWhiteSpace(tipoDeConteudo);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(tamanhoMaximoEmBytes, 0);

        var expira = _relogio.AgoraUtc.Add(_opcoes.ValidadeDaUrlDeEnvio);
        var url = Montar(OperacaoDeEnvio, chave, tipoDeConteudo, tamanhoMaximoEmBytes, expira);

        return Task.FromResult(new UrlAssinada(
            url,
            expira,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Content-Type"] = tipoDeConteudo }));
    }

    /// <inheritdoc />
    public Task<UrlAssinada> AutorizarLeituraAsync(string chave, CancellationToken cancelamento)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chave);

        var expira = _relogio.AgoraUtc.Add(_opcoes.ValidadeDaUrlDeLeitura);
        var url = Montar(OperacaoDeLeitura, chave, string.Empty, 0, expira);

        return Task.FromResult(new UrlAssinada(url, expira, new Dictionary<string, string>(StringComparer.Ordinal)));
    }

    /// <inheritdoc />
    public async Task<ObjetoArmazenado?> ObterAsync(string chave, CancellationToken cancelamento)
    {
        var caminho = CaminhoDe(chave);
        var metadados = CaminhoDosMetadados(chave);

        if (!File.Exists(caminho) || !File.Exists(metadados))
        {
            return null;
        }

        var linhas = await File.ReadAllLinesAsync(metadados, cancelamento).ConfigureAwait(false);
        if (linhas.Length < 3)
        {
            return null;
        }

        var informacoes = new FileInfo(caminho);
        return new ObjetoArmazenado(
            chave,
            informacoes.Length,
            linhas[0],
            linhas[1],
            DateTimeOffset.Parse(linhas[2], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    /// <summary>Confere a assinatura de uma URL e devolve o que ela autoriza.</summary>
    public bool Autorizado(string operacao, string chave, string? tipoDeConteudo, long tamanhoMaximo, long expiraEm, string? assinatura)
    {
        if (assinatura is null || _relogio.AgoraUtc.ToUnixTimeSeconds() > expiraEm)
        {
            return false;
        }

        return _assinatura.Confere(assinatura, operacao, chave, tipoDeConteudo ?? string.Empty, tamanhoMaximo, expiraEm);
    }

    /// <summary>Grava o objeto conferindo tipo, tamanho e calculando o hash.</summary>
    /// <returns>Os metadados gravados.</returns>
    public async Task<ObjetoArmazenado> GravarAsync(string chave, string tipoDeConteudo, Stream conteudo, long tamanhoMaximo, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(conteudo);

        var caminho = CaminhoDe(chave);
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);

        var temporario = caminho + ".parcial";
        long tamanho;
        byte[] hash;

        await using (var destino = File.Create(temporario))
        using (var digestor = SHA256.Create())
        {
            var buffer = new byte[81_920];
            tamanho = 0;

            int lidos;
            while ((lidos = await conteudo.ReadAsync(buffer, cancelamento).ConfigureAwait(false)) > 0)
            {
                tamanho += lidos;

                if (tamanho > tamanhoMaximo)
                {
                    destino.Close();
                    File.Delete(temporario);
                    throw new ArmazenamentoRecusouException("arquivo_grande_demais", "O arquivo passou do tamanho autorizado.");
                }

                digestor.TransformBlock(buffer, 0, lidos, null, 0);
                await destino.WriteAsync(buffer.AsMemory(0, lidos), cancelamento).ConfigureAwait(false);
            }

            digestor.TransformFinalBlock([], 0, 0);
            hash = digestor.Hash!;
        }

        if (tamanho == 0)
        {
            File.Delete(temporario);
            throw new ArmazenamentoRecusouException("arquivo_vazio", "O arquivo está vazio.");
        }

        var gravadoEm = _relogio.AgoraUtc;
        var hexadecimal = Convert.ToHexString(hash).ToLowerInvariant();

        File.Move(temporario, caminho, overwrite: true);
        await File.WriteAllLinesAsync(
            CaminhoDosMetadados(chave),
            [tipoDeConteudo, hexadecimal, gravadoEm.ToString("O", CultureInfo.InvariantCulture)],
            cancelamento).ConfigureAwait(false);

        return new ObjetoArmazenado(chave, tamanho, tipoDeConteudo, hexadecimal, gravadoEm);
    }

    /// <summary>Abre o objeto para leitura, ou <see langword="null"/> se ele não existe.</summary>
    public Stream? Abrir(string chave)
    {
        var caminho = CaminhoDe(chave);
        return File.Exists(caminho) ? File.OpenRead(caminho) : null;
    }

    private Uri Montar(string operacao, string chave, string tipoDeConteudo, long tamanhoMaximo, DateTimeOffset expira)
    {
        var expiraEm = expira.ToUnixTimeSeconds();
        var assinatura = _assinatura.Assinar(operacao, chave, tipoDeConteudo, tamanhoMaximo, expiraEm);
        var consulta = $"?expiraEm={expiraEm}&assinatura={assinatura}";

        if (operacao == OperacaoDeEnvio)
        {
            consulta += $"&tipoDeConteudo={Uri.EscapeDataString(tipoDeConteudo)}&tamanhoMaximo={tamanhoMaximo}";
        }

        return new Uri($"{_opcoes.EnderecoBase.TrimEnd('/')}{CaminhoBase}/{chave}{consulta}");
    }

    private string CaminhoDe(string chave)
    {
        var partes = ChaveDeObjeto.Partes(chave);
        return Path.Combine([_diretorio, .. partes]);
    }

    private string CaminhoDosMetadados(string chave) => CaminhoDe(chave) + ".meta";
}

/// <summary>O storage recusou a gravação.</summary>
public sealed class ArmazenamentoRecusouException(string codigo, string mensagem) : Exception(mensagem)
{
    /// <summary>Código estável da recusa.</summary>
    public string Codigo { get; } = codigo;
}

/// <summary>Chave de objeto: caminho relativo seguro, sem travessia de diretório.</summary>
public static class ChaveDeObjeto
{
    /// <summary>Monta a chave de um arquivo de comprovante.</summary>
    public static string DeComprovante(Guid organizacaoId, Guid entregaId, Guid arquivoId, string extensao) =>
        $"organizacoes/{organizacaoId:N}/entregas/{entregaId:N}/{arquivoId:N}.{extensao}";

    /// <summary>
    /// Divide a chave em partes de caminho, recusando qualquer segmento perigoso.
    /// </summary>
    /// <remarks>
    /// Path traversal é a falha clássica de storage em disco: <c>../</c> na chave transformaria o storage em
    /// acesso ao sistema de arquivos inteiro. Só letras, números, hífen, sublinhado e ponto passam — e nunca
    /// um segmento que seja <c>.</c> ou <c>..</c>.
    /// </remarks>
    public static string[] Partes(string? chave)
    {
        if (string.IsNullOrWhiteSpace(chave) || chave.Length > 200)
        {
            throw new ArmazenamentoRecusouException("chave_invalida", "Chave de objeto inválida.");
        }

        var partes = chave.Split('/', StringSplitOptions.None);

        foreach (var parte in partes)
        {
            var valida = parte.Length is > 0 and <= 80
                && parte is not ("." or "..")
                && parte.All(caractere => char.IsAsciiLetterOrDigit(caractere) || caractere is '-' or '_' or '.');

            if (!valida)
            {
                throw new ArmazenamentoRecusouException("chave_invalida", "Chave de objeto inválida.");
            }
        }

        return partes;
    }
}
