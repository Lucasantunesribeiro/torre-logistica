using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

// `IPNetwork` existe nos dois espacos de nomes usados aqui. O apelido diz qual, sem obrigar a
// escrever o nome completo em cada assinatura.
using RedeIp = System.Net.IPNetwork;

namespace TorreLogistica.Api.Seguranca;

/// <summary>
/// Configuração do proxy reverso que fica à frente da API.
/// </summary>
/// <remarks>
/// <para>
/// Atrás de um proxy, toda requisição chega com o endereço do proxy. Quem depende do endereço real do
/// cliente passa a ver um cliente só: o limite de tentativas de login por endereço deixa de proteger
/// cada visitante e passa a somar todos num balde único — o primeiro a errar a senha cinco vezes
/// bloqueia o resto do mundo. A auditoria registra o mesmo endereço em toda linha, e o esquema visto
/// pela aplicação vira <c>http</c> mesmo quando o visitante chegou por <c>https</c>.
/// </para>
/// <para>
/// A correção é ler <c>X-Forwarded-For</c> e <c>X-Forwarded-Proto</c> — e ela só é segura quando se
/// sabe de quem aceitar esses cabeçalhos. Qualquer cliente pode enviá-los. Confiar neles sem saber a
/// origem troca um problema por outro pior: em vez de todos compartilharem um balde, cada atacante
/// escolhe o seu, e o limite por endereço deixa de existir.
/// </para>
/// <para>
/// Por isso o padrão é <see cref="Habilitado"/> desligado, e ligá-lo sem declarar de quem confiar
/// derruba a subida em vez de atender inseguro.
/// </para>
/// </remarks>
public sealed class OpcoesDeProxyReverso
{
    /// <summary>Seção correspondente na configuração.</summary>
    public const string Secao = "Torre:ProxyReverso";

    /// <summary>
    /// Se a API deve interpretar os cabeçalhos encaminhados.
    /// </summary>
    /// <remarks>
    /// Desligado por padrão: exposta diretamente, a API não deve acreditar em cabeçalho nenhum
    /// vindo do cliente. Quem liga isto é a implantação que sabe ter um proxy à frente.
    /// </remarks>
    public bool Habilitado { get; set; }

    /// <summary>
    /// Redes, em notação CIDR, de onde os cabeçalhos encaminhados são aceitos.
    /// </summary>
    /// <remarks>
    /// Numa implantação em contêiner, é a rede interna do orquestrador — o proxy recebe um endereço
    /// dela a cada subida, e fixar o endereço exato quebraria no primeiro <c>up</c>.
    /// </remarks>
    public IReadOnlyList<string> RedesConfiaveis { get; set; } = [];

    /// <summary>Endereços exatos de proxy confiáveis, quando forem estáveis.</summary>
    public IReadOnlyList<string> ProxiesConfiaveis { get; set; } = [];

    /// <summary>
    /// Quantos endereços ler, da direita para a esquerda, em <c>X-Forwarded-For</c>.
    /// </summary>
    /// <remarks>
    /// Um, porque há um proxy só. O cabeçalho é uma lista que cada salto acrescenta ao final, e o
    /// valor que o nosso proxy escreveu é o último. Ler mais que o número real de saltos é ler o que
    /// o cliente escreveu — que é exatamente o que não se pode acreditar.
    /// </remarks>
    public int LimiteDeSaltos { get; set; } = 1;
}

/// <summary>
/// Registro e ativação do tratamento de cabeçalhos encaminhados.
/// </summary>
public static class ConfiguracaoDoProxyReverso
{
    /// <summary>Lê a configuração e prepara o tratamento dos cabeçalhos encaminhados.</summary>
    /// <exception cref="InvalidOperationException">
    /// Quando o tratamento está habilitado sem nenhuma rede ou proxy confiável declarado.
    /// </exception>
    public static IServiceCollection AdicionarProxyReverso(
        this IServiceCollection servicos,
        IConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(servicos);
        ArgumentNullException.ThrowIfNull(configuracao);

        var opcoes = configuracao.GetSection(OpcoesDeProxyReverso.Secao).Get<OpcoesDeProxyReverso>()
            ?? new OpcoesDeProxyReverso();

        servicos.Configure<OpcoesDeProxyReverso>(configuracao.GetSection(OpcoesDeProxyReverso.Secao));

        if (!opcoes.Habilitado)
        {
            return servicos;
        }

        if (opcoes.RedesConfiaveis.Count == 0 && opcoes.ProxiesConfiaveis.Count == 0)
        {
            throw new InvalidOperationException(
                $"'{OpcoesDeProxyReverso.Secao}:Habilitado' está ligado sem nenhuma entrada em "
                + "'RedesConfiaveis' ou 'ProxiesConfiaveis'. Aceitar cabeçalho encaminhado de qualquer "
                + "origem deixa qualquer cliente escolher o próprio endereço e anula o limite de "
                + "requisições por endereço. Declare de quem confiar, ou desligue o tratamento.");
        }

        if (opcoes.LimiteDeSaltos < 1)
        {
            throw new InvalidOperationException(
                $"'{OpcoesDeProxyReverso.Secao}:LimiteDeSaltos' precisa ser pelo menos 1.");
        }

        // As duas listas sao analisadas AQUI, e nao dentro do `Configure` abaixo.
        //
        // O `Configure` e preguicoso: o que esta dentro dele so roda quando alguem resolve
        // `IOptions<ForwardedHeadersOptions>` pela primeira vez — ou seja, na primeira requisicao
        // que passar pelo middleware. Uma rede mal escrita ali dentro derrubaria a primeira
        // requisicao de um visitante, com a subida ja dada como bem-sucedida e ninguem olhando.
        //
        // Analisadas antes, o mesmo erro derruba a subida, que e quando ainda ha quem esteja vendo.
        var redes = opcoes.RedesConfiaveis.Select(AnalisarRede).ToArray();
        var proxies = opcoes.ProxiesConfiaveis.Select(AnalisarEndereco).ToArray();

        servicos.Configure<ForwardedHeadersOptions>(encaminhados =>
        {
            encaminhados.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            encaminhados.ForwardLimit = opcoes.LimiteDeSaltos;

            // O padrão já traz o loopback. Ele é apagado porque a lista precisa dizer exatamente de
            // quem se confia: uma entrada herdada é uma entrada que ninguém revisou.
            encaminhados.KnownIPNetworks.Clear();
            encaminhados.KnownProxies.Clear();

            foreach (var rede in redes)
            {
                encaminhados.KnownIPNetworks.Add(rede);
            }

            foreach (var proxy in proxies)
            {
                encaminhados.KnownProxies.Add(proxy);
            }
        });

        return servicos;
    }

    /// <summary>
    /// Aplica o tratamento dos cabeçalhos encaminhados, quando habilitado.
    /// </summary>
    /// <remarks>
    /// Precisa ser o primeiro da fila. Qualquer coisa que rode antes — registro de requisição,
    /// correlação, limite por endereço — leria o endereço do proxy, e corrigi-lo depois não desfaz a
    /// decisão já tomada com o valor errado.
    /// </remarks>
    public static IApplicationBuilder UsarProxyReverso(this IApplicationBuilder aplicacao)
    {
        ArgumentNullException.ThrowIfNull(aplicacao);

        var opcoes = aplicacao.ApplicationServices
            .GetRequiredService<IOptions<OpcoesDeProxyReverso>>().Value;

        if (opcoes.Habilitado)
        {
            aplicacao.UseForwardedHeaders();
        }

        return aplicacao;
    }

    private static RedeIp AnalisarRede(string valor)
    {
        if (RedeIp.TryParse(valor, out var rede))
        {
            return rede;
        }

        throw new InvalidOperationException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"'{valor}' não é uma rede válida em '{OpcoesDeProxyReverso.Secao}:RedesConfiaveis'. "
                + $"O formato é CIDR, por exemplo 172.18.0.0/16."));
    }

    private static IPAddress AnalisarEndereco(string valor)
    {
        if (IPAddress.TryParse(valor, out var endereco))
        {
            return endereco;
        }

        throw new InvalidOperationException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"'{valor}' não é um endereço IP válido em "
                + $"'{OpcoesDeProxyReverso.Secao}:ProxiesConfiaveis'."));
    }
}
