using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TorreLogistica.Application.Abstracoes.Seguranca;
using TorreLogistica.Application.Usuarios;
using TorreLogistica.Domain.Abstracoes.Identificadores;
using TorreLogistica.Domain.Abstracoes.Tempo;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.Infrastructure.Persistencia;

namespace TorreLogistica.Infrastructure.Desenvolvimento;

/// <summary>Configuração da semeadura local de desenvolvimento.</summary>
public sealed class OpcoesDeSemeaduraDeDesenvolvimento
{
    /// <summary>Seção correspondente na configuração.</summary>
    public const string Secao = "Torre:Desenvolvimento:Semeadura";

    /// <summary>Liga a semeadura.</summary>
    public bool Habilitada { get; set; }

    /// <summary>
    /// Autoriza a semeadura fora de um ambiente de desenvolvimento.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Existe porque um banco recém-criado não tem organização nenhuma, e sem organização não há
    /// conta para autenticar — nem a do visitante da demonstração, nem a que o simulador usa para
    /// montar o palco. O elenco fictício precisa existir antes de a história poder ser contada, e
    /// criá-lo é justamente o que esta classe faz.
    /// </para>
    /// <para>
    /// Continua desligado por padrão, e por um motivo concreto: isto cria contas com senha comum,
    /// entre elas uma administradora. Num ambiente publicado isso só pode acontecer se alguém
    /// tiver escrito que quer — como aqui, onde a demonstração pública declara a intenção em
    /// `infra-demo/docker-compose.demo.yml`. Ligar <see cref="Habilitada"/> num ambiente publicado
    /// sem esta autorização **derruba a subida**, em vez de semear em silêncio ou de não semear
    /// em silêncio: as duas alternativas escondem a decisão de quem a tomou.
    /// </para>
    /// </remarks>
    public bool PermitirForaDeDesenvolvimento { get; set; }

    /// <summary>
    /// Senha comum das contas semeadas. Vem de user-secrets ou variável de ambiente, nunca
    /// de arquivo versionado.
    /// </summary>
    [MinLength(PoliticaDeSenha.TamanhoMinimo)]
    public string? SenhaInicial { get; set; }
}

/// <summary>
/// Cria duas organizações fictícias com contas de cada perfil: o elenco da operação.
/// </summary>
/// <remarks>
/// <para>
/// Não confundir com o roteiro da demonstração. O roteiro — as seis histórias, determinísticas e
/// narrativas — é encenado pelo simulador, contra a API, pelos mesmos caminhos de produção
/// (Fase 23). O que esta classe faz é anterior e menor: pôr no banco as organizações e as contas
/// **com quem** aquelas histórias acontecem.
/// </para>
/// <para>
/// Serve a dois lugares, e por isso não é mais exclusiva de desenvolvimento:
/// </para>
/// <list type="bullet">
/// <item>na máquina de quem desenvolve, dá com quem fazer login e contra quem testar o isolamento
/// entre organizações;</item>
/// <item>na demonstração pública, resolve o problema do banco recém-criado — sem organização não há
/// conta, sem conta o visitante não entra e o simulador não tem como montar o palco.</item>
/// </list>
/// <para>
/// Três travas, todas explícitas: precisa estar habilitada; fora de desenvolvimento precisa também
/// de <see cref="OpcoesDeSemeaduraDeDesenvolvimento.PermitirForaDeDesenvolvimento"/>, e ligar sem
/// ele derruba a subida; e sem senha configurada a subida falha — gerar uma senha "padrão" seria
/// criar credencial conhecida por qualquer leitor do repositório.
/// </para>
/// </remarks>
public static class SemeadorDeDesenvolvimento
{
    private static readonly (string Slug, string Nome, (string Nome, string Email, Perfil Perfil)[] Contas)[] Organizacoes =
    [
        ("transportadora-aurora", "Transportadora Aurora",
        [
            ("Helena Duarte", "helena.duarte@aurora.test", Perfil.Administrador),
            ("Marcos Vieira", "marcos.vieira@aurora.test", Perfil.Supervisor),
            ("Paula Siqueira", "paula.siqueira@aurora.test", Perfil.Operador),
            ("Rafael Mendes", "rafael.mendes@aurora.test", Perfil.Motorista),
        ]),
        ("logistica-boreal", "Logística Boreal",
        [
            ("Tiago Fontes", "tiago.fontes@boreal.test", Perfil.Administrador),
        ]),
    ];

    /// <summary>Semeia, se habilitado. Operação idempotente: organização existente é ignorada.</summary>
    public static async Task SemearSeHabilitadoAsync(IServiceProvider provedor, CancellationToken cancelamento = default)
    {
        ArgumentNullException.ThrowIfNull(provedor);

        await using var escopo = provedor.CreateAsyncScope();
        var servicos = escopo.ServiceProvider;
        var opcoes = servicos.GetRequiredService<IOptions<OpcoesDeSemeaduraDeDesenvolvimento>>().Value;

        if (!opcoes.Habilitada)
        {
            return;
        }

        var ambiente = servicos.GetRequiredService<IHostEnvironment>();

        if (!ambiente.IsDevelopment() && !opcoes.PermitirForaDeDesenvolvimento)
        {
            throw new InvalidOperationException(
                $"{OpcoesDeSemeaduraDeDesenvolvimento.Secao}:Habilitada está ligada no ambiente "
                + $"'{ambiente.EnvironmentName}', que não é de desenvolvimento. Esta semeadura cria contas "
                + "com senha comum, entre elas uma administradora. Se a intenção é povoar o elenco fictício "
                + $"de uma demonstração, declare {OpcoesDeSemeaduraDeDesenvolvimento.Secao}:PermitirForaDeDesenvolvimento. "
                + "Se não é, desligue a semeadura.");
        }

        if (string.IsNullOrWhiteSpace(opcoes.SenhaInicial) || opcoes.SenhaInicial.Length < PoliticaDeSenha.TamanhoMinimo)
        {
            throw new InvalidOperationException(
                $"A semeadura de desenvolvimento está habilitada, mas {OpcoesDeSemeaduraDeDesenvolvimento.Secao}:SenhaInicial "
                + $"não foi configurada com ao menos {PoliticaDeSenha.TamanhoMinimo} caracteres. "
                + "Defina por user-secrets ou variável de ambiente.");
        }

        var contexto = servicos.GetRequiredService<TorreLogisticaDbContext>();
        var hasher = servicos.GetRequiredService<IHasherDeSenha>();
        var identificadores = servicos.GetRequiredService<IGeradorDeIdentificador>();
        var relogio = servicos.GetRequiredService<IRelogio>();
        var log = servicos.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SemeadorDeDesenvolvimento));

        var hash = hasher.GerarHash(opcoes.SenhaInicial);
        var agora = relogio.AgoraUtc;

        foreach (var (slug, nome, contas) in Organizacoes)
        {
            var existe = await contexto.Organizacoes
                .AnyAsync(organizacao => organizacao.Slug == slug, cancelamento)
                .ConfigureAwait(false);

            if (existe)
            {
                continue;
            }

            var organizacao = Organizacao.Criar(identificadores.Novo(), nome, slug, agora);
            contexto.Organizacoes.Add(organizacao);

            foreach (var (nomeDaConta, email, perfil) in contas)
            {
                contexto.Usuarios.Add(Usuario.Criar(
                    identificadores.Novo(),
                    organizacao.Id,
                    nomeDaConta,
                    EnderecoDeEmail.Criar(email),
                    hash,
                    perfil,
                    agora));
            }

            await contexto.SaveChangesAsync(cancelamento).ConfigureAwait(false);

            log.LogInformation(
                "Semeadura de desenvolvimento: organização {OrganizacaoSlug} criada com {QuantidadeDeContas} contas.",
                slug,
                contas.Length);
        }
    }
}
