using System.ComponentModel.DataAnnotations;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Application.Observabilidade;
using TorreLogistica.Domain.Abstracoes.Tempo;

namespace TorreLogistica.Application.Retencao;

/// <summary>
/// Por quanto tempo a localização bruta fica guardada, e em que ritmo a limpeza roda.
/// </summary>
/// <remarks>
/// O prazo é configuração, não constante de código: a finalidade legítima de guardar o rastro de um
/// motorista dura o tempo de auditar a operação, e esse tempo é decisão de quem opera — não de quem
/// programa. O padrão de 30 dias cobre a contestação de uma entrega sem virar arquivo permanente de
/// deslocamento de pessoas.
/// </remarks>
public sealed class OpcoesDeRetencao
{
    /// <summary>Seção correspondente na configuração.</summary>
    public const string Secao = "Torre:Retencao";

    /// <summary>Prazo mínimo aceito, para uma configuração errada não apagar o que ainda está em uso.</summary>
    public static readonly TimeSpan PrazoMinimo = TimeSpan.FromDays(1);

    /// <summary>Por quanto tempo o histórico bruto de posições é guardado, contado do recebimento.</summary>
    public TimeSpan PosicoesBrutas { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Intervalo entre rodadas da limpeza.</summary>
    public TimeSpan Intervalo { get; set; } = TimeSpan.FromHours(6);

    /// <summary>Quantas linhas cada comando de exclusão remove.</summary>
    /// <remarks>
    /// Apagar em lote pequeno é deliberado: um único <c>DELETE</c> de milhões de linhas segura bloqueio e
    /// infla o log de transação, e a limpeza passaria a ser o incidente em vez de evitá-lo.
    /// </remarks>
    [Range(100, 100_000)]
    public int TamanhoDoLote { get; set; } = 5_000;

    /// <summary>Teto de lotes por rodada, para a limpeza não monopolizar o banco.</summary>
    [Range(1, 1_000)]
    public int LotesPorRodada { get; set; } = 20;

    /// <summary>Rodar a limpeza em segundo plano neste processo.</summary>
    public bool LimparEmSegundoPlano { get; set; } = true;
}

/// <summary>Quanto foi removido numa rodada.</summary>
/// <param name="PosicoesRemovidas">Linhas apagadas do histórico bruto.</param>
/// <param name="RestouTrabalho">
/// <see langword="true"/> quando a rodada parou no teto de lotes com dado vencido ainda no banco.
/// </param>
public sealed record ResultadoDaLimpeza(int PosicoesRemovidas, bool RestouTrabalho);

/// <summary>
/// Apaga a localização bruta que passou do prazo de retenção.
/// </summary>
/// <remarks>
/// <para>
/// O corte é pelo <b>recebimento</b>, não pela captura. Captura é carimbo do aparelho, e aparelho é
/// cliente: um relógio errado — ou um cliente mal-intencionado — decidiria por nós quando o dado sai ou
/// fica. Recebimento é carimbo do servidor, e é o instante em que a guarda começou de fato.
/// </para>
/// <para>
/// Só o histórico bruto é apagado. A posição atual é projeção de estado, não rastro: apagá-la por idade
/// deixaria o mapa sem a última posição conhecida de um motorista parado, sem nenhum ganho de privacidade —
/// é uma linha por motorista, não um caminho percorrido. Evento operacional derivado da localização
/// (entrada em geofence, chegada, conclusão) também fica: é o que explica a operação, e some junto com a
/// entrega, não com o rastro.
/// </para>
/// <para>
/// A limpeza atravessa organizações de propósito. O prazo é do sistema e a varredura é por idade; filtrar
/// por organização faria a rodada percorrer o mesmo índice uma vez por tenant para chegar ao mesmo lugar.
/// </para>
/// </remarks>
public sealed class LimpezaPorRetencao(
    IContextoDePersistencia contexto,
    IOptions<OpcoesDeRetencao> opcoes,
    IRelogio relogio,
    MetricasDeRetencao metricas,
    ILogger<LimpezaPorRetencao> log)
{
    /// <summary>Executa uma rodada.</summary>
    public async Task<ResultadoDaLimpeza> ExecutarAsync(CancellationToken cancelamento)
    {
        using var rastro = RastroDaOperacao.Fonte.StartActivity("retencao.limpeza");
        var configuracao = opcoes.Value;
        var prazo = configuracao.PosicoesBrutas < OpcoesDeRetencao.PrazoMinimo
            ? OpcoesDeRetencao.PrazoMinimo
            : configuracao.PosicoesBrutas;

        var corte = relogio.AgoraUtc - prazo;
        var removidas = 0;
        var restou = false;

        for (var lote = 0; lote < configuracao.LotesPorRodada; lote++)
        {
            cancelamento.ThrowIfCancellationRequested();

            var apagadas = await contexto
                .RemoverPosicoesRecebidasAntesAsync(corte, configuracao.TamanhoDoLote, cancelamento)
                .ConfigureAwait(false);

            removidas += apagadas;

            if (apagadas < configuracao.TamanhoDoLote)
            {
                break;
            }

            // Lote cheio no último giro significa que ainda há vencido esperando a próxima rodada.
            restou = lote == configuracao.LotesPorRodada - 1;
        }

        metricas.RegistrarPosicoesRemovidas(removidas);
        rastro?.SetTag("retencao.posicoes_removidas", removidas);
        rastro?.SetTag("retencao.corte", corte.ToString("o"));

        if (removidas > 0)
        {
            log.LogInformation(
                "Retenção: {Removidas} posição(ões) bruta(s) anterior(es) a {Corte:o} apagada(s). Restou trabalho: {Restou}.",
                removidas,
                corte,
                restou);
        }

        return new ResultadoDaLimpeza(removidas, restou);
    }
}

/// <summary>Instrumentos da limpeza por retenção.</summary>
public sealed class MetricasDeRetencao : IDisposable
{
    /// <summary>Nome do medidor.</summary>
    public const string NomeDoMedidor = "TorreLogistica.Retencao";

    private readonly Meter _medidor = new(NomeDoMedidor);
    private readonly Counter<long> _posicoesRemovidas;

    /// <summary>Cria os instrumentos.</summary>
    public MetricasDeRetencao() =>
        _posicoesRemovidas = _medidor.CreateCounter<long>(
            "retention.positions.deleted",
            "{posicao}",
            "Posições brutas apagadas por vencimento do prazo de retenção.");

    /// <summary>Registra o que uma rodada apagou.</summary>
    public void RegistrarPosicoesRemovidas(int quantidade)
    {
        if (quantidade > 0)
        {
            _posicoesRemovidas.Add(quantidade);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _medidor.Dispose();
        GC.SuppressFinalize(this);
    }
}
