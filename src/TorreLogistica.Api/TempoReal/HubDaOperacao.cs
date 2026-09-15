using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using TorreLogistica.Api.Autenticacao;

namespace TorreLogistica.Api.TempoReal;

/// <summary>Nomes dos avisos de tempo real no fio, como o ROADMAP os define.</summary>
public static class EventosDeTempoReal
{
    /// <summary>Posição atual de motorista avançou.</summary>
    public const string PosicaoDoMotoristaAtualizada = "DriverPositionUpdated";

    /// <summary>Status de entrega mudou.</summary>
    public const string StatusDaEntregaAlterado = "DeliveryStatusChanged";

    /// <summary>Risco de entrega mudou — produzido a partir da Fase 9 (ETA e SLA).</summary>
    public const string RiscoDaEntregaAlterado = "DeliveryRiskChanged";

    /// <summary>Alerta criado — produzido a partir da Fase 10.</summary>
    public const string AlertaCriado = "AlertCreated";

    /// <summary>Ocorrência criada — produzida a partir da fase de ocorrências.</summary>
    public const string OcorrenciaCriada = "IncidentCreated";
}

/// <summary>
/// Canal de tempo real do console operacional.
/// </summary>
/// <remarks>
/// <para>
/// Só servidor → cliente: o hub não tem método que o cliente possa chamar, e por isso nenhum cliente
/// escolhe grupo. A organização vem da sessão autenticada, na conexão, e define o único grupo em que
/// a conexão entra.
/// </para>
/// <para>
/// A política é a do console: o token do motorista não autentica aqui, e sem sessão não há conexão.
/// O rastreamento público nunca usa este canal.
/// </para>
/// </remarks>
public sealed class HubDaOperacao(RegistroDeConexoesDaOperacao registro, ILogger<HubDaOperacao> log) : Hub
{
    /// <summary>Caminho do hub.</summary>
    public const string Caminho = "/tempo-real/operacao";

    /// <summary>Grupo da organização.</summary>
    public static string GrupoDaOrganizacao(Guid organizacaoId) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"organizacao:{organizacaoId:N}");

    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        var principal = Context.User;

        if (principal is null
            || !Guid.TryParse(principal.FindFirstValue(ReivindicacoesDaTorre.Organizacao), out var organizacaoId)
            || !Guid.TryParse(principal.FindFirstValue(ReivindicacoesDaTorre.Sessao), out var sessaoId))
        {
            // A política do endpoint já exige sessão; chegar aqui sem as reivindicações é defeito — falha fechada.
            log.LogWarning("Conexão de tempo real sem organização ou sessão na identidade; encerrada.");
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GrupoDaOrganizacao(organizacaoId)).ConfigureAwait(false);
        registro.Registrar(Context, sessaoId);

        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        registro.Remover(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}

/// <summary>
/// Conexões abertas do console, por sessão — para derrubá-las quando a sessão é revogada.
/// </summary>
/// <remarks>
/// Estado em memória da instância. Com mais de uma instância, cada uma derruba as próprias conexões
/// ao receber o aviso — o que exige backplane, decisão adiada no ADR 0005.
/// </remarks>
public sealed class RegistroDeConexoesDaOperacao : IDisposable
{
    private readonly ConcurrentDictionary<string, (HubCallerContext Contexto, Guid SessaoId)> _conexoes = new(StringComparer.Ordinal);
    private readonly Meter _medidor = new("TorreLogistica.TempoReal");

    /// <summary>Cria o registro e o medidor de clientes conectados.</summary>
    public RegistroDeConexoesDaOperacao() =>
        _medidor.CreateObservableGauge("signalr.connected_clients", () => _conexoes.Count, "{conexao}", "Conexões abertas no canal da operação.");

    /// <summary>Conexões abertas.</summary>
    public int Quantidade => _conexoes.Count;

    /// <summary>Registra uma conexão aberta.</summary>
    public void Registrar(HubCallerContext contexto, Guid sessaoId)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        _conexoes[contexto.ConnectionId] = (contexto, sessaoId);
    }

    /// <summary>Esquece uma conexão fechada.</summary>
    public void Remover(string conexaoId) => _conexoes.TryRemove(conexaoId, out _);

    /// <summary>Derruba as conexões das sessões informadas.</summary>
    /// <returns>Quantas conexões foram derrubadas.</returns>
    public int EncerrarSessoes(IReadOnlyCollection<Guid> sessoes)
    {
        ArgumentNullException.ThrowIfNull(sessoes);

        var encerradas = 0;
        foreach (var (conexaoId, (contexto, sessaoId)) in _conexoes)
        {
            if (sessoes.Contains(sessaoId) && _conexoes.TryRemove(conexaoId, out _))
            {
                contexto.Abort();
                encerradas++;
            }
        }

        return encerradas;
    }

    /// <summary>Derruba uma conexão — o equivalente, no servidor, a uma queda de rede.</summary>
    public bool EncerrarConexao(string conexaoId)
    {
        if (!_conexoes.TryRemove(conexaoId, out var conexao))
        {
            return false;
        }

        conexao.Contexto.Abort();
        return true;
    }

    /// <inheritdoc />
    public void Dispose() => _medidor.Dispose();
}
