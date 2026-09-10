using Microsoft.Extensions.DependencyInjection;

namespace TorreLogistica.Application;

/// <summary>
/// Ponto único de registro da camada de aplicação no contêiner de DI.
/// </summary>
public static class ConfiguracaoDeServicosDaApplication
{
    /// <summary>
    /// Registra os serviços da camada de aplicação.
    /// </summary>
    /// <remarks>
    /// Na Fase 0 a camada existe com suas fronteiras declaradas e nenhum caso de uso:
    /// casos de uso nascem junto com as regras de negócio que os justificam.
    /// </remarks>
    public static IServiceCollection AdicionarCamadaDeApplication(this IServiceCollection servicos)
    {
        ArgumentNullException.ThrowIfNull(servicos);
        return servicos;
    }
}
