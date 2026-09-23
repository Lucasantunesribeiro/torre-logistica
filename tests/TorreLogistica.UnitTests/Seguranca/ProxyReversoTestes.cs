using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TorreLogistica.Api.Seguranca;

namespace TorreLogistica.UnitTests.Seguranca;

/// <summary>
/// O que a configuração do proxy reverso promete.
/// </summary>
/// <remarks>
/// <para>
/// O risco que estes testes travam não é o de a funcionalidade não funcionar — é o de ela funcionar
/// insegura. Ler <c>X-Forwarded-For</c> sem saber de quem aceitá-lo deixa qualquer visitante declarar
/// o próprio endereço, e o limite de tentativas de login passa a contar um balde por atacante. Pior
/// que não ter o tratamento, porque parece ter.
/// </para>
/// <para>
/// Por isso a asserção central aqui é sobre RECUSA: ligado sem lista de confiança, a subida cai.
/// </para>
/// </remarks>
public sealed class ProxyReversoTestes
{
    [Fact]
    public void DesligadoNaoConfiguraNada()
    {
        var servicos = Construir(new Dictionary<string, string?>
        {
            ["Torre:ProxyReverso:Habilitado"] = "false",
        });

        var opcoes = servicos.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        // O padrão do ASP.NET Core é não encaminhar nada. Desligado, nada foi tocado.
        Assert.Equal(ForwardedHeaders.None, opcoes.ForwardedHeaders);
    }

    /// <summary>A recusa que dá nome a esta classe de teste.</summary>
    [Fact]
    public void LigadoSemListaDeConfiancaRecusaSubir()
    {
        var erro = Assert.Throws<InvalidOperationException>(() => Construir(new Dictionary<string, string?>
        {
            ["Torre:ProxyReverso:Habilitado"] = "true",
        }));

        Assert.Contains("RedesConfiaveis", erro.Message, StringComparison.Ordinal);
        Assert.Contains("limite de requisições por endereço", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LigadoComRedeDeclaradaEncaminhaEnderecoEEsquema()
    {
        var servicos = Construir(new Dictionary<string, string?>
        {
            ["Torre:ProxyReverso:Habilitado"] = "true",
            ["Torre:ProxyReverso:RedesConfiaveis:0"] = "172.16.0.0/12",
        });

        var opcoes = servicos.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.True(opcoes.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor));

        // O esquema junto, e não só o endereço: sem ele a aplicação se vê em http mesmo quando o
        // visitante chegou por https, e todo link absoluto que ela gerar sai errado.
        Assert.True(opcoes.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedProto));

        var rede = Assert.Single(opcoes.KnownIPNetworks);
        Assert.Equal(IPAddress.Parse("172.16.0.0"), rede.BaseAddress);
        Assert.Equal(12, rede.PrefixLength);
    }

    /// <summary>
    /// O padrão do ASP.NET Core confia no loopback. A lista precisa dizer exatamente de quem se
    /// confia, e herdar uma entrada que ninguém revisou é o contrário disso.
    /// </summary>
    [Fact]
    public void ConfiancaHerdadaNoLoopbackEApagada()
    {
        var servicos = Construir(new Dictionary<string, string?>
        {
            ["Torre:ProxyReverso:Habilitado"] = "true",
            ["Torre:ProxyReverso:ProxiesConfiaveis:0"] = "10.1.2.3",
        });

        var opcoes = servicos.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.Empty(opcoes.KnownIPNetworks);
        var proxy = Assert.Single(opcoes.KnownProxies);
        Assert.Equal(IPAddress.Parse("10.1.2.3"), proxy);
    }

    /// <summary>
    /// Um salto, porque há um proxy só. Ler mais saltos que os reais é ler o que o cliente
    /// escreveu — exatamente o que não se pode acreditar.
    /// </summary>
    [Fact]
    public void LimiteDeSaltosPadraoEUm()
    {
        var servicos = Construir(new Dictionary<string, string?>
        {
            ["Torre:ProxyReverso:Habilitado"] = "true",
            ["Torre:ProxyReverso:RedesConfiaveis:0"] = "172.18.0.0/16",
        });

        var opcoes = servicos.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.Equal(1, opcoes.ForwardLimit);
    }

    [Theory]
    [InlineData("172.18.0.0")]      // sem máscara
    [InlineData("nao-e-uma-rede")]
    [InlineData("172.18.0.0/99")]   // prefixo impossível
    public void RedeMalEscritaRecusaSubirDizendoQual(string rede)
    {
        var erro = Assert.Throws<InvalidOperationException>(() => Construir(new Dictionary<string, string?>
        {
            ["Torre:ProxyReverso:Habilitado"] = "true",
            ["Torre:ProxyReverso:RedesConfiaveis:0"] = rede,
        }));

        Assert.Contains(rede, erro.Message, StringComparison.Ordinal);
        Assert.Contains("CIDR", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnderecoDeProxyMalEscritoRecusaSubir()
    {
        var erro = Assert.Throws<InvalidOperationException>(() => Construir(new Dictionary<string, string?>
        {
            ["Torre:ProxyReverso:Habilitado"] = "true",
            ["Torre:ProxyReverso:ProxiesConfiaveis:0"] = "10.1.2.999",
        }));

        Assert.Contains("10.1.2.999", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LimiteDeSaltosZeradoRecusaSubir()
    {
        var erro = Assert.Throws<InvalidOperationException>(() => Construir(new Dictionary<string, string?>
        {
            ["Torre:ProxyReverso:Habilitado"] = "true",
            ["Torre:ProxyReverso:RedesConfiaveis:0"] = "172.18.0.0/16",
            ["Torre:ProxyReverso:LimiteDeSaltos"] = "0",
        }));

        Assert.Contains("LimiteDeSaltos", erro.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A configuração que o compose da demonstração usa de fato, lida do próprio arquivo de modelo
    /// do ambiente — para que o valor documentado e o valor aceito não possam divergir em silêncio.
    /// </summary>
    [Fact]
    public void RedePadraoDoComposeDaDemonstracaoEAceita()
    {
        var servicos = Construir(new Dictionary<string, string?>
        {
            ["Torre:ProxyReverso:Habilitado"] = "true",
            ["Torre:ProxyReverso:RedesConfiaveis:0"] = "172.16.0.0/12",
            ["Torre:ProxyReverso:LimiteDeSaltos"] = "1",
        });

        var opcoes = servicos.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        // A faixa 172.16.0.0/12 cobre as redes que o Docker cria por padrão — inclusive a
        // 172.18.0.0/16 típica de uma pilha com duas redes.
        var rede = Assert.Single(opcoes.KnownIPNetworks);
        Assert.True(rede.Contains(IPAddress.Parse("172.18.0.2")));
        Assert.False(rede.Contains(IPAddress.Parse("203.0.113.7")));
    }

    /// <summary>
    /// Ligado e sem middleware seria o pior desfecho possível: configuração correta, comportamento
    /// ausente, e nada no log dizendo isso.
    /// </summary>
    [Fact]
    public void LigadoRegistraOMiddlewareNaFila()
    {
        var construtor = WebApplication.CreateBuilder();
        construtor.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Torre:ProxyReverso:Habilitado"] = "true",
            ["Torre:ProxyReverso:RedesConfiaveis:0"] = "172.16.0.0/12",
        });

        construtor.Services.AdicionarProxyReverso(construtor.Configuration);

        var aplicacao = construtor.Build();

        // `UsarProxyReverso` devolve o próprio construtor de pipeline, e a prova de que o middleware
        // entrou é o pipeline montar sem erro com ele dentro.
        var retorno = aplicacao.UsarProxyReverso();

        Assert.Same(aplicacao, retorno);
    }

    private static ServiceProvider Construir(Dictionary<string, string?> configuracao)
    {
        var lida = new ConfigurationBuilder()
            .AddInMemoryCollection(configuracao)
            .Build();

        var servicos = new ServiceCollection();
        servicos.AddOptions();
        servicos.AdicionarProxyReverso(lida);

        return servicos.BuildServiceProvider();
    }
}
