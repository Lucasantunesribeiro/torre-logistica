using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace TorreLogistica.Infrastructure.Webhooks;

/// <summary>
/// Abre a conexão TCP de um webhook SÓ para um endereço aprovado pela política de destino, revalidando a
/// resolução de nome no instante exato de conectar.
/// </summary>
/// <remarks>
/// É isto que fecha o DNS rebinding: o pré-voo (<see cref="DestinoDeWebhook.EhPermitidoAsync"/>) resolve o
/// DNS uma vez, mas sem fixar nada o <see cref="HttpClient"/> resolveria de novo ao conectar e poderia cair
/// num IP interno. Aqui a resolução usada para conectar é a MESMA que passa pela política, e só um endereço
/// aprovado recebe conexão. O <see cref="SocketsHttpHandler"/> faz o TLS depois, sobre o <see cref="Stream"/>
/// devolvido, usando o nome do host do pedido — então SNI e validação de certificado continuam corretos.
///
/// A resolução de nome e a política são injetáveis para que o teste prove qual destino foi <b>efetivamente</b>
/// usado (e não apenas que o validador foi chamado), sem depender de DNS real nem de IP roteável.
/// </remarks>
internal sealed class ConectorDeWebhook
{
    private readonly Func<string, CancellationToken, ValueTask<IPAddress[]>> resolver;
    private readonly Func<IPAddress, bool> ehPermitido;

    public ConectorDeWebhook(
        bool permitirLocal = false,
        Func<string, CancellationToken, ValueTask<IPAddress[]>>? resolver = null,
        Func<IPAddress, bool>? ehPermitido = null)
    {
        this.resolver = resolver
            ?? ((host, cancelamento) => new ValueTask<IPAddress[]>(Dns.GetHostAddressesAsync(host, cancelamento)));

        // Em dev/teste autorizado, destino local é liberado — é a mesma porta que o pré-voo abre com
        // PermitirDestinoLocal. Fora disso, só endereço público, revalidado na conexão.
        this.ehPermitido = ehPermitido ?? (permitirLocal ? (_ => true) : DestinoDeWebhook.EhEnderecoPublico);
    }

    /// <summary>Conecta e devolve o fluxo bruto; o handler embrulha em TLS quando o esquema é HTTPS.</summary>
    public async ValueTask<Stream> ConectarAsync(
        SocketsHttpConnectionContext contexto, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var endpoint = contexto.DnsEndPoint;

        IPAddress[] resolvidos;
        if (IPAddress.TryParse(endpoint.Host, out var literal))
        {
            resolvidos = [literal];
        }
        else
        {
            resolvidos = await this.resolver(endpoint.Host, cancelamento).ConfigureAwait(false);
        }

        // Revalida CADA endereço resolvido agora, no momento de conectar: um nome que apontava para público
        // no pré-voo e passou a apontar para interno é barrado aqui.
        var permitidos = Array.FindAll(resolvidos, ip => this.ehPermitido(ip));
        if (permitidos.Length == 0)
        {
            throw new DestinoDeWebhookBloqueadoException(
                "Nenhum endereço público foi resolvido para o destino do webhook.");
        }

        Exception? ultimaFalha = null;
        foreach (var ip in permitidos)
        {
            var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(ip, endpoint.Port), cancelamento).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception excecao) when (!cancelamento.IsCancellationRequested)
            {
                socket.Dispose();
                ultimaFalha = excecao;
            }
        }

        throw new DestinoDeWebhookBloqueadoException(
            "Não foi possível conectar a um endereço público aprovado do destino do webhook.", ultimaFalha);
    }
}

/// <summary>Sinaliza que o destino do webhook não tinha endereço público conectável.</summary>
internal sealed class DestinoDeWebhookBloqueadoException : Exception
{
    public DestinoDeWebhookBloqueadoException(string mensagem)
        : base(mensagem)
    {
    }

    public DestinoDeWebhookBloqueadoException(string mensagem, Exception? interna)
        : base(mensagem, interna)
    {
    }
}
