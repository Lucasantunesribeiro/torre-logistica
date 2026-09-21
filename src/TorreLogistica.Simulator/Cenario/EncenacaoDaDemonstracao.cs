using Microsoft.Extensions.Logging;
using TorreLogistica.Simulator.Cliente;
using TorreLogistica.Simulator.Configuracao;

namespace TorreLogistica.Simulator.Cenario;

/// <summary>O que uma história produziu, para conferência e para o relato final.</summary>
/// <param name="Historia">Qual história.</param>
/// <param name="Titulo">Título legível.</param>
/// <param name="EntregaId">A entrega encenada.</param>
/// <param name="Codigo">O código humano que o sistema atribuiu.</param>
/// <param name="StatusFinal">Em que estado a entrega terminou.</param>
/// <param name="Eventos">A linha do tempo, na ordem — é ela que define o "mesmo storytelling".</param>
public sealed record DesfechoDaHistoria(
    Historia Historia,
    string Titulo,
    Guid EntregaId,
    string Codigo,
    string StatusFinal,
    IReadOnlyList<string> Eventos);

/// <summary>
/// Encena o roteiro contra a API real.
/// </summary>
/// <remarks>
/// <para>
/// Toda mudança de estado acontece por chamada HTTP, autenticada, passando pela mesma máquina de estados
/// que a operação de verdade usa. Se uma história exige que a entrega vá para <c>ProximaDoDestino</c>, o
/// simulador não escreve esse estado: ele aproxima o motorista até o PostGIS calcular a distância e o
/// domínio decidir sozinho.
/// </para>
/// <para>
/// O tempo é comprimido pelo multiplicador. As janelas prometidas são criadas na mesma escala, de modo que
/// "faltam 20 minutos para o fim da janela" continue verdadeiro dentro da história — o que muda é quanto
/// tempo de relógio de parede a plateia espera.
/// </para>
/// </remarks>
public sealed class EncenacaoDaDemonstracao(
    ClienteDaApiDaTorre api,
    OpcoesDoSimulador opcoes,
    ILogger<EncenacaoDaDemonstracao> log)
{
    private readonly RoteiroDaDemonstracao _roteiro = new(opcoes.Semente);
    private long _sequenciaDePosicao;

    /// <summary>Encena o roteiro inteiro e devolve o desfecho de cada história.</summary>
    public async Task<IReadOnlyList<DesfechoDaHistoria>> EncenarAsync(CancellationToken cancelamento)
    {
        var palco = await PrepararPalcoAsync(cancelamento).ConfigureAwait(false);
        var desfechos = new List<DesfechoDaHistoria>(_roteiro.Capitulos.Count);

        foreach (var (capitulo, entregaId) in _roteiro.Capitulos.Zip(palco.Entregas))
        {
            log.LogInformation("História {Historia}: {Titulo}", capitulo.Historia, capitulo.Titulo);

            await EncenarCapituloAsync(palco, capitulo, entregaId, cancelamento).ConfigureAwait(false);
            desfechos.Add(await ApurarAsync(palco, capitulo, entregaId, cancelamento).ConfigureAwait(false));
        }

        return desfechos;
    }

    private async Task EncenarCapituloAsync(
        PalcoDaDemonstracao palco,
        CapituloDoRoteiro capitulo,
        Guid entregaId,
        CancellationToken cancelamento)
    {
        switch (capitulo.Historia)
        {
            case Historia.OperacaoNormal:
                // Para fora do raio do geofence, de propósito: aqui quem diz que chegou é o motorista.
                // Se o roteiro entrasse no raio, o sistema detectaria sozinho e esta história viraria a E.
                await AproximarAsync(palco, capitulo, paradas: 3, ateMetros: 500, cancelamento).ConfigureAwait(false);
                await api.RegistrarChegadaAsync(palco.TokenDoMotorista, entregaId, cancelamento).ConfigureAwait(false);
                await EsperarAsync(TimeSpan.FromMinutes(2), cancelamento).ConfigureAwait(false);
                await api.ConcluirEntregaAsync(palco.TokenDoMotorista, entregaId, cancelamento).ConfigureAwait(false);
                break;

            case Historia.RiscoDeAtraso:
                // O motorista mal sai do lugar: a distância não cai e a folga até o fim da janela some.
                // Quem detecta é a reavaliação periódica do servidor, não o simulador.
                await AproximarAsync(palco, capitulo, paradas: 3, ateMetros: capitulo.DistanciaInicialEmMetros - 400, cancelamento)
                    .ConfigureAwait(false);
                await EsperarAsync(opcoes.EsperaPelaTorre, cancelamento).ConfigureAwait(false);
                break;

            case Historia.MotoristaOffline:
                // Uma posição e silêncio. O alerta nasce do envelhecimento, que é trabalho do servidor.
                await EnviarPosicaoAsync(palco, capitulo, capitulo.DistanciaInicialEmMetros, cancelamento).ConfigureAwait(false);
                await EsperarAsync(opcoes.EsperaPelaTorre, cancelamento).ConfigureAwait(false);
                break;

            case Historia.TentativaFrustrada:
                await AproximarAsync(palco, capitulo, paradas: 2, ateMetros: 500, cancelamento).ConfigureAwait(false);
                await api.RegistrarChegadaAsync(palco.TokenDoMotorista, entregaId, cancelamento).ConfigureAwait(false);
                await api.RegistrarTentativaFrustradaAsync(
                        palco.TokenDoMotorista, entregaId, "DestinatarioAusente", "Ninguém atendeu; portaria fechada.", cancelamento)
                    .ConfigureAwait(false);
                break;

            case Historia.EntradaNoGeofence:
                // Sem chamar chegada: a aproximação basta, e o estado muda por conta do domínio.
                await AproximarAsync(palco, capitulo, paradas: 4, ateMetros: 80, cancelamento).ConfigureAwait(false);
                await EsperarAsync(opcoes.EsperaPelaTorre, cancelamento).ConfigureAwait(false);
                break;

            case Historia.ProvaDeEntrega:
                await AproximarAsync(palco, capitulo, paradas: 3, ateMetros: 500, cancelamento).ConfigureAwait(false);
                await api.RegistrarChegadaAsync(palco.TokenDoMotorista, entregaId, cancelamento).ConfigureAwait(false);
                await api.ConcluirComComprovanteAsync(
                        palco.TokenDoMotorista,
                        entregaId,
                        new
                        {
                            recebidoPor = "Portaria — " + capitulo.Destino.Nome,
                            observacao = "Entregue na recepção, conferido pelo porteiro.",
                            latitude = capitulo.Destino.Latitude,
                            longitude = capitulo.Destino.Longitude,
                            arquivos = Array.Empty<object>(),
                        },
                        cancelamento)
                    .ConfigureAwait(false);
                break;

            default:
                throw new InvalidOperationException($"História sem encenação: {capitulo.Historia}.");
        }
    }

    private async Task<DesfechoDaHistoria> ApurarAsync(
        PalcoDaDemonstracao palco,
        CapituloDoRoteiro capitulo,
        Guid entregaId,
        CancellationToken cancelamento)
    {
        var eventos = await api.ConsultarEventosDaEntregaAsync(palco.TokenDoSupervisor, entregaId, cancelamento)
            .ConfigureAwait(false);

        var tipos = eventos.EnumerateArray()
            .Select(evento => evento.GetProperty("tipo").GetString() ?? "?")
            .ToList();

        // O status vem da entrega, não do último evento: os dois costumam coincidir, mas não são a mesma
        // coisa — a timeline conta o que aconteceu, e o status diz onde a entrega parou.
        var entrega = await api.ConsultarEntregaAsync(palco.TokenDoSupervisor, entregaId, cancelamento).ConfigureAwait(false);
        var status = entrega.TryGetProperty("status", out var doStatus) ? doStatus.GetString() ?? "?" : "?";

        log.LogInformation(
            "História {Historia} terminou em {Status}, com {Quantidade} evento(s): {Eventos}",
            capitulo.Historia,
            status,
            tipos.Count,
            string.Join(" → ", tipos));

        return new DesfechoDaHistoria(capitulo.Historia, capitulo.Titulo, entregaId, palco.Codigos[entregaId], status, tipos);
    }

    /// <summary>Aproxima o motorista do destino em passos, enviando posição a cada um.</summary>
    private async Task AproximarAsync(
        PalcoDaDemonstracao palco,
        CapituloDoRoteiro capitulo,
        int paradas,
        int ateMetros,
        CancellationToken cancelamento)
    {
        var passo = (capitulo.DistanciaInicialEmMetros - ateMetros) / (double)paradas;

        for (var indice = 0; indice <= paradas; indice++)
        {
            var distancia = capitulo.DistanciaInicialEmMetros - (passo * indice);
            await EnviarPosicaoAsync(palco, capitulo, distancia, cancelamento).ConfigureAwait(false);
            await EsperarAsync(opcoes.IntervaloEntrePosicoes, cancelamento).ConfigureAwait(false);
        }
    }

    private async Task EnviarPosicaoAsync(
        PalcoDaDemonstracao palco,
        CapituloDoRoteiro capitulo,
        double distanciaEmMetros,
        CancellationToken cancelamento)
    {
        var (latitude, longitude) = RoteiroDaDemonstracao.AoSulDe(capitulo.Destino, distanciaEmMetros);

        await api.EnviarPosicoesAsync(
                palco.TokenDoMotorista,
                new
                {
                    posicoes = new[]
                    {
                        new
                        {
                            eventoDeLocalizacaoId = Guid.CreateVersion7(),
                            latitude,
                            longitude,
                            precisaoEmMetros = 8d,
                            capturadaEm = DateTimeOffset.UtcNow,
                            sequencia = Interlocked.Increment(ref _sequenciaDePosicao),
                            velocidadeEmMetrosPorSegundo = (double?)9,
                            direcaoEmGraus = (double?)0,
                        },
                    },
                },
                cancelamento)
            .ConfigureAwait(false);
    }

    /// <summary>Espera o tempo do roteiro, comprimido pelo multiplicador.</summary>
    private Task EsperarAsync(TimeSpan doRoteiro, CancellationToken cancelamento)
    {
        var real = doRoteiro / opcoes.MultiplicadorDeTempo;
        return real <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(real, cancelamento);
    }

    /// <summary>Monta cadastros, entregas e a rota — tudo pelas APIs do console.</summary>
    private async Task<PalcoDaDemonstracao> PrepararPalcoAsync(CancellationToken cancelamento)
    {
        var administrador = await api.EntrarNoConsoleAsync(
            opcoes.Organizacao, opcoes.EmailDoAdministrador, opcoes.Senha, cancelamento).ConfigureAwait(false);

        log.LogInformation("Preparando o palco da demonstração {Etiqueta} na organização {Organizacao}.", _roteiro.Etiqueta, opcoes.Organizacao);

        var contaDoMotorista = await api.CriarUsuarioAsync(
            administrador.TokenDeAcesso,
            _roteiro.NomeDoMotorista,
            _roteiro.EmailDoMotorista,
            opcoes.Senha,
            "Motorista",
            cancelamento).ConfigureAwait(false);

        var motorista = await api.CriarMotoristaAsync(
            administrador.TokenDeAcesso,
            new { nome = _roteiro.NomeDoMotorista, telefone = "(19) 99999-0000" },
            cancelamento).ConfigureAwait(false);

        await api.VincularContaDoMotoristaAsync(administrador.TokenDeAcesso, motorista, contaDoMotorista, cancelamento)
            .ConfigureAwait(false);

        var veiculo = await api.CriarVeiculoAsync(
            administrador.TokenDeAcesso,
            new
            {
                placa = _roteiro.PlacaDoVeiculo,
                identificacao = "Furgão da demonstração",
                tipo = "Utilitario",
                capacidadeEmKg = 900,
            },
            cancelamento).ConfigureAwait(false);

        var cliente = await api.CriarClienteAsync(
            administrador.TokenDeAcesso,
            new { nome = "Comércio Modelo " + _roteiro.Etiqueta, cnpj = _roteiro.CnpjDoCliente },
            cancelamento).ConfigureAwait(false);

        var agora = DateTimeOffset.UtcNow;
        var entregas = new List<Guid>(_roteiro.Capitulos.Count);
        var codigos = new Dictionary<Guid, string>();

        foreach (var capitulo in _roteiro.Capitulos)
        {
            var destinatario = await api.CriarDestinatarioAsync(
                administrador.TokenDeAcesso,
                new
                {
                    nome = capitulo.Destino.Nome,
                    telefone = "(19) 3456-7890",
                    endereco = new
                    {
                        logradouro = capitulo.Destino.Logradouro,
                        numero = capitulo.Destino.Numero,
                        complemento = (string?)null,
                        bairro = capitulo.Destino.Bairro,
                        cidade = "Campinas",
                        uf = "SP",
                        cep = "13010-000",
                    },
                    latitude = capitulo.Destino.Latitude,
                    longitude = capitulo.Destino.Longitude,
                    instrucoesDeEntrega = "Recepção; falar com a portaria.",
                },
                cancelamento).ConfigureAwait(false);

            // A janela inteira é criada na escala do roteiro — começo e fim. Comprimir só o fim faria a
            // promessa terminar antes de começar, e comprimir nenhum dos dois faria toda entrega nascer
            // atrasada numa demonstração acelerada.
            // O relógio é lido agora, e não no começo do preparo: montar doze cadastros leva tempo, e
            // numa demonstração acelerada esse tempo já consumiria a janela inteira antes de ela começar.
            var inicioDaJanela = DateTimeOffset.UtcNow.Add(TimeSpan.FromMinutes(1) / opcoes.MultiplicadorDeTempo);
            var janela = TimeSpan.FromMinutes(capitulo.MinutosDeJanela) / opcoes.MultiplicadorDeTempo;

            var entrega = await api.CriarEntregaAsync(
                administrador.TokenDeAcesso,
                new
                {
                    clienteId = cliente,
                    destinatarioId = destinatario,
                    prometidaDe = inicioDaJanela,
                    prometidaAte = inicioDaJanela.Add(janela),
                    observacoes = capitulo.Titulo,
                },
                cancelamento).ConfigureAwait(false);

            entregas.Add(entrega);
        }

        var rota = await api.CriarRotaAsync(
            administrador.TokenDeAcesso, DateOnly.FromDateTime(agora.UtcDateTime.Date), cancelamento).ConfigureAwait(false);

        await api.AdicionarParadasAsync(administrador.TokenDeAcesso, rota, entregas, cancelamento).ConfigureAwait(false);
        await api.AtribuirMotoristaAsync(administrador.TokenDeAcesso, rota, motorista, cancelamento).ConfigureAwait(false);
        await api.AtribuirVeiculoAsync(administrador.TokenDeAcesso, rota, veiculo, cancelamento).ConfigureAwait(false);
        // A saída planejada é informativa e não entra no SLA; uma folga de minutos reais evita que o
        // próprio tempo de preparo a empurre para o passado.
        await api.DefinirSaidaAsync(administrador.TokenDeAcesso, rota, DateTimeOffset.UtcNow.AddMinutes(5), cancelamento)
            .ConfigureAwait(false);
        await api.PlanejarRotaAsync(administrador.TokenDeAcesso, rota, cancelamento).ConfigureAwait(false);

        var sessaoDoMotorista = await api.EntrarComoMotoristaAsync(
            opcoes.Organizacao, _roteiro.EmailDoMotorista, opcoes.Senha, cancelamento).ConfigureAwait(false);

        await api.IniciarRotaAsync(sessaoDoMotorista.TokenDeAcesso, rota, cancelamento).ConfigureAwait(false);

        foreach (var entrega in entregas)
        {
            var eventos = await api.ConsultarEventosDaEntregaAsync(administrador.TokenDeAcesso, entrega, cancelamento)
                .ConfigureAwait(false);
            codigos[entrega] = eventos.GetArrayLength() > 0
                ? eventos[0].TryGetProperty("codigoDaEntrega", out var codigo) ? codigo.GetString() ?? "?" : "?"
                : "?";
        }

        log.LogInformation("Palco pronto: rota {Rota} iniciada com {Quantidade} entregas.", rota, entregas.Count);

        return new PalcoDaDemonstracao(
            administrador.TokenDeAcesso, sessaoDoMotorista.TokenDeAcesso, rota, entregas, codigos);
    }

    private sealed record PalcoDaDemonstracao(
        string TokenDoSupervisor,
        string TokenDoMotorista,
        Guid Rota,
        IReadOnlyList<Guid> Entregas,
        IReadOnlyDictionary<Guid, string> Codigos);
}
