using System.Globalization;
using System.Text;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Previsao;

namespace TorreLogistica.Application.Previsao;

/// <summary>Os números de uma previsão ou de um registro, como a explicação os lê.</summary>
public sealed record DadosDaExplicacao(
    TipoDeRegistroDePrevisao? Tipo,
    SituacaoDoSla? SituacaoAnterior,
    SituacaoDoSla Situacao,
    MotivoDaSituacao Motivo,
    DateTimeOffset? ChegadaPrevistaEm,
    MotivoSemChegadaPrevista? MotivoSemChegadaPrevista,
    bool JaNoDestino,
    int? FolgaEmSegundos,
    int DeslocamentoEmSegundos,
    double DistanciaEmMetros,
    int ParadasAntes,
    int TempoDasParadasAntesEmSegundos,
    FonteDaPrevisao Fonte,
    string? Provedor,
    MotivoDaContingencia? MotivoDaContingencia,
    int LimiarDeAtencaoEmSegundos,
    int LimiarDeRiscoEmSegundos,
    DateTimeOffset? PosicaoCapturadaEm,
    DateTimeOffset CalculadaEm,
    StatusDaEntrega? StatusDaEntrega);

/// <summary>
/// Texto que explica uma previsão: qual regra decidiu a situação e de que parcelas a chegada prevista é feita.
/// </summary>
/// <remarks>
/// Só durações e distâncias — nenhum horário de relógio. Fuso é assunto de apresentação (CLAUDE.md, seção 49):
/// o console mostra os instantes, que vão em campos próprios, no fuso de quem olha. A explicação também não
/// leva coordenada nem dado do destinatário.
/// </remarks>
public static class ExplicacaoDaPrevisao
{
    /// <summary>Explicação da previsão atual — sem registro, portanto sem transição.</summary>
    public static string DaPrevisaoAtual(PrevisaoDaEntrega previsao)
    {
        ArgumentNullException.ThrowIfNull(previsao);

        return Montar(new DadosDaExplicacao(
            null,
            null,
            previsao.Situacao,
            previsao.MotivoDaSituacao,
            previsao.ChegadaPrevistaEm,
            previsao.MotivoSemChegadaPrevista,
            previsao.JaNoDestino,
            previsao.FolgaEmSegundos,
            previsao.DeslocamentoEmSegundos,
            previsao.DistanciaEmMetros,
            previsao.ParadasAntes,
            previsao.TempoDasParadasAntesEmSegundos,
            previsao.Fonte,
            previsao.Provedor,
            previsao.MotivoDaContingencia,
            previsao.LimiarDeAtencaoEmSegundos,
            previsao.LimiarDeRiscoEmSegundos,
            previsao.PosicaoCapturadaEm,
            previsao.CalculadaEm,
            null));
    }

    /// <summary>Monta a explicação.</summary>
    public static string Montar(DadosDaExplicacao dados)
    {
        ArgumentNullException.ThrowIfNull(dados);

        if (dados.Tipo == TipoDeRegistroDePrevisao.Encerrada)
        {
            return dados.StatusDaEntrega is { } status
                ? $"Previsão encerrada: a entrega saiu da execução com o status {NomeDoStatus(status)}. A última situação era {Nome(dados.Situacao)}."
                : $"Previsão encerrada: a entrega saiu da rota. A última situação era {Nome(dados.Situacao)}.";
        }

        var texto = new StringBuilder();
        texto.Append(Abertura(dados)).Append(' ').Append(Motivo(dados));

        if (Composicao(dados) is { } composicao)
        {
            texto.Append(' ').Append(composicao);
        }

        if (dados.PosicaoCapturadaEm is { } captura && dados.ChegadaPrevistaEm is not null && !dados.JaNoDestino)
        {
            var idade = dados.CalculadaEm - captura;
            texto.Append(CultureInfo.InvariantCulture, $" A posição do motorista usada foi capturada {Duracao(idade < TimeSpan.Zero ? TimeSpan.Zero : idade)} antes do cálculo.");
        }

        return texto.ToString();
    }

    private static string Abertura(DadosDaExplicacao dados) =>
        dados.SituacaoAnterior is { } anterior && anterior != dados.Situacao
            ? $"Passou de {Nome(anterior)} para {Nome(dados.Situacao)} porque"
            : dados.Tipo == TipoDeRegistroDePrevisao.Inicial
                ? $"Primeira previsão, {Nome(dados.Situacao)}, porque"
                : $"{Nome(dados.Situacao)} porque";

    private static string Motivo(DadosDaExplicacao dados)
    {
        var folga = TimeSpan.FromSeconds(dados.FolgaEmSegundos ?? 0);
        var atencao = Duracao(TimeSpan.FromSeconds(dados.LimiarDeAtencaoEmSegundos));
        var risco = Duracao(TimeSpan.FromSeconds(dados.LimiarDeRiscoEmSegundos));

        return dados.Motivo switch
        {
            MotivoDaSituacao.FolgaSuficiente =>
                $"a chegada prevista deixa {Duracao(folga)} de folga até o fim da janela prometida, sem ficar abaixo do limiar de atenção de {atencao}.",
            MotivoDaSituacao.FolgaAbaixoDoLimiarDeAtencao =>
                $"a folga até o fim da janela prometida é de {Duracao(folga)}, abaixo do limiar de atenção de {atencao}.",
            MotivoDaSituacao.FolgaAbaixoDoLimiarDeRisco =>
                $"a folga até o fim da janela prometida caiu para {Duracao(folga)}, abaixo do limiar de risco de {risco}.",
            MotivoDaSituacao.ChegadaPrevistaDepoisDaJanela =>
                $"a chegada prevista fica {Duracao(folga.Negate())} depois do fim da janela prometida.",
            MotivoDaSituacao.JanelaEncerrada =>
                $"a janela prometida terminou há {Duracao(folga.Negate())} sem a entrega concluída.",
            MotivoDaSituacao.SemPrevisao =>
                $"ainda não há chegada prevista ({SemChegada(dados.MotivoSemChegadaPrevista)}), e a janela prometida não está a menos de {atencao} do fim.",
            MotivoDaSituacao.SemPrevisaoComJanelaProximaDoFim =>
                $"não há chegada prevista ({SemChegada(dados.MotivoSemChegadaPrevista)}) e faltam menos de {atencao} para o fim da janela prometida.",
            _ => throw new ArgumentOutOfRangeException(nameof(dados), dados.Motivo, "Motivo de situação desconhecido."),
        };
    }

    private static string? Composicao(DadosDaExplicacao dados)
    {
        if (dados.ChegadaPrevistaEm is null)
        {
            return null;
        }

        if (dados.JaNoDestino)
        {
            return "O motorista já está no destino: a chegada prevista é a chegada registrada.";
        }

        var paradas = dados.ParadasAntes switch
        {
            0 => "nenhuma parada antes desta",
            1 => $"{Duracao(TimeSpan.FromSeconds(dados.TempoDasParadasAntesEmSegundos))} de 1 parada antes desta",
            _ => $"{Duracao(TimeSpan.FromSeconds(dados.TempoDasParadasAntesEmSegundos))} de {dados.ParadasAntes} paradas antes desta",
        };

        return $"A chegada prevista soma {Duracao(TimeSpan.FromSeconds(dados.DeslocamentoEmSegundos))} de deslocamento "
            + $"({Quilometros(dados.DistanciaEmMetros)}, {Fonte(dados)}) e {paradas}.";
    }

    private static string Fonte(DadosDaExplicacao dados) => dados.Fonte switch
    {
        FonteDaPrevisao.Provedor => $"pelo provedor de rotas {dados.Provedor}",
        FonteDaPrevisao.Contingencia => dados.MotivoDaContingencia switch
        {
            MotivoDaContingencia.ProvedorAusente => "estimado em linha reta porque não há provedor de rotas configurado",
            MotivoDaContingencia.TempoLimite => "estimado em linha reta porque o provedor de rotas não respondeu no tempo limite",
            MotivoDaContingencia.FalhaDoProvedor => "estimado em linha reta porque o provedor de rotas falhou",
            MotivoDaContingencia.RespostaInvalida => "estimado em linha reta porque o provedor de rotas respondeu com trajeto inválido",
            _ => "estimado em linha reta",
        },
        _ => "sem deslocamento calculado",
    };

    private static string SemChegada(MotivoSemChegadaPrevista? motivo) => motivo switch
    {
        MotivoSemChegadaPrevista.SemPosicaoDoMotorista => "o motorista ainda não enviou posição desde a saída",
        MotivoSemChegadaPrevista.DestinoSemCoordenada => "o destino não tem coordenada",
        _ => "sem dados para prever",
    };

    /// <summary>Nome da situação para leitura.</summary>
    public static string Nome(SituacaoDoSla situacao) => situacao switch
    {
        SituacaoDoSla.Normal => "Normal",
        SituacaoDoSla.Atencao => "Atenção",
        SituacaoDoSla.Risco => "Risco",
        SituacaoDoSla.Atrasada => "Atrasada",
        _ => situacao.ToString(),
    };

    private static string NomeDoStatus(StatusDaEntrega status) => status switch
    {
        StatusDaEntrega.Entregue => "Entregue",
        StatusDaEntrega.TentativaFrustrada => "Tentativa frustrada",
        StatusDaEntrega.Reagendada => "Reagendada",
        StatusDaEntrega.Cancelada => "Cancelada",
        _ => status.ToString(),
    };

    /// <summary>Duração legível, arredondada ao minuto.</summary>
    public static string Duracao(TimeSpan duracao)
    {
        var minutos = (long)Math.Round(duracao.Duration().TotalMinutes, MidpointRounding.AwayFromZero);

        if (minutos == 0)
        {
            return "menos de 1 min";
        }

        return minutos < 60
            ? string.Create(CultureInfo.InvariantCulture, $"{minutos} min")
            : string.Create(CultureInfo.InvariantCulture, $"{minutos / 60} h {minutos % 60:00} min");
    }

    // Formatação manual: o projeto roda com InvariantGlobalization, sem cultura pt-BR disponível.
    private static string Quilometros(double metros) =>
        (metros / 1000).ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') + " km";
}
