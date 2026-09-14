using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>Liga a semeadura. Só é respeitada em ambiente de desenvolvimento.</summary>
    public bool Habilitada { get; set; }

    /// <summary>
    /// Senha comum das contas semeadas. Vem de user-secrets ou variável de ambiente, nunca
    /// de arquivo versionado.
    /// </summary>
    [MinLength(PoliticaDeSenha.TamanhoMinimo)]
    public string? SenhaInicial { get; set; }
}

/// <summary>
/// Cria duas organizações fictícias com contas de cada perfil, para uso manual local.
/// </summary>
/// <remarks>
/// <para>
/// Não é o seed da demonstração pública — esse é determinístico, narrativo e passa pela
/// API (Fase 23). Este existe apenas para que, na máquina do desenvolvedor, haja com quem
/// fazer login e contra quem testar isolamento entre organizações.
/// </para>
/// <para>
/// Duas travas: roda somente em ambiente de desenvolvimento e somente se habilitado.
/// Sem senha configurada, a subida falha — gerar uma senha "padrão" seria criar
/// credencial conhecida por qualquer leitor do repositório.
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
