using System.ComponentModel.DataAnnotations;
using TorreLogistica.Api.Autenticacao;
using TorreLogistica.Application.Rastreamento;
using TorreLogistica.Domain.Rastreamento;

namespace TorreLogistica.Api.Rastreamento;

/// <summary>
/// Uma posição enviada. Os campos são anuláveis para que um item incompleto seja recusado sozinho,
/// com motivo, em vez de derrubar o lote inteiro.
/// </summary>
public sealed record RequisicaoDePosicao(
    Guid? EventoDeLocalizacaoId,
    double? Latitude,
    double? Longitude,
    double? PrecisaoEmMetros,
    DateTimeOffset? CapturadaEm,
    long? Sequencia,
    double? VelocidadeEmMetrosPorSegundo,
    double? DirecaoEmGraus);

/// <summary>Envio de posições: uma, ou um lote de recuperação offline.</summary>
public sealed record RequisicaoDePosicoes(
    [property: Required(ErrorMessage = "Informe as posições.")]
    [property: MinLength(1, ErrorMessage = "Informe ao menos uma posição.")]
    [property: MaxLength(PoliticaDeLocalizacao.TamanhoMaximoDoLote, ErrorMessage = "Lote maior que o permitido.")]
    RequisicaoDePosicao[]? Posicoes);

/// <summary>
/// Endpoints de rastreamento: ingestão pelo motorista e consulta pelo console.
/// </summary>
/// <remarks>
/// <para>
/// A ingestão não recebe motorista nem organização: os dois vêm da sessão. Campo desconhecido no
/// corpo — inclusive <c>motoristaId</c> — responde 400.
/// </para>
/// <para>
/// O lote responde 200 com o resultado de cada posição, na ordem do envio. Recusa de item não é
/// erro do lote: é informação para o aplicativo descartar o que não adianta reenviar.
/// </para>
/// </remarks>
public static class EndpointsDeRastreamento
{
    /// <summary>Registra os endpoints.</summary>
    public static IEndpointRouteBuilder MapearEndpointsDeRastreamento(this IEndpointRouteBuilder rotas)
    {
        ArgumentNullException.ThrowIfNull(rotas);

        rotas.MapPost("/api/motorista/posicoes", (RequisicaoDePosicoes requisicao, IngestaoDeLocalizacao ingestao, CancellationToken cancelamento) =>
                ingestao.ReceberAsync(
                    [.. requisicao.Posicoes!.Select(posicao => new PosicaoEnviada(
                        posicao.EventoDeLocalizacaoId,
                        posicao.Latitude,
                        posicao.Longitude,
                        posicao.PrecisaoEmMetros,
                        posicao.CapturadaEm,
                        posicao.Sequencia,
                        posicao.VelocidadeEmMetrosPorSegundo,
                        posicao.DirecaoEmGraus))],
                    cancelamento))
            .WithTags("Rastreamento")
            .RequireAuthorization(Politicas.Motorista)
            .RequireRateLimiting(PoliticasDeLimite.Telemetria)
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDePosicoes>>();

        var motoristas = rotas.MapGroup("/api/motoristas").WithTags("Rastreamento");

        motoristas.MapGet("/{id:guid}/posicao-atual", (Guid id, ConsultaDeLocalizacao consulta, CancellationToken cancelamento) =>
                consulta.ObterPosicaoAtualAsync(id, cancelamento))
            .RequireAuthorization(Politicas.LeituraDaOperacao);

        // Histórico de localização é dado pessoal volumoso: só gestão, por período limitado.
        motoristas.MapGet("/{id:guid}/posicoes", async (Guid id, DateTimeOffset? de, DateTimeOffset? ate, ConsultaDeLocalizacao consulta, CancellationToken cancelamento) =>
                de is { } inicio && ate is { } fim && fim > inicio && fim - inicio <= ConsultaDeLocalizacao.PeriodoMaximoDoHistorico
                    ? Results.Ok(await consulta.ListarHistoricoAsync(id, inicio, fim, cancelamento).ConfigureAwait(false))
                    : Results.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["periodo"] = [$"Informe de e até, com até posterior a de e período de no máximo {ConsultaDeLocalizacao.PeriodoMaximoDoHistorico.TotalHours} horas."],
                        },
                        title: "Requisição inválida"))
            .RequireAuthorization(Politicas.GestaoDaOperacao);

        rotas.MapGet("/api/entregas/{id:guid}/geofence", (Guid id, ConsultaDeGeofence consulta, CancellationToken cancelamento) =>
                consulta.ObterAsync(id, cancelamento))
            .WithTags("Rastreamento")
            .RequireAuthorization(Politicas.LeituraDaOperacao);

        return rotas;
    }
}
