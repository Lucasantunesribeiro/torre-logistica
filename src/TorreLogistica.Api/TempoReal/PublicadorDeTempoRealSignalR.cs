using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using TorreLogistica.Api.Autenticacao;
using TorreLogistica.Application.Abstracoes.TempoReal;

namespace TorreLogistica.Api.TempoReal;

/// <summary>
/// Publicação de avisos pelo SignalR, sempre para o grupo da organização do aviso.
/// </summary>
/// <remarks>
/// Melhor esforço: falha ao enviar é registrada e não volta para a operação — o banco já confirmou a
/// mudança, e o cliente recupera o estado pela API (ADR 0005). O aviso não carrega endereço, nome nem
/// dado de destinatário; quem precisar do detalhe consulta a API com a própria autorização.
/// </remarks>
public sealed class PublicadorDeTempoRealSignalR(
    IHubContext<HubDaOperacao> hub,
    RegistroDeConexoesDaOperacao registro,
    ILogger<PublicadorDeTempoRealSignalR> log) : IPublicadorDeTempoReal
{
    /// <inheritdoc />
    public async Task PublicarAsync(IReadOnlyList<NotificacaoDaOperacao> notificacoes, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(notificacoes);

        foreach (var notificacao in notificacoes)
        {
            var (evento, carga) = notificacao switch
            {
                PosicaoDoMotoristaAtualizada posicao => (EventosDeTempoReal.PosicaoDoMotoristaAtualizada, (object)posicao),
                StatusDaEntregaAlterado status => (EventosDeTempoReal.StatusDaEntregaAlterado, status),
                RiscoDaEntregaAlterado risco => (EventosDeTempoReal.RiscoDaEntregaAlterado, risco),
                _ => throw new ArgumentOutOfRangeException(nameof(notificacoes), notificacao.GetType().Name, "Aviso de tempo real desconhecido."),
            };

            try
            {
                await hub.Clients
                    .Group(HubDaOperacao.GrupoDaOrganizacao(notificacao.OrganizacaoId))
                    .SendAsync(evento, carga, cancelamento)
                    .ConfigureAwait(false);
            }
            catch (Exception excecao) when (excecao is not OperationCanceledException)
            {
                log.LogWarning(excecao, "Aviso de tempo real {Evento} não enviado à organização {OrganizacaoId}.", evento, notificacao.OrganizacaoId);
            }
        }
    }

    /// <inheritdoc />
    public Task EncerrarConexoesDasSessoesAsync(IReadOnlyCollection<Guid> sessoes, CancellationToken cancelamento)
    {
        var encerradas = registro.EncerrarSessoes(sessoes);
        if (encerradas > 0)
        {
            log.LogInformation("{Quantidade} conexão(ões) de tempo real encerrada(s) por sessão revogada.", encerradas);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Registro e mapeamento do tempo real da operação.</summary>
public static class ConfiguracaoDeTempoReal
{
    /// <summary>Registra SignalR, publicador e registro de conexões.</summary>
    public static IServiceCollection AdicionarTempoRealDaOperacao(this IServiceCollection servicos)
    {
        ArgumentNullException.ThrowIfNull(servicos);

        servicos
            .AddSignalR(opcoes =>
            {
                // Mensagem do cliente não é esperada: o hub não tem métodos. Limite baixo contra abuso.
                opcoes.MaximumReceiveMessageSize = 4 * 1024;
                opcoes.EnableDetailedErrors = false;
            })
            .AddJsonProtocol(json => json.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        servicos.AddSingleton<RegistroDeConexoesDaOperacao>();
        servicos.AddSingleton<IPublicadorDeTempoReal, PublicadorDeTempoRealSignalR>();

        return servicos;
    }

    /// <summary>Mapeia o hub do console, com a política do console.</summary>
    public static IEndpointRouteBuilder MapearTempoRealDaOperacao(this IEndpointRouteBuilder rotas)
    {
        ArgumentNullException.ThrowIfNull(rotas);

        rotas.MapHub<HubDaOperacao>(HubDaOperacao.Caminho, opcoes =>
            {
                // Conexão aberta não sobrevive ao token: fecha no vencimento do token de acesso.
                opcoes.CloseOnAuthenticationExpiration = true;
                opcoes.Transports = HttpTransportType.WebSockets | HttpTransportType.ServerSentEvents | HttpTransportType.LongPolling;
            })
            .RequireAuthorization(Politicas.Console);

        return rotas;
    }
}
