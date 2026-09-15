using Microsoft.Extensions.DependencyInjection;
using TorreLogistica.Application.Alertas;
using TorreLogistica.Application.Cadastros;
using TorreLogistica.Application.Entregas;
using TorreLogistica.Application.Execucao;
using TorreLogistica.Application.Identidade;
using TorreLogistica.Application.Organizacoes;
using TorreLogistica.Application.Previsao;
using TorreLogistica.Application.Rastreamento;
using TorreLogistica.Application.Rotas;
using TorreLogistica.Application.Usuarios;

namespace TorreLogistica.Application;

/// <summary>
/// Ponto único de registro da camada de aplicação no contêiner de DI.
/// </summary>
public static class ConfiguracaoDeServicosDaApplication
{
    /// <summary>Registra os casos de uso da camada de aplicação.</summary>
    /// <remarks>
    /// Tudo com tempo de vida <c>scoped</c>: os casos de uso dependem do contexto de
    /// persistência e do usuário da requisição, que também são por escopo. Registrar
    /// qualquer um deles como singleton prenderia o contexto da primeira requisição.
    /// </remarks>
    public static IServiceCollection AdicionarCamadaDeApplication(this IServiceCollection servicos)
    {
        ArgumentNullException.ThrowIfNull(servicos);

        servicos.AddScoped<AutenticarUsuario>();
        servicos.AddScoped<RenovarSessao>();
        servicos.AddScoped<EncerrarSessao>();
        servicos.AddScoped<ValidarSessaoAtiva>();
        servicos.AddScoped<GestaoDeUsuarios>();
        servicos.AddScoped<ConsultaDeOrganizacao>();

        servicos.AddScoped<SuporteDeCadastro>();
        servicos.AddScoped<GestaoDeMotoristas>();
        servicos.AddScoped<GestaoDeVeiculos>();
        servicos.AddScoped<GestaoDeHubs>();
        servicos.AddScoped<GestaoDeClientes>();
        servicos.AddScoped<GestaoDeDestinatarios>();

        servicos.AddScoped<GestaoDeEntregas>();
        servicos.AddScoped<GestaoDeRotas>();
        servicos.AddScoped<ExecucaoPeloMotorista>();

        // Métricas são instrumentos do processo: uma instância só.
        servicos.AddSingleton<MetricasDeRastreamento>();
        servicos.AddSingleton<MetricasDeGeofence>();
        servicos.AddScoped<AvaliacaoDeGeofence>();
        servicos.AddScoped<IngestaoDeLocalizacao>();
        servicos.AddScoped<ConsultaDeLocalizacao>();
        servicos.AddScoped<ConsultaDeGeofence>();

        servicos.AddSingleton<MetricasDePrevisao>();
        servicos.AddScoped<RecalculoDePrevisoes>();
        servicos.AddScoped<ConsultaDeRotasParaReavaliacao>();
        servicos.AddScoped<ConsultaDePrevisao>();

        servicos.AddSingleton<MetricasDeAlertas>();
        servicos.AddScoped<MonitoramentoOperacional>();
        servicos.AddScoped<GestaoDeAlertas>();

        return servicos;
    }
}
