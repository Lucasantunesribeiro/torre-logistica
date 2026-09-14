using TorreLogistica.Domain.Abstracoes.Erros;

namespace TorreLogistica.Domain.Rastreamento;

/// <summary>Raio e margem da geofence do destino.</summary>
/// <remarks>
/// <para>
/// A entrada vale a partir de <see cref="RaioDeChegadaEmMetros"/>; a saída só a partir de
/// <see cref="RaioDeChegadaEmMetros"/> + <see cref="HistereseDeSaidaEmMetros"/>. Sem a margem, um GPS
/// oscilando na borda — 298 m, 303 m, 299 m — geraria entrada, saída e entrada em sequência.
/// </para>
/// <para>
/// O raio fica registrado em cada estado e em cada evento: se a política mudar, o que foi decidido
/// antes continua explicável.
/// </para>
/// </remarks>
public static class PoliticaDeGeofence
{
    /// <summary>Distância ao destino que conta como chegada.</summary>
    public const double RaioDeChegadaEmMetros = 300;

    /// <summary>Margem além do raio antes de contar como saída.</summary>
    public const double HistereseDeSaidaEmMetros = 50;
}

/// <summary>Transição da geofence.</summary>
public enum TipoDeTransicaoDeGeofence
{
    /// <summary>Fora → dentro.</summary>
    Entrada = 1,

    /// <summary>Dentro → fora, além da margem.</summary>
    Saida = 2,
}

/// <summary>
/// Estado da geofence do destino de uma entrega: dentro ou fora, e a última posição avaliada.
/// </summary>
/// <remarks>
/// <para>
/// Geofence detecta <b>transição</b>, não "ponto dentro do raio" (CLAUDE.md, seção 23). Receber 30
/// posições dentro do raio produz uma entrada, não 30: o estado lembra que já está dentro.
/// </para>
/// <para>
/// A avaliação só aceita posição mais recente que a última avaliada, por (captura, sequência). Uma
/// posição antiga nunca decide transição — mesmo que chegue por outro caminho que não a posição atual.
/// </para>
/// </remarks>
public sealed class EstadoDeGeofence
{
    private EstadoDeGeofence()
    {
    }

    /// <summary>Entrega — chave.</summary>
    public Guid EntregaId { get; private set; }

    /// <summary>Organização da entrega.</summary>
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Motorista da última avaliação.</summary>
    public Guid MotoristaId { get; private set; }

    /// <summary>O motorista está dentro da geofence.</summary>
    public bool Dentro { get; private set; }

    /// <summary>Raio usado.</summary>
    public double RaioEmMetros { get; private set; }

    /// <summary>Distância ao destino na última avaliação.</summary>
    public double DistanciaEmMetros { get; private set; }

    /// <summary>Captura da última posição avaliada.</summary>
    public DateTimeOffset? UltimaCapturaAvaliadaEm { get; private set; }

    /// <summary>Sequência da última posição avaliada.</summary>
    public long UltimaSequenciaAvaliada { get; private set; }

    /// <summary>Quantas entradas já houve.</summary>
    public int Entradas { get; private set; }

    /// <summary>Última avaliação.</summary>
    public DateTimeOffset AtualizadaEm { get; private set; }

    /// <summary>Versão da linha.</summary>
    public uint Versao { get; private set; }

    /// <summary>Estado inicial: fora, sem avaliação.</summary>
    public static EstadoDeGeofence Iniciar(Guid entregaId, Guid organizacaoId, Guid motoristaId, DateTimeOffset agora)
    {
        ExcecaoDeDominio.LancarSe(
            entregaId == Guid.Empty || organizacaoId == Guid.Empty || motoristaId == Guid.Empty,
            "identificador_invalido",
            "Identificador inválido.");

        return new EstadoDeGeofence
        {
            EntregaId = entregaId,
            OrganizacaoId = organizacaoId,
            MotoristaId = motoristaId,
            RaioEmMetros = PoliticaDeGeofence.RaioDeChegadaEmMetros,
            AtualizadaEm = agora.ToUniversalTime(),
        };
    }

    /// <summary>Avalia uma posição.</summary>
    /// <param name="distanciaEmMetros">Distância geodésica ao destino, calculada pelo PostGIS.</param>
    /// <param name="dentroDoRaio">O ponto está a no máximo <see cref="RaioEmMetros"/> do destino (<c>ST_DWithin</c>).</param>
    /// <param name="dentroDaMargemDeSaida">O ponto está a no máximo o raio mais a histerese (<c>ST_DWithin</c>).</param>
    /// <param name="motoristaId">Motorista da posição.</param>
    /// <param name="capturadaEm">Captura da posição.</param>
    /// <param name="sequencia">Sequência da posição.</param>
    /// <param name="agora">Instante da avaliação.</param>
    /// <returns>A transição, ou <see langword="null"/> quando nada muda ou a posição é antiga.</returns>
    public TipoDeTransicaoDeGeofence? Avaliar(
        double distanciaEmMetros,
        bool dentroDoRaio,
        bool dentroDaMargemDeSaida,
        Guid motoristaId,
        DateTimeOffset capturadaEm,
        long sequencia,
        DateTimeOffset agora)
    {
        ExcecaoDeDominio.LancarSe(
            !double.IsFinite(distanciaEmMetros) || distanciaEmMetros < 0,
            "distancia_invalida",
            "Distância inválida.");

        var captura = capturadaEm.ToUniversalTime();
        if (UltimaCapturaAvaliadaEm is { } ultima
            && (captura < ultima || (captura == ultima && sequencia <= UltimaSequenciaAvaliada)))
        {
            return null;
        }

        UltimaCapturaAvaliadaEm = captura;
        UltimaSequenciaAvaliada = sequencia;
        DistanciaEmMetros = distanciaEmMetros;
        MotoristaId = motoristaId;
        AtualizadaEm = agora.ToUniversalTime();

        if (!Dentro && dentroDoRaio)
        {
            Dentro = true;
            Entradas++;
            return TipoDeTransicaoDeGeofence.Entrada;
        }

        if (Dentro && !dentroDaMargemDeSaida)
        {
            Dentro = false;
            return TipoDeTransicaoDeGeofence.Saida;
        }

        return null;
    }
}

/// <summary>
/// Entrada ou saída da geofence do destino — histórico somente-inserção.
/// </summary>
/// <remarks>
/// Registra a distância e o evento de localização que decidiu a transição, sem a coordenada: o que
/// explica o alerta está aqui, e a localização exata continua só no histórico de posições.
/// </remarks>
public sealed class EventoDeGeofence
{
    private EventoDeGeofence()
    {
    }

    /// <summary>Identificador (UUIDv7).</summary>
    public Guid Id { get; private set; }

    /// <summary>Organização.</summary>
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Entrega.</summary>
    public Guid EntregaId { get; private set; }

    /// <summary>Motorista.</summary>
    public Guid MotoristaId { get; private set; }

    /// <summary>Entrada ou saída.</summary>
    public TipoDeTransicaoDeGeofence Tipo { get; private set; }

    /// <summary>Distância ao destino na transição.</summary>
    public double DistanciaEmMetros { get; private set; }

    /// <summary>Raio em vigor.</summary>
    public double RaioEmMetros { get; private set; }

    /// <summary>Evento de localização que decidiu a transição.</summary>
    public Guid EventoDeLocalizacaoId { get; private set; }

    /// <summary>Captura da posição que decidiu a transição.</summary>
    public DateTimeOffset CapturadaEm { get; private set; }

    /// <summary>Instante do registro.</summary>
    public DateTimeOffset OcorridoEm { get; private set; }

    /// <summary>Registra a transição decidida pelo estado.</summary>
    public static EventoDeGeofence Registrar(
        Guid id,
        EstadoDeGeofence estado,
        TipoDeTransicaoDeGeofence tipo,
        Guid eventoDeLocalizacaoId,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(estado);
        ExcecaoDeDominio.LancarSe(id == Guid.Empty || eventoDeLocalizacaoId == Guid.Empty, "identificador_invalido", "Identificador inválido.");
        ExcecaoDeDominio.LancarSe(!Enum.IsDefined(tipo), "transicao_invalida", "Transição de geofence inválida.");

        return new EventoDeGeofence
        {
            Id = id,
            OrganizacaoId = estado.OrganizacaoId,
            EntregaId = estado.EntregaId,
            MotoristaId = estado.MotoristaId,
            Tipo = tipo,
            DistanciaEmMetros = estado.DistanciaEmMetros,
            RaioEmMetros = estado.RaioEmMetros,
            EventoDeLocalizacaoId = eventoDeLocalizacaoId,
            CapturadaEm = estado.UltimaCapturaAvaliadaEm ?? agora.ToUniversalTime(),
            OcorridoEm = agora.ToUniversalTime(),
        };
    }
}
