using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TorreLogistica.Domain.Identidade;

namespace TorreLogistica.IntegrationTests.Infra;

/// <summary>Base dos testes de integração: fábrica, cenário e atalhos de requisição.</summary>
public abstract class TesteDeIntegracao(ContainerPostgis banco) : IAsyncLifetime
{
    private FabricaDaApi? _fabrica;

    /// <summary>Banco da coleção.</summary>
    protected ContainerPostgis Banco => banco;

    /// <summary>Criação de organizações e contas de teste.</summary>
    protected Cenario Cenario { get; } = new(banco);

    /// <summary>Fábrica da API deste teste.</summary>
    protected FabricaDaApi Fabrica => _fabrica ?? throw new InvalidOperationException("Fábrica não inicializada.");

    /// <summary>Configuração extra aplicada à API deste teste.</summary>
    protected virtual IReadOnlyDictionary<string, string?>? ConfiguracaoAdicional => null;

    /// <summary>Relógio controlado, quando o teste precisa passar o tempo.</summary>
    protected virtual TimeProvider? Relogio => null;

    /// <summary>Troca de serviços da API deste teste — por exemplo, um provedor externo falso.</summary>
    protected virtual Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? ServicosDeTeste => null;

    /// <summary>Cancelamento do teste corrente.</summary>
    protected static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    /// <inheritdoc />
    public virtual ValueTask InitializeAsync()
    {
        _fabrica = new FabricaDaApi(banco, ConfiguracaoAdicional, Relogio, ServicosDeTeste);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_fabrica is not null)
        {
            await _fabrica.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Cliente HTTP sem cookies automáticos.</summary>
    protected HttpClient Cliente() => Fabrica.Cliente();

    /// <summary>Faz login e devolve a resposta crua.</summary>
    protected static async Task<HttpResponseMessage> LoginAsync(
        HttpClient cliente,
        string? organizacao,
        string? email,
        string? senha,
        CanalDeAcesso canal = CanalDeAcesso.Operacao,
        string? origem = FabricaDaApi.OrigemAutorizada)
    {
        using var requisicao = new HttpRequestMessage(HttpMethod.Post, $"{Caminho(canal)}/login")
        {
            Content = JsonContent.Create(new { organizacao, email, senha }),
        };

        if (origem is not null)
        {
            requisicao.Headers.Add("Origin", origem);
        }

        return await cliente.SendAsync(requisicao, Cancelamento);
    }

    /// <summary>Faz login com sucesso e devolve tokens.</summary>
    protected static async Task<SessaoDeTeste> EntrarAsync(HttpClient cliente, ContaDeTeste conta)
    {
        using var resposta = await LoginAsync(cliente, conta.OrganizacaoSlug, conta.Email, conta.Senha, conta.Perfil.Canal());
        return await LerSessaoAsync(resposta, conta.Perfil.Canal());
    }

    /// <summary>Lê token de acesso e cookie de renovação de uma resposta 200.</summary>
    protected static async Task<SessaoDeTeste> LerSessaoAsync(HttpResponseMessage resposta, CanalDeAcesso canal)
    {
        var corpo = await resposta.Content.ReadAsStringAsync(Cancelamento);
        Assert.True(resposta.StatusCode == HttpStatusCode.OK, $"Esperava 200, veio {(int)resposta.StatusCode}: {corpo}");

        using var json = JsonDocument.Parse(corpo);
        var token = json.RootElement.GetProperty("tokenDeAcesso").GetString()!;
        var cookie = ValorDoCookie(resposta, canal) ?? throw new InvalidOperationException("Resposta sem cookie de renovação.");

        return new SessaoDeTeste(token, cookie);
    }

    /// <summary>Valor do cookie de renovação gravado na resposta.</summary>
    protected static string? ValorDoCookie(HttpResponseMessage resposta, CanalDeAcesso canal) =>
        CabecalhoDoCookie(resposta, canal) is { } cabecalho
            ? cabecalho[(cabecalho.IndexOf('=', StringComparison.Ordinal) + 1)..].Split(';')[0]
            : null;

    /// <summary>Cabeçalho <c>Set-Cookie</c> completo do cookie de renovação.</summary>
    protected static string? CabecalhoDoCookie(HttpResponseMessage resposta, CanalDeAcesso canal)
    {
        var nome = canal == CanalDeAcesso.Operacao ? "torre_renovacao_operacao" : "torre_renovacao_motorista";

        return resposta.Headers.TryGetValues("Set-Cookie", out var valores)
            ? valores.FirstOrDefault(valor => valor.StartsWith(nome + "=", StringComparison.Ordinal))
            : null;
    }

    /// <summary>Chama renovação com o cookie informado.</summary>
    protected static Task<HttpResponseMessage> RenovarAsync(
        HttpClient cliente,
        string? cookie,
        CanalDeAcesso canal = CanalDeAcesso.Operacao,
        string? origem = FabricaDaApi.OrigemAutorizada,
        string? nomeDoCookie = null) =>
        EnviarComCookieAsync(cliente, $"{Caminho(canal)}/renovar", cookie, canal, origem, nomeDoCookie);

    /// <summary>Chama logout com o cookie informado.</summary>
    protected static Task<HttpResponseMessage> SairAsync(
        HttpClient cliente,
        string? cookie,
        CanalDeAcesso canal = CanalDeAcesso.Operacao,
        string? origem = FabricaDaApi.OrigemAutorizada) =>
        EnviarComCookieAsync(cliente, $"{Caminho(canal)}/sair", cookie, canal, origem, nomeDoCookie: null);

    /// <summary>Requisição autenticada por token de acesso.</summary>
    protected static async Task<HttpResponseMessage> EnviarAsync(
        HttpClient cliente,
        HttpMethod metodo,
        string url,
        string? tokenDeAcesso,
        object? corpo = null)
    {
        using var requisicao = new HttpRequestMessage(metodo, url);

        if (tokenDeAcesso is not null)
        {
            requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenDeAcesso);
        }

        if (corpo is not null)
        {
            requisicao.Content = corpo as HttpContent ?? JsonContent.Create(corpo);
        }

        return await cliente.SendAsync(requisicao, Cancelamento);
    }

    /// <summary>Lê o corpo como JSON.</summary>
    protected static async Task<JsonElement> JsonAsync(HttpResponseMessage resposta)
    {
        var texto = await resposta.Content.ReadAsStringAsync(Cancelamento);
        using var documento = JsonDocument.Parse(texto);
        return documento.RootElement.Clone();
    }

    /// <summary>Código de erro de um ProblemDetails.</summary>
    protected static async Task<string?> CodigoDoErroAsync(HttpResponseMessage resposta) =>
        (await JsonAsync(resposta)).TryGetProperty("codigo", out var codigo) ? codigo.GetString() : null;

    /// <summary>Cria um cliente e um destinatário válidos na organização do token de gestão.</summary>
    protected static async Task<(Guid ClienteId, Guid DestinatarioId)> CriarClienteEDestinatarioAsync(
        HttpClient cliente,
        string tokenDeGestao)
    {
        using var clienteCriado = await EnviarAsync(
            cliente, HttpMethod.Post, "/api/clientes", tokenDeGestao, RoteirosDeCadastro.Obter("cliente").Corpo());
        using var destinatarioCriado = await EnviarAsync(
            cliente, HttpMethod.Post, "/api/destinatarios", tokenDeGestao, RoteirosDeCadastro.Obter("destinatario").Corpo());

        return (
            (await JsonAsync(clienteCriado)).GetProperty("id").GetGuid(),
            (await JsonAsync(destinatarioCriado)).GetProperty("id").GetGuid());
    }

    private static string Caminho(CanalDeAcesso canal) =>
        canal == CanalDeAcesso.Operacao ? "/api/autenticacao" : "/api/motorista/autenticacao";

    private static async Task<HttpResponseMessage> EnviarComCookieAsync(
        HttpClient cliente,
        string url,
        string? cookie,
        CanalDeAcesso canal,
        string? origem,
        string? nomeDoCookie)
    {
        using var requisicao = new HttpRequestMessage(HttpMethod.Post, url);
        var nome = nomeDoCookie ?? (canal == CanalDeAcesso.Operacao ? "torre_renovacao_operacao" : "torre_renovacao_motorista");

        if (cookie is not null)
        {
            requisicao.Headers.Add("Cookie", $"{nome}={cookie}");
        }

        if (origem is not null)
        {
            requisicao.Headers.Add("Origin", origem);
        }

        return await cliente.SendAsync(requisicao, Cancelamento);
    }
}

/// <summary>Tokens de uma sessão aberta em teste.</summary>
public sealed record SessaoDeTeste(string TokenDeAcesso, string CookieDeRenovacao);
