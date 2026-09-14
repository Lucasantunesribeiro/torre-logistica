using Microsoft.EntityFrameworkCore;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Rastreamento;

namespace TorreLogistica.Application.Rastreamento;

/// <summary>Posição como devolvida pela API.</summary>
public sealed record PosicaoResumo(
    Guid EventoDeLocalizacaoId,
    long Sequencia,
    double Latitude,
    double Longitude,
    double PrecisaoEmMetros,
    double? VelocidadeEmMetrosPorSegundo,
    double? DirecaoEmGraus,
    DateTimeOffset CapturadaEm,
    DateTimeOffset RecebidaEm,
    Guid? RotaId,
    QualidadeDaPosicao? Qualidade);

/// <summary>Trecho do histórico de posições.</summary>
/// <param name="Posicoes">Posições na ordem de captura.</param>
/// <param name="Truncado">Havia mais posições no período do que o limite devolvido.</param>
public sealed record HistoricoDePosicoes(IReadOnlyList<PosicaoResumo> Posicoes, bool Truncado);

/// <summary>
/// Consulta de posição para o console: a atual, para operar; o histórico, para investigar.
/// </summary>
/// <remarks>
/// Histórico de localização é dado pessoal volumoso. A consulta exige período de no máximo
/// <see cref="PeriodoMaximoDoHistorico"/> e devolve no máximo <see cref="LimiteDoHistorico"/>
/// posições.
/// </remarks>
public sealed class ConsultaDeLocalizacao(IContextoDePersistencia contexto)
{
    /// <summary>Período máximo de uma consulta ao histórico.</summary>
    public static readonly TimeSpan PeriodoMaximoDoHistorico = TimeSpan.FromHours(24);

    /// <summary>Posições devolvidas numa consulta ao histórico.</summary>
    public const int LimiteDoHistorico = 2_000;

    /// <summary>Última posição confiável do motorista.</summary>
    public async Task<PosicaoResumo> ObterPosicaoAtualAsync(Guid motoristaId, CancellationToken cancelamento)
    {
        await GarantirMotoristaAsync(motoristaId, cancelamento).ConfigureAwait(false);

        var atual = await contexto.PosicoesAtuais
            .AsNoTracking()
            .SingleOrDefaultAsync(posicao => posicao.MotoristaId == motoristaId, cancelamento)
            .ConfigureAwait(false)
            ?? throw ExcecaoDeDominio.NaoEncontrado("posicao_nao_encontrada", "O motorista ainda não tem posição registrada.");

        return new PosicaoResumo(
            atual.EventoDeLocalizacaoId,
            atual.Sequencia,
            atual.Localizacao.Latitude,
            atual.Localizacao.Longitude,
            atual.PrecisaoEmMetros,
            atual.VelocidadeEmMetrosPorSegundo,
            atual.DirecaoEmGraus,
            atual.CapturadaEm,
            atual.RecebidaEm,
            atual.RotaId,
            null);
    }

    /// <summary>Posições capturadas no período, em ordem de captura.</summary>
    public async Task<HistoricoDePosicoes> ListarHistoricoAsync(
        Guid motoristaId,
        DateTimeOffset de,
        DateTimeOffset ate,
        CancellationToken cancelamento)
    {
        await GarantirMotoristaAsync(motoristaId, cancelamento).ConfigureAwait(false);

        var inicio = de.ToUniversalTime();
        var fim = ate.ToUniversalTime();

        var posicoes = await contexto.Posicoes
            .AsNoTracking()
            .Where(posicao => posicao.MotoristaId == motoristaId && posicao.CapturadaEm >= inicio && posicao.CapturadaEm < fim)
            .OrderBy(posicao => posicao.CapturadaEm)
            .ThenBy(posicao => posicao.Sequencia)
            .Take(LimiteDoHistorico + 1)
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

        return new HistoricoDePosicoes(
            [.. posicoes.Take(LimiteDoHistorico).Select(posicao => new PosicaoResumo(
                posicao.EventoDeLocalizacaoId,
                posicao.Sequencia,
                posicao.Localizacao.Latitude,
                posicao.Localizacao.Longitude,
                posicao.PrecisaoEmMetros,
                posicao.VelocidadeEmMetrosPorSegundo,
                posicao.DirecaoEmGraus,
                posicao.CapturadaEm,
                posicao.RecebidaEm,
                posicao.RotaId,
                posicao.Qualidade))],
            posicoes.Count > LimiteDoHistorico);
    }

    private async Task GarantirMotoristaAsync(Guid motoristaId, CancellationToken cancelamento)
    {
        var existe = await contexto.Motoristas
            .AnyAsync(motorista => motorista.Id == motoristaId, cancelamento)
            .ConfigureAwait(false);

        if (!existe)
        {
            throw ExcecaoDeDominio.NaoEncontrado("motorista_nao_encontrado", "Motorista não encontrado.");
        }
    }
}
