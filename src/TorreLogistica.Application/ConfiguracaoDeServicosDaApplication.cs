using Microsoft.Extensions.DependencyInjection;
using TorreLogistica.Application.Alertas;
using TorreLogistica.Application.Cadastros;
using TorreLogistica.Application.Comprovantes;
using TorreLogistica.Application.Entregas;
using TorreLogistica.Application.Execucao;
using TorreLogistica.Application.Identidade;
using TorreLogistica.Application.Indicadores;
using TorreLogistica.Application.Integracoes;
using TorreLogistica.Application.Observabilidade;
using TorreLogistica.Application.Ocorrencias;
using TorreLogistica.Application.Organizacoes;
using TorreLogistica.Application.Previsao;
using TorreLogistica.Application.Rastreamento;
using TorreLogistica.Application.Rotas;
using TorreLogistica.Application.Usuarios;
using TorreLogistica.Application.Webhooks;

namespace TorreLogistica.Application;

/// <summary>
/// Registro da camada de aplicação no contêiner de DI, em duas metades.
/// </summary>
/// <remarks>
/// <para>
/// A divisão não é organizacional: ela separa casos de uso por <b>do que eles dependem</b>.
/// </para>
/// <list type="bullet">
/// <item><description>
/// <see cref="AdicionarCasosDeUsoDaOperacao"/> — o que funciona sem ninguém autenticado. É o trabalho que
/// acontece de madrugada: reavaliar previsão, abrir alerta, despachar webhook, apagar rastro vencido, medir
/// o estado da operação. Depende de persistência, relógio, identificador e opções, e de mais nada.
/// </description></item>
/// <item><description>
/// <see cref="AdicionarCasosDeUsoDaBorda"/> — o que só existe dentro de uma requisição autenticada. Tudo
/// aqui alcança, direta ou indiretamente, <c>IContextoDoUsuario</c> ou <c>IEmissorDeTokenDeAcesso</c>:
/// abstrações que a borda HTTP implementa e que processo de fundo nenhum pode satisfazer.
/// </description></item>
/// </list>
/// <para>
/// Por que isso importa: enquanto havia um registro só, o processo de trabalho precisava chamá-lo inteiro
/// para obter os seis casos de uso que executa, e arrastava junto quarenta que só a borda usa. Em produção
/// o contêiner nunca reclamava, porque a validação na construção fica desligada; em desenvolvimento, que a
/// liga, o processo <b>não subia</b>. O sintoma aparecia no ambiente errado, e a causa era esta: um
/// registro que não distinguia quem precisa de quê.
/// </para>
/// </remarks>
public static class ConfiguracaoDeServicosDaApplication
{
    /// <summary>
    /// Casos de uso que não dependem de usuário autenticado — os dois processos registram.
    /// </summary>
    /// <remarks>
    /// Esta é a lista mínima que o processo de trabalho precisa, e ela é curta de propósito: cada linha
    /// aqui é uma dependência que os laços de fundo realmente resolvem. Acrescentar algo que só a borda
    /// usa recoloca o problema que a divisão resolveu.
    /// </remarks>
    public static IServiceCollection AdicionarCasosDeUsoDaOperacao(this IServiceCollection servicos)
    {
        ArgumentNullException.ThrowIfNull(servicos);

        // Instrumentos do processo: uma instância só, independentes de escopo.
        servicos.AddSingleton<MetricasDePrevisao>();
        servicos.AddSingleton<MetricasDeAlertas>();
        servicos.AddSingleton<MedidasDaOperacao>();

        // Previsão: o laço recalcula, a borda lê o resultado.
        servicos.AddScoped<RecalculoDePrevisoes>();
        servicos.AddScoped<ConsultaDeRotasParaReavaliacao>();

        // Alertas: o motor que os constata roda no laço, logo depois do recálculo.
        servicos.AddScoped<MonitoramentoOperacional>();

        // Webhooks: reserva do outbox e entrega ao assinante.
        servicos.AddScoped<DespachoDeWebhooks>();
        servicos.AddScoped<EntregaDeWebhooks>();

        // Medidas: a leitura periódica que alimenta os indicadores do processo.
        servicos.AddScoped<LeituraDoEstadoDaOperacao>();

        return servicos;
    }

    /// <summary>
    /// Casos de uso que exigem um usuário autenticado no escopo — só a API registra.
    /// </summary>
    /// <remarks>
    /// Tudo com tempo de vida <c>scoped</c>: dependem do contexto de persistência e do usuário da
    /// requisição, que também são por escopo. Registrar qualquer um como singleton prenderia o contexto da
    /// primeira requisição.
    /// </remarks>
    public static IServiceCollection AdicionarCasosDeUsoDaBorda(this IServiceCollection servicos)
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
        servicos.AddScoped<ConsultaDoMotorista>();
        servicos.AddSingleton<MetricasDeSincronizacao>();
        servicos.AddScoped<SincronizacaoDoMotorista>();

        servicos.AddSingleton<MetricasDeRastreamento>();
        servicos.AddSingleton<MetricasDeGeofence>();
        servicos.AddScoped<AvaliacaoDeGeofence>();
        servicos.AddScoped<IngestaoDeLocalizacao>();
        servicos.AddScoped<ConsultaDeLocalizacao>();
        servicos.AddScoped<ConsultaDeGeofence>();

        servicos.AddScoped<ConsultaDePrevisao>();

        servicos.AddScoped<ConsultaDeIndicadores>();

        servicos.AddScoped<GestaoDeOcorrencias>();
        servicos.AddScoped<GestaoDeComprovantes>();
        servicos.AddScoped<GestaoDoRastreamentoPublico>();

        servicos.AddScoped<GestaoDeIntegracoes>();
        servicos.AddScoped<AutenticacaoDeIntegracao>();
        servicos.AddScoped<RecepcaoDeEntregasExternas>();
        servicos.AddScoped<ImportacaoDeEntregas>();

        servicos.AddScoped<GestaoDeAssinaturasDeWebhook>();
        servicos.AddScoped<ConsultaDeWebhooks>();

        servicos.AddScoped<GestaoDeAlertas>();

        return servicos;
    }

    /// <summary>As duas metades: a composição da API, que expõe a borda e lê o que a operação produz.</summary>
    public static IServiceCollection AdicionarCamadaDeApplication(this IServiceCollection servicos) =>
        servicos
            .AdicionarCasosDeUsoDaOperacao()
            .AdicionarCasosDeUsoDaBorda();
}
