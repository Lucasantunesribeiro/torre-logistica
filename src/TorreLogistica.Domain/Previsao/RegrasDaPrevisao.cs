using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Entregas;

namespace TorreLogistica.Domain.Previsao;

/// <summary>Situação da entrega em relação à janela prometida (CLAUDE.md, seção 26).</summary>
public enum SituacaoDoSla
{
    /// <summary>Folga confortável até o fim da janela.</summary>
    Normal = 1,

    /// <summary>Folga abaixo do limiar de atenção.</summary>
    Atencao = 2,

    /// <summary>Folga abaixo do limiar de risco, ou chegada prevista depois da janela.</summary>
    Risco = 3,

    /// <summary>A janela terminou sem a entrega concluída.</summary>
    Atrasada = 4,
}

/// <summary>Por que a entrega está na situação: a regra que decidiu, em vocabulário fechado.</summary>
public enum MotivoDaSituacao
{
    /// <summary>Folga maior ou igual ao limiar de atenção.</summary>
    FolgaSuficiente = 1,

    /// <summary>Folga abaixo do limiar de atenção.</summary>
    FolgaAbaixoDoLimiarDeAtencao = 2,

    /// <summary>Folga abaixo do limiar de risco.</summary>
    FolgaAbaixoDoLimiarDeRisco = 3,

    /// <summary>A chegada prevista cai depois do fim da janela.</summary>
    ChegadaPrevistaDepoisDaJanela = 4,

    /// <summary>O fim da janela já passou.</summary>
    JanelaEncerrada = 5,

    /// <summary>Sem chegada prevista, e a janela ainda longe do fim.</summary>
    SemPrevisao = 6,

    /// <summary>Sem chegada prevista, e menos tempo até o fim da janela que o limiar de atenção.</summary>
    SemPrevisaoComJanelaProximaDoFim = 7,
}

/// <summary>De onde veio a duração do deslocamento.</summary>
public enum FonteDaPrevisao
{
    /// <summary>Provedor de rotas configurado.</summary>
    Provedor = 1,

    /// <summary>Contingência: distância em linha reta pelo PostGIS, com velocidade e sinuosidade conservadoras.</summary>
    Contingencia = 2,

    /// <summary>Nenhum deslocamento calculado: sem posição do motorista, ou só paradas já no destino.</summary>
    SemDeslocamento = 3,
}

/// <summary>Por que a previsão usou a contingência em vez do provedor.</summary>
public enum MotivoDaContingencia
{
    /// <summary>Nenhum provedor de rotas configurado.</summary>
    ProvedorAusente = 1,

    /// <summary>O provedor não respondeu no tempo limite.</summary>
    TempoLimite = 2,

    /// <summary>O provedor falhou.</summary>
    FalhaDoProvedor = 3,

    /// <summary>O provedor respondeu com trechos que não correspondem ao trajeto pedido.</summary>
    RespostaInvalida = 4,
}

/// <summary>Por que uma entrega em execução não tem chegada prevista.</summary>
public enum MotivoSemChegadaPrevista
{
    /// <summary>O motorista ainda não enviou posição desde a saída.</summary>
    SemPosicaoDoMotorista = 1,

    /// <summary>A entrega não tem coordenada de destino.</summary>
    DestinoSemCoordenada = 2,
}

/// <summary>Limiares de folga que separam Normal, Atenção e Risco.</summary>
/// <remarks>
/// Folga é o tempo entre a chegada prevista e o fim da janela prometida. "Abaixo" é estrito: folga igual
/// ao limiar fica na situação mais branda.
/// </remarks>
public sealed class LimiaresDeSla
{
    private LimiaresDeSla(TimeSpan folgaParaAtencao, TimeSpan folgaParaRisco)
    {
        FolgaParaAtencao = folgaParaAtencao;
        FolgaParaRisco = folgaParaRisco;
    }

    /// <summary>Folga abaixo da qual a entrega entra em atenção.</summary>
    public TimeSpan FolgaParaAtencao { get; }

    /// <summary>Folga abaixo da qual a entrega entra em risco.</summary>
    public TimeSpan FolgaParaRisco { get; }

    /// <summary>Cria limiares válidos: risco não negativo e menor que atenção.</summary>
    public static LimiaresDeSla Criar(TimeSpan folgaParaAtencao, TimeSpan folgaParaRisco)
    {
        ExcecaoDeDominio.LancarSe(
            folgaParaRisco < TimeSpan.Zero || folgaParaAtencao <= folgaParaRisco,
            "limiares_invalidos",
            "O limiar de risco não pode ser negativo e precisa ser menor que o limiar de atenção.");

        return new LimiaresDeSla(folgaParaAtencao, folgaParaRisco);
    }
}

/// <summary>Resultado da classificação.</summary>
/// <param name="situacao">Situação.</param>
/// <param name="motivo">Regra que decidiu.</param>
/// <param name="folga">Fim da janela menos a chegada prevista; na entrega atrasada, fim da janela menos agora.</param>
public sealed class ClassificacaoDoSla(SituacaoDoSla situacao, MotivoDaSituacao motivo, TimeSpan? folga)
{
    /// <summary>Situação.</summary>
    public SituacaoDoSla Situacao { get; } = situacao;

    /// <summary>Regra que decidiu.</summary>
    public MotivoDaSituacao Motivo { get; } = motivo;

    /// <summary>Folga até o fim da janela; negativa quando a chegada prevista ou o agora já passaram dele.</summary>
    public TimeSpan? Folga { get; } = folga;
}

/// <summary>
/// Classificação da entrega contra a janela prometida — determinística, sem estado, testável linha a linha.
/// </summary>
public static class RegrasDeSla
{
    /// <summary>Classifica.</summary>
    /// <param name="janela">Janela prometida.</param>
    /// <param name="chegadaPrevista">Chegada prevista, se houver.</param>
    /// <param name="agora">Instante da classificação.</param>
    /// <param name="limiares">Limiares em vigor.</param>
    public static ClassificacaoDoSla Classificar(
        JanelaDeEntrega janela,
        DateTimeOffset? chegadaPrevista,
        DateTimeOffset agora,
        LimiaresDeSla limiares)
    {
        ArgumentNullException.ThrowIfNull(janela);
        ArgumentNullException.ThrowIfNull(limiares);

        var instante = agora.ToUniversalTime();

        if (instante > janela.Fim)
        {
            return new ClassificacaoDoSla(SituacaoDoSla.Atrasada, MotivoDaSituacao.JanelaEncerrada, janela.Fim - instante);
        }

        if (chegadaPrevista is not { } chegada)
        {
            return janela.Fim - instante < limiares.FolgaParaAtencao
                ? new ClassificacaoDoSla(SituacaoDoSla.Atencao, MotivoDaSituacao.SemPrevisaoComJanelaProximaDoFim, null)
                : new ClassificacaoDoSla(SituacaoDoSla.Normal, MotivoDaSituacao.SemPrevisao, null);
        }

        var folga = janela.Fim - chegada.ToUniversalTime();

        if (folga < TimeSpan.Zero)
        {
            return new ClassificacaoDoSla(SituacaoDoSla.Risco, MotivoDaSituacao.ChegadaPrevistaDepoisDaJanela, folga);
        }

        if (folga < limiares.FolgaParaRisco)
        {
            return new ClassificacaoDoSla(SituacaoDoSla.Risco, MotivoDaSituacao.FolgaAbaixoDoLimiarDeRisco, folga);
        }

        return folga < limiares.FolgaParaAtencao
            ? new ClassificacaoDoSla(SituacaoDoSla.Atencao, MotivoDaSituacao.FolgaAbaixoDoLimiarDeAtencao, folga)
            : new ClassificacaoDoSla(SituacaoDoSla.Normal, MotivoDaSituacao.FolgaSuficiente, folga);
    }
}

/// <summary>Um trecho do trajeto: duração e distância de deslocamento entre dois pontos consecutivos.</summary>
public sealed class TrechoDeTrajeto
{
    private TrechoDeTrajeto(TimeSpan duracao, double distanciaEmMetros)
    {
        Duracao = duracao;
        DistanciaEmMetros = distanciaEmMetros;
    }

    /// <summary>Duração do deslocamento.</summary>
    public TimeSpan Duracao { get; }

    /// <summary>Distância percorrida.</summary>
    public double DistanciaEmMetros { get; }

    /// <summary>Cria um trecho válido.</summary>
    public static TrechoDeTrajeto Criar(TimeSpan duracao, double distanciaEmMetros)
    {
        ExcecaoDeDominio.LancarSe(
            duracao < TimeSpan.Zero || !double.IsFinite(distanciaEmMetros) || distanciaEmMetros < 0,
            "trecho_invalido",
            "Trecho de trajeto com duração ou distância inválida.");

        return new TrechoDeTrajeto(duracao, distanciaEmMetros);
    }
}

/// <summary>Uma parada ainda pendente da rota, na ordem da rota.</summary>
/// <param name="entregaId">Entrega.</param>
/// <param name="status">Em rota ou próxima do destino.</param>
/// <param name="temCoordenada">A entrega tem coordenada de destino.</param>
/// <param name="chegadaRegistradaEm">Chegada registrada, quando próxima do destino.</param>
public sealed class ParadaParaPrevisao(Guid entregaId, StatusDaEntrega status, bool temCoordenada, DateTimeOffset? chegadaRegistradaEm)
{
    /// <summary>Entrega.</summary>
    public Guid EntregaId { get; } = entregaId;

    /// <summary>Em rota ou próxima do destino.</summary>
    public StatusDaEntrega Status { get; } = status;

    /// <summary>A entrega tem coordenada de destino.</summary>
    public bool TemCoordenada { get; } = temCoordenada;

    /// <summary>Chegada registrada, quando próxima do destino.</summary>
    public DateTimeOffset? ChegadaRegistradaEm { get; } = chegadaRegistradaEm;

    /// <summary>Esta parada ainda exige deslocamento até ela — e por isso entra no trajeto.</summary>
    public bool ExigeDeslocamento => Status == StatusDaEntrega.EmRota && TemCoordenada;
}

/// <summary>Como a chegada prevista de uma parada foi composta.</summary>
public sealed class ComposicaoDaChegada
{
    internal ComposicaoDaChegada(
        Guid entregaId,
        DateTimeOffset? chegadaPrevista,
        MotivoSemChegadaPrevista? motivoSemChegada,
        bool jaNoDestino,
        TimeSpan deslocamento,
        double distanciaEmMetros,
        int paradasAntes,
        TimeSpan tempoDasParadasAntes)
    {
        EntregaId = entregaId;
        ChegadaPrevista = chegadaPrevista;
        MotivoSemChegada = motivoSemChegada;
        JaNoDestino = jaNoDestino;
        Deslocamento = deslocamento;
        DistanciaEmMetros = distanciaEmMetros;
        ParadasAntes = paradasAntes;
        TempoDasParadasAntes = tempoDasParadasAntes;
    }

    /// <summary>Entrega.</summary>
    public Guid EntregaId { get; }

    /// <summary>Chegada prevista, truncada ao segundo; ausente quando não há como prever.</summary>
    public DateTimeOffset? ChegadaPrevista { get; }

    /// <summary>Por que não há chegada prevista.</summary>
    public MotivoSemChegadaPrevista? MotivoSemChegada { get; }

    /// <summary>O motorista já está no destino: a chegada prevista é a chegada registrada.</summary>
    public bool JaNoDestino { get; }

    /// <summary>Soma do deslocamento desde a posição atual até esta parada.</summary>
    public TimeSpan Deslocamento { get; }

    /// <summary>Soma das distâncias desde a posição atual até esta parada.</summary>
    public double DistanciaEmMetros { get; }

    /// <summary>Paradas pendentes antes desta.</summary>
    public int ParadasAntes { get; }

    /// <summary>Tempo de atendimento das paradas pendentes antes desta.</summary>
    public TimeSpan TempoDasParadasAntes { get; }
}

/// <summary>
/// Chegada prevista de cada parada pendente de uma rota em andamento (CLAUDE.md, seção 24).
/// </summary>
/// <remarks>
/// <para>
/// Determinística e explicável: a chegada prevista de uma parada é <c>agora</c> + deslocamento acumulado
/// desde a posição atual + tempo de atendimento das paradas pendentes antes dela. Nada de média móvel,
/// aprendizado ou fator escondido: cada parcela fica registrada e aparece na explicação.
/// </para>
/// <para>
/// Parada próxima do destino não tem deslocamento: a chegada prevista é a chegada registrada, e o
/// atendimento dela ainda por fazer — tempo por parada menos o que já passou desde a chegada — atrasa as
/// seguintes. Parada sem coordenada não tem chegada prevista, mas o atendimento dela conta para as
/// seguintes, que continuam previstas a partir do trajeto das paradas com coordenada.
/// </para>
/// </remarks>
public static class CalculadoraDeChegada
{
    /// <summary>Calcula.</summary>
    /// <param name="agora">Instante do cálculo.</param>
    /// <param name="temPosicao">Há posição atual do motorista válida para a rota.</param>
    /// <param name="paradas">Paradas pendentes (em rota ou próximas do destino), na ordem da rota.</param>
    /// <param name="trechos">
    /// Um trecho da posição atual até a primeira parada que <see cref="ParadaParaPrevisao.ExigeDeslocamento"/>,
    /// e um entre cada par seguinte dessas paradas. Vazio sem posição.
    /// </param>
    /// <param name="tempoPorParada">Tempo médio de atendimento configurado por parada.</param>
    public static IReadOnlyList<ComposicaoDaChegada> Calcular(
        DateTimeOffset agora,
        bool temPosicao,
        IReadOnlyList<ParadaParaPrevisao> paradas,
        IReadOnlyList<TrechoDeTrajeto> trechos,
        TimeSpan tempoPorParada)
    {
        ArgumentNullException.ThrowIfNull(paradas);
        ArgumentNullException.ThrowIfNull(trechos);
        ArgumentOutOfRangeException.ThrowIfLessThan(tempoPorParada, TimeSpan.Zero);

        if (paradas.Any(parada => !RegrasDaEntrega.EstaEmExecucao(parada.Status)))
        {
            throw new ArgumentException("Só paradas em execução entram na previsão.", nameof(paradas));
        }

        var trechosEsperados = temPosicao ? paradas.Count(parada => parada.ExigeDeslocamento) : 0;
        if (trechos.Count != trechosEsperados)
        {
            throw new ArgumentException($"Esperados {trechosEsperados} trechos, recebidos {trechos.Count}.", nameof(trechos));
        }

        var instante = agora.ToUniversalTime();
        var deslocamento = TimeSpan.Zero;
        var distancia = 0d;
        var paradasAntes = 0;
        var tempoDasParadasAntes = TimeSpan.Zero;
        var proximoTrecho = 0;
        var composicoes = new List<ComposicaoDaChegada>(paradas.Count);

        foreach (var parada in paradas)
        {
            if (parada.Status == StatusDaEntrega.ProximaDoDestino)
            {
                var chegada = (parada.ChegadaRegistradaEm ?? instante).ToUniversalTime();
                composicoes.Add(new ComposicaoDaChegada(
                    parada.EntregaId, TruncarAoSegundo(chegada), null, jaNoDestino: true, TimeSpan.Zero, 0, paradasAntes, tempoDasParadasAntes));

                var decorrido = instante > chegada ? instante - chegada : TimeSpan.Zero;
                tempoDasParadasAntes += tempoPorParada > decorrido ? tempoPorParada - decorrido : TimeSpan.Zero;
                paradasAntes++;
                continue;
            }

            if (!temPosicao || !parada.TemCoordenada)
            {
                var motivo = parada.TemCoordenada ? MotivoSemChegadaPrevista.SemPosicaoDoMotorista : MotivoSemChegadaPrevista.DestinoSemCoordenada;
                composicoes.Add(new ComposicaoDaChegada(
                    parada.EntregaId, null, motivo, jaNoDestino: false, deslocamento, distancia, paradasAntes, tempoDasParadasAntes));
            }
            else
            {
                var trecho = trechos[proximoTrecho++];
                deslocamento += trecho.Duracao;
                distancia += trecho.DistanciaEmMetros;

                composicoes.Add(new ComposicaoDaChegada(
                    parada.EntregaId,
                    TruncarAoSegundo(instante + deslocamento + tempoDasParadasAntes),
                    null,
                    jaNoDestino: false,
                    deslocamento,
                    distancia,
                    paradasAntes,
                    tempoDasParadasAntes));
            }

            tempoDasParadasAntes += tempoPorParada;
            paradasAntes++;
        }

        return composicoes;
    }

    private static DateTimeOffset TruncarAoSegundo(DateTimeOffset instante)
    {
        var utc = instante.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
    }
}
