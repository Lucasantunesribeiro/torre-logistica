using System.Text.Json;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Comum;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Previsao;

namespace TorreLogistica.Domain.Alertas;

/// <summary>Tipos de alerta operacional (CLAUDE.md, seção 27).</summary>
public enum TipoDeAlerta
{
    /// <summary>A previsão da entrega está em Risco.</summary>
    RiscoDeAtraso = 1,

    /// <summary>A janela prometida terminou sem a entrega concluída.</summary>
    EntregaAtrasada = 2,

    /// <summary>O motorista de uma rota em andamento está sem enviar posição além do limite.</summary>
    MotoristaOffline = 3,

    /// <summary>O motorista permanece no mesmo lugar além do limite.</summary>
    ParadoTempoExcessivo = 4,

    /// <summary>A entrega acumulou tentativas sem sucesso até o limite.</summary>
    TentativasExcedidas = 5,

    /// <summary>Ocorrência crítica registrada — produzido com as ocorrências (Fase 13).</summary>
    OcorrenciaCritica = 6,
}

/// <summary>Severidade, da menor para a maior.</summary>
public enum SeveridadeDoAlerta
{
    /// <summary>Informativa.</summary>
    Baixa = 1,

    /// <summary>Pede acompanhamento.</summary>
    Media = 2,

    /// <summary>Pede ação.</summary>
    Alta = 3,

    /// <summary>Pede ação imediata.</summary>
    Critica = 4,
}

/// <summary>Estado do alerta.</summary>
public enum EstadoDoAlerta
{
    /// <summary>Precisa de atenção.</summary>
    Aberto = 1,

    /// <summary>Não precisa mais de atenção.</summary>
    Resolvido = 2,
}

/// <summary>Como o alerta foi resolvido.</summary>
public enum FormaDeResolucao
{
    /// <summary>A condição que o abriu deixou de valer.</summary>
    Automatica = 1,

    /// <summary>Um operador tratou e encerrou.</summary>
    PeloOperador = 2,
}

/// <summary>Tipos de evento do ciclo de vida do alerta.</summary>
public enum TipoDeEventoDoAlerta
{
    /// <summary>Alerta aberto.</summary>
    Aberto = 1,

    /// <summary>Resolvido porque a condição deixou de valer.</summary>
    Resolvido = 2,

    /// <summary>A condição voltou dentro da janela de reabertura.</summary>
    Reaberto = 3,

    /// <summary>Resolvido por um operador.</summary>
    ResolvidoPeloOperador = 4,
}

/// <summary>O que uma constatação fez com o alerta existente.</summary>
public enum ResultadoDaConstatacao
{
    /// <summary>Nada mudou no ciclo de vida; a evidência pode ter sido atualizada.</summary>
    SemMudanca = 1,

    /// <summary>Resolvido automaticamente.</summary>
    Resolvido = 2,

    /// <summary>Reaberto.</summary>
    Reaberto = 3,

    /// <summary>A condição voltou depois da janela de reabertura: é um alerta novo.</summary>
    ExigeNovoAlerta = 4,
}

/// <summary>Severidade de cada tipo e a chave que deduplica.</summary>
public static class CatalogoDeAlertas
{
    /// <summary>Severidade do tipo.</summary>
    public static SeveridadeDoAlerta Severidade(TipoDeAlerta tipo) => tipo switch
    {
        TipoDeAlerta.RiscoDeAtraso => SeveridadeDoAlerta.Media,
        TipoDeAlerta.EntregaAtrasada => SeveridadeDoAlerta.Alta,
        TipoDeAlerta.MotoristaOffline => SeveridadeDoAlerta.Alta,
        TipoDeAlerta.ParadoTempoExcessivo => SeveridadeDoAlerta.Media,
        TipoDeAlerta.TentativasExcedidas => SeveridadeDoAlerta.Alta,
        TipoDeAlerta.OcorrenciaCritica => SeveridadeDoAlerta.Critica,
        _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "Tipo de alerta desconhecido."),
    };

    /// <summary>O alerta é sobre uma entrega (e não sobre o motorista numa rota).</summary>
    public static bool EhDeEntrega(TipoDeAlerta tipo) =>
        tipo is TipoDeAlerta.RiscoDeAtraso or TipoDeAlerta.EntregaAtrasada or TipoDeAlerta.TentativasExcedidas or TipoDeAlerta.OcorrenciaCritica;

    /// <summary>
    /// Chave de deduplicação: o mesmo problema, no mesmo alvo, é sempre o mesmo alerta aberto. Entrega é o
    /// alvo dos alertas de entrega; motorista e rota, dos alertas de execução — o mesmo motorista offline em
    /// outra rota é outro problema.
    /// </summary>
    public static string Chave(TipoDeAlerta tipo, Guid? entregaId, Guid? motoristaId, Guid? rotaId) =>
        EhDeEntrega(tipo)
            ? $"{tipo}:entrega:{entregaId:N}"
            : $"{tipo}:rota:{rotaId:N}:motorista:{motoristaId:N}";
}

/// <summary>Resultado de uma regra para um alvo: a condição vale ou não, e a evidência que decidiu.</summary>
public sealed class Constatacao
{
    private Constatacao(TipoDeAlerta tipo, Guid? entregaId, Guid? motoristaId, Guid? rotaId, bool condicao, string evidencia)
    {
        Tipo = tipo;
        EntregaId = entregaId;
        MotoristaId = motoristaId;
        RotaId = rotaId;
        Condicao = condicao;
        Evidencia = evidencia;
        Chave = CatalogoDeAlertas.Chave(tipo, entregaId, motoristaId, rotaId);
    }

    /// <summary>Tipo.</summary>
    public TipoDeAlerta Tipo { get; }

    /// <summary>Entrega.</summary>
    public Guid? EntregaId { get; }

    /// <summary>Motorista.</summary>
    public Guid? MotoristaId { get; }

    /// <summary>Rota.</summary>
    public Guid? RotaId { get; }

    /// <summary>A condição do alerta vale agora.</summary>
    public bool Condicao { get; }

    /// <summary>Evidência em JSON.</summary>
    public string Evidencia { get; }

    /// <summary>Chave de deduplicação.</summary>
    public string Chave { get; }

    /// <summary>Cria a constatação, conferindo que o alvo combina com o tipo.</summary>
    public static Constatacao Criar(TipoDeAlerta tipo, Guid? entregaId, Guid? motoristaId, Guid? rotaId, bool condicao, object evidencia)
    {
        ArgumentNullException.ThrowIfNull(evidencia);
        ExcecaoDeDominio.LancarSe(!Enum.IsDefined(tipo), "tipo_de_alerta_invalido", "Tipo de alerta inválido.");
        ExcecaoDeDominio.LancarSe(
            CatalogoDeAlertas.EhDeEntrega(tipo)
                ? entregaId is null || entregaId == Guid.Empty
                : motoristaId is null || motoristaId == Guid.Empty || rotaId is null || rotaId == Guid.Empty,
            "alvo_de_alerta_invalido",
            "O alvo não combina com o tipo de alerta.");

        return new Constatacao(tipo, entregaId, motoristaId, rotaId, condicao, JsonSerializer.Serialize(evidencia, RegrasDeAlerta.OpcoesDeJson));
    }
}

/// <summary>Quanto tempo o motorista está no mesmo lugar, pelo histórico de posições.</summary>
/// <param name="inicio">Captura da posição mais antiga da permanência atual.</param>
/// <param name="ultimaCaptura">Captura da posição atual.</param>
/// <param name="posicoes">Posições confiáveis dentro da permanência.</param>
public sealed class PermanenciaNoLocal(DateTimeOffset inicio, DateTimeOffset ultimaCaptura, int posicoes)
{
    /// <summary>Captura da posição mais antiga da permanência atual.</summary>
    public DateTimeOffset Inicio { get; } = inicio;

    /// <summary>Captura da posição atual.</summary>
    public DateTimeOffset UltimaCaptura { get; } = ultimaCaptura;

    /// <summary>Posições confiáveis dentro da permanência.</summary>
    public int Posicoes { get; } = posicoes;

    /// <summary>Tempo observado no mesmo lugar.</summary>
    public TimeSpan Duracao => UltimaCaptura > Inicio ? UltimaCaptura - Inicio : TimeSpan.Zero;
}

/// <summary>
/// As regras tipadas dos alertas: cada uma decide a condição a partir de fatos já calculados e devolve a
/// evidência que a explica.
/// </summary>
/// <remarks>
/// Alerta não é <c>if</c> disperso (CLAUDE.md, seção 27): toda regra mora aqui, é pura e testável linha a
/// linha. A evidência leva números, instantes e limites — nunca coordenada nem dado do destinatário.
/// </remarks>
public static class RegrasDeAlerta
{
    internal static readonly JsonSerializerOptions OpcoesDeJson = new(JsonSerializerDefaults.Web);

    /// <summary>A previsão ativa da entrega está em Risco.</summary>
    public static Constatacao RiscoDeAtraso(Guid entregaId, Guid rotaId, PrevisaoDaEntrega? previsao, string? explicacaoDaPrevisao) =>
        DaPrevisao(TipoDeAlerta.RiscoDeAtraso, SituacaoDoSla.Risco, entregaId, rotaId, previsao, explicacaoDaPrevisao);

    /// <summary>A previsão ativa da entrega está Atrasada: a janela terminou sem a entrega concluída.</summary>
    public static Constatacao EntregaAtrasada(Guid entregaId, Guid rotaId, PrevisaoDaEntrega? previsao, string? explicacaoDaPrevisao) =>
        DaPrevisao(TipoDeAlerta.EntregaAtrasada, SituacaoDoSla.Atrasada, entregaId, rotaId, previsao, explicacaoDaPrevisao);

    /// <summary>
    /// Motorista de rota em andamento, com entregas pendentes, sem posição há pelo menos o limite. Sem
    /// nenhuma posição desde a saída, conta a partir da saída.
    /// </summary>
    public static Constatacao MotoristaOffline(
        Guid motoristaId,
        Guid rotaId,
        bool rotaEmAndamento,
        DateTimeOffset rotaIniciadaEm,
        DateTimeOffset? ultimaPosicaoCapturadaEm,
        int entregasPendentes,
        DateTimeOffset agora,
        TimeSpan limite)
    {
        var referencia = ultimaPosicaoCapturadaEm ?? rotaIniciadaEm;
        var semPosicao = agora > referencia ? agora - referencia : TimeSpan.Zero;

        return Constatacao.Criar(
            TipoDeAlerta.MotoristaOffline,
            null,
            motoristaId,
            rotaId,
            rotaEmAndamento && entregasPendentes > 0 && semPosicao >= limite,
            new
            {
                ultimaPosicaoCapturadaEm,
                rotaIniciadaEm,
                minutosSemPosicao = (int)Math.Floor(semPosicao.TotalMinutes),
                limiteEmMinutos = (int)Math.Round(limite.TotalMinutes),
                entregasPendentes,
            });
    }

    /// <summary>
    /// Motorista de rota em andamento que permanece dentro do raio de imobilidade pelo menos o limite. Com uma
    /// entrega já próxima do destino, vale o limite de atendimento, maior. Motorista offline não é avaliado:
    /// sem posição nova não há como saber se continua parado.
    /// </summary>
    public static Constatacao ParadoTempoExcessivo(
        Guid motoristaId,
        Guid rotaId,
        bool rotaEmAndamento,
        bool motoristaOffline,
        PermanenciaNoLocal? permanencia,
        bool atendendoParada,
        TimeSpan limite,
        TimeSpan limiteAtendendoParada,
        double raioEmMetros)
    {
        var limiteAplicado = atendendoParada ? limiteAtendendoParada : limite;

        return Constatacao.Criar(
            TipoDeAlerta.ParadoTempoExcessivo,
            null,
            motoristaId,
            rotaId,
            rotaEmAndamento && !motoristaOffline && permanencia is not null && permanencia.Duracao >= limiteAplicado,
            new
            {
                paradoDesde = permanencia?.Inicio,
                ultimaPosicaoCapturadaEm = permanencia?.UltimaCaptura,
                minutosParado = permanencia is null ? 0 : (int)Math.Floor(permanencia.Duracao.TotalMinutes),
                limiteEmMinutos = (int)Math.Round(limiteAplicado.TotalMinutes),
                raioEmMetros,
                posicoesNoLocal = permanencia?.Posicoes ?? 0,
                atendendoParada,
            });
    }

    /// <summary>A entrega acumulou tentativas sem sucesso até o limite e ainda não foi entregue nem cancelada.</summary>
    public static Constatacao TentativasExcedidas(Entrega entrega, Guid? rotaId, int limite)
    {
        ArgumentNullException.ThrowIfNull(entrega);
        ArgumentOutOfRangeException.ThrowIfLessThan(limite, 1);

        return Constatacao.Criar(
            TipoDeAlerta.TentativasExcedidas,
            entrega.Id,
            null,
            rotaId,
            entrega.TentativasFrustradas >= limite && !RegrasDaEntrega.EhFinal(entrega.Status),
            new
            {
                tentativas = entrega.TentativasFrustradas,
                limite,
                ultimoMotivo = entrega.MotivoDaUltimaTentativa?.ToString(),
                ultimaTentativaEm = entrega.UltimaTentativaFrustradaEm,
                statusDaEntrega = entrega.Status.ToString(),
            });
    }

    private static Constatacao DaPrevisao(
        TipoDeAlerta tipo,
        SituacaoDoSla situacao,
        Guid entregaId,
        Guid rotaId,
        PrevisaoDaEntrega? previsao,
        string? explicacaoDaPrevisao) =>
        Constatacao.Criar(
            tipo,
            entregaId,
            null,
            rotaId,
            previsao is { Ativa: true } && previsao.Situacao == situacao,
            new
            {
                previsaoAtiva = previsao?.Ativa ?? false,
                situacao = previsao?.Situacao.ToString(),
                motivo = previsao?.MotivoDaSituacao.ToString(),
                folgaEmSegundos = previsao?.FolgaEmSegundos,
                chegadaPrevistaEm = previsao?.ChegadaPrevistaEm,
                janelaAte = previsao?.JanelaFim,
                sequenciaNoHistoricoDePrevisoes = previsao?.UltimaSequenciaDeRegistro,
                calculadaEm = previsao?.CalculadaEm,
                explicacaoDaPrevisao,
            });
}

/// <summary>
/// Alerta operacional com ciclo de vida: aberto, resolvido e, quando a regra define, reaberto.
/// </summary>
/// <remarks>
/// <para>
/// Não gerar centenas de alertas idênticos a cada avaliação: a <see cref="Chave"/> identifica o problema, e
/// só um alerta por chave fica aberto — garantido por índice único parcial no banco. Constatar de novo uma
/// condição que continua valendo atualiza a evidência, sem novo alerta nem novo evento.
/// </para>
/// <para>
/// Resolução automática quando a condição deixa de valer. Se ela voltar dentro da janela de reabertura, o
/// mesmo alerta reabre — é o mesmo problema oscilando, e o operador vê a contagem. Depois da janela, é um
/// alerta novo. Resolvido pelo operador com a condição ainda valendo não reabre enquanto ela persistir: a
/// decisão humana não é desfeita no próximo ciclo; só volta se a condição sumir e reaparecer.
/// </para>
/// </remarks>
public sealed class AlertaOperacional
{
    /// <summary>Tamanho máximo da observação de resolução.</summary>
    public const int TamanhoMaximoDaObservacao = 280;

    private AlertaOperacional()
    {
        Chave = string.Empty;
        EvidenciaDeAbertura = string.Empty;
        UltimaEvidencia = string.Empty;
    }

    /// <summary>Identificador (UUIDv7).</summary>
    public Guid Id { get; private set; }

    /// <summary>Organização.</summary>
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Tipo.</summary>
    public TipoDeAlerta Tipo { get; private set; }

    /// <summary>Severidade.</summary>
    public SeveridadeDoAlerta Severidade { get; private set; }

    /// <summary>Chave de deduplicação.</summary>
    public string Chave { get; private set; }

    /// <summary>Entrega, nos alertas de entrega.</summary>
    public Guid? EntregaId { get; private set; }

    /// <summary>Motorista, nos alertas de execução.</summary>
    public Guid? MotoristaId { get; private set; }

    /// <summary>Rota em que o problema foi constatado.</summary>
    public Guid? RotaId { get; private set; }

    /// <summary>Estado.</summary>
    public EstadoDoAlerta Estado { get; private set; }

    /// <summary>A condição valia na última constatação.</summary>
    public bool CondicaoAtiva { get; private set; }

    /// <summary>Evidência que abriu o alerta (JSON).</summary>
    public string EvidenciaDeAbertura { get; private set; }

    /// <summary>Evidência da última constatação (JSON).</summary>
    public string UltimaEvidencia { get; private set; }

    /// <summary>Abertura.</summary>
    public DateTimeOffset AbertoEm { get; private set; }

    /// <summary>Última constatação.</summary>
    public DateTimeOffset UltimaConstatacaoEm { get; private set; }

    /// <summary>Resolução.</summary>
    public DateTimeOffset? ResolvidoEm { get; private set; }

    /// <summary>Como foi resolvido.</summary>
    public FormaDeResolucao? FormaDeResolucao { get; private set; }

    /// <summary>Quem resolveu, quando foi um operador.</summary>
    public Guid? ResolvidoPorUsuarioId { get; private set; }

    /// <summary>Observação do operador ao resolver.</summary>
    public string? ObservacaoDaResolucao { get; private set; }

    /// <summary>Quantas vezes reabriu.</summary>
    public int Reaberturas { get; private set; }

    /// <summary>Sequência do último evento.</summary>
    public int UltimaSequenciaDeEvento { get; private set; }

    /// <summary>Versão da linha.</summary>
    public uint Versao { get; private set; }

    /// <summary>Abre o alerta de uma condição que vale.</summary>
    public static (AlertaOperacional Alerta, EventoDoAlerta Evento) Abrir(
        Guid id,
        Guid organizacaoId,
        Constatacao constatacao,
        Guid idDoEvento,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(constatacao);
        ExcecaoDeDominio.LancarSe(id == Guid.Empty, "identificador_invalido", "Identificador inválido.");
        ExcecaoDeDominio.LancarSe(organizacaoId == Guid.Empty, "organizacao_invalida", "Organização inválida.");
        ExcecaoDeDominio.LancarSe(!constatacao.Condicao, "condicao_ausente", "Só se abre alerta de condição que vale.");

        var instante = agora.ToUniversalTime();
        var alerta = new AlertaOperacional
        {
            Id = id,
            OrganizacaoId = organizacaoId,
            Tipo = constatacao.Tipo,
            Severidade = CatalogoDeAlertas.Severidade(constatacao.Tipo),
            Chave = constatacao.Chave,
            EntregaId = constatacao.EntregaId,
            MotoristaId = constatacao.MotoristaId,
            RotaId = constatacao.RotaId,
            Estado = EstadoDoAlerta.Aberto,
            CondicaoAtiva = true,
            EvidenciaDeAbertura = constatacao.Evidencia,
            UltimaEvidencia = constatacao.Evidencia,
            AbertoEm = instante,
            UltimaConstatacaoEm = instante,
        };

        return (alerta, alerta.Registrar(TipoDeEventoDoAlerta.Aberto, constatacao.Evidencia, null, null, idDoEvento, instante));
    }

    /// <summary>Aplica uma nova constatação da mesma chave.</summary>
    public (ResultadoDaConstatacao Resultado, EventoDoAlerta? Evento) Constatar(
        Constatacao constatacao,
        TimeSpan janelaDeReabertura,
        Guid idDoEvento,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(constatacao);
        ArgumentOutOfRangeException.ThrowIfLessThan(janelaDeReabertura, TimeSpan.Zero);
        ExcecaoDeDominio.LancarSe(constatacao.Chave != Chave, "constatacao_de_outro_alerta", "Constatação de outro alerta.");

        var instante = agora.ToUniversalTime();

        if (Estado == EstadoDoAlerta.Aberto)
        {
            UltimaEvidencia = constatacao.Evidencia;
            UltimaConstatacaoEm = instante;

            if (constatacao.Condicao)
            {
                CondicaoAtiva = true;
                return (ResultadoDaConstatacao.SemMudanca, null);
            }

            Estado = EstadoDoAlerta.Resolvido;
            CondicaoAtiva = false;
            ResolvidoEm = instante;
            FormaDeResolucao = Alertas.FormaDeResolucao.Automatica;
            ResolvidoPorUsuarioId = null;
            ObservacaoDaResolucao = null;
            return (ResultadoDaConstatacao.Resolvido, Registrar(TipoDeEventoDoAlerta.Resolvido, constatacao.Evidencia, null, null, idDoEvento, instante));
        }

        if (!constatacao.Condicao || CondicaoAtiva)
        {
            // Condição ausente, ou ainda a mesma que o operador já tratou: nada reabre.
            CondicaoAtiva = constatacao.Condicao;
            UltimaEvidencia = constatacao.Evidencia;
            UltimaConstatacaoEm = instante;
            return (ResultadoDaConstatacao.SemMudanca, null);
        }

        if (ResolvidoEm is { } resolvido && instante - resolvido > janelaDeReabertura)
        {
            return (ResultadoDaConstatacao.ExigeNovoAlerta, null);
        }

        Estado = EstadoDoAlerta.Aberto;
        CondicaoAtiva = true;
        Reaberturas++;
        ResolvidoEm = null;
        FormaDeResolucao = null;
        ResolvidoPorUsuarioId = null;
        ObservacaoDaResolucao = null;
        UltimaEvidencia = constatacao.Evidencia;
        UltimaConstatacaoEm = instante;
        return (ResultadoDaConstatacao.Reaberto, Registrar(TipoDeEventoDoAlerta.Reaberto, constatacao.Evidencia, null, null, idDoEvento, instante));
    }

    /// <summary>Resolução por um operador. Repetir não tem efeito.</summary>
    /// <returns>O evento, ou <see langword="null"/> se já estava resolvido.</returns>
    public EventoDoAlerta? ResolverPeloOperador(Guid usuarioId, string? observacao, Guid idDoEvento, DateTimeOffset agora)
    {
        ExcecaoDeDominio.LancarSe(usuarioId == Guid.Empty, "usuario_invalido", "Usuário inválido.");
        var observacaoValida = TextoNormalizado.Opcional(observacao, TamanhoMaximoDaObservacao, "observacao_invalida", "A observação");

        if (Estado == EstadoDoAlerta.Resolvido)
        {
            return null;
        }

        var instante = agora.ToUniversalTime();
        Estado = EstadoDoAlerta.Resolvido;
        ResolvidoEm = instante;
        FormaDeResolucao = Alertas.FormaDeResolucao.PeloOperador;
        ResolvidoPorUsuarioId = usuarioId;
        ObservacaoDaResolucao = observacaoValida;

        return Registrar(TipoDeEventoDoAlerta.ResolvidoPeloOperador, UltimaEvidencia, usuarioId, observacaoValida, idDoEvento, instante);
    }

    private EventoDoAlerta Registrar(
        TipoDeEventoDoAlerta tipo,
        string evidencia,
        Guid? usuarioId,
        string? observacao,
        Guid idDoEvento,
        DateTimeOffset instante)
    {
        ExcecaoDeDominio.LancarSe(idDoEvento == Guid.Empty, "identificador_invalido", "Identificador inválido.");

        UltimaSequenciaDeEvento++;
        return EventoDoAlerta.Registrar(idDoEvento, this, UltimaSequenciaDeEvento, tipo, evidencia, usuarioId, observacao, instante);
    }
}

/// <summary>Evento do ciclo de vida de um alerta — somente-inserção.</summary>
public sealed class EventoDoAlerta
{
    private EventoDoAlerta()
    {
        Evidencia = string.Empty;
    }

    /// <summary>Identificador (UUIDv7).</summary>
    public Guid Id { get; private set; }

    /// <summary>Organização.</summary>
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Alerta.</summary>
    public Guid AlertaId { get; private set; }

    /// <summary>Posição no ciclo de vida, a partir de 1.</summary>
    public int Sequencia { get; private set; }

    /// <summary>Tipo.</summary>
    public TipoDeEventoDoAlerta Tipo { get; private set; }

    /// <summary>Evidência no momento do evento (JSON).</summary>
    public string Evidencia { get; private set; }

    /// <summary>Operador, quando foi uma ação humana.</summary>
    public Guid? UsuarioId { get; private set; }

    /// <summary>Observação do operador.</summary>
    public string? Observacao { get; private set; }

    /// <summary>Instante.</summary>
    public DateTimeOffset OcorridoEm { get; private set; }

    internal static EventoDoAlerta Registrar(
        Guid id,
        AlertaOperacional alerta,
        int sequencia,
        TipoDeEventoDoAlerta tipo,
        string evidencia,
        Guid? usuarioId,
        string? observacao,
        DateTimeOffset instante) =>
        new()
        {
            Id = id,
            OrganizacaoId = alerta.OrganizacaoId,
            AlertaId = alerta.Id,
            Sequencia = sequencia,
            Tipo = tipo,
            Evidencia = evidencia,
            UsuarioId = usuarioId,
            Observacao = observacao,
            OcorridoEm = instante,
        };
}
