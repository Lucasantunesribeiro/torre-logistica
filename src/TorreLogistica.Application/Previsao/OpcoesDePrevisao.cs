using Microsoft.Extensions.Options;

namespace TorreLogistica.Application.Previsao;

/// <summary>
/// Parâmetros da previsão de chegada e do SLA — seção <c>Torre:Previsao</c>.
/// </summary>
/// <remarks>
/// Documentados em <c>docs/adr/0018-previsao-de-chegada-e-sla.md</c>. Cada previsão grava os limiares, o
/// tempo por parada e a fonte que usou: mudar um número aqui não reescreve a explicação do que já foi
/// registrado.
/// </remarks>
public sealed class OpcoesDePrevisao
{
    /// <summary>Seção de configuração.</summary>
    public const string Secao = "Torre:Previsao";

    /// <summary>Valor de <see cref="Provedor"/> que desliga o provedor de rotas.</summary>
    public const string SemProvedor = "Nenhum";

    /// <summary>
    /// Nome do provedor de rotas em uso — <c>simulado</c> por padrão, ou <see cref="SemProvedor"/>. Nome sem
    /// provedor registrado é tratado como provedor ausente: a previsão usa a contingência e registra o motivo.
    /// </summary>
    public string Provedor { get; set; } = "simulado";

    /// <summary>Tempo médio de atendimento em cada parada.</summary>
    public TimeSpan TempoMedioPorParada { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Folga até o fim da janela abaixo da qual a entrega entra em atenção.</summary>
    public TimeSpan FolgaParaAtencao { get; set; } = TimeSpan.FromMinutes(20);

    /// <summary>Folga até o fim da janela abaixo da qual a entrega entra em risco.</summary>
    public TimeSpan FolgaParaRisco { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Quanto a chegada prevista precisa andar, sem mudança de situação, para entrar no histórico.</summary>
    public TimeSpan MudancaRelevanteDaChegadaPrevista { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Tempo máximo de espera pelo provedor de rotas.</summary>
    public TimeSpan TempoLimiteDoProvedor { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Velocidade média da contingência em linha reta (6 m/s ≈ 21,6 km/h).</summary>
    public double VelocidadeDeContingenciaEmMetrosPorSegundo { get; set; } = 6;

    /// <summary>Quanto o caminho real é maior que a linha reta, na contingência.</summary>
    public double FatorDeSinuosidadeDeContingencia { get; set; } = 1.4;

    /// <summary>Intervalo da reavaliação periódica das rotas em andamento.</summary>
    public TimeSpan IntervaloDeReavaliacao { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Intervalo mínimo entre recálculos da mesma rota disparados por posição.</summary>
    public TimeSpan IntervaloMinimoEntreRecalculosPorPosicao { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Parâmetros do provedor simulado.</summary>
    public OpcoesDoProvedorSimulado ProvedorSimulado { get; set; } = new();
}

/// <summary>Parâmetros do provedor de rotas simulado.</summary>
public sealed class OpcoesDoProvedorSimulado
{
    /// <summary>Nome do provedor simulado.</summary>
    public const string Nome = "simulado";

    /// <summary>Velocidade média urbana (8,5 m/s ≈ 30,6 km/h).</summary>
    public double VelocidadeMediaEmMetrosPorSegundo { get; set; } = 8.5;

    /// <summary>Quanto o caminho por ruas é maior que a linha reta.</summary>
    public double FatorDeSinuosidade { get; set; } = 1.3;
}

/// <summary>Validação das opções na subida: configuração incoerente é erro de implantação.</summary>
public sealed class ValidacaoDeOpcoesDePrevisao : IValidateOptions<OpcoesDePrevisao>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, OpcoesDePrevisao options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var falhas = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Provedor))
        {
            falhas.Add($"{nameof(options.Provedor)} precisa ser informado (use \"{OpcoesDePrevisao.SemProvedor}\" para desligar).");
        }

        if (options.TempoMedioPorParada < TimeSpan.Zero || options.TempoMedioPorParada > TimeSpan.FromHours(2))
        {
            falhas.Add($"{nameof(options.TempoMedioPorParada)} vai de zero a 2 horas.");
        }

        if (options.FolgaParaRisco < TimeSpan.Zero || options.FolgaParaAtencao <= options.FolgaParaRisco)
        {
            falhas.Add($"{nameof(options.FolgaParaRisco)} não pode ser negativa e precisa ser menor que {nameof(options.FolgaParaAtencao)}.");
        }

        if (options.MudancaRelevanteDaChegadaPrevista < TimeSpan.Zero)
        {
            falhas.Add($"{nameof(options.MudancaRelevanteDaChegadaPrevista)} não pode ser negativa.");
        }

        if (options.TempoLimiteDoProvedor <= TimeSpan.Zero || options.TempoLimiteDoProvedor > TimeSpan.FromSeconds(30))
        {
            falhas.Add($"{nameof(options.TempoLimiteDoProvedor)} vai de mais de zero a 30 segundos.");
        }

        if (options.IntervaloDeReavaliacao < TimeSpan.FromSeconds(1))
        {
            falhas.Add($"{nameof(options.IntervaloDeReavaliacao)} precisa ser de pelo menos 1 segundo.");
        }

        if (options.IntervaloMinimoEntreRecalculosPorPosicao < TimeSpan.Zero)
        {
            falhas.Add($"{nameof(options.IntervaloMinimoEntreRecalculosPorPosicao)} não pode ser negativo.");
        }

        ValidarVelocidade(falhas, nameof(options.VelocidadeDeContingenciaEmMetrosPorSegundo), options.VelocidadeDeContingenciaEmMetrosPorSegundo);
        ValidarFator(falhas, nameof(options.FatorDeSinuosidadeDeContingencia), options.FatorDeSinuosidadeDeContingencia);

        if (options.ProvedorSimulado is null)
        {
            falhas.Add($"{nameof(options.ProvedorSimulado)} precisa ser informado.");
        }
        else
        {
            ValidarVelocidade(falhas, "ProvedorSimulado:VelocidadeMediaEmMetrosPorSegundo", options.ProvedorSimulado.VelocidadeMediaEmMetrosPorSegundo);
            ValidarFator(falhas, "ProvedorSimulado:FatorDeSinuosidade", options.ProvedorSimulado.FatorDeSinuosidade);
        }

        return falhas.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(falhas);
    }

    private static void ValidarVelocidade(List<string> falhas, string nome, double valor)
    {
        if (!double.IsFinite(valor) || valor <= 0 || valor > 70)
        {
            falhas.Add($"{nome} vai de mais de zero a 70 m/s.");
        }
    }

    private static void ValidarFator(List<string> falhas, string nome, double valor)
    {
        if (!double.IsFinite(valor) || valor < 1 || valor > 3)
        {
            falhas.Add($"{nome} vai de 1 a 3.");
        }
    }
}
