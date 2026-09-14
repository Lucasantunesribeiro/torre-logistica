using System.ComponentModel.DataAnnotations;
using TorreLogistica.Api.Autenticacao;
using TorreLogistica.Application.Execucao;
using TorreLogistica.Domain.Entregas;

namespace TorreLogistica.Api.Execucao;

/// <summary>Tentativa de entrega sem sucesso.</summary>
public sealed record RequisicaoDeTentativaFrustrada(
    [property: Required(ErrorMessage = "Informe o motivo.")] MotivoDeTentativaFrustrada? Motivo);

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

        return rotas;
    }
}
