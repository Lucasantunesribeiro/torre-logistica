using System.Net;

using TorreLogistica.Infrastructure.Webhooks;

using Xunit;

namespace TorreLogistica.UnitTests.Infraestrutura;

/// <summary>
/// Política de destino do webhook (anti-SSRF). Prova, como regressão permanente, que IPv4 mapeado em IPv6
/// cai na mesma política IPv4 — o buraco que deixava <c>::ffff:10.0.0.5</c> e <c>::ffff:169.254.169.254</c>
/// alcançarem a rede interna.
/// </summary>
public sealed class DestinoDeWebhookTestes
{
    [Theory]
    // IPv4 mapeado em IPv6: o buraco original. Têm que ser tratados como o IPv4 que embutem.
    [InlineData("::ffff:10.0.0.5", false)]
    [InlineData("::ffff:169.254.169.254", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("::ffff:192.168.0.1", false)]
    [InlineData("::ffff:8.8.8.8", true)]
    // IPv4 direto: privados, loopback, link-local, CGNAT, multicast/broadcast recusados; público liberado.
    [InlineData("10.0.0.5", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.31.255.255", false)]
    [InlineData("172.15.0.1", true)]
    [InlineData("172.32.0.1", true)]
    [InlineData("192.168.1.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("100.128.0.1", true)]
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("255.255.255.255", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("1.1.1.1", true)]
    // IPv6 direto: loopback, não especificado, link/site/unique-local, multicast e IPv4-compatível recusados.
    [InlineData("::1", false)]
    [InlineData("::", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fec0::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("ff02::1", false)]
    [InlineData("::1.2.3.4", false)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("2001:4860:4860::8888", true)]
    public void EnderecoClassificadoCorretamente(string texto, bool esperaPublico)
    {
        var endereco = IPAddress.Parse(texto);

        Assert.Equal(esperaPublico, DestinoDeWebhook.EhEnderecoPublico(endereco));
    }
}
