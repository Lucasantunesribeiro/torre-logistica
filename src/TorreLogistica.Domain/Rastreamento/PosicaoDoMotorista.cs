using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Comum;

namespace TorreLogistica.Domain.Rastreamento;

/// <summary>
/// Uma posição do histórico de GPS do motorista.
/// </summary>
/// <remarks>
/// <para>
/// O histórico guarda o que foi capturado — inclusive posições imprecisas e fora de ordem. A
/// posição atual é outra tabela (<see cref="PosicaoAtual"/>), atualizada só quando a posição é
/// confiável e mais recente que a vigente (CLAUDE.md, seções 18 e 19).
/// </para>
/// <para>
/// <see cref="CapturadaEm"/> é o relógio do aparelho no momento da leitura; <see cref="RecebidaEm"/>
/// é o relógio do servidor na chegada. Os dois nunca se fundem: a diferença é o atraso de ingestão,
/// e a ordem entre posições é decidida pela captura.
/// </para>
/// <para>
/// O identificador do evento é gerado pelo aplicativo antes do envio. Reenviar a mesma posição é
/// comportamento esperado, e produz um único registro.
/// </para>
/// </remarks>
public sealed class PosicaoDoMotorista
{
    private PosicaoDoMotorista()
    {
        Localizacao = null!;
    }

    /// <summary>Identificador (UUIDv7), gerado no servidor.</summary>
    public Guid Id { get; private set; }

    /// <summary>Organização do motorista.</summary>
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Motorista que enviou.</summary>
    public Guid MotoristaId { get; private set; }

    /// <summary>Rota em execução quando a posição foi capturada.</summary>
    public Guid? RotaId { get; private set; }

    /// <summary>Identificador do evento, gerado pelo aplicativo (<c>LocationEventId</c>).</summary>
    public Guid EventoDeLocalizacaoId { get; private set; }

    /// <summary>Contador crescente do aparelho (<c>Sequence</c>).</summary>
    public long Sequencia { get; private set; }

    /// <summary>Ponto capturado.</summary>
    public CoordenadaGeografica Localizacao { get; private set; }

    /// <summary>Raio de incerteza informado pelo aparelho, em metros.</summary>
    public double PrecisaoEmMetros { get; private set; }

    /// <summary>Velocidade informada pelo aparelho, quando houver.</summary>
    public double? VelocidadeEmMetrosPorSegundo { get; private set; }

    /// <summary>Direção de deslocamento em graus (0 = norte), quando houver.</summary>
    public double? DirecaoEmGraus { get; private set; }

    /// <summary>Instante da captura, pelo relógio do aparelho (<c>CapturedAt</c>).</summary>
    public DateTimeOffset CapturadaEm { get; private set; }

    /// <summary>Instante da chegada ao servidor (<c>ReceivedAt</c>).</summary>
    public DateTimeOffset RecebidaEm { get; private set; }

    /// <summary>Qualidade pela política de precisão.</summary>
    public QualidadeDaPosicao Qualidade { get; private set; }

    /// <summary>Valida a posição enviada contra a política e a registra.</summary>
    /// <exception cref="ExcecaoDeDominio">Quando a posição é recusada; o código é o motivo.</exception>
    public static PosicaoDoMotorista Registrar(
        Guid id,
        Guid organizacaoId,
        Guid motoristaId,
        Guid eventoDeLocalizacaoId,
        long sequencia,
        double latitude,
        double longitude,
        double precisaoEmMetros,
        double? velocidadeEmMetrosPorSegundo,
        double? direcaoEmGraus,
        DateTimeOffset capturadaEm,
        DateTimeOffset recebidaEm,
        IReadOnlyCollection<JanelaDeExecucaoDaRota> janelasDoMotorista)
    {
        ArgumentNullException.ThrowIfNull(janelasDoMotorista);
        ExcecaoDeDominio.LancarSe(
            id == Guid.Empty || organizacaoId == Guid.Empty || motoristaId == Guid.Empty,
            "identificador_invalido",
            "Identificador inválido.");
        ExcecaoDeDominio.LancarSe(eventoDeLocalizacaoId == Guid.Empty, "evento_de_localizacao_invalido", "Identificador do evento inválido.");
        ExcecaoDeDominio.LancarSe(sequencia < 0, "sequencia_invalida", "A sequência não pode ser negativa.");

        var coordenada = CoordenadaGeografica.Criar(latitude, longitude);

        ExcecaoDeDominio.LancarSe(
            !double.IsFinite(precisaoEmMetros) || precisaoEmMetros <= 0 || precisaoEmMetros > PoliticaDeLocalizacao.PrecisaoMaximaAceitaEmMetros,
            "precisao_invalida",
            $"A precisão precisa ser maior que zero e no máximo {PoliticaDeLocalizacao.PrecisaoMaximaAceitaEmMetros} metros.");

        ExcecaoDeDominio.LancarSe(
            velocidadeEmMetrosPorSegundo is { } velocidade
                && (!double.IsFinite(velocidade) || velocidade < 0 || velocidade > PoliticaDeLocalizacao.VelocidadeMaximaEmMetrosPorSegundo),
            "velocidade_invalida",
            "Velocidade fora da faixa plausível.");

        ExcecaoDeDominio.LancarSe(
            direcaoEmGraus is { } direcao && (!double.IsFinite(direcao) || direcao < 0 || direcao >= 360),
            "direcao_invalida",
            "A direção vai de 0 a 360 graus.");

        var recebida = TruncarAoMicrossegundo(recebidaEm);
        var capturada = TruncarAoMicrossegundo(capturadaEm);

        ExcecaoDeDominio.LancarSe(
            capturada > recebida + PoliticaDeLocalizacao.ToleranciaDeRelogioDoAparelho,
            "capturada_no_futuro",
            "A posição foi capturada no futuro: confira o relógio do aparelho.");

        ExcecaoDeDominio.LancarSe(
            capturada < recebida - PoliticaDeLocalizacao.IdadeMaximaAceita,
            "capturada_antiga_demais",
            "A posição é mais antiga que o horizonte de recuperação.");

        // Coleta mínima: fora da execução de uma rota, a localização do motorista não é necessária.
        var rota = janelasDoMotorista
            .Where(janela => janela.Contem(capturada))
            .OrderByDescending(janela => janela.IniciadaEm)
            .FirstOrDefault()
            ?? throw ExcecaoDeDominio.RegraViolada("fora_de_rota", "A posição não pertence a nenhuma rota em execução do motorista.");

        return new PosicaoDoMotorista
        {
            Id = id,
            OrganizacaoId = organizacaoId,
            MotoristaId = motoristaId,
            RotaId = rota.RotaId,
            EventoDeLocalizacaoId = eventoDeLocalizacaoId,
            Sequencia = sequencia,
            Localizacao = coordenada,
            PrecisaoEmMetros = precisaoEmMetros,
            VelocidadeEmMetrosPorSegundo = velocidadeEmMetrosPorSegundo,
            DirecaoEmGraus = direcaoEmGraus,
            CapturadaEm = capturada,
            RecebidaEm = recebida,
            Qualidade = precisaoEmMetros <= PoliticaDeLocalizacao.PrecisaoMaximaConfiavelEmMetros
                ? QualidadeDaPosicao.Confiavel
                : QualidadeDaPosicao.Imprecisa,
        };
    }

    // O PostgreSQL guarda microssegundos: truncar aqui mantém a ordem gravada igual à comparada.
    private static DateTimeOffset TruncarAoMicrossegundo(DateTimeOffset instante)
    {
        var utc = instante.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero);
    }
}

/// <summary>
/// Última posição confiável do motorista — projeção para abrir o mapa sem varrer o histórico.
/// </summary>
/// <remarks>
/// Escrita só pela gravação de posição, numa operação atômica que avança apenas quando a posição
/// nova é mais recente por (captura, sequência). Evento antigo nunca regressa a posição atual
/// (CLAUDE.md, seção 19).
/// </remarks>
public sealed class PosicaoAtual
{
    private PosicaoAtual()
    {
        Localizacao = null!;
    }

    /// <summary>Motorista — chave.</summary>
    public Guid MotoristaId { get; private set; }

    /// <summary>Organização do motorista.</summary>
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Rota em execução na captura.</summary>
    public Guid? RotaId { get; private set; }

    /// <summary>Evento que definiu esta posição.</summary>
    public Guid EventoDeLocalizacaoId { get; private set; }

    /// <summary>Sequência do aparelho.</summary>
    public long Sequencia { get; private set; }

    /// <summary>Ponto.</summary>
    public CoordenadaGeografica Localizacao { get; private set; }

    /// <summary>Precisão, em metros.</summary>
    public double PrecisaoEmMetros { get; private set; }

    /// <summary>Velocidade, quando houver.</summary>
    public double? VelocidadeEmMetrosPorSegundo { get; private set; }

    /// <summary>Direção, quando houver.</summary>
    public double? DirecaoEmGraus { get; private set; }

    /// <summary>Captura.</summary>
    public DateTimeOffset CapturadaEm { get; private set; }

    /// <summary>Chegada ao servidor.</summary>
    public DateTimeOffset RecebidaEm { get; private set; }

    /// <summary>Última vez que a projeção avançou.</summary>
    public DateTimeOffset AtualizadaEm { get; private set; }
}
