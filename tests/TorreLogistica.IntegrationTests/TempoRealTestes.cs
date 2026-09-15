using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TorreLogistica.Api.TempoReal;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// Canal de tempo real do console com cliente SignalR real, por WebSocket, contra a API e o
/// PostgreSQL reais.
/// </summary>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class TempoRealTestes(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    private const double LatitudeDoDestino = -22.9100;
    private const double LongitudeDoDestino = -47.0650;

    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Silencio = TimeSpan.FromSeconds(2);

    /// <summary>Critério de aceite da Fase 8.</summary>
    [Fact]
    public async Task PosicaoValidaApareceNoClienteOperacionalConectadoSemRecarregar()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        await using var console = await ConectarAsync(cenario.Operador);
        var capturada = DateTimeOffset.UtcNow.AddSeconds(-5);

        await EnviarPosicoesAsync(http, cenario, PosicoesTestes.Posicao(10, capturada, latitude: -22.9500, longitude: -47.1000));

        var aviso = await console.EsperarAsync(
            EventosDeTempoReal.PosicaoDoMotoristaAtualizada,
            carga => carga.GetProperty("motoristaId").GetGuid() == cenario.Motorista);
        Assert.Equal(-22.9500, aviso.GetProperty("latitude").GetDouble(), 6);
        Assert.Equal(-47.1000, aviso.GetProperty("longitude").GetDouble(), 6);
        Assert.Equal(10, aviso.GetProperty("sequencia").GetInt64());
        Assert.Equal(cenario.Rota, aviso.GetProperty("rotaId").GetGuid());
        Assert.True((aviso.GetProperty("capturadaEm").GetDateTimeOffset() - capturada).Duration() < TimeSpan.FromMilliseconds(1));

        // Posição atrasada e posição recusada não movem a posição atual — logo, não avisam.
        await EnviarPosicoesAsync(http, cenario, PosicoesTestes.Posicao(9, capturada.AddSeconds(-30)));
        await EnviarPosicoesAsync(http, cenario, PosicoesTestes.Posicao(11, DateTimeOffset.UtcNow.AddMinutes(10)));
        await Task.Delay(Silencio, Cancelamento);

        Assert.Equal(1, console.Contar(EventosDeTempoReal.PosicaoDoMotoristaAtualizada));
    }

    [Fact]
    public async Task MudancaDeStatusChegaComOEventoCertoEOQueNaoMudaStatusNaoAvisa()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http, iniciarRota: false);
        await using var console = await ConectarAsync(cenario.Supervisor);

        await OkAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{cenario.Rota}/inicio", cenario.TokenDoMotorista);
        var saida = await console.EsperarAsync(EventosDeTempoReal.StatusDaEntregaAlterado, carga => carga.GetProperty("entregaId").GetGuid() == cenario.Entrega);
        Assert.Equal("EmRota", saida.GetProperty("status").GetString());
        Assert.Equal("SaiuParaRota", saida.GetProperty("evento").GetString());

        // Alteração de dados e comando recusado não mudam status.
        var atual = await OkAsync(http, HttpMethod.Get, $"/api/entregas/{cenario.Entrega}", cenario.Operador);
        await OkAsync(http, HttpMethod.Put, $"/api/entregas/{cenario.Entrega}", cenario.Operador, new
        {
            versao = atual.GetProperty("versao").GetUInt32(),
            clienteId = atual.GetProperty("clienteId").GetGuid(),
            destinatarioId = atual.GetProperty("destinatarioId").GetGuid(),
            endereco = atual.GetProperty("endereco"),
            latitude = atual.GetProperty("localizacao").GetProperty("latitude").GetDouble(),
            longitude = atual.GetProperty("localizacao").GetProperty("longitude").GetDouble(),
            prometidaDe = atual.GetProperty("janelaPrometida").GetProperty("de").GetDateTimeOffset(),
            prometidaAte = atual.GetProperty("janelaPrometida").GetProperty("ate").GetDateTimeOffset(),
            observacoes = "Portão azul",
        });
        using var cancelamento = await EnviarAsync(
            http, HttpMethod.Post, $"/api/entregas/{cenario.Entrega}/cancelamento", cenario.Operador, new { motivo = "SolicitacaoDoCliente" });
        Assert.Equal(HttpStatusCode.Conflict, cancelamento.StatusCode);

        // Entrada na geofence do destino: posição e mudança de status, os dois avisos.
        await EnviarPosicoesAsync(http, cenario, PosicoesTestes.Posicao(1, DateTimeOffset.UtcNow.AddSeconds(-2), latitude: LatitudeDoDestino + 0.00045, longitude: LongitudeDoDestino));
        var proximidade = await console.EsperarAsync(
            EventosDeTempoReal.StatusDaEntregaAlterado,
            carga => carga.GetProperty("evento").GetString() == "ProximidadeDetectada");
        Assert.Equal("ProximaDoDestino", proximidade.GetProperty("status").GetString());
        await console.EsperarAsync(EventosDeTempoReal.PosicaoDoMotoristaAtualizada, _ => true);

        await Task.Delay(Silencio, Cancelamento);
        Assert.Equal(2, console.Contar(EventosDeTempoReal.StatusDaEntregaAlterado));
    }

    [Fact]
    public async Task ConexaoExigeSessaoDoConsoleEOHubNaoAceitaComando()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http, iniciarRota: false);

        // Recusa vista na negociação HTTP: sem token, token de motorista e token adulterado.
        foreach (var token in new[] { null, cenario.TokenDoMotorista, cenario.Supervisor + "x" })
        {
            var erro = await Assert.ThrowsAsync<HttpRequestException>(async () =>
            {
                await using var cliente = await ConectarPorLongPollingAsync(token);
            });
            Assert.Equal(HttpStatusCode.Unauthorized, erro.StatusCode);
        }

        // Navegador: o token vai na query string, e só no hub vale.
        await using var peloNavegador = await ConectarAsync(cenario.Supervisor, pelaQueryString: true);
        Assert.Equal(HubConnectionState.Connected, peloNavegador.Conexao.State);

        using var tokenNaUrlDaApi = await EnviarAsync(
            http, HttpMethod.Get, $"/api/organizacao?access_token={Uri.EscapeDataString(cenario.Supervisor)}", tokenDeAcesso: null);
        Assert.Equal(HttpStatusCode.Unauthorized, tokenNaUrlDaApi.StatusCode);

        // Nenhum método do hub: o cliente não escolhe grupo nem organização.
        await Assert.ThrowsAsync<HubException>(() =>
            peloNavegador.Conexao.InvokeAsync("EntrarNoGrupo", "organizacao:qualquer", Cancelamento));

        Assert.DoesNotContain(cenario.Supervisor, Fabrica.Logs.TextoCompleto(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AvisoNaoVazaEntreOrganizacoes()
    {
        using var http = Cliente();
        var organizacaoA = await CenarioAsync(http);
        var organizacaoB = await CenarioAsync(http);
        await using var consoleA = await ConectarAsync(organizacaoA.Supervisor);
        await using var consoleB = await ConectarAsync(organizacaoB.Supervisor);

        await EnviarPosicoesAsync(http, organizacaoA, PosicoesTestes.Posicao(1, DateTimeOffset.UtcNow.AddSeconds(-3)));
        await consoleA.EsperarAsync(EventosDeTempoReal.PosicaoDoMotoristaAtualizada, carga => carga.GetProperty("motoristaId").GetGuid() == organizacaoA.Motorista);

        await EnviarPosicoesAsync(http, organizacaoB, PosicoesTestes.Posicao(1, DateTimeOffset.UtcNow.AddSeconds(-3)));
        await consoleB.EsperarAsync(EventosDeTempoReal.PosicaoDoMotoristaAtualizada, carga => carga.GetProperty("motoristaId").GetGuid() == organizacaoB.Motorista);

        await Task.Delay(Silencio, Cancelamento);

        Assert.Equal(1, consoleA.Contar(EventosDeTempoReal.PosicaoDoMotoristaAtualizada));
        Assert.Equal(1, consoleB.Contar(EventosDeTempoReal.PosicaoDoMotoristaAtualizada));
        Assert.All(consoleA.Todos(), item => Assert.Equal(organizacaoA.Organizacao, item.GetProperty("organizacaoId").GetGuid()));
        Assert.All(consoleB.Todos(), item => Assert.Equal(organizacaoB.Organizacao, item.GetProperty("organizacaoId").GetGuid()));
    }

    /// <summary>A rede cai do lado do cliente; ele reconecta sozinho e volta ao grupo da organização.</summary>
    [Fact]
    public async Task ReconexaoRetomaOCanalDaOrganizacao()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        await using var console = await ConectarAsync(cenario.Supervisor, reconectar: true);

        console.DerrubarRede();
        await console.EsperarReconexaoAsync();
        Assert.Equal(HubConnectionState.Connected, console.Conexao.State);

        // A conexão nova passou de novo pela autenticação e voltou ao grupo da organização.
        await EnviarPosicoesAsync(http, cenario, PosicoesTestes.Posicao(1, DateTimeOffset.UtcNow.AddSeconds(-3)));
        await console.EsperarAsync(EventosDeTempoReal.PosicaoDoMotoristaAtualizada, carga => carga.GetProperty("motoristaId").GetGuid() == cenario.Motorista);
    }

    /// <summary>Quem sai não continua recebendo dados da organização até o token vencer.</summary>
    [Fact]
    public async Task SessaoEncerradaDerrubaAConexaoEAReconexaoEhRecusada()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        var sessao = await EntrarAsync(http, cenario.ContaDoSupervisor);
        await using var console = await ConectarAsync(sessao.TokenDeAcesso, reconectar: true);

        using var saida = await SairAsync(http, sessao.CookieDeRenovacao);
        Assert.True(saida.IsSuccessStatusCode, $"logout: {(int)saida.StatusCode}");

        await console.EsperarFechamentoAsync();
        Assert.Equal(HubConnectionState.Disconnected, console.Conexao.State);

        await EnviarPosicoesAsync(http, cenario, PosicoesTestes.Posicao(1, DateTimeOffset.UtcNow.AddSeconds(-3)));
        await Task.Delay(Silencio, Cancelamento);
        Assert.Equal(0, console.Contar(EventosDeTempoReal.PosicaoDoMotoristaAtualizada));
    }

    private sealed record CenarioDeTempoReal(
        Guid Organizacao,
        ContaDeTeste ContaDoSupervisor,
        string Supervisor,
        string Operador,
        Guid Motorista,
        string TokenDoMotorista,
        Guid Entrega,
        Guid Rota);

    private async Task<CenarioDeTempoReal> CenarioAsync(HttpClient http, bool iniciarRota = true)
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Supervisor, Perfil.Operador, Perfil.Motorista);
        var supervisor = (await EntrarAsync(http, organizacao.Com(Perfil.Supervisor))).TokenDeAcesso;
        var operador = (await EntrarAsync(http, organizacao.Com(Perfil.Operador))).TokenDeAcesso;

        var clienteId = await CriarAsync(http, supervisor, "/api/clientes", RoteirosDeCadastro.Obter("cliente").Corpo());
        var destinatarioId = await CriarAsync(http, supervisor, "/api/destinatarios", new
        {
            nome = "Carla Nunes",
            endereco = RoteirosDeCadastro.Endereco(),
            latitude = LatitudeDoDestino,
            longitude = LongitudeDoDestino,
        });

        var motorista = await CriarAsync(http, supervisor, "/api/motoristas", RoteirosDeCadastro.Obter("motorista").Corpo());
        await OkAsync(http, HttpMethod.Put, $"/api/motoristas/{motorista}/conta", supervisor, new { usuarioId = organizacao.Com(Perfil.Motorista).Id });
        var tokenDoMotorista = (await EntrarAsync(http, organizacao.Com(Perfil.Motorista))).TokenDeAcesso;

        var entrega = await CriarAsync(http, operador, "/api/entregas", RoteirosDeEntrega.Corpo(clienteId, destinatarioId));
        var veiculo = await CriarAsync(http, supervisor, "/api/veiculos", RoteirosDeCadastro.Obter("veiculo").Corpo());
        var rota = await CriarAsync(http, supervisor, "/api/rotas", new { data = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1)) });
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/paradas", supervisor, new { entregas = new[] { entrega } });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/motorista", supervisor, new { motoristaId = motorista });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/veiculo", supervisor, new { veiculoId = veiculo });
        await OkAsync(http, HttpMethod.Put, $"/api/rotas/{rota}/saida", supervisor, new { saidaPlanejada = RoteirosDeEntrega.Amanha(8) });
        await OkAsync(http, HttpMethod.Post, $"/api/rotas/{rota}/planejamento", supervisor);

        if (iniciarRota)
        {
            await OkAsync(http, HttpMethod.Post, $"/api/motorista/rotas/{rota}/inicio", tokenDoMotorista);
        }

        return new CenarioDeTempoReal(
            organizacao.Id, organizacao.Com(Perfil.Supervisor), supervisor, operador, motorista, tokenDoMotorista, entrega, rota);
    }

    /// <summary>
    /// Conecta por WebSocket, como o navegador. O token vai no cabeçalho do handshake ou, no modo
    /// navegador, na query string.
    /// </summary>
    private async Task<ClienteDeTempoReal> ConectarAsync(string token, bool pelaQueryString = false, bool reconectar = false)
    {
        var caminho = pelaQueryString
            ? $"{HubDaOperacao.Caminho}?access_token={Uri.EscapeDataString(token)}"
            : HubDaOperacao.Caminho;

        ClienteDeTempoReal? cliente = null;

        var construtor = new HubConnectionBuilder()
            .WithUrl(new Uri(Fabrica.Server.BaseAddress, caminho), opcoes =>
            {
                opcoes.Transports = HttpTransportType.WebSockets;
                opcoes.SkipNegotiation = true;
                opcoes.WebSocketFactory = async (contexto, cancelamento) =>
                {
                    var fabricaDeSocket = Fabrica.Server.CreateWebSocketClient();
                    if (!pelaQueryString)
                    {
                        fabricaDeSocket.ConfigureRequest = requisicao => requisicao.Headers.Authorization = $"Bearer {token}";
                    }

                    var socket = await fabricaDeSocket.ConnectAsync(contexto.Uri, cancelamento);
                    cliente!.SocketAtual = socket;
                    return socket;
                };
            })
            .AddJsonProtocol(json => json.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        if (reconectar)
        {
            construtor = construtor.WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(500)]);
        }

        cliente = new ClienteDeTempoReal(construtor.Build());
        return await IniciarAsync(cliente);
    }

    /// <summary>Conecta por long polling: a recusa de autenticação chega como resposta HTTP 401.</summary>
    private async Task<ClienteDeTempoReal> ConectarPorLongPollingAsync(string? token)
    {
        var conexao = new HubConnectionBuilder()
            .WithUrl(new Uri(Fabrica.Server.BaseAddress, HubDaOperacao.Caminho), opcoes =>
            {
                opcoes.Transports = HttpTransportType.LongPolling;
                opcoes.HttpMessageHandlerFactory = _ => Fabrica.Server.CreateHandler();
                if (token is not null)
                {
                    opcoes.AccessTokenProvider = () => Task.FromResult<string?>(token);
                }
            })
            .Build();

        return await IniciarAsync(new ClienteDeTempoReal(conexao));
    }

    private static async Task<ClienteDeTempoReal> IniciarAsync(ClienteDeTempoReal cliente)
    {
        try
        {
            await cliente.Conexao.StartAsync(Cancelamento);
            return cliente;
        }
        catch
        {
            await cliente.DisposeAsync();
            throw;
        }
    }

    private static async Task EnviarPosicoesAsync(HttpClient http, CenarioDeTempoReal cenario, params object[] posicoes)
    {
        using var resposta = await EnviarAsync(http, HttpMethod.Post, "/api/motorista/posicoes", cenario.TokenDoMotorista, PosicoesTestes.Corpo(posicoes));
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    private static async Task<Guid> CriarAsync(HttpClient http, string token, string url, object corpo)
    {
        using var resposta = await EnviarAsync(http, HttpMethod.Post, url, token, corpo);
        var json = await JsonAsync(resposta);
        Assert.True(resposta.StatusCode == HttpStatusCode.Created, $"{url}: {json}");
        return json.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> OkAsync(HttpClient http, HttpMethod metodo, string url, string token, object? corpo = null)
    {
        using var resposta = await EnviarAsync(http, metodo, url, token, corpo);
        var json = await JsonAsync(resposta);
        Assert.True(resposta.StatusCode == HttpStatusCode.OK, $"{metodo} {url}: {(int)resposta.StatusCode} {json}");
        return json;
    }

    /// <summary>Cliente de tempo real que guarda o que recebe, para o teste esperar e contar.</summary>
    private sealed class ClienteDeTempoReal : IAsyncDisposable
    {
        private static readonly string[] EventosOuvidos =
            [EventosDeTempoReal.PosicaoDoMotoristaAtualizada, EventosDeTempoReal.StatusDaEntregaAlterado];

        private readonly ConcurrentQueue<(string Evento, JsonElement Carga)> _recebidos = new();
        private readonly TaskCompletionSource _reconectado = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _fechado = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ClienteDeTempoReal(HubConnection conexao)
        {
            Conexao = conexao;

            foreach (var evento in EventosOuvidos)
            {
                conexao.On<JsonElement>(evento, carga => _recebidos.Enqueue((evento, carga.Clone())));
            }

            conexao.Reconnected += _ =>
            {
                _reconectado.TrySetResult();
                return Task.CompletedTask;
            };
            conexao.Closed += _ =>
            {
                _fechado.TrySetResult();
                return Task.CompletedTask;
            };
        }

        public HubConnection Conexao { get; }

        public WebSocket? SocketAtual { get; set; }

        /// <summary>Queda de rede do lado do cliente: o socket morre sem fechamento combinado.</summary>
        public void DerrubarRede() => (SocketAtual ?? throw new InvalidOperationException("Conexão sem WebSocket.")).Abort();

        public async Task<JsonElement> EsperarAsync(string evento, Func<JsonElement, bool> filtro)
        {
            var limite = DateTime.UtcNow + Espera;
            while (DateTime.UtcNow < limite)
            {
                foreach (var (nome, carga) in _recebidos)
                {
                    if (nome == evento && filtro(carga))
                    {
                        return carga;
                    }
                }

                await Task.Delay(50, TestContext.Current.CancellationToken);
            }

            throw new TimeoutException($"O aviso {evento} não chegou em {Espera.TotalSeconds} s.");
        }

        public int Contar(string evento) => _recebidos.Count(item => item.Evento == evento);

        public IEnumerable<JsonElement> Todos() => _recebidos.Select(item => item.Carga);

        public Task EsperarReconexaoAsync() => _reconectado.Task.WaitAsync(Espera, TestContext.Current.CancellationToken);

        public Task EsperarFechamentoAsync() => _fechado.Task.WaitAsync(Espera, TestContext.Current.CancellationToken);

        public ValueTask DisposeAsync() => Conexao.DisposeAsync();
    }
}
