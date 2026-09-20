using TorreLogistica.Application.Webhooks;
using TorreLogistica.Domain.Webhooks;
using TorreLogistica.Infrastructure.Webhooks;

namespace TorreLogistica.UnitTests.Aplicacao;

public sealed class AssinaturaHmacDeWebhookTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
    private const string Segredo = "3f1a9c0d5e7b2a4c6d8e0f1a3b5c7d9e";
    private const string Conteudo = """{"deliveryId":"0198f0e2-0000-7000-8000-000000000001","status":"Entregue"}""";

    [Fact]
    public void OCabecalhoLevaCarimboEAssinaturaEConfere()
    {
        var cabecalho = AssinaturaHmacDeWebhook.Gerar(Segredo, Conteudo, Agora);

        Assert.StartsWith($"t={Agora.ToUnixTimeSeconds()},v1=", cabecalho, StringComparison.Ordinal);
        Assert.True(AssinaturaHmacDeWebhook.Confere(cabecalho, Segredo, Conteudo, Agora, PoliticaDeWebhook.ToleranciaDoCarimbo));
    }

    [Fact]
    public void CorpoAlteradoNaoConfere()
    {
        var cabecalho = AssinaturaHmacDeWebhook.Gerar(Segredo, Conteudo, Agora);

        Assert.False(AssinaturaHmacDeWebhook.Confere(
            cabecalho, Segredo, Conteudo.Replace("Entregue", "Cancelada", StringComparison.Ordinal), Agora, PoliticaDeWebhook.ToleranciaDoCarimbo));
    }

    [Fact]
    public void SegredoErradoNaoConfere()
    {
        var cabecalho = AssinaturaHmacDeWebhook.Gerar(Segredo, Conteudo, Agora);

        Assert.False(AssinaturaHmacDeWebhook.Confere(
            cabecalho, "outro-segredo", Conteudo, Agora, PoliticaDeWebhook.ToleranciaDoCarimbo));
    }

    /// <summary>
    /// O carimbo entra na assinatura justamente para isto: entrega capturada hoje não vale amanhã.
    /// </summary>
    [Fact]
    public void ForaDaJanelaDeToleranciaNaoConfere()
    {
        var cabecalho = AssinaturaHmacDeWebhook.Gerar(Segredo, Conteudo, Agora);

        Assert.False(AssinaturaHmacDeWebhook.Confere(
            cabecalho, Segredo, Conteudo, Agora.AddHours(1), PoliticaDeWebhook.ToleranciaDoCarimbo));

        Assert.True(AssinaturaHmacDeWebhook.Confere(
            cabecalho, Segredo, Conteudo, Agora.AddMinutes(4), PoliticaDeWebhook.ToleranciaDoCarimbo));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v1=abc")]
    [InlineData("t=abc,v1=def")]
    [InlineData("t=1758369600")]
    public void CabecalhoMalformadoNaoConfere(string? cabecalho) =>
        Assert.False(AssinaturaHmacDeWebhook.Confere(cabecalho, Segredo, Conteudo, Agora, PoliticaDeWebhook.ToleranciaDoCarimbo));

    [Fact]
    public void AssinaturaTrocadaPorOutraValidaNaoConfere()
    {
        var doOutroCorpo = AssinaturaHmacDeWebhook.Calcular(Segredo, "{}", Agora.ToUnixTimeSeconds());

        Assert.False(AssinaturaHmacDeWebhook.Confere(
            $"t={Agora.ToUnixTimeSeconds()},v1={doOutroCorpo}", Segredo, Conteudo, Agora, PoliticaDeWebhook.ToleranciaDoCarimbo));
    }
}

public sealed class DestinoDeWebhookTestes
{
    [Theory]
    [InlineData("http://127.0.0.1/hook")]
    [InlineData("http://localhost/hook")]
    [InlineData("http://10.0.0.5/admin")]
    [InlineData("http://172.16.9.1/hook")]
    [InlineData("http://192.168.1.10/hook")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://100.100.100.200/hook")]
    [InlineData("http://[::1]/hook")]
    public async Task EnderecoInternoEhRecusado(string url) =>
        Assert.False(await DestinoDeWebhook.EhPermitidoAsync(new Uri(url), permitirLocal: false, TestContext.Current.CancellationToken));

    [Theory]
    [InlineData("https://203.0.113.10/hook")]
    [InlineData("https://8.8.8.8/hook")]
    public async Task EnderecoPublicoEhAceito(string url) =>
        Assert.True(await DestinoDeWebhook.EhPermitidoAsync(new Uri(url), permitirLocal: false, TestContext.Current.CancellationToken));

    /// <summary>
    /// A liberação existe só para desenvolvimento e teste, onde o assinante é um servidor da própria máquina.
    /// </summary>
    [Fact]
    public async Task ComLiberacaoOEnderecoLocalPassa() =>
        Assert.True(await DestinoDeWebhook.EhPermitidoAsync(
            new Uri("http://127.0.0.1/hook"), permitirLocal: true, TestContext.Current.CancellationToken));
}
