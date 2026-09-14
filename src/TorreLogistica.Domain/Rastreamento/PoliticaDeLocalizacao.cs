namespace TorreLogistica.Domain.Rastreamento;

/// <summary>Qualidade de uma posição aceita.</summary>
public enum QualidadeDaPosicao
{
    /// <summary>Precisão boa o bastante para representar onde o motorista está.</summary>
    Confiavel = 1,

    /// <summary>Aceita no histórico, mas imprecisa demais para mover a posição atual.</summary>
    Imprecisa = 2,
}

/// <summary>
/// Política de aceitação de posições GPS (CLAUDE.md, seção 21).
/// </summary>
/// <remarks>
/// <para>
/// Localização chega imprecisa, antiga, duplicada, impossível, fora de ordem e em rajadas. Cada
/// caso tem destino explícito:
/// </para>
/// <list type="table">
/// <item><term>Coordenada, precisão, velocidade ou direção fora da faixa</term><description>recusada</description></item>
/// <item><term>Capturada no futuro além da tolerância do relógio do aparelho</term><description>recusada</description></item>
/// <item><term>Capturada há mais tempo que o horizonte de recuperação offline</term><description>recusada</description></item>
/// <item><term>Fora da janela de uma rota iniciada pelo motorista</term><description>recusada — coleta mínima</description></item>
/// <item><term>Precisão acima do limite confiável</term><description>histórico, sem mover a posição atual</description></item>
/// <item><term>Mais antiga que a posição atual</term><description>histórico, sem regredir a posição atual</description></item>
/// <item><term>Mesmo identificador de evento já recebido</term><description>duplicada, sem novo registro</description></item>
/// </list>
/// <para>
/// Nada é descartado em silêncio: toda recusa volta ao aplicativo com o motivo e é contada em métrica.
/// </para>
/// </remarks>
public static class PoliticaDeLocalizacao
{
    /// <summary>Precisão até a qual a posição move a posição atual.</summary>
    public const double PrecisaoMaximaConfiavelEmMetros = 100;

    /// <summary>Precisão acima da qual a posição nem entra no histórico.</summary>
    public const double PrecisaoMaximaAceitaEmMetros = 2_000;

    /// <summary>Velocidade máxima plausível informada pelo aparelho (252 km/h).</summary>
    public const double VelocidadeMaximaEmMetrosPorSegundo = 70;

    /// <summary>Posições num único envio.</summary>
    public const int TamanhoMaximoDoLote = 500;

    /// <summary>Quanto o relógio do aparelho pode estar adiantado.</summary>
    public static readonly TimeSpan ToleranciaDeRelogioDoAparelho = TimeSpan.FromMinutes(2);

    /// <summary>Horizonte de recuperação offline: posição mais antiga que isto é recusada.</summary>
    public static readonly TimeSpan IdadeMaximaAceita = TimeSpan.FromDays(7);

    /// <summary>O GPS pode começar a registrar um pouco antes de o motorista tocar "iniciar rota".</summary>
    public static readonly TimeSpan ToleranciaAntesDoInicioDaRota = TimeSpan.FromMinutes(15);

    /// <summary>Posições capturadas logo depois de concluir a rota ainda pertencem a ela.</summary>
    public static readonly TimeSpan ToleranciaDepoisDaConclusaoDaRota = TimeSpan.FromMinutes(5);
}

/// <summary>Período em que uma rota esteve em execução por um motorista.</summary>
/// <param name="rotaId">Rota.</param>
/// <param name="iniciadaEm">Saída do motorista.</param>
/// <param name="concluidaEm">Conclusão, se houve.</param>
public sealed class JanelaDeExecucaoDaRota(Guid rotaId, DateTimeOffset iniciadaEm, DateTimeOffset? concluidaEm)
{
    /// <summary>Rota.</summary>
    public Guid RotaId { get; } = rotaId;

    /// <summary>Saída do motorista.</summary>
    public DateTimeOffset IniciadaEm { get; } = iniciadaEm;

    /// <summary>Conclusão, se houve.</summary>
    public DateTimeOffset? ConcluidaEm { get; } = concluidaEm;

    /// <summary>O instante cai na execução da rota, com as tolerâncias da política.</summary>
    public bool Contem(DateTimeOffset instante) =>
        instante >= IniciadaEm - PoliticaDeLocalizacao.ToleranciaAntesDoInicioDaRota
        && (ConcluidaEm is not { } concluida || instante <= concluida + PoliticaDeLocalizacao.ToleranciaDepoisDaConclusaoDaRota);
}
