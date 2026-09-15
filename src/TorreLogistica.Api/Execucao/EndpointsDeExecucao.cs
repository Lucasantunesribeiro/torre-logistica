using System.ComponentModel.DataAnnotations;
using TorreLogistica.Api.Autenticacao;
using TorreLogistica.Application.Execucao;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Sincronizacao;

namespace TorreLogistica.Api.Execucao;

/// <summary>Tentativa de entrega sem sucesso.</summary>
public sealed record RequisicaoDeTentativaFrustrada(
    [property: Required(ErrorMessage = "Informe o motivo.")] MotivoDeTentativaFrustrada? Motivo);

/// <summary>Uma operação feita no aparelho. Campos anuláveis para recusar item a item.</summary>
public sealed record RequisicaoDeOperacao(
    Guid? OperacaoDoClienteId,
    TipoDeOperacaoDoCliente? Tipo,
    Guid? AlvoId,
    MotivoDeTentativaFrustrada? Motivo,
    DateTimeOffset? CriadaEm);

/// <summary>Lote de operações feitas no aparelho, na ordem em que aconteceram.</summary>
public sealed record RequisicaoDeSincronizacao(
    [property: Required(ErrorMessage = "Informe as operações.")]
    [property: MinLength(1, ErrorMessage = "Informe ao menos uma operação.")]
    [property: MaxLength(PoliticaDeOperacaoDoCliente.TamanhoMaximoDoLote, ErrorMessage = "Lote maior que o permitido.")]
    RequisicaoDeOperacao[]? Operacoes);

/// <summary>
/// Comandos de execução do motorista, pelo canal da PWA.
/// </summary>
/// <remarks>
/// <para>
/// Cada mudança de status tem a própria rota com o nome do que aconteceu — saída, chegada,
/// conclusão, tentativa sem sucesso. Não existe rota que receba um status: o motorista informa o
/// fato, e a máquina de estados decide se ele é possível.
/// </para>
/// <para>
/// Só a sessão de motorista entra aqui; token do console não é credencial neste canal (401).
/// </para>
/// </remarks>
public static class EndpointsDeExecucao
{
    /// <summary>Registra os endpoints.</summary>
    public static IEndpointRouteBuilder MapearEndpointsDeExecucao(this IEndpointRouteBuilder rotas)
    {
        ArgumentNullException.ThrowIfNull(rotas);

        var grupo = rotas.MapGroup("/api/motorista").WithTags("Execução pelo motorista");

        // Leitura do aplicativo: só o que é do motorista da sessão, com modelo próprio e dados mínimos.
        grupo.MapGet("/rotas", (ConsultaDoMotorista consulta, CancellationToken cancelamento) =>
                consulta.ListarRotasAsync(cancelamento))
            .RequireAuthorization(Politicas.Motorista);

        grupo.MapGet("/rotas/{id:guid}", (Guid id, ConsultaDoMotorista consulta, CancellationToken cancelamento) =>
                consulta.ObterRotaAsync(id, cancelamento))
            .RequireAuthorization(Politicas.Motorista);

        grupo.MapGet("/entregas/{id:guid}", (Guid id, ConsultaDoMotorista consulta, CancellationToken cancelamento) =>
                consulta.ObterEntregaAsync(id, cancelamento))
            .RequireAuthorization(Politicas.Motorista);

        grupo.MapPost("/rotas/{id:guid}/inicio", (Guid id, ExecucaoPeloMotorista execucao, CancellationToken cancelamento) =>
                execucao.IniciarRotaAsync(id, cancelamento))
            .RequireAuthorization(Politicas.Motorista);

        grupo.MapPost("/rotas/{id:guid}/conclusao", (Guid id, ExecucaoPeloMotorista execucao, CancellationToken cancelamento) =>
                execucao.ConcluirRotaAsync(id, cancelamento))
            .RequireAuthorization(Politicas.Motorista);

        grupo.MapPost("/entregas/{id:guid}/chegada", (Guid id, ExecucaoPeloMotorista execucao, CancellationToken cancelamento) =>
                execucao.RegistrarChegadaAsync(id, cancelamento))
            .RequireAuthorization(Politicas.Motorista);

        grupo.MapPost("/entregas/{id:guid}/conclusao", (Guid id, ExecucaoPeloMotorista execucao, CancellationToken cancelamento) =>
                execucao.ConcluirEntregaAsync(id, cancelamento))
            .RequireAuthorization(Politicas.Motorista);

        grupo.MapPost("/entregas/{id:guid}/tentativa-frustrada", (Guid id, RequisicaoDeTentativaFrustrada requisicao, ExecucaoPeloMotorista execucao, CancellationToken cancelamento) =>
                execucao.RegistrarTentativaFrustradaAsync(id, requisicao.Motivo!.Value, cancelamento))
            .RequireAuthorization(Politicas.Motorista)
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDeTentativaFrustrada>>();

        // Caminho do aplicativo para toda ação crítica: cada operação com o identificador do aparelho,
        // aplicada exatamente uma vez. Responde 200 com o desfecho de cada operação, na ordem do envio.
        grupo.MapPost("/sincronizacao", (RequisicaoDeSincronizacao requisicao, SincronizacaoDoMotorista sincronizacao, CancellationToken cancelamento) =>
                sincronizacao.ProcessarAsync(
                    [.. requisicao.Operacoes!.Select(operacao => new OperacaoEnviada(
                        operacao.OperacaoDoClienteId, operacao.Tipo, operacao.AlvoId, operacao.Motivo, operacao.CriadaEm))],
                    cancelamento))
            .RequireAuthorization(Politicas.Motorista)
            .RequireRateLimiting(PoliticasDeLimite.Sincronizacao)
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDeSincronizacao>>();

        return rotas;
    }
}
