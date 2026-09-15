using Microsoft.EntityFrameworkCore;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Previsao;

namespace TorreLogistica.Application.Previsao;

/// <summary>De que parcelas a chegada prevista é feita, e de onde veio o deslocamento.</summary>
public sealed record ComposicaoDaPrevisaoResumo(
    bool JaNoDestino,
    int DeslocamentoEmSegundos,
    double DistanciaEmMetros,
    int ParadasAntes,
    int TempoDasParadasAntesEmSegundos,
    int TempoPorParadaEmSegundos,
    FonteDaPrevisao Fonte,
    string? Provedor,
    MotivoDaContingencia? MotivoDaContingencia,
    DateTimeOffset? PosicaoCapturadaEm);

/// <summary>Janela e limiares contra os quais a situação foi decidida.</summary>
public sealed record CriteriosDaPrevisaoResumo(
    DateTimeOffset JanelaDe,
    DateTimeOffset JanelaAte,
    int LimiarDeAtencaoEmSegundos,
    int LimiarDeRiscoEmSegundos);

/// <summary>Um registro do histórico de previsões, com a explicação.</summary>
public sealed record RegistroDePrevisaoResumo(
    int Sequencia,
    TipoDeRegistroDePrevisao Tipo,
    SituacaoDoSla? SituacaoAnterior,
    SituacaoDoSla Situacao,
    MotivoDaSituacao Motivo,
    DateTimeOffset? ChegadaPrevistaAnteriorEm,
    DateTimeOffset? ChegadaPrevistaEm,
    MotivoSemChegadaPrevista? MotivoSemChegadaPrevista,
    int? FolgaEmSegundos,
    string Explicacao,
    ComposicaoDaPrevisaoResumo Composicao,
    CriteriosDaPrevisaoResumo Criterios,
    StatusDaEntrega? StatusDaEntrega,
    DateTimeOffset CalculadaEm,
    DateTimeOffset RegistradoEm);

/// <summary>Previsão atual e histórico de uma entrega.</summary>
/// <param name="Disponivel">Já houve cálculo para a entrega.</param>
/// <param name="Ativa">Continua sendo recalculada.</param>
/// <param name="Situacao">Situação do SLA.</param>
/// <param name="Motivo">Regra que decidiu.</param>
/// <param name="ChegadaPrevistaEm">Chegada prevista.</param>
/// <param name="MotivoSemChegadaPrevista">Por que não há chegada prevista.</param>
/// <param name="FolgaEmSegundos">Folga até o fim da janela.</param>
/// <param name="Explicacao">Por que a entrega está nesta situação.</param>
/// <param name="Composicao">Parcelas da chegada prevista.</param>
/// <param name="Criterios">Janela e limiares.</param>
/// <param name="CalculadaEm">Último cálculo.</param>
/// <param name="Historico">Mudanças relevantes, da mais antiga para a mais recente.</param>
public sealed record PrevisaoResumo(
    bool Disponivel,
    bool Ativa,
    SituacaoDoSla? Situacao,
    MotivoDaSituacao? Motivo,
    DateTimeOffset? ChegadaPrevistaEm,
    MotivoSemChegadaPrevista? MotivoSemChegadaPrevista,
    int? FolgaEmSegundos,
    string? Explicacao,
    ComposicaoDaPrevisaoResumo? Composicao,
    CriteriosDaPrevisaoResumo? Criterios,
    DateTimeOffset? CalculadaEm,
    IReadOnlyList<RegistroDePrevisaoResumo> Historico);

/// <summary>Consulta da previsão de uma entrega, para o console.</summary>
public sealed class ConsultaDePrevisao(IContextoDePersistencia contexto)
{
    /// <summary>Registros mais recentes devolvidos no histórico.</summary>
    public const int LimiteDoHistorico = 200;

    /// <summary>Previsão atual e histórico.</summary>
    public async Task<PrevisaoResumo> ObterAsync(Guid entregaId, CancellationToken cancelamento)
    {
        var existe = await contexto.Entregas
            .AsNoTracking()
            .AnyAsync(entrega => entrega.Id == entregaId, cancelamento)
            .ConfigureAwait(false);

        if (!existe)
        {
            throw ExcecaoDeDominio.NaoEncontrado("entrega_nao_encontrada", "Entrega não encontrada.");
        }

        var previsao = await contexto.PrevisoesDaEntrega
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.EntregaId == entregaId, cancelamento)
            .ConfigureAwait(false);

        var registros = await contexto.RegistrosDePrevisao
            .AsNoTracking()
            .Where(registro => registro.EntregaId == entregaId)
            .OrderByDescending(registro => registro.Sequencia)
            .Take(LimiteDoHistorico)
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

        var historico = registros
            .OrderBy(registro => registro.Sequencia)
            .Select(Resumir)
            .ToList();

        if (previsao is null)
        {
            return new PrevisaoResumo(false, false, null, null, null, null, null, null, null, null, null, historico);
        }

        var explicacao = ExplicacaoDaPrevisao.DaPrevisaoAtual(previsao);

        return new PrevisaoResumo(
            true,
            previsao.Ativa,
            previsao.Situacao,
            previsao.MotivoDaSituacao,
            previsao.ChegadaPrevistaEm,
            previsao.MotivoSemChegadaPrevista,
            previsao.FolgaEmSegundos,
            explicacao,
            new ComposicaoDaPrevisaoResumo(
                previsao.JaNoDestino,
                previsao.DeslocamentoEmSegundos,
                previsao.DistanciaEmMetros,
                previsao.ParadasAntes,
                previsao.TempoDasParadasAntesEmSegundos,
                previsao.TempoPorParadaEmSegundos,
                previsao.Fonte,
                previsao.Provedor,
                previsao.MotivoDaContingencia,
                previsao.PosicaoCapturadaEm),
            new CriteriosDaPrevisaoResumo(
                previsao.JanelaInicio, previsao.JanelaFim, previsao.LimiarDeAtencaoEmSegundos, previsao.LimiarDeRiscoEmSegundos),
            previsao.CalculadaEm,
            historico);
    }

    private static RegistroDePrevisaoResumo Resumir(RegistroDePrevisao registro) =>
        new(
            registro.Sequencia,
            registro.Tipo,
            registro.SituacaoAnterior,
            registro.Situacao,
            registro.MotivoDaSituacao,
            registro.ChegadaPrevistaAnteriorEm,
            registro.ChegadaPrevistaEm,
            registro.MotivoSemChegadaPrevista,
            registro.FolgaEmSegundos,
            ExplicacaoDaPrevisao.Montar(new DadosDaExplicacao(
                registro.Tipo,
                registro.SituacaoAnterior,
                registro.Situacao,
                registro.MotivoDaSituacao,
                registro.ChegadaPrevistaEm,
                registro.MotivoSemChegadaPrevista,
                registro.JaNoDestino,
                registro.FolgaEmSegundos,
                registro.DeslocamentoEmSegundos,
                registro.DistanciaEmMetros,
                registro.ParadasAntes,
                registro.TempoDasParadasAntesEmSegundos,
                registro.Fonte,
                registro.Provedor,
                registro.MotivoDaContingencia,
                registro.LimiarDeAtencaoEmSegundos,
                registro.LimiarDeRiscoEmSegundos,
                registro.PosicaoCapturadaEm,
                registro.CalculadaEm,
                registro.StatusDaEntrega)),
            new ComposicaoDaPrevisaoResumo(
                registro.JaNoDestino,
                registro.DeslocamentoEmSegundos,
                registro.DistanciaEmMetros,
                registro.ParadasAntes,
                registro.TempoDasParadasAntesEmSegundos,
                registro.TempoPorParadaEmSegundos,
                registro.Fonte,
                registro.Provedor,
                registro.MotivoDaContingencia,
                registro.PosicaoCapturadaEm),
            new CriteriosDaPrevisaoResumo(
                registro.JanelaInicio, registro.JanelaFim, registro.LimiarDeAtencaoEmSegundos, registro.LimiarDeRiscoEmSegundos),
            registro.StatusDaEntrega,
            registro.CalculadaEm,
            registro.RegistradoEm);
}
