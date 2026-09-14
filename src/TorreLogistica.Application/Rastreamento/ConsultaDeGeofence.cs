using Microsoft.EntityFrameworkCore;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Rastreamento;

namespace TorreLogistica.Application.Rastreamento;

/// <summary>Transição de geofence como devolvida pela API.</summary>
public sealed record EventoDeGeofenceResumo(
    TipoDeTransicaoDeGeofence Tipo,
    double DistanciaEmMetros,
    double RaioEmMetros,
    Guid MotoristaId,
    DateTimeOffset CapturadaEm,
    DateTimeOffset OcorridoEm);

/// <summary>Geofence do destino de uma entrega.</summary>
/// <param name="Disponivel">A entrega tem coordenada de destino.</param>
/// <param name="RaioEmMetros">Raio de chegada.</param>
/// <param name="Dentro">O motorista está dentro, pela última avaliação.</param>
/// <param name="DistanciaEmMetros">Distância na última avaliação.</param>
/// <param name="UltimaAvaliacaoEm">Captura da última posição avaliada.</param>
/// <param name="Entradas">Quantas entradas houve.</param>
/// <param name="Eventos">Entradas e saídas, em ordem.</param>
public sealed record GeofenceResumo(
    bool Disponivel,
    double RaioEmMetros,
    bool Dentro,
    double? DistanciaEmMetros,
    DateTimeOffset? UltimaAvaliacaoEm,
    int Entradas,
    IReadOnlyList<EventoDeGeofenceResumo> Eventos);

/// <summary>Consulta da geofence do destino de uma entrega, para o console.</summary>
public sealed class ConsultaDeGeofence(IContextoDePersistencia contexto)
{
    /// <summary>Estado e transições da geofence do destino.</summary>
    public async Task<GeofenceResumo> ObterAsync(Guid entregaId, CancellationToken cancelamento)
    {
        var entrega = await contexto.Entregas
            .AsNoTracking()
            .Where(item => item.Id == entregaId)
            .Select(item => new { TemDestino = item.Localizacao != null })
            .SingleOrDefaultAsync(cancelamento)
            .ConfigureAwait(false)
            ?? throw ExcecaoDeDominio.NaoEncontrado("entrega_nao_encontrada", "Entrega não encontrada.");

        var estado = await contexto.EstadosDeGeofence
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.EntregaId == entregaId, cancelamento)
            .ConfigureAwait(false);

        var eventos = await contexto.EventosDeGeofence
            .AsNoTracking()
            .Where(evento => evento.EntregaId == entregaId)
            .OrderBy(evento => evento.OcorridoEm)
            .ThenBy(evento => evento.Id)
            .Select(evento => new EventoDeGeofenceResumo(
                evento.Tipo, evento.DistanciaEmMetros, evento.RaioEmMetros, evento.MotoristaId, evento.CapturadaEm, evento.OcorridoEm))
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

        return new GeofenceResumo(
            entrega.TemDestino,
            estado?.RaioEmMetros ?? PoliticaDeGeofence.RaioDeChegadaEmMetros,
            estado?.Dentro ?? false,
            estado?.DistanciaEmMetros,
            estado?.UltimaCapturaAvaliadaEm,
            estado?.Entradas ?? 0,
            eventos);
    }
}
