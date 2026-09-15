using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Entregas;

namespace TorreLogistica.Domain.Previsao;

/// <summary>Por que um registro entrou no histórico de previsões.</summary>
public enum TipoDeRegistroDePrevisao
{
    /// <summary>Primeira previsão da entrega nesta execução.</summary>
    Inicial = 1,

    /// <summary>A situação do SLA mudou.</summary>
    SituacaoAlterada = 2,

    /// <summary>A chegada prevista mudou além da mudança relevante, apareceu, sumiu ou trocou de fonte.</summary>
    ChegadaPrevistaAlterada = 3,

    /// <summary>A entrega saiu da execução: a previsão deixa de ser recalculada.</summary>
    Encerrada = 4,
}

/// <summary>Resultado de um cálculo para uma entrega, com tudo o que o explica.</summary>
public sealed class CalculoDaPrevisao
{
    /// <summary>Monta o cálculo.</summary>
    /// <param name="rotaId">Rota em andamento.</param>
    /// <param name="composicao">Como a chegada prevista foi composta.</param>
    /// <param name="classificacao">Situação contra a janela.</param>
    /// <param name="janela">Janela prometida usada.</param>
    /// <param name="limiares">Limiares usados.</param>
    /// <param name="tempoPorParada">Tempo por parada usado.</param>
    /// <param name="fonte">De onde veio o deslocamento.</param>
    /// <param name="provedor">Nome do provedor, quando a fonte é o provedor.</param>
    /// <param name="motivoDaContingencia">Por que usou contingência, quando usou.</param>
    /// <param name="posicaoCapturadaEm">Captura da posição do motorista usada.</param>
    /// <param name="calculadaEm">Instante do cálculo.</param>
    public CalculoDaPrevisao(
        Guid rotaId,
        ComposicaoDaChegada composicao,
        ClassificacaoDoSla classificacao,
        JanelaDeEntrega janela,
        LimiaresDeSla limiares,
        TimeSpan tempoPorParada,
        FonteDaPrevisao fonte,
        string? provedor,
        MotivoDaContingencia? motivoDaContingencia,
        DateTimeOffset? posicaoCapturadaEm,
        DateTimeOffset calculadaEm)
    {
        ArgumentNullException.ThrowIfNull(composicao);
        ArgumentNullException.ThrowIfNull(classificacao);
        ArgumentNullException.ThrowIfNull(janela);
        ArgumentNullException.ThrowIfNull(limiares);
        ExcecaoDeDominio.LancarSe(rotaId == Guid.Empty, "rota_invalida", "Rota inválida.");
        ExcecaoDeDominio.LancarSe(
            (fonte == FonteDaPrevisao.Contingencia) != (motivoDaContingencia is not null),
            "fonte_invalida",
            "Contingência exige motivo, e só contingência tem motivo.");

        RotaId = rotaId;
        Composicao = composicao;
        Classificacao = classificacao;
        Janela = janela;
        Limiares = limiares;
        TempoPorParada = tempoPorParada;
        Fonte = fonte;
        Provedor = fonte == FonteDaPrevisao.Provedor ? provedor : null;
        MotivoDaContingencia = motivoDaContingencia;
        PosicaoCapturadaEm = posicaoCapturadaEm?.ToUniversalTime();
        CalculadaEm = calculadaEm.ToUniversalTime();
    }

    /// <summary>Rota em andamento.</summary>
    public Guid RotaId { get; }

    /// <summary>Como a chegada prevista foi composta.</summary>
    public ComposicaoDaChegada Composicao { get; }

    /// <summary>Situação contra a janela.</summary>
    public ClassificacaoDoSla Classificacao { get; }

    /// <summary>Janela prometida usada.</summary>
    public JanelaDeEntrega Janela { get; }

    /// <summary>Limiares usados.</summary>
    public LimiaresDeSla Limiares { get; }

    /// <summary>Tempo por parada usado.</summary>
    public TimeSpan TempoPorParada { get; }

    /// <summary>De onde veio o deslocamento.</summary>
    public FonteDaPrevisao Fonte { get; }

    /// <summary>Nome do provedor, quando a fonte é o provedor.</summary>
    public string? Provedor { get; }

    /// <summary>Por que usou contingência.</summary>
    public MotivoDaContingencia? MotivoDaContingencia { get; }

    /// <summary>Captura da posição do motorista usada.</summary>
    public DateTimeOffset? PosicaoCapturadaEm { get; }

    /// <summary>Instante do cálculo.</summary>
    public DateTimeOffset CalculadaEm { get; }
}

/// <summary>
/// Previsão atual de uma entrega: chegada prevista, situação do SLA e a composição que as explica.
/// </summary>
/// <remarks>
/// <para>
/// Estado atual e histórico são coisas diferentes (CLAUDE.md, seções 11 e 25). Esta linha é sobrescrita a
/// cada cálculo; o que muda de forma relevante vira um <see cref="RegistroDePrevisao"/>, somente-inserção,
/// com a fotografia completa daquele momento — valores, janela, limiares e fonte. Mudar a configuração
/// amanhã não reescreve por que a entrega entrou em risco ontem.
/// </para>
/// <para>
/// Nem todo cálculo vira registro: sem mudança de situação, a chegada prevista precisa andar pelo menos a
/// mudança relevante configurada. Sem esse filtro, cada posição GPS escreveria uma linha de histórico.
/// </para>
/// </remarks>
public sealed class PrevisaoDaEntrega
{
    private PrevisaoDaEntrega()
    {
    }

    /// <summary>Entrega — chave.</summary>
    public Guid EntregaId { get; private set; }

    /// <summary>Organização.</summary>
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Rota do último cálculo.</summary>
    public Guid RotaId { get; private set; }

    /// <summary>Continua sendo recalculada.</summary>
    public bool Ativa { get; private set; }

    /// <summary>Situação do SLA.</summary>
    public SituacaoDoSla Situacao { get; private set; }

    /// <summary>Regra que decidiu a situação.</summary>
    public MotivoDaSituacao MotivoDaSituacao { get; private set; }

    /// <summary>Chegada prevista.</summary>
    public DateTimeOffset? ChegadaPrevistaEm { get; private set; }

    /// <summary>Por que não há chegada prevista.</summary>
    public MotivoSemChegadaPrevista? MotivoSemChegadaPrevista { get; private set; }

    /// <summary>O motorista já estava no destino.</summary>
    public bool JaNoDestino { get; private set; }

    /// <summary>Folga até o fim da janela, em segundos.</summary>
    public int? FolgaEmSegundos { get; private set; }

    /// <summary>Deslocamento acumulado até a entrega, em segundos.</summary>
    public int DeslocamentoEmSegundos { get; private set; }

    /// <summary>Distância acumulada até a entrega.</summary>
    public double DistanciaEmMetros { get; private set; }

    /// <summary>Paradas pendentes antes da entrega.</summary>
    public int ParadasAntes { get; private set; }

    /// <summary>Atendimento das paradas pendentes antes da entrega, em segundos.</summary>
    public int TempoDasParadasAntesEmSegundos { get; private set; }

    /// <summary>Tempo por parada usado, em segundos.</summary>
    public int TempoPorParadaEmSegundos { get; private set; }

    /// <summary>De onde veio o deslocamento.</summary>
    public FonteDaPrevisao Fonte { get; private set; }

    /// <summary>Provedor usado.</summary>
    public string? Provedor { get; private set; }

    /// <summary>Por que usou contingência.</summary>
    public MotivoDaContingencia? MotivoDaContingencia { get; private set; }

    /// <summary>Início da janela usada.</summary>
    public DateTimeOffset JanelaInicio { get; private set; }

    /// <summary>Fim da janela usada.</summary>
    public DateTimeOffset JanelaFim { get; private set; }

    /// <summary>Limiar de atenção usado, em segundos.</summary>
    public int LimiarDeAtencaoEmSegundos { get; private set; }

    /// <summary>Limiar de risco usado, em segundos.</summary>
    public int LimiarDeRiscoEmSegundos { get; private set; }

    /// <summary>Captura da posição usada.</summary>
    public DateTimeOffset? PosicaoCapturadaEm { get; private set; }

    /// <summary>Último cálculo.</summary>
    public DateTimeOffset CalculadaEm { get; private set; }

    /// <summary>Sequência do último registro do histórico.</summary>
    public int UltimaSequenciaDeRegistro { get; private set; }

    /// <summary>Versão da linha.</summary>
    public uint Versao { get; private set; }

    /// <summary>Primeira previsão da entrega e o registro inicial.</summary>
    public static (PrevisaoDaEntrega Previsao, RegistroDePrevisao Registro) Iniciar(
        Guid organizacaoId,
        CalculoDaPrevisao calculo,
        Guid idDoRegistro)
    {
        ArgumentNullException.ThrowIfNull(calculo);
        ExcecaoDeDominio.LancarSe(organizacaoId == Guid.Empty, "organizacao_invalida", "Organização inválida.");
        ExcecaoDeDominio.LancarSe(calculo.Composicao.EntregaId == Guid.Empty, "identificador_invalido", "Identificador inválido.");

        var previsao = new PrevisaoDaEntrega
        {
            EntregaId = calculo.Composicao.EntregaId,
            OrganizacaoId = organizacaoId,
        };

        previsao.Aplicar(calculo);
        return (previsao, previsao.Registrar(TipoDeRegistroDePrevisao.Inicial, null, null, null, idDoRegistro, calculo.CalculadaEm));
    }

    /// <summary>
    /// Aplica um novo cálculo. A previsão atual sempre é atualizada; o histórico só recebe registro quando a
    /// mudança é relevante.
    /// </summary>
    /// <returns>O registro, ou <see langword="null"/> quando a mudança não é relevante.</returns>
    public RegistroDePrevisao? Atualizar(CalculoDaPrevisao calculo, TimeSpan mudancaRelevante, Guid idDoRegistro)
    {
        ArgumentNullException.ThrowIfNull(calculo);
        ArgumentOutOfRangeException.ThrowIfLessThan(mudancaRelevante, TimeSpan.Zero);
        ExcecaoDeDominio.LancarSe(calculo.Composicao.EntregaId != EntregaId, "previsao_de_outra_entrega", "Cálculo de outra entrega.");

        // Cálculo mais antigo que o vigente não reescreve o presente com o passado.
        if (calculo.CalculadaEm < CalculadaEm)
        {
            return null;
        }

        var situacaoAnterior = Situacao;
        var chegadaAnterior = ChegadaPrevistaEm;
        var fonteAnterior = Fonte;
        var estavaAtiva = Ativa;

        Aplicar(calculo);

        TipoDeRegistroDePrevisao? tipo = !estavaAtiva
            ? TipoDeRegistroDePrevisao.Inicial
            : situacaoAnterior != Situacao
                ? TipoDeRegistroDePrevisao.SituacaoAlterada
                : ChegadaMudouDeFormaRelevante(chegadaAnterior, ChegadaPrevistaEm, mudancaRelevante) || fonteAnterior != Fonte
                    ? TipoDeRegistroDePrevisao.ChegadaPrevistaAlterada
                    : null;

        return tipo is { } registro
            ? Registrar(registro, situacaoAnterior, chegadaAnterior, null, idDoRegistro, calculo.CalculadaEm)
            : null;
    }

    /// <summary>A entrega saiu da execução: a previsão para de ser recalculada.</summary>
    /// <param name="statusDaEntrega">Status em que a entrega saiu, quando conhecido.</param>
    /// <param name="idDoRegistro">Identificador do registro.</param>
    /// <param name="agora">Instante.</param>
    /// <returns>O registro, ou <see langword="null"/> se já estava encerrada.</returns>
    public RegistroDePrevisao? Encerrar(StatusDaEntrega? statusDaEntrega, Guid idDoRegistro, DateTimeOffset agora)
    {
        if (!Ativa)
        {
            return null;
        }

        Ativa = false;
        return Registrar(TipoDeRegistroDePrevisao.Encerrada, Situacao, ChegadaPrevistaEm, statusDaEntrega, idDoRegistro, agora);
    }

    private static bool ChegadaMudouDeFormaRelevante(DateTimeOffset? anterior, DateTimeOffset? atual, TimeSpan mudancaRelevante) =>
        (anterior, atual) switch
        {
            (null, null) => false,
            ({ } antes, { } depois) => (depois - antes).Duration() >= mudancaRelevante,
            _ => true,
        };

    private static int Segundos(TimeSpan duracao) => (int)Math.Round(duracao.TotalSeconds, MidpointRounding.AwayFromZero);

    private void Aplicar(CalculoDaPrevisao calculo)
    {
        var composicao = calculo.Composicao;

        RotaId = calculo.RotaId;
        Ativa = true;
        Situacao = calculo.Classificacao.Situacao;
        MotivoDaSituacao = calculo.Classificacao.Motivo;
        ChegadaPrevistaEm = composicao.ChegadaPrevista;
        MotivoSemChegadaPrevista = composicao.MotivoSemChegada;
        JaNoDestino = composicao.JaNoDestino;
        FolgaEmSegundos = calculo.Classificacao.Folga is { } folga ? Segundos(folga) : null;
        DeslocamentoEmSegundos = Segundos(composicao.Deslocamento);
        DistanciaEmMetros = Math.Round(composicao.DistanciaEmMetros, 1);
        ParadasAntes = composicao.ParadasAntes;
        TempoDasParadasAntesEmSegundos = Segundos(composicao.TempoDasParadasAntes);
        TempoPorParadaEmSegundos = Segundos(calculo.TempoPorParada);
        Fonte = calculo.Fonte;
        Provedor = calculo.Provedor;
        MotivoDaContingencia = calculo.MotivoDaContingencia;
        JanelaInicio = calculo.Janela.Inicio;
        JanelaFim = calculo.Janela.Fim;
        LimiarDeAtencaoEmSegundos = Segundos(calculo.Limiares.FolgaParaAtencao);
        LimiarDeRiscoEmSegundos = Segundos(calculo.Limiares.FolgaParaRisco);
        PosicaoCapturadaEm = calculo.PosicaoCapturadaEm;
        CalculadaEm = calculo.CalculadaEm;
    }

    private RegistroDePrevisao Registrar(
        TipoDeRegistroDePrevisao tipo,
        SituacaoDoSla? situacaoAnterior,
        DateTimeOffset? chegadaAnterior,
        StatusDaEntrega? statusDaEntrega,
        Guid idDoRegistro,
        DateTimeOffset registradoEm)
    {
        ExcecaoDeDominio.LancarSe(idDoRegistro == Guid.Empty, "identificador_invalido", "Identificador inválido.");

        UltimaSequenciaDeRegistro++;
        return RegistroDePrevisao.Fotografar(idDoRegistro, this, tipo, situacaoAnterior, chegadaAnterior, statusDaEntrega, registradoEm);
    }
}

/// <summary>
/// Uma mudança relevante de previsão — histórico somente-inserção (CLAUDE.md, seção 25).
/// </summary>
/// <remarks>
/// Fotografia completa: situação e chegada anteriores e novas, regra que decidiu, composição, fonte,
/// janela e limiares daquele momento. É o que responde "por que esta entrega passou de Normal para Risco"
/// sem depender do estado atual nem da configuração atual.
/// </remarks>
public sealed class RegistroDePrevisao
{
    private RegistroDePrevisao()
    {
    }

    /// <summary>Identificador (UUIDv7).</summary>
    public Guid Id { get; private set; }

    /// <summary>Organização.</summary>
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Entrega.</summary>
    public Guid EntregaId { get; private set; }

    /// <summary>Rota do cálculo.</summary>
    public Guid RotaId { get; private set; }

    /// <summary>Posição no histórico da entrega, a partir de 1.</summary>
    public int Sequencia { get; private set; }

    /// <summary>Por que entrou no histórico.</summary>
    public TipoDeRegistroDePrevisao Tipo { get; private set; }

    /// <summary>Situação antes deste registro.</summary>
    public SituacaoDoSla? SituacaoAnterior { get; private set; }

    /// <summary>Situação.</summary>
    public SituacaoDoSla Situacao { get; private set; }

    /// <summary>Regra que decidiu a situação.</summary>
    public MotivoDaSituacao MotivoDaSituacao { get; private set; }

    /// <summary>Chegada prevista antes deste registro.</summary>
    public DateTimeOffset? ChegadaPrevistaAnteriorEm { get; private set; }

    /// <summary>Chegada prevista.</summary>
    public DateTimeOffset? ChegadaPrevistaEm { get; private set; }

    /// <summary>Por que não havia chegada prevista.</summary>
    public MotivoSemChegadaPrevista? MotivoSemChegadaPrevista { get; private set; }

    /// <summary>O motorista já estava no destino.</summary>
    public bool JaNoDestino { get; private set; }

    /// <summary>Folga até o fim da janela, em segundos.</summary>
    public int? FolgaEmSegundos { get; private set; }

    /// <summary>Deslocamento acumulado, em segundos.</summary>
    public int DeslocamentoEmSegundos { get; private set; }

    /// <summary>Distância acumulada.</summary>
    public double DistanciaEmMetros { get; private set; }

    /// <summary>Paradas pendentes antes.</summary>
    public int ParadasAntes { get; private set; }

    /// <summary>Atendimento das paradas antes, em segundos.</summary>
    public int TempoDasParadasAntesEmSegundos { get; private set; }

    /// <summary>Tempo por parada usado, em segundos.</summary>
    public int TempoPorParadaEmSegundos { get; private set; }

    /// <summary>De onde veio o deslocamento.</summary>
    public FonteDaPrevisao Fonte { get; private set; }

    /// <summary>Provedor usado.</summary>
    public string? Provedor { get; private set; }

    /// <summary>Por que usou contingência.</summary>
    public MotivoDaContingencia? MotivoDaContingencia { get; private set; }

    /// <summary>Início da janela usada.</summary>
    public DateTimeOffset JanelaInicio { get; private set; }

    /// <summary>Fim da janela usada.</summary>
    public DateTimeOffset JanelaFim { get; private set; }

    /// <summary>Limiar de atenção usado, em segundos.</summary>
    public int LimiarDeAtencaoEmSegundos { get; private set; }

    /// <summary>Limiar de risco usado, em segundos.</summary>
    public int LimiarDeRiscoEmSegundos { get; private set; }

    /// <summary>Captura da posição usada.</summary>
    public DateTimeOffset? PosicaoCapturadaEm { get; private set; }

    /// <summary>Status da entrega no encerramento.</summary>
    public StatusDaEntrega? StatusDaEntrega { get; private set; }

    /// <summary>Instante do cálculo fotografado.</summary>
    public DateTimeOffset CalculadaEm { get; private set; }

    /// <summary>Instante do registro.</summary>
    public DateTimeOffset RegistradoEm { get; private set; }

    internal static RegistroDePrevisao Fotografar(
        Guid id,
        PrevisaoDaEntrega previsao,
        TipoDeRegistroDePrevisao tipo,
        SituacaoDoSla? situacaoAnterior,
        DateTimeOffset? chegadaAnterior,
        StatusDaEntrega? statusDaEntrega,
        DateTimeOffset registradoEm) =>
        new()
        {
            Id = id,
            OrganizacaoId = previsao.OrganizacaoId,
            EntregaId = previsao.EntregaId,
            RotaId = previsao.RotaId,
            Sequencia = previsao.UltimaSequenciaDeRegistro,
            Tipo = tipo,
            SituacaoAnterior = situacaoAnterior,
            Situacao = previsao.Situacao,
            MotivoDaSituacao = previsao.MotivoDaSituacao,
            ChegadaPrevistaAnteriorEm = chegadaAnterior,
            ChegadaPrevistaEm = previsao.ChegadaPrevistaEm,
            MotivoSemChegadaPrevista = previsao.MotivoSemChegadaPrevista,
            JaNoDestino = previsao.JaNoDestino,
            FolgaEmSegundos = previsao.FolgaEmSegundos,
            DeslocamentoEmSegundos = previsao.DeslocamentoEmSegundos,
            DistanciaEmMetros = previsao.DistanciaEmMetros,
            ParadasAntes = previsao.ParadasAntes,
            TempoDasParadasAntesEmSegundos = previsao.TempoDasParadasAntesEmSegundos,
            TempoPorParadaEmSegundos = previsao.TempoPorParadaEmSegundos,
            Fonte = previsao.Fonte,
            Provedor = previsao.Provedor,
            MotivoDaContingencia = previsao.MotivoDaContingencia,
            JanelaInicio = previsao.JanelaInicio,
            JanelaFim = previsao.JanelaFim,
            LimiarDeAtencaoEmSegundos = previsao.LimiarDeAtencaoEmSegundos,
            LimiarDeRiscoEmSegundos = previsao.LimiarDeRiscoEmSegundos,
            PosicaoCapturadaEm = previsao.PosicaoCapturadaEm,
            StatusDaEntrega = statusDaEntrega,
            CalculadaEm = previsao.CalculadaEm,
            RegistradoEm = registradoEm.ToUniversalTime(),
        };
}
