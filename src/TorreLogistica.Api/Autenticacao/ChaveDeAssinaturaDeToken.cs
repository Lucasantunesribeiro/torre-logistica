using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace TorreLogistica.Api.Autenticacao;

/// <summary>Configuração do token de acesso.</summary>
public sealed class OpcoesDeTokenDeAcesso
{
    /// <summary>Seção correspondente na configuração.</summary>
    public const string Secao = "Torre:TokenDeAcesso";

    /// <summary>Emissor (<c>iss</c>) gravado e exigido nos tokens.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Emissor { get; set; } = "torre-logistica";

    /// <summary>
    /// Chave HMAC em Base64, com ao menos 32 bytes. Segredo: vem do gerenciador de
    /// segredos em produção e nunca de arquivo versionado.
    /// </summary>
    public string? ChaveDeAssinatura { get; set; }
}

/// <summary>Configuração dos limites de requisição por endereço de origem.</summary>
public sealed class OpcoesDeLimiteDeRequisicoes
{
    /// <summary>Seção correspondente na configuração.</summary>
    public const string Secao = "Torre:LimiteDeRequisicoes";

    /// <summary>Tentativas de login por minuto por endereço.</summary>
    [Range(1, 100_000)]
    public int TentativasDeLoginPorMinuto { get; set; } = 10;

    /// <summary>Renovações de sessão por minuto por endereço.</summary>
    [Range(1, 100_000)]
    public int RenovacoesPorMinuto { get; set; } = 30;
}

/// <summary>
/// Chave simétrica que assina e valida os tokens de acesso.
/// </summary>
/// <remarks>
/// <para>
/// HMAC-SHA256 com chave única basta porque emissor e validador são o mesmo processo
/// (ADR 0001). Assinatura assimétrica só compensaria se terceiros precisassem validar os
/// tokens sem poder emiti-los — não é o caso.
/// </para>
/// <para>
/// Sem chave configurada, desenvolvimento e teste recebem uma chave efêmera gerada na
/// subida: sessões morrem a cada reinício, o que ali é aceitável. Em qualquer outro
/// ambiente, a ausência derruba a inicialização — gerar chave aleatória em produção
/// faria cada instância rejeitar os tokens das outras, e escolher uma chave fixa no
/// código seria publicar o segredo.
/// </para>
/// </remarks>
public sealed class ChaveDeAssinaturaDeToken
{
    /// <summary>Tamanho mínimo da chave, em bytes.</summary>
    public const int TamanhoMinimoEmBytes = 32;

    /// <summary>Resolve a chave a partir da configuração e do ambiente.</summary>
    public ChaveDeAssinaturaDeToken(
        IOptions<OpcoesDeTokenDeAcesso> opcoes,
        IHostEnvironment ambiente,
        ILogger<ChaveDeAssinaturaDeToken> log)
    {
        ArgumentNullException.ThrowIfNull(opcoes);
        ArgumentNullException.ThrowIfNull(ambiente);
        ArgumentNullException.ThrowIfNull(log);

        var configurada = opcoes.Value.ChaveDeAssinatura;
        byte[] bytes;

        if (!string.IsNullOrWhiteSpace(configurada))
        {
            try
            {
                bytes = Convert.FromBase64String(configurada);
            }
            catch (FormatException excecao)
            {
                throw new InvalidOperationException(
                    $"{OpcoesDeTokenDeAcesso.Secao}:ChaveDeAssinatura não está em Base64.", excecao);
            }

            if (bytes.Length < TamanhoMinimoEmBytes)
            {
                throw new InvalidOperationException(
                    $"{OpcoesDeTokenDeAcesso.Secao}:ChaveDeAssinatura precisa ter ao menos {TamanhoMinimoEmBytes} bytes.");
            }
        }
        else if (ambiente.IsDevelopment() || ambiente.IsEnvironment("Testing"))
        {
            bytes = RandomNumberGenerator.GetBytes(64);
            log.LogWarning(
                "Nenhuma chave de assinatura configurada; usando chave efêmera no ambiente {Ambiente}. "
                + "Sessões não sobrevivem a reinício.",
                ambiente.EnvironmentName);
        }
        else
        {
            throw new InvalidOperationException(
                $"{OpcoesDeTokenDeAcesso.Secao}:ChaveDeAssinatura é obrigatória no ambiente {ambiente.EnvironmentName}.");
        }

        Chave = new SymmetricSecurityKey(bytes)
        {
            KeyId = Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant(),
        };
    }

    /// <summary>Chave de assinatura.</summary>
    public SymmetricSecurityKey Chave { get; }
}
