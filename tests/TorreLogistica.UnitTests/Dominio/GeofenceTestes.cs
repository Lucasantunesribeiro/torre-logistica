using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Comum;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Rastreamento;

namespace TorreLogistica.UnitTests.Dominio;

public sealed class EstadoDeGeofenceTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Motorista = Guid.CreateVersion7();

    [Fact]
    public void ForaParaDentroGeraUmaEntradaEPermanecerDentroNaoRepete()
    {
        var estado = NovoEstado();

        Assert.Null(Avaliar(estado, 301, segundos: 1));
        Assert.Equal(TipoDeTransicaoDeGeofence.Entrada, Avaliar(estado, 299, segundos: 2));

        for (var segundos = 3; segundos < 30; segundos++)
        {
            Assert.Null(Avaliar(estado, 120, segundos));
        }

        Assert.True(estado.Dentro);
        Assert.Equal(1, estado.Entradas);
        Assert.Equal(120, estado.DistanciaEmMetros);
    }

    [Fact]
    public void SaidaSoAlemDaHistereseEReentradaConta()
    {
        var estado = NovoEstado();
        Avaliar(estado, 100, segundos: 1);

        // Oscilação na borda: entre o raio e a margem, continua dentro.
        Assert.Null(Avaliar(estado, 320, segundos: 2));
        Assert.Null(Avaliar(estado, 350, segundos: 3));
        Assert.True(estado.Dentro);

        Assert.Equal(TipoDeTransicaoDeGeofence.Saida, Avaliar(estado, 351, segundos: 4));
        Assert.False(estado.Dentro);
        Assert.Null(Avaliar(estado, 800, segundos: 5));

        Assert.Equal(TipoDeTransicaoDeGeofence.Entrada, Avaliar(estado, 250, segundos: 6));
        Assert.Equal(2, estado.Entradas);
    }

    [Fact]
    public void PosicaoAntigaNaoDecideTransicaoNemMudaOEstado()
    {
        var estado = NovoEstado();
        Avaliar(estado, 900, segundos: 10, sequencia: 10);

        Assert.Null(Avaliar(estado, 50, segundos: 5, sequencia: 5));
        Assert.Null(Avaliar(estado, 50, segundos: 10, sequencia: 10));

        Assert.False(estado.Dentro);
        Assert.Equal(900, estado.DistanciaEmMetros);
        Assert.Equal(Agora.AddSeconds(10), estado.UltimaCapturaAvaliadaEm);
    }

    [Fact]
    public void MesmaCapturaComSequenciaMaiorEhAvaliada()
    {
        var estado = NovoEstado();
        Avaliar(estado, 900, segundos: 10, sequencia: 10);

        Assert.Equal(TipoDeTransicaoDeGeofence.Entrada, Avaliar(estado, 50, segundos: 10, sequencia: 11));
    }

    [Fact]
    public void DistanciaInvalidaEhRecusada()
    {
        var estado = NovoEstado();

        Assert.Equal("distancia_invalida", Assert.Throws<ExcecaoDeDominio>(() => Avaliar(estado, -1, segundos: 1)).Codigo);
        Assert.Equal("distancia_invalida", Assert.Throws<ExcecaoDeDominio>(() => Avaliar(estado, double.NaN, segundos: 1)).Codigo);
    }

    [Fact]
    public void EventoRegistraDistanciaRaioEPosicaoQueDecidiu()
    {
        var estado = NovoEstado();
        Avaliar(estado, 180, segundos: 7);
        var eventoDeLocalizacao = Guid.CreateVersion7();

        var evento = EventoDeGeofence.Registrar(Guid.CreateVersion7(), estado, TipoDeTransicaoDeGeofence.Entrada, eventoDeLocalizacao, Agora.AddSeconds(8));

        Assert.Equal(180, evento.DistanciaEmMetros);
        Assert.Equal(PoliticaDeGeofence.RaioDeChegadaEmMetros, evento.RaioEmMetros);
        Assert.Equal(eventoDeLocalizacao, evento.EventoDeLocalizacaoId);
        Assert.Equal(Agora.AddSeconds(7), evento.CapturadaEm);
        Assert.Equal(Motorista, evento.MotoristaId);
    }

    private static EstadoDeGeofence NovoEstado() =>
        EstadoDeGeofence.Iniciar(Guid.CreateVersion7(), Guid.CreateVersion7(), Motorista, Agora);

    /// <summary>
    /// Reproduz o que o PostGIS devolve: dentro do raio por <c>ST_DWithin</c> até o raio, inclusive; dentro da
    /// margem até raio + histerese, inclusive.
    /// </summary>
    private static TipoDeTransicaoDeGeofence? Avaliar(EstadoDeGeofence estado, double distancia, int segundos, long? sequencia = null) =>
        estado.Avaliar(
            distancia,
            dentroDoRaio: distancia <= PoliticaDeGeofence.RaioDeChegadaEmMetros,
            dentroDaMargemDeSaida: distancia <= PoliticaDeGeofence.RaioDeChegadaEmMetros + PoliticaDeGeofence.HistereseDeSaidaEmMetros,
            Motorista,
            Agora.AddSeconds(segundos),
            sequencia ?? segundos,
            Agora.AddSeconds(segundos));
}

public sealed class ProximidadeDaEntregaTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ProximidadeLevaEntregaEmRotaAProximaDoDestinoUmaVez()
    {
        var entrega = EntregaEmRota();

        var evento = entrega.RegistrarProximidade(212.34, 300, Guid.CreateVersion7(), Agora);

        Assert.NotNull(evento);
        Assert.Equal(StatusDaEntrega.ProximaDoDestino, entrega.Status);
        Assert.Equal(TipoDeEventoDaEntrega.ProximidadeDetectada, evento.Tipo);
        Assert.Null(evento.AutorUsuarioId);
        Assert.Contains("212.3", evento.Dados, StringComparison.Ordinal);
        Assert.Equal(Agora, entrega.ChegadaRegistradaEm);

        Assert.Null(entrega.RegistrarProximidade(100, 300, Guid.CreateVersion7(), Agora));
        Assert.Null(entrega.RegistrarChegada(null, Guid.CreateVersion7(), Agora));
    }

    [Fact]
    public void ProximidadeNaoTransicionaForaDeRota()
    {
        var entrega = EntregaEmRota();
        entrega.Concluir(null, Guid.CreateVersion7(), Agora);

        Assert.Equal("transicao_invalida", Assert.Throws<ExcecaoDeDominio>(() =>
            entrega.RegistrarProximidade(10, 300, Guid.CreateVersion7(), Agora)).Codigo);
        Assert.Equal(StatusDaEntrega.Entregue, entrega.Status);
    }

    private static Entrega EntregaEmRota()
    {
        var rota = Guid.CreateVersion7();
        var entrega = Entrega.Criar(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "ENT-2026-000001",
            new DadosDaEntrega(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Endereco.Criar("Rua das Palmeiras", "120", null, "Centro", "Campinas", "SP", "13010000"),
                CoordenadaGeografica.Criar(-22.91, -47.065),
                JanelaDeEntrega.Criar(Agora.AddHours(1), Agora.AddHours(3)),
                null),
            null,
            Guid.CreateVersion7(),
            Agora).Entrega;

        entrega.Planejar(rota, null, Guid.CreateVersion7(), Agora);
        entrega.Atribuir(rota, Guid.CreateVersion7(), null, Guid.CreateVersion7(), Agora);
        entrega.IniciarRota(rota, null, Guid.CreateVersion7(), Agora);
        return entrega;
    }
}
