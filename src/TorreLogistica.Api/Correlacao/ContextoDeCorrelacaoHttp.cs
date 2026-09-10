using TorreLogistica.Application.Abstracoes.Correlacao;

namespace TorreLogistica.Api.Correlacao;

/// <summary>
/// Expõe para a camada de aplicação o identificador de correlação da requisição HTTP.
/// </summary>
public sealed class ContextoDeCorrelacaoHttp(IHttpContextAccessor acessorDeContexto)
    : IContextoDeCorrelacao
{
    /// <summary>Valor devolvido quando não há requisição HTTP em andamento.</summary>
    public const string SemCorrelacao = "sem-correlacao";

    private readonly IHttpContextAccessor _acessorDeContexto =
        acessorDeContexto ?? throw new ArgumentNullException(nameof(acessorDeContexto));

    /// <inheritdoc />
    public string IdDeCorrelacao =>
        _acessorDeContexto.HttpContext?.Items.TryGetValue(
            CorrelacaoHttp.ChaveNoContexto, out var valor) == true
        && valor is string identificador
            ? identificador
            : SemCorrelacao;
}
