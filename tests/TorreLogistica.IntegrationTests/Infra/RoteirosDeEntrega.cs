namespace TorreLogistica.IntegrationTests.Infra;

/// <summary>Corpos válidos de entrega para os testes.</summary>
/// <remarks>
/// A API de teste usa o relógio real, então as janelas são relativas ao dia de amanhã em UTC:
/// sempre no futuro, e sem depender da hora em que a suíte roda.
/// </remarks>
public static class RoteirosDeEntrega
{
    /// <summary>Amanhã, na hora cheia informada, em UTC.</summary>
    public static DateTimeOffset Amanha(int hora) =>
        new(DateTime.UtcNow.Date.AddDays(1).AddHours(hora), TimeSpan.Zero);

    /// <summary>Criação sem endereço: vale o do destinatário.</summary>
    public static object Corpo(
        Guid clienteId,
        Guid destinatarioId,
        DateTimeOffset? de = null,
        DateTimeOffset? ate = null,
        string? observacoes = null) => new
        {
            clienteId,
            destinatarioId,
            prometidaDe = de ?? Amanha(9),
            prometidaAte = ate ?? Amanha(12),
            observacoes,
        };
}
