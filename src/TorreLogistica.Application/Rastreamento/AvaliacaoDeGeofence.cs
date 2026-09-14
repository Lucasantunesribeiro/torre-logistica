using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Domain.Abstracoes.Identificadores;
using TorreLogistica.Domain.Abstracoes.Tempo;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Rastreamento;

namespace TorreLogistica.Application.Rastreamento;

/// <summary>Entregas e estados de geofence do motorista, carregados uma vez por lote.</summary>
public sealed class GeofencesDoMotorista
{
    internal GeofencesDoMotorista(Guid motoristaId, Dictionary<Guid, Entrega> entregas, Dictionary<Guid, EstadoDeGeofence> estados)
    {
        MotoristaId = motoristaId;
        Entregas = entregas;
        Estados = estados;
    }

    internal Guid MotoristaId { get; }

    internal Dictionary<Guid, Entrega> Entregas { get; }

    internal Dictionary<Guid, EstadoDeGeofence> Estados { get; }

    /// <summary>Há destino a avaliar.</summary>
    public bool TemDestinos => Entregas.Count > 0;
}

/// <summary>
/// Avalia a geofence do destino das entregas em execução do motorista, a cada posição que avança a
/// posição atual.
/// </summary>
/// <remarks>
/// <para>
/// Roda dentro da transação da ingestão. Só é chamada para posição confiável que avançou a posição
/// atual — posição atrasada não chega aqui, e o estado ainda recusa qualquer captura mais antiga que a
/// última avaliada. GPS antigo não produz transição retroativa por dois caminhos independentes.
/// </para>
/// <para>
/// Entrada na geofence de entrega em rota executa o comando <c>RegistrarProximidade</c> da máquina de
/// estados. Se o motorista registrou a chegada antes, o comando é repetição sem efeito.
/// </para>
/// </remarks>
public sealed class AvaliacaoDeGeofence(
    IContextoDePersistencia contexto,
    IGeradorDeIdentificador identificadores,
    IRelogio relogio,
    MetricasDeGeofence metricas,
    ILogger<AvaliacaoDeGeofence> log)
{
    private static readonly StatusDaEntrega[] EmExecucao = [StatusDaEntrega.EmRota, StatusDaEntrega.ProximaDoDestino];

    /// <summary>Carrega as entregas em execução do motorista que têm coordenada de destino.</summary>
    public async Task<GeofencesDoMotorista> PrepararAsync(Guid motoristaId, CancellationToken cancelamento)
    {
        var entregas = await contexto.Entregas
            .Where(entrega => entrega.MotoristaId == motoristaId
                && EmExecucao.Contains(entrega.Status)
                && entrega.Localizacao != null)
            .ToDictionaryAsync(entrega => entrega.Id, cancelamento)
            .ConfigureAwait(false);

        var ids = entregas.Keys.ToArray();
        Dictionary<Guid, EstadoDeGeofence> estados = ids.Length == 0
            ? []
            : await contexto.EstadosDeGeofence
                .Where(estado => ids.Contains(estado.EntregaId))
                .ToDictionaryAsync(estado => estado.EntregaId, cancelamento)
                .ConfigureAwait(false);

        return new GeofencesDoMotorista(motoristaId, entregas, estados);
    }

    /// <summary>Avalia a posição contra cada destino. As mudanças ficam no contexto, para a gravação do lote.</summary>
    /// <returns>Quantas transições houve.</returns>
    public async Task<int> AvaliarAsync(PosicaoDoMotorista posicao, GeofencesDoMotorista geofences, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(posicao);
        ArgumentNullException.ThrowIfNull(geofences);

        if (!geofences.TemDestinos)
        {
            return 0;
        }

        var distancias = await contexto.CalcularDistanciasAosDestinosAsync(
            geofences.Entregas.Keys,
            posicao.Localizacao.Latitude,
            posicao.Localizacao.Longitude,
            PoliticaDeGeofence.RaioDeChegadaEmMetros,
            PoliticaDeGeofence.RaioDeChegadaEmMetros + PoliticaDeGeofence.HistereseDeSaidaEmMetros,
            cancelamento).ConfigureAwait(false);

        var agora = relogio.AgoraUtc;
        var transicoes = 0;

        foreach (var distancia in distancias)
        {
            if (!geofences.Estados.TryGetValue(distancia.EntregaId, out var estado))
            {
                estado = EstadoDeGeofence.Iniciar(distancia.EntregaId, posicao.OrganizacaoId, geofences.MotoristaId, agora);
                geofences.Estados.Add(distancia.EntregaId, estado);
                contexto.EstadosDeGeofence.Add(estado);
            }

            var transicao = estado.Avaliar(
                distancia.DistanciaEmMetros,
                distancia.DentroDoRaio,
                distancia.DentroDaMargemDeSaida,
                posicao.MotoristaId,
                posicao.CapturadaEm,
                posicao.Sequencia,
                agora);

            if (transicao is not { } tipo)
            {
                continue;
            }

            transicoes++;
            contexto.EventosDeGeofence.Add(EventoDeGeofence.Registrar(identificadores.Novo(), estado, tipo, posicao.EventoDeLocalizacaoId, agora));
            metricas.Registrar(tipo);

            var entrega = geofences.Entregas[distancia.EntregaId];
            if (tipo == TipoDeTransicaoDeGeofence.Entrada
                && entrega.RegistrarProximidade(distancia.DistanciaEmMetros, estado.RaioEmMetros, identificadores.Novo(), agora) is { } proximidade)
            {
                contexto.EventosDaEntrega.Add(proximidade);
            }

            log.LogInformation(
                "Geofence do destino da entrega {EntregaId}: {Transicao} a {DistanciaEmMetros:F0} m (raio {RaioEmMetros} m).",
                entrega.Id, tipo, distancia.DistanciaEmMetros, estado.RaioEmMetros);
        }

        return transicoes;
    }
}

/// <summary>Métricas de geofence, no medidor de rastreamento.</summary>
public sealed class MetricasDeGeofence : IDisposable
{
    private readonly Meter _medidor = new(MetricasDeRastreamento.NomeDoMedidor);
    private readonly Counter<long> _entradas;
    private readonly Counter<long> _saidas;

    /// <summary>Cria os instrumentos.</summary>
    public MetricasDeGeofence()
    {
        _entradas = _medidor.CreateCounter<long>("geofence.entries", "{transicao}", "Entradas na geofence de destino.");
        _saidas = _medidor.CreateCounter<long>("geofence.exits", "{transicao}", "Saídas da geofence de destino.");
    }

    /// <summary>Conta uma transição.</summary>
    public void Registrar(TipoDeTransicaoDeGeofence tipo)
    {
        if (tipo == TipoDeTransicaoDeGeofence.Entrada)
        {
            _entradas.Add(1);
        }
        else
        {
            _saidas.Add(1);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _medidor.Dispose();
}
