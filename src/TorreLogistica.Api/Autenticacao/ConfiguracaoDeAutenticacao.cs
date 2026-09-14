using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TorreLogistica.Api.Erros;
using TorreLogistica.Application.Abstracoes.Seguranca;
using TorreLogistica.Application.Identidade;
using TorreLogistica.Domain.Identidade;

namespace TorreLogistica.Api.Autenticacao;

/// <summary>
/// Autenticação, autorização e limite de requisições da API.
/// </summary>
public static class ConfiguracaoDeAutenticacao
{
    /// <summary>Tolerância de relógio entre emissão e validação.</summary>
    public static readonly TimeSpan ToleranciaDeRelogio = TimeSpan.FromSeconds(30);

    /// <summary>Registra esquemas, políticas e limites.</summary>
    public static IServiceCollection AdicionarAutenticacaoDaTorre(
        this IServiceCollection servicos,
        IConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(servicos);
        ArgumentNullException.ThrowIfNull(configuracao);

        servicos.AddOptions<OpcoesDeTokenDeAcesso>()
            .Bind(configuracao.GetSection(OpcoesDeTokenDeAcesso.Secao))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        servicos.AddOptions<OpcoesDeAutenticacao>()
            .Bind(configuracao.GetSection(OpcoesDeAutenticacao.Secao))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        servicos.AddOptions<OpcoesDeLimiteDeRequisicoes>()
            .Bind(configuracao.GetSection(OpcoesDeLimiteDeRequisicoes.Secao))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        servicos.AddSingleton<ChaveDeAssinaturaDeToken>();
        servicos.AddSingleton<IEmissorDeTokenDeAcesso, EmissorDeTokenDeAcessoJwt>();

        servicos.AddAuthentication()
            .AddJwtBearer(EsquemasDeAutenticacao.Operacao)
            .AddJwtBearer(EsquemasDeAutenticacao.Motorista);

        foreach (var canal in Enum.GetValues<CanalDeAcesso>())
        {
            ConfigurarEsquema(servicos, canal);
        }

        servicos.AddAuthorizationBuilder()
            // Endpoint sem política declarada exige sessão do console. Esquecer a
            // anotação não abre o endpoint ao público: falha fechada.
            .SetFallbackPolicy(PoliticaDoConsole().Build())
            .AddPolicy(Politicas.Console, PoliticaDoConsole()
                .RequireRole(nameof(Perfil.Administrador), nameof(Perfil.Supervisor), nameof(Perfil.Operador))
                .Build())
            .AddPolicy(Politicas.LeituraDeUsuarios, PoliticaDoConsole()
                .RequireRole(nameof(Perfil.Administrador), nameof(Perfil.Supervisor))
                .Build())
            .AddPolicy(Politicas.GestaoDeUsuarios, PoliticaDoConsole()
                .RequireRole(nameof(Perfil.Administrador))
                .Build())
            .AddPolicy(Politicas.LeituraDaOperacao, PoliticaDoConsole()
                .RequireRole(nameof(Perfil.Administrador), nameof(Perfil.Supervisor), nameof(Perfil.Operador))
                .Build())
            // Supervisor prepara a operação (critério de aceite da Fase 2); operador opera sobre ela.
            .AddPolicy(Politicas.GestaoDaOperacao, PoliticaDoConsole()
                .RequireRole(nameof(Perfil.Administrador), nameof(Perfil.Supervisor))
                .Build())
            // Entrega é o trabalho do dia: o operador cria, corrige e cancela.
            .AddPolicy(Politicas.OperacaoDeEntregas, PoliticaDoConsole()
                .RequireRole(nameof(Perfil.Administrador), nameof(Perfil.Supervisor), nameof(Perfil.Operador))
                .Build())
            .AddPolicy(Politicas.Motorista, new AuthorizationPolicyBuilder(EsquemasDeAutenticacao.Motorista)
                .RequireAuthenticatedUser()
                .RequireClaim(ReivindicacoesDaTorre.Canal, nameof(CanalDeAcesso.Motorista))
                .RequireRole(nameof(Perfil.Motorista))
                .Build());

        servicos.AddRateLimiter(limites =>
        {
            limites.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limites.OnRejected = async (contexto, cancelamento) =>
            {
                if (contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera))
                {
                    contexto.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(espera.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                await RespostasDeProblema.EscreverAsync(
                    contexto.HttpContext,
                    StatusCodes.Status429TooManyRequests,
                    "limite_de_requisicoes",
                    "Muitas requisições",
                    "Muitas tentativas em pouco tempo. Aguarde e tente novamente.").ConfigureAwait(false);
            };

            limites.AddPolicy(PoliticasDeLimite.Login, http => JanelaPorEndereco(
                http, PoliticasDeLimite.Login, opcoes => opcoes.TentativasDeLoginPorMinuto));

            limites.AddPolicy(PoliticasDeLimite.Renovacao, http => JanelaPorEndereco(
                http, PoliticasDeLimite.Renovacao, opcoes => opcoes.RenovacoesPorMinuto));

            // Telemetria por motorista, não por endereço: aparelhos de operadora móvel saem pelo mesmo
            // IP público (CGNAT), e um limite por endereço bloquearia motoristas legítimos juntos.
            limites.AddPolicy(PoliticasDeLimite.Telemetria, JanelaPorMotorista);
        });

        return servicos;
    }

    private static AuthorizationPolicyBuilder PoliticaDoConsole() =>
        new AuthorizationPolicyBuilder(EsquemasDeAutenticacao.Operacao)
            .RequireAuthenticatedUser()
            .RequireClaim(ReivindicacoesDaTorre.Canal, nameof(CanalDeAcesso.Operacao));

    private static RateLimitPartition<string> JanelaPorEndereco(
        HttpContext http,
        string politica,
        Func<OpcoesDeLimiteDeRequisicoes, int> limite)
    {
        var opcoes = http.RequestServices.GetRequiredService<IOptions<OpcoesDeLimiteDeRequisicoes>>().Value;

        // Atrás de proxy, este endereço é o do proxy até os cabeçalhos encaminhados
        // serem tratados — decisão da Fase 25, registrada no ADR 0009.
        var endereco = http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";

        return RateLimitPartition.GetFixedWindowLimiter(
            $"{politica}:{endereco}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limite(opcoes),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    }

    private static RateLimitPartition<string> JanelaPorMotorista(HttpContext http)
    {
        var opcoes = http.RequestServices.GetRequiredService<IOptions<OpcoesDeLimiteDeRequisicoes>>().Value;

        // O limitador roda depois da autorização (Program.cs): aqui a conta já foi validada.
        var conta = http.User.FindFirstValue(ReivindicacoesDaTorre.Usuario)
            ?? http.Connection.RemoteIpAddress?.ToString()
            ?? "desconhecido";

        return RateLimitPartition.GetFixedWindowLimiter(
            $"{PoliticasDeLimite.Telemetria}:{conta}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = opcoes.EnviosDePosicaoPorMinuto,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    }

    private static void ConfigurarEsquema(IServiceCollection servicos, CanalDeAcesso canal)
    {
        servicos
            .AddOptions<JwtBearerOptions>(EsquemasDeAutenticacao.Para(canal))
            .Configure<ChaveDeAssinaturaDeToken, IOptions<OpcoesDeTokenDeAcesso>, TimeProvider>(
                (opcoes, chave, opcoesDoToken, provedorDeTempo) =>
                {
                    // Sem isto, o manipulador renomeia "sub" e companhia para URIs longas
                    // do WS-Federation, e o nome da reivindicação deixa de ser previsível.
                    opcoes.MapInboundClaims = false;

                    opcoes.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = opcoesDoToken.Value.Emissor,
                        ValidateAudience = true,
                        ValidAudience = EsquemasDeAutenticacao.AudienciaDe(canal),
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = chave.Chave,
                        // Aceitar só HS256 fecha a troca de algoritmo por "none" ou por um
                        // algoritmo assimétrico usando a chave simétrica como chave pública.
                        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                        RequireSignedTokens = true,
                        RequireExpirationTime = true,
                        ValidateLifetime = true,
                        // Validade conferida contra o mesmo relógio que emitiu o token, para
                        // que teste e simulador consigam controlar o tempo.
                        LifetimeValidator = (inicio, fim, _, _) =>
                        {
                            var agora = provedorDeTempo.GetUtcNow().UtcDateTime;
                            return fim is { } expira
                                && expira > agora - ToleranciaDeRelogio
                                && (inicio is null || inicio.Value <= agora + ToleranciaDeRelogio);
                        },
                        NameClaimType = ReivindicacoesDaTorre.Usuario,
                        RoleClaimType = ReivindicacoesDaTorre.Perfil,
                    };

                    opcoes.Events = new JwtBearerEvents
                    {
                        OnTokenValidated = contexto => ValidarSessaoAsync(contexto, canal),
                        OnChallenge = EscreverDesafioAsync,
                        OnForbidden = contexto => RespostasDeProblema.EscreverAsync(
                            contexto.HttpContext,
                            StatusCodes.Status403Forbidden,
                            "acesso_negado",
                            "Acesso negado",
                            "O perfil da sessão não permite esta operação."),
                    };
                });
    }

    /// <summary>
    /// Assinatura e validade corretas não bastam: a sessão do token precisa continuar
    /// aberta. É o que faz logout, desativação e detecção de reuso valerem na hora.
    /// </summary>
    private static async Task ValidarSessaoAsync(TokenValidatedContext contexto, CanalDeAcesso canal)
    {
        var principal = contexto.Principal;

        if (principal is null
            || !Guid.TryParse(principal.FindFirstValue(ReivindicacoesDaTorre.Usuario), out var usuarioId)
            || !Guid.TryParse(principal.FindFirstValue(ReivindicacoesDaTorre.Organizacao), out var organizacaoId)
            || !Guid.TryParse(principal.FindFirstValue(ReivindicacoesDaTorre.Sessao), out var sessaoId)
            || !Enum.TryParse<CanalDeAcesso>(principal.FindFirstValue(ReivindicacoesDaTorre.Canal), out var canalDoToken)
            || canalDoToken != canal)
        {
            contexto.Fail("Token sem as reivindicações esperadas.");
            return;
        }

        var validar = contexto.HttpContext.RequestServices.GetRequiredService<ValidarSessaoAtiva>();
        var ativa = await validar
            .ExecutarAsync(sessaoId, usuarioId, organizacaoId, canal, contexto.HttpContext.RequestAborted)
            .ConfigureAwait(false);

        if (!ativa)
        {
            contexto.Fail("Sessão encerrada.");
        }
    }

    private static async Task EscreverDesafioAsync(JwtBearerChallengeContext contexto)
    {
        // Assume a resposta para escrever ProblemDetails no lugar do corpo vazio padrão,
        // mantendo o cabeçalho WWW-Authenticate que clientes HTTP esperam.
        contexto.HandleResponse();
        contexto.Response.Headers.WWWAuthenticate = "Bearer";

        await RespostasDeProblema.EscreverAsync(
            contexto.HttpContext,
            StatusCodes.Status401Unauthorized,
            "nao_autenticado",
            "Não autenticado",
            "É preciso uma sessão válida para acessar este recurso.").ConfigureAwait(false);
    }
}
