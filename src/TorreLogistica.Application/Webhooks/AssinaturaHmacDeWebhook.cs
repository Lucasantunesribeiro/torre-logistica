using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TorreLogistica.Application.Webhooks;

/// <summary>
/// Assinatura HMAC-SHA256 que acompanha cada entrega de webhook.
/// </summary>
/// <remarks>
/// <para>
/// O cabeçalho carrega o carimbo de tempo e a assinatura: <c>t=1758067200,v1=hex</c>. O que é assinado é
/// <c>t.corpo</c>, e não só o corpo — sem o carimbo dentro da assinatura, quem interceptasse uma entrega
/// poderia reenviá-la para sempre, e ela continuaria válida. Com ele, o assinante recusa o que chegou
/// fora da janela de tolerância.
/// </para>
/// <para>
/// A conferência do lado de quem recebe deve usar comparação em tempo constante. É o que
/// <see cref="Confere"/> faz, e é isso que a documentação do assinante precisa dizer.
/// </para>
/// </remarks>
public static class AssinaturaHmacDeWebhook
{
    /// <summary>Cabeçalho que leva o carimbo e a assinatura.</summary>
    public const string Cabecalho = "X-Torre-Signature";

    /// <summary>Cabeçalho com o identificador do evento, para o assinante deduplicar.</summary>
    public const string CabecalhoDoEvento = "X-Torre-Event-Id";

    /// <summary>Cabeçalho com o nome do evento.</summary>
    public const string CabecalhoDoTipo = "X-Torre-Event-Type";

    /// <summary>Monta o valor do cabeçalho de assinatura.</summary>
    public static string Gerar(string segredo, string conteudo, DateTimeOffset agora)
    {
        var carimbo = agora.ToUnixTimeSeconds();
        return string.Create(CultureInfo.InvariantCulture, $"t={carimbo},v1={Calcular(segredo, conteudo, carimbo)}");
    }

    /// <summary>Calcula a assinatura de um carimbo e um corpo.</summary>
    public static string Calcular(string segredo, string conteudo, long carimbo)
    {
        ArgumentNullException.ThrowIfNull(segredo);

        var assinado = string.Create(CultureInfo.InvariantCulture, $"{carimbo}.{conteudo}");

        return Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(segredo), Encoding.UTF8.GetBytes(assinado))).ToLowerInvariant();
    }

    /// <summary>
    /// Confere um cabeçalho recebido, do jeito que o assinante deveria conferir.
    /// </summary>
    /// <remarks>Existe para o teste provar o contrato que documentamos — e para o exemplo na documentação.</remarks>
    public static bool Confere(string? cabecalho, string segredo, string conteudo, DateTimeOffset agora, TimeSpan tolerancia)
    {
        if (cabecalho is null)
        {
            return false;
        }

        long? carimbo = null;
        string? assinatura = null;

        foreach (var parte in cabecalho.Split(','))
        {
            var separador = parte.IndexOf('=', StringComparison.Ordinal);

            if (separador <= 0)
            {
                continue;
            }

            var nome = parte[..separador].Trim();
            var valor = parte[(separador + 1)..].Trim();

            if (nome == "t" && long.TryParse(valor, CultureInfo.InvariantCulture, out var lido))
            {
                carimbo = lido;
            }
            else if (nome == "v1")
            {
                assinatura = valor;
            }
        }

        if (carimbo is not { } instante || assinatura is null)
        {
            return false;
        }

        var idade = agora - DateTimeOffset.FromUnixTimeSeconds(instante);

        if (idade > tolerancia || idade < -tolerancia)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Calcular(segredo, conteudo, instante)),
            Encoding.UTF8.GetBytes(assinatura));
    }
}
