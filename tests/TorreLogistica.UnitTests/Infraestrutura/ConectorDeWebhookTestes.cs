using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;

using TorreLogistica.Infrastructure.Webhooks;

using Xunit;

namespace TorreLogistica.UnitTests.Infraestrutura;

/// <summary>
/// Conexão de webhook fixada no IP aprovado. Prova — pelo destino EFETIVAMENTE conectado, não só pela
/// chamada do validador — que só um endereço aprovado recebe conexão e que uma resolução que vira interna
/// no instante de conectar é barrada (DNS rebinding).
/// </summary>
public sealed class ConectorDeWebhookTestes
{
    private static HttpClient ClienteCom(ConectorDeWebhook conector) =>
        new(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            Proxy = null,
            ConnectCallback = conector.ConectarAsync,
        });

    // Servidor TCP mínimo no loopback: responde um 200 enxuto, fecha a conexão (força reconectar no próximo
    // pedido) e conta quantas conexões aceitou.
    private static (TcpListener Listener, int Porta, Task<int> Aceitas) ServidorDeTeste(
        CancellationToken cancelamento, int quantas = 1)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var porta = ((IPEndPoint)listener.LocalEndpoint).Port;

        var aceitas = Task.Run(
            async () =>
            {
                var contador = 0;
                for (var i = 0; i < quantas; i++)
                {
                    using var cliente = await listener.AcceptTcpClientAsync(cancelamento).ConfigureAwait(false);
                    contador++;
                    using var fluxo = cliente.GetStream();
                    var buffer = new byte[1024];
                    await fluxo.ReadAsync(buffer, cancelamento).ConfigureAwait(false);
                    var resposta = Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok");
                    await fluxo.WriteAsync(resposta, cancelamento).ConfigureAwait(false);
                }

                return contador;
            },
            cancelamento);

        return (listener, porta, aceitas);
    }

    private static bool TemBloqueio(Exception? excecao)
    {
        for (var atual = excecao; atual is not null; atual = atual.InnerException)
        {
            if (atual is DestinoDeWebhookBloqueadoException)
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public async Task ConectaSoAoEnderecoAprovadoEIgnoraOInterno()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var (listener, porta, aceitas) = ServidorDeTeste(cts.Token);
        try
        {
            // O nome resolve para dois endereços — um interno, um loopback — e a política só aprova o
            // loopback. Logo, só o loopback pode receber conexão.
            var conector = new ConectorDeWebhook(
                resolver: (_, _) => new ValueTask<IPAddress[]>([IPAddress.Parse("10.0.0.5"), IPAddress.Loopback]),
                ehPermitido: ip => ip.Equals(IPAddress.Loopback));
            using var http = ClienteCom(conector);

            var resposta = await http.GetAsync($"http://assinante.invalido:{porta}/webhook", cts.Token);

            Assert.True(resposta.IsSuccessStatusCode);
            Assert.Equal(1, await aceitas);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task RecusaQuandoTodosOsEnderecosSaoInternos()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var conector = new ConectorDeWebhook(
            resolver: (_, _) => new ValueTask<IPAddress[]>(
                [IPAddress.Parse("10.0.0.5"), IPAddress.Parse("169.254.169.254")]),
            ehPermitido: DestinoDeWebhook.EhEnderecoPublico);
        using var http = ClienteCom(conector);

        var erro = await Assert.ThrowsAsync<HttpRequestException>(
            () => http.GetAsync("http://alvo.interno:8080/", cts.Token));

        Assert.True(TemBloqueio(erro));
    }

    [Fact]
    public async Task ReavaliaNaConexaoEBarraResolucaoQueViraInterna()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var (listener, porta, aceitas) = ServidorDeTeste(cts.Token);
        try
        {
            // 1ª resolução: loopback aprovado (entrega). 2ª resolução (rebinding): interno — e como o
            // servidor fecha a conexão, o 2º pedido reconecta e passa de novo pela política.
            var chamada = 0;
            var conector = new ConectorDeWebhook(
                resolver: (_, _) =>
                {
                    var atual = Interlocked.Increment(ref chamada);
                    IPAddress[] enderecos = atual == 1 ? [IPAddress.Loopback] : [IPAddress.Parse("10.0.0.5")];
                    return new ValueTask<IPAddress[]>(enderecos);
                },
                ehPermitido: ip => ip.Equals(IPAddress.Loopback));
            using var http = ClienteCom(conector);

            var ok = await http.GetAsync($"http://assinante.invalido:{porta}/", cts.Token);
            Assert.True(ok.IsSuccessStatusCode);

            var erro = await Assert.ThrowsAsync<HttpRequestException>(
                () => http.GetAsync($"http://assinante.invalido:{porta}/", cts.Token));
            Assert.True(TemBloqueio(erro));

            Assert.Equal(1, await aceitas);
        }
        finally
        {
            listener.Stop();
        }
    }
}
