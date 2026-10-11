using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using TorreLogistica.Application.Abstracoes.Webhooks;
using TorreLogistica.Application.Webhooks;
using TorreLogistica.Domain.Abstracoes.Tempo;

namespace TorreLogistica.Infrastructure.Webhooks;

/// <summary>
/// Decide se um endereço de webhook pode ser chamado.
/// </summary>
/// <remarks>
/// A URL é cadastrada pelo cliente, e o POST sai da nossa rede: sem esta trava, quem cadastra
/// <c>http://169.254.169.254/</c> ou <c>http://10.0.0.5/admin</c> usa o nosso servidor como procurador para
/// alcançar o que ele não alcança de fora. É o SSRF clássico, e a defesa é conferir o endereço <b>resolvido</b>,
/// não o texto da URL.
/// </remarks>
public static class DestinoDeWebhook
{
    /// <summary>Indica se todos os endereços resolvidos do host são públicos.</summary>
    public static async Task<bool> EhPermitidoAsync(Uri url, bool permitirLocal, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(url);

        if (permitirLocal)
        {
            return true;
        }

        IPAddress[] enderecos;

        if (IPAddress.TryParse(url.Host, out var literal))
        {
            enderecos = [literal];
        }
        else
        {
            try
            {
                enderecos = await Dns.GetHostAddressesAsync(url.Host, cancelamento).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                return false;
            }
        }

        return enderecos.Length > 0 && Array.TrueForAll(enderecos, EhEnderecoPublico);
    }

    /// <summary>Decide se UM endereço resolvido é público, aplicando a mesma política a IPv4 e IPv6.</summary>
    /// <remarks>
    /// Normaliza IPv4 mapeado em IPv6 (<c>::ffff:a.b.c.d</c>) <b>antes</b> de decidir: sem isto,
    /// <c>::ffff:10.0.0.5</c> e <c>::ffff:169.254.169.254</c> passavam como "públicos" (o código anterior
    /// liberava todo endereço não-IPv4) e alcançavam a rede interna. É <see langword="internal"/> para o
    /// conector revalidar exatamente este critério no instante de conectar.
    /// </remarks>
    internal static bool EhEnderecoPublico(IPAddress endereco)
    {
        ArgumentNullException.ThrowIfNull(endereco);

        // IPv4 disfarçado de IPv6 volta a ser IPv4 antes da política — senão escaparia da faixa privada.
        if (endereco.IsIPv4MappedToIPv6)
        {
            endereco = endereco.MapToIPv4();
        }

        if (endereco.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (IPAddress.IsLoopback(endereco)               // ::1
                || endereco.Equals(IPAddress.IPv6Any)        // :: (não especificado)
                || endereco.IsIPv6LinkLocal                  // fe80::/10
                || endereco.IsIPv6SiteLocal                  // fec0::/10 (obsoleto)
                || endereco.IsIPv6UniqueLocal                // fc00::/7
                || endereco.IsIPv6Multicast)                 // ff00::/8
            {
                return false;
            }

            // ::x.x.x.x (IPv4-compatível, obsoleto) embute um IPv4 nos 32 bits baixos, com os 96 bits
            // altos zerados. Unicast global de verdade tem bits altos não nulos — então "altos zerados"
            // denuncia a forma embutida e é recusado por garantia.
            var octetos = endereco.GetAddressBytes();
            for (var i = 0; i < 12; i++)
            {
                if (octetos[i] != 0)
                {
                    return true;
                }
            }

            return false;
        }

        var partes = endereco.GetAddressBytes();

        return partes[0] switch
        {
            0 or 10 or 127 => false,
            169 when partes[1] == 254 => false,
            172 when partes[1] >= 16 && partes[1] <= 31 => false,
            192 when partes[1] == 168 => false,
            100 when partes[1] >= 64 && partes[1] <= 127 => false,
            >= 224 => false,
            _ => true,
        };
    }
}

/// <summary>
/// Entrega o evento por HTTP, assinado, com tempo limite curto e sem seguir redirecionamento.
/// </summary>
/// <remarks>
/// Não lança por falha do assinante: erro dele é dado do nosso fluxo, não exceção. Quem chama registra a
/// tentativa e decide o backoff.
/// </remarks>
public sealed class ClienteDeWebhookHttp(
    HttpClient http,
    IOptions<OpcoesDeWebhooks> opcoes,
    IRelogio relogio) : IClienteDeWebhook
{
    /// <summary>Nome do cliente registrado na fábrica.</summary>
    public const string NomeDoCliente = "webhooks";

    /// <inheritdoc />
    public async Task<ResultadoDaEntregaDeWebhook> EntregarAsync(
        string url,
        string tipo,
        Guid mensagemId,
        string conteudo,
        string segredo,
        CancellationToken cancelamento)
    {
        var relogioDaTentativa = Stopwatch.StartNew();

        if (!Uri.TryCreate(url, UriKind.Absolute, out var destino))
        {
            return new ResultadoDaEntregaDeWebhook(false, null, "Endereço inválido.", 0);
        }

        if (!await DestinoDeWebhook.EhPermitidoAsync(destino, opcoes.Value.PermitirDestinoLocal, cancelamento).ConfigureAwait(false))
        {
            return new ResultadoDaEntregaDeWebhook(
                false, null, "Endereço de destino não é público.", (int)relogioDaTentativa.ElapsedMilliseconds);
        }

        using var requisicao = new HttpRequestMessage(HttpMethod.Post, destino)
        {
            Content = new StringContent(conteudo, Encoding.UTF8, "application/json"),
        };

        requisicao.Headers.TryAddWithoutValidation(
            AssinaturaHmacDeWebhook.Cabecalho, AssinaturaHmacDeWebhook.Gerar(segredo, conteudo, relogio.AgoraUtc));
        requisicao.Headers.TryAddWithoutValidation(
            AssinaturaHmacDeWebhook.CabecalhoDoEvento, mensagemId.ToString());
        requisicao.Headers.TryAddWithoutValidation(AssinaturaHmacDeWebhook.CabecalhoDoTipo, tipo);

        using var prazo = CancellationTokenSource.CreateLinkedTokenSource(cancelamento);
        prazo.CancelAfter(opcoes.Value.TempoLimite);

        try
        {
            using var resposta = await http
                .SendAsync(requisicao, HttpCompletionOption.ResponseHeadersRead, prazo.Token)
                .ConfigureAwait(false);

            var duracao = (int)relogioDaTentativa.ElapsedMilliseconds;
            var status = (int)resposta.StatusCode;

            return resposta.IsSuccessStatusCode
                ? new ResultadoDaEntregaDeWebhook(true, status, null, duracao)
                : new ResultadoDaEntregaDeWebhook(
                    false,
                    status,
                    string.Create(CultureInfo.InvariantCulture, $"O assinante respondeu {status}."),
                    duracao);
        }
        catch (OperationCanceledException) when (!cancelamento.IsCancellationRequested)
        {
            return new ResultadoDaEntregaDeWebhook(
                false, null, "O assinante não respondeu dentro do tempo limite.", (int)relogioDaTentativa.ElapsedMilliseconds);
        }
        catch (HttpRequestException excecao)
        {
            return new ResultadoDaEntregaDeWebhook(
                false, null, excecao.Message, (int)relogioDaTentativa.ElapsedMilliseconds);
        }
    }
}
