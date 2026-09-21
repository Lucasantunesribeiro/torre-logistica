using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TorreLogistica.Infrastructure.Persistencia;

namespace TorreLogistica.Workers;

/// <summary>
/// Confere, na subida do host de workers, se o banco está alcançável.
/// </summary>
/// <remarks>
/// Um worker sem banco não tem trabalho útil a fazer: ele apenas acumularia falhas
/// silenciosas a cada ciclo. Falhar na inicialização deixa o problema visível para
/// quem implantou, em vez de escondê-lo num processo aparentemente vivo.
/// </remarks>
public sealed class ServicoDeVerificacaoDeInfraestrutura(
    IServiceProvider provedorDeServicos,
    IHostApplicationLifetime cicloDeVida,
    ILogger<ServicoDeVerificacaoDeInfraestrutura> log) : IHostedService
{
    private readonly IServiceProvider _provedorDeServicos =
        provedorDeServicos ?? throw new ArgumentNullException(nameof(provedorDeServicos));

    private readonly IHostApplicationLifetime _cicloDeVida =
        cicloDeVida ?? throw new ArgumentNullException(nameof(cicloDeVida));

    private readonly ILogger<ServicoDeVerificacaoDeInfraestrutura> _log =
        log ?? throw new ArgumentNullException(nameof(log));

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var escopo = _provedorDeServicos.CreateAsyncScope();
        var contexto = escopo.ServiceProvider.GetRequiredService<TorreLogisticaDbContext>();

        var alcancavel = await contexto.Database
            .CanConnectAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!alcancavel)
        {
            _log.LogCritical(
                "Banco de dados inacessível na inicialização. O host de workers será encerrado.");
            _cicloDeVida.StopApplication();
            return;
        }

        _log.LogInformation(
            "Host de workers iniciado. Banco alcançável; os laços de fundo assumem daqui.");
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
