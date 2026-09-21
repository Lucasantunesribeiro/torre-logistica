using System.Diagnostics;

namespace TorreLogistica.Application.Observabilidade;

/// <summary>
/// Fonte dos rastros de operação — o que acontece fora de uma requisição HTTP.
/// </summary>
/// <remarks>
/// <para>
/// A instrumentação automática cobre bem o que entra pela borda: requisição HTTP, comando SQL, chamada a
/// serviço externo. O que ela não vê é o trabalho que acontece depois que a resposta já foi devolvida —
/// despachar o outbox, entregar um webhook, recalcular previsão, apagar rastro vencido. Sem um rastro
/// próprio, esse trabalho não existe para quem investiga.
/// </para>
/// <para>
/// Um único <see cref="ActivitySource"/> para o projeto inteiro é deliberado: nome de fonte serve para
/// ligar e desligar coleta, e ninguém liga "recálculo de previsão" sem ligar o resto.
/// </para>
/// </remarks>
public static class RastroDaOperacao
{
    /// <summary>Nome da fonte, usado no registro da coleta.</summary>
    public const string Nome = "TorreLogistica.Operacao";

    /// <summary>Prefixo que cobre todas as fontes e medidores do projeto.</summary>
    public const string Prefixo = "TorreLogistica.*";

    /// <summary>A fonte.</summary>
    public static readonly ActivitySource Fonte = new(Nome);

    /// <summary>
    /// Abre um rastro para trabalho de segundo plano, ligado ao rastro que o originou.
    /// </summary>
    /// <param name="nome">Nome da operação.</param>
    /// <param name="rastroDeOrigem">
    /// O <c>traceparent</c> guardado quando o trabalho foi enfileirado, ou <see langword="null"/> quando
    /// não há origem conhecida.
    /// </param>
    /// <remarks>
    /// O elo é o que faz a investigação valer: sem ele, a entrega do webhook seria um rastro solto, e
    /// ninguém ligaria "o cliente não recebeu o aviso" à conclusão da entrega que devia tê-lo produzido.
    /// Quando a origem não é reconhecível, o rastro começa do zero em vez de falhar — telemetria não
    /// derruba operação.
    /// </remarks>
    public static Activity? Iniciar(string nome, string? rastroDeOrigem)
    {
        if (!string.IsNullOrWhiteSpace(rastroDeOrigem)
            && ActivityContext.TryParse(rastroDeOrigem, traceState: null, out var contexto))
        {
            return Fonte.StartActivity(nome, ActivityKind.Consumer, contexto);
        }

        return Fonte.StartActivity(nome, ActivityKind.Internal);
    }

    /// <summary>O <c>traceparent</c> do rastro atual, para guardar junto com o trabalho enfileirado.</summary>
    public static string? RastroAtual() => Activity.Current?.Id;
}
