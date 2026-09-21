using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace TorreLogistica.Simulator.Cliente;

/// <summary>Resposta da sonda de prontidão.</summary>
/// <param name="Status">Situação relatada.</param>
/// <param name="DuracaoEmMs">Quanto a verificação levou.</param>
public sealed record RespostaDeProntidao(string Status, double DuracaoEmMs);

/// <summary>Uma sessão aberta na API.</summary>
/// <param name="TokenDeAcesso">Credencial das chamadas seguintes.</param>
public sealed record SessaoDoSimulador(string TokenDeAcesso);

/// <summary>Falha ao chamar a API, com o que o servidor respondeu.</summary>
public sealed class ErroDaApiDaTorre(HttpMethod metodo, string caminho, HttpStatusCode status, string corpo)
    : Exception($"{metodo} {caminho} respondeu {(int)status}: {corpo}")
{
    /// <summary>Status devolvido.</summary>
    public HttpStatusCode Status { get; } = status;

    /// <summary>Corpo devolvido, para diagnóstico.</summary>
    public string Corpo { get; } = corpo;
}

/// <summary>
/// Cliente HTTP da API da Torre — o simulador fala com ela como qualquer integrador falaria.
/// </summary>
/// <remarks>
/// <para>
/// Nenhum método aqui toca no banco, e o projeto nem referencia a persistência: é o que garante que a
/// demonstração exercita os mesmos caminhos da produção, incluindo autenticação, validação, máquina de
/// estados e concorrência. Um atalho que escrevesse direto na tabela provaria apenas que o atalho funciona.
/// </para>
/// <para>
/// Toda falha vira <see cref="ErroDaApiDaTorre"/> com o corpo do servidor. Numa demonstração que quebra, a
/// pergunta é sempre "o que a API respondeu" — e engolir isso transformaria cada investigação numa
/// arqueologia de log.
/// </para>
/// </remarks>
public sealed class ClienteDaApiDaTorre(HttpClient http, string? origemDeclarada = null)
{
    /// <summary>Nome do cliente nomeado.</summary>
    public const string NomeDoCliente = "api-torre";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Consulta a prontidão da API.</summary>
    public async Task<RespostaDeProntidao?> ConsultarProntidaoAsync(CancellationToken cancelamento)
    {
        using var resposta = await http.GetAsync(new Uri("health/ready", UriKind.Relative), cancelamento).ConfigureAwait(false);
        var corpo = await resposta.Content.ReadAsStringAsync(cancelamento).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(corpo))
        {
            return null;
        }

        using var documento = JsonDocument.Parse(corpo);
        var raiz = documento.RootElement;

        return new RespostaDeProntidao(
            raiz.GetProperty("status").GetString() ?? "Desconhecido",
            raiz.TryGetProperty("duracaoEmMs", out var duracao) ? duracao.GetDouble() : 0);
    }

    /// <summary>Entra no canal do console.</summary>
    public Task<SessaoDoSimulador> EntrarNoConsoleAsync(
        string organizacao,
        string email,
        string senha,
        CancellationToken cancelamento) =>
        EntrarAsync("api/autenticacao/login", organizacao, email, senha, cancelamento);

    /// <summary>Entra no canal do motorista.</summary>
    public Task<SessaoDoSimulador> EntrarComoMotoristaAsync(
        string organizacao,
        string email,
        string senha,
        CancellationToken cancelamento) =>
        EntrarAsync("api/motorista/autenticacao/login", organizacao, email, senha, cancelamento);

    /// <summary>Cria uma conta de usuário na organização da sessão.</summary>
    public Task<Guid> CriarUsuarioAsync(string token, string nome, string email, string senha, string perfil, CancellationToken cancelamento) =>
        CriarAsync(token, "api/usuarios", new { nome, email, senha, perfil }, cancelamento);

    /// <summary>Cadastra um cliente.</summary>
    public Task<Guid> CriarClienteAsync(string token, object corpo, CancellationToken cancelamento) =>
        CriarAsync(token, "api/clientes", corpo, cancelamento);

    /// <summary>Cadastra um destinatário.</summary>
    public Task<Guid> CriarDestinatarioAsync(string token, object corpo, CancellationToken cancelamento) =>
        CriarAsync(token, "api/destinatarios", corpo, cancelamento);

    /// <summary>Cadastra um motorista.</summary>
    public Task<Guid> CriarMotoristaAsync(string token, object corpo, CancellationToken cancelamento) =>
        CriarAsync(token, "api/motoristas", corpo, cancelamento);

    /// <summary>Cadastra um veículo.</summary>
    public Task<Guid> CriarVeiculoAsync(string token, object corpo, CancellationToken cancelamento) =>
        CriarAsync(token, "api/veiculos", corpo, cancelamento);

    /// <summary>Vincula uma conta a um cadastro de motorista.</summary>
    public Task<JsonElement> VincularContaDoMotoristaAsync(string token, Guid motoristaId, Guid usuarioId, CancellationToken cancelamento) =>
        EnviarAsync(HttpMethod.Put, $"api/motoristas/{motoristaId}/conta", token, new { usuarioId }, cancelamento);

    /// <summary>Cria uma entrega.</summary>
    public Task<Guid> CriarEntregaAsync(string token, object corpo, CancellationToken cancelamento) =>
        CriarAsync(token, "api/entregas", corpo, cancelamento);

    /// <summary>Cria uma rota.</summary>
    public Task<Guid> CriarRotaAsync(string token, DateOnly data, CancellationToken cancelamento) =>
        CriarAsync(token, "api/rotas", new { data = data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }, cancelamento);

    /// <summary>Acrescenta paradas à rota.</summary>
    public Task<JsonElement> AdicionarParadasAsync(string token, Guid rotaId, IReadOnlyList<Guid> entregas, CancellationToken cancelamento) =>
        EnviarAsync(HttpMethod.Post, $"api/rotas/{rotaId}/paradas", token, new { entregas }, cancelamento);

    /// <summary>Atribui o motorista à rota.</summary>
    public Task<JsonElement> AtribuirMotoristaAsync(string token, Guid rotaId, Guid motoristaId, CancellationToken cancelamento) =>
        EnviarAsync(HttpMethod.Put, $"api/rotas/{rotaId}/motorista", token, new { motoristaId }, cancelamento);

    /// <summary>Atribui o veículo à rota.</summary>
    public Task<JsonElement> AtribuirVeiculoAsync(string token, Guid rotaId, Guid veiculoId, CancellationToken cancelamento) =>
        EnviarAsync(HttpMethod.Put, $"api/rotas/{rotaId}/veiculo", token, new { veiculoId }, cancelamento);

    /// <summary>Define a saída planejada.</summary>
    public Task<JsonElement> DefinirSaidaAsync(string token, Guid rotaId, DateTimeOffset saidaPlanejada, CancellationToken cancelamento) =>
        EnviarAsync(HttpMethod.Put, $"api/rotas/{rotaId}/saida", token, new { saidaPlanejada }, cancelamento);

    /// <summary>Planeja a rota.</summary>
    public Task<JsonElement> PlanejarRotaAsync(string token, Guid rotaId, CancellationToken cancelamento) =>
        EnviarAsync(HttpMethod.Post, $"api/rotas/{rotaId}/planejamento", token, corpo: null, cancelamento);

    /// <summary>Cancela a rota.</summary>
    public Task<JsonElement> CancelarRotaAsync(string token, Guid rotaId, string motivo, CancellationToken cancelamento) =>
        EnviarAsync(HttpMethod.Post, $"api/rotas/{rotaId}/cancelamento", token, new { motivo }, cancelamento);

    /// <summary>Cancela a entrega.</summary>
    public Task<JsonElement> CancelarEntregaAsync(string token, Guid entregaId, string motivo, CancellationToken cancelamento) =>
        EnviarAsync(HttpMethod.Post, $"api/entregas/{entregaId}/cancelamento", token, new { motivo }, cancelamento);

    /// <summary>Motorista inicia a rota.</summary>
    public Task<JsonElement> IniciarRotaAsync(string token, Guid rotaId, CancellationToken cancelamento) =>
        EnviarAsync(HttpMethod.Post, $"api/motorista/rotas/{rotaId}/inicio", token, corpo: null, cancelamento);

    /// <summary>Motorista envia posições.</summary>
    public Task<JsonElement> EnviarPosicoesAsync(string token, object posicoes, CancellationToken cancelamento) =>
        EnviarAsync(HttpMethod.Post, "api/motorista/posicoes", token, posicoes, cancelamento);

    /// <summary>Motorista registra chegada.</summary>
    public Task<JsonElement> RegistrarChegadaAsync(string token, Guid entregaId, CancellationToken cancelamento) =>
        EnviarAsync(HttpMethod.Post, $"api/motorista/entregas/{entregaId}/chegada", token, corpo: null, cancelamento);

    /// <summary>Motorista conclui a entrega.</summary>
    public Task<JsonElement> ConcluirEntregaAsync(string token, Guid entregaId, CancellationToken cancelamento) =>
        EnviarAsync(HttpMethod.Post, $"api/motorista/entregas/{entregaId}/conclusao", token, corpo: null, cancelamento);

    /// <summary>Motorista conclui com comprovante.</summary>
    public Task<JsonElement> ConcluirComComprovanteAsync(string token, Guid entregaId, object corpo, CancellationToken cancelamento) =>
        EnviarAsync(HttpMethod.Post, $"api/motorista/entregas/{entregaId}/comprovante", token, corpo, cancelamento);

    /// <summary>Motorista registra tentativa frustrada.</summary>
    public Task<JsonElement> RegistrarTentativaFrustradaAsync(
        string token,
        Guid entregaId,
        string motivo,
        string? observacao,
        CancellationToken cancelamento) =>
        EnviarAsync(
            HttpMethod.Post, $"api/motorista/entregas/{entregaId}/tentativa-frustrada", token, new { motivo, observacao }, cancelamento);

    /// <summary>Consulta uma entrega.</summary>
    public Task<JsonElement> ConsultarEntregaAsync(string token, Guid entregaId, CancellationToken cancelamento) =>
        EnviarAsync(HttpMethod.Get, $"api/entregas/{entregaId}", token, corpo: null, cancelamento);

    /// <summary>Consulta a linha do tempo de uma entrega.</summary>
    public Task<JsonElement> ConsultarEventosDaEntregaAsync(string token, Guid entregaId, CancellationToken cancelamento) =>
        EnviarAsync(HttpMethod.Get, $"api/entregas/{entregaId}/eventos", token, corpo: null, cancelamento);

    /// <summary>Consulta a previsão de chegada.</summary>
    public Task<JsonElement> ConsultarPrevisaoAsync(string token, Guid entregaId, CancellationToken cancelamento) =>
        EnviarAsync(HttpMethod.Get, $"api/entregas/{entregaId}/previsao", token, corpo: null, cancelamento);

    /// <summary>Consulta os alertas abertos.</summary>
    public Task<JsonElement> ConsultarAlertasAsync(string token, CancellationToken cancelamento) =>
        EnviarAsync(HttpMethod.Get, "api/alertas?estado=Aberto&tamanhoDaPagina=50", token, corpo: null, cancelamento);

    /// <summary>Emite o link público de rastreamento.</summary>
    public Task<JsonElement> EmitirLinkDeRastreamentoAsync(string token, Guid entregaId, CancellationToken cancelamento) =>
        EnviarAsync(HttpMethod.Post, $"api/entregas/{entregaId}/link-de-rastreamento", token, corpo: null, cancelamento);

    private async Task<SessaoDoSimulador> EntrarAsync(
        string caminho,
        string organizacao,
        string email,
        string senha,
        CancellationToken cancelamento)
    {
        var json = await EnviarAsync(HttpMethod.Post, caminho, token: null, new { organizacao, email, senha }, cancelamento)
            .ConfigureAwait(false);

        return new SessaoDoSimulador(json.GetProperty("tokenDeAcesso").GetString()!);
    }

    private async Task<Guid> CriarAsync(string token, string caminho, object corpo, CancellationToken cancelamento)
    {
        var json = await EnviarAsync(HttpMethod.Post, caminho, token, corpo, cancelamento).ConfigureAwait(false);
        return json.GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> EnviarAsync(
        HttpMethod metodo,
        string caminho,
        string? token,
        object? corpo,
        CancellationToken cancelamento)
    {
        using var requisicao = new HttpRequestMessage(metodo, new Uri(caminho, UriKind.Relative));

        if (token is not null)
        {
            requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (!string.IsNullOrWhiteSpace(origemDeclarada))
        {
            requisicao.Headers.Add("Origin", origemDeclarada);
        }

        if (corpo is not null)
        {
            requisicao.Content = JsonContent.Create(corpo, options: Json);
        }

        using var resposta = await http.SendAsync(requisicao, cancelamento).ConfigureAwait(false);
        var texto = await resposta.Content.ReadAsStringAsync(cancelamento).ConfigureAwait(false);

        if (!resposta.IsSuccessStatusCode)
        {
            throw new ErroDaApiDaTorre(metodo, caminho, resposta.StatusCode, texto);
        }

        if (string.IsNullOrWhiteSpace(texto))
        {
            return default;
        }

        using var documento = JsonDocument.Parse(texto);
        return documento.RootElement.Clone();
    }
}
