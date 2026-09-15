using Microsoft.Extensions.Options;

namespace TorreLogistica.Application.Alertas;

/// <summary>
/// Limites das regras de alerta — seção <c>Torre:Alertas</c>.
/// </summary>
/// <remarks>
/// Documentados em <c>docs/adr/0019-motor-de-alertas-operacionais.md</c>. Cada alerta grava na evidência o
/// limite que usou.
/// </remarks>
public sealed class OpcoesDeAlertas
{
    /// <summary>Seção de configuração.</summary>
    public const string Secao = "Torre:Alertas";

    /// <summary>Tempo sem posição, com a rota em andamento, para o motorista contar como offline.</summary>
    public TimeSpan TempoSemPosicaoParaOffline { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Tempo no mesmo lugar, fora de atendimento, para alertar.</summary>
    public TimeSpan TempoParadoParaAlerta { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Tempo no mesmo lugar com uma entrega já próxima do destino (atendendo) para alertar.</summary>
    public TimeSpan TempoParadoAtendendoParadaParaAlerta { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Raio dentro do qual o motorista conta como no mesmo lugar.</summary>
    public double RaioDeImobilidadeEmMetros { get; set; } = 100;

    /// <summary>Tentativas sem sucesso a partir das quais a entrega alerta.</summary>
    public int LimiteDeTentativas { get; set; } = 2;

    /// <summary>Quanto tempo depois da resolução automática a volta da condição reabre o mesmo alerta.</summary>
    public TimeSpan JanelaDeReabertura { get; set; } = TimeSpan.FromMinutes(30);
}

/// <summary>Validação das opções na subida.</summary>
public sealed class ValidacaoDeOpcoesDeAlertas : IValidateOptions<OpcoesDeAlertas>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, OpcoesDeAlertas options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var falhas = new List<string>();

        if (options.TempoSemPosicaoParaOffline < TimeSpan.FromMinutes(1))
        {
            falhas.Add($"{nameof(options.TempoSemPosicaoParaOffline)} precisa ser de pelo menos 1 minuto.");
        }

        if (options.TempoParadoParaAlerta < TimeSpan.FromMinutes(1)
            || options.TempoParadoAtendendoParadaParaAlerta < options.TempoParadoParaAlerta)
        {
            falhas.Add($"{nameof(options.TempoParadoParaAlerta)} precisa ser de pelo menos 1 minuto, e {nameof(options.TempoParadoAtendendoParadaParaAlerta)} não pode ser menor que ele.");
        }

        if (!double.IsFinite(options.RaioDeImobilidadeEmMetros) || options.RaioDeImobilidadeEmMetros is < 10 or > 2_000)
        {
            falhas.Add($"{nameof(options.RaioDeImobilidadeEmMetros)} vai de 10 a 2.000 metros.");
        }

        if (options.LimiteDeTentativas is < 1 or > 20)
        {
            falhas.Add($"{nameof(options.LimiteDeTentativas)} vai de 1 a 20.");
        }

        if (options.JanelaDeReabertura < TimeSpan.Zero)
        {
            falhas.Add($"{nameof(options.JanelaDeReabertura)} não pode ser negativa.");
        }

        return falhas.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(falhas);
    }
}
