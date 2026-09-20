using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TorreLogistica.Application.Abstracoes.Webhooks;

namespace TorreLogistica.Infrastructure.Webhooks;

/// <summary>Configuração dos webhooks de saída.</summary>
public sealed class OpcoesDeWebhooks
{
    /// <summary>Seção correspondente na configuração.</summary>
    public const string Secao = "Torre:Webhooks";

    /// <summary>
    /// Chave de criptografia dos segredos, em Base64. Precisa ter 32 bytes.
    /// </summary>
    /// <remarks>
    /// Sem ela, desenvolvimento e teste recebem uma chave efêmera: os segredos gravados morrem no
    /// reinício, o que ali é aceitável. Em qualquer outro ambiente a ausência derruba a subida — cada
    /// instância com chave própria não conseguiria decifrar o que a outra gravou.
    /// </remarks>
    public string? ChaveDeCriptografia { get; set; }

    /// <summary>Intervalo entre rodadas do despachante do outbox.</summary>
    public TimeSpan IntervaloDeDespacho { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Intervalo entre rodadas do entregador.</summary>
    public TimeSpan IntervaloDeEntrega { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Aceitar destino em endereço local ou privado.
    /// </summary>
    /// <remarks>
    /// Desligado em produção: webhook aponta para endereço que o cliente cadastrou, e sem esta trava a
    /// nossa rede vira instrumento para alcançar serviço interno ou o endereço de metadados da nuvem.
    /// Desenvolvimento e teste ligam, porque o assinante ali é um servidor na própria máquina.
    /// </remarks>
    public bool PermitirDestinoLocal { get; set; }

    /// <summary>Processar a fila em segundo plano neste processo.</summary>
    public bool ProcessarEmSegundoPlano { get; set; } = true;

    /// <summary>Tempo limite de cada tentativa.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public TimeSpan TempoLimite { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Cifra os segredos de webhook com AES-GCM.
/// </summary>
/// <remarks>
/// AES-GCM porque ele autentica junto com cifrar: conteúdo adulterado no banco não decifra em silêncio,
/// falha. O valor guardado é <c>nonce (12 bytes) + etiqueta (16) + texto cifrado</c>, e cada operação
/// sorteia um nonce novo — repetir nonce com a mesma chave quebra a garantia do modo.
/// </remarks>
public sealed class ProtecaoDeSegredoAesGcm : IProtecaoDeSegredo
{
    private const int TamanhoDoNonce = 12;
    private const int TamanhoDaEtiqueta = 16;
    private const int TamanhoDaChave = 32;

    private readonly byte[] _chave;

    /// <summary>Lê a chave da configuração, ou sorteia uma efêmera em desenvolvimento e teste.</summary>
    public ProtecaoDeSegredoAesGcm(
        IOptions<OpcoesDeWebhooks> opcoes,
        IHostEnvironment ambiente,
        ILogger<ProtecaoDeSegredoAesGcm> log)
    {
        ArgumentNullException.ThrowIfNull(opcoes);
        ArgumentNullException.ThrowIfNull(ambiente);
        ArgumentNullException.ThrowIfNull(log);

        var configurada = opcoes.Value.ChaveDeCriptografia;

        if (!string.IsNullOrWhiteSpace(configurada))
        {
            _chave = Convert.FromBase64String(configurada);

            if (_chave.Length != TamanhoDaChave)
            {
                throw new InvalidOperationException(
                    $"{OpcoesDeWebhooks.Secao}:ChaveDeCriptografia precisa ter {TamanhoDaChave} bytes em Base64.");
            }
        }
        else if (ambiente.IsDevelopment() || ambiente.IsEnvironment("Testing"))
        {
            _chave = RandomNumberGenerator.GetBytes(TamanhoDaChave);
            log.LogWarning(
                "Nenhuma chave de criptografia de webhook configurada; usando chave efêmera no ambiente {Ambiente}. "
                + "Segredos gravados não sobrevivem a reinício.",
                ambiente.EnvironmentName);
        }
        else
        {
            throw new InvalidOperationException(
                $"{OpcoesDeWebhooks.Secao}:ChaveDeCriptografia é obrigatória fora de desenvolvimento e teste.");
        }
    }

    /// <inheritdoc />
    public byte[] Cifrar(string segredo)
    {
        ArgumentNullException.ThrowIfNull(segredo);

        var conteudo = Encoding.UTF8.GetBytes(segredo);
        var resultado = new byte[TamanhoDoNonce + TamanhoDaEtiqueta + conteudo.Length];
        var nonce = resultado.AsSpan(0, TamanhoDoNonce);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_chave, TamanhoDaEtiqueta);
        aes.Encrypt(
            nonce,
            conteudo,
            resultado.AsSpan(TamanhoDoNonce + TamanhoDaEtiqueta),
            resultado.AsSpan(TamanhoDoNonce, TamanhoDaEtiqueta));

        return resultado;
    }

    /// <inheritdoc />
    public string Decifrar(byte[] cifrado)
    {
        ArgumentNullException.ThrowIfNull(cifrado);

        if (cifrado.Length < TamanhoDoNonce + TamanhoDaEtiqueta)
        {
            throw new InvalidOperationException("Segredo cifrado com tamanho inválido.");
        }

        var conteudo = new byte[cifrado.Length - TamanhoDoNonce - TamanhoDaEtiqueta];

        try
        {
            using var aes = new AesGcm(_chave, TamanhoDaEtiqueta);
            aes.Decrypt(
                cifrado.AsSpan(0, TamanhoDoNonce),
                cifrado.AsSpan(TamanhoDoNonce + TamanhoDaEtiqueta),
                cifrado.AsSpan(TamanhoDoNonce, TamanhoDaEtiqueta),
                conteudo);
        }
        catch (CryptographicException excecao)
        {
            // Chave trocada ou linha adulterada. Falhar alto é melhor que assinar com segredo errado e
            // fazer o assinante recusar tudo sem entender por quê.
            throw new InvalidOperationException("Não foi possível decifrar o segredo do webhook.", excecao);
        }

        return Encoding.UTF8.GetString(conteudo);
    }
}
