using TorreLogistica.Domain.Abstracoes.Erros;

namespace TorreLogistica.Domain.Entregas;

/// <summary>
/// Janela prometida ao destinatário: de quando a quando a entrega pode chegar.
/// </summary>
/// <remarks>
/// <para>
/// Janela, e não horário único (CLAUDE.md, seção 26): SLA se mede contra o fim da janela, e
/// chegar antes do início também é descumprir o combinado.
/// </para>
/// <para>
/// Os instantes são guardados em UTC e truncados ao segundo. O PostgreSQL guarda
/// microssegundos e o .NET, décimos de microssegundo: sem truncar, reenviar a mesma janela lida
/// da API contaria como alteração.
/// </para>
/// </remarks>
public sealed record JanelaDeEntrega
{
    /// <summary>Duração máxima de uma janela.</summary>
    public static readonly TimeSpan DuracaoMaxima = TimeSpan.FromDays(7);

    private JanelaDeEntrega()
    {
    }

    private JanelaDeEntrega(DateTimeOffset inicio, DateTimeOffset fim)
    {
        Inicio = inicio;
        Fim = fim;
    }

    /// <summary>Início da janela, em UTC.</summary>
    public DateTimeOffset Inicio { get; private set; }

    /// <summary>Fim da janela, em UTC.</summary>
    public DateTimeOffset Fim { get; private set; }

    /// <summary>Cria uma janela válida.</summary>
    public static JanelaDeEntrega Criar(DateTimeOffset inicio, DateTimeOffset fim)
    {
        var inicioUtc = TruncarAoSegundo(inicio);
        var fimUtc = TruncarAoSegundo(fim);

        ExcecaoDeDominio.LancarSe(
            fimUtc <= inicioUtc,
            "janela_invalida",
            "O fim da janela prometida deve ser posterior ao início.");

        ExcecaoDeDominio.LancarSe(
            fimUtc - inicioUtc > DuracaoMaxima,
            "janela_invalida",
            "A janela prometida pode durar no máximo 7 dias.");

        return new JanelaDeEntrega(inicioUtc, fimUtc);
    }

    private static DateTimeOffset TruncarAoSegundo(DateTimeOffset instante)
    {
        var utc = instante.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
    }
}
