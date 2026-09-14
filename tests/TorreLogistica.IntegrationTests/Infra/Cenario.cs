using TorreLogistica.Domain.Identidade;
using TorreLogistica.Infrastructure.Persistencia;
using TorreLogistica.Infrastructure.Seguranca;

namespace TorreLogistica.IntegrationTests.Infra;

/// <summary>Conta criada para teste, com a senha em claro para fazer login.</summary>
public sealed record ContaDeTeste(
    Guid Id,
    Guid OrganizacaoId,
    string OrganizacaoSlug,
    string Email,
    string Senha,
    Perfil Perfil);

/// <summary>Organização criada para teste.</summary>
public sealed record OrganizacaoDeTeste(Guid Id, string Slug, IReadOnlyList<ContaDeTeste> Contas)
{
    /// <summary>Primeira conta com o perfil.</summary>
    public ContaDeTeste Com(Perfil perfil) => Contas.First(conta => conta.Perfil == perfil);

    /// <summary>Todas as contas com o perfil.</summary>
    public IReadOnlyList<ContaDeTeste> TodasCom(Perfil perfil) => [.. Contas.Where(conta => conta.Perfil == perfil)];
}

/// <summary>
/// Prepara organizações e contas direto no banco.
/// </summary>
/// <remarks>
/// Preparação de cenário é o único lugar em que o teste escreve direto no banco. O que se
/// está provando — login, sessão, autorização, isolamento — sempre passa pela API.
/// </remarks>
public sealed class Cenario(ContainerPostgis banco)
{
    /// <summary>Senha comum das contas de teste.</summary>
    public const string SenhaPadrao = "senha-de-teste-bem-comprida";

    private static readonly HasherDeSenha Hasher = new();
    private static readonly Lazy<string> HashPadrao = new(() => Hasher.GerarHash(SenhaPadrao));

    /// <summary>Cria uma organização com uma conta para cada perfil pedido.</summary>
    public async Task<OrganizacaoDeTeste> CriarOrganizacaoAsync(params Perfil[] perfis)
    {
        var agora = DateTimeOffset.UtcNow;
        var slug = $"org-{Guid.NewGuid():n}";
        var organizacao = Organizacao.Criar(Guid.CreateVersion7(), $"Organização {slug}", slug, agora);
        var contas = new List<ContaDeTeste>();

        await using var contexto = banco.CriarContexto(new ContextoDeTenantAusente());
        contexto.Organizacoes.Add(organizacao);

        foreach (var perfil in perfis.Length == 0 ? [Perfil.Administrador] : perfis)
        {
            var email = $"{perfil.ToString().ToLowerInvariant()}.{Guid.NewGuid():n}@teste.test";
            var usuario = Usuario.Criar(
                Guid.CreateVersion7(),
                organizacao.Id,
                $"{perfil} de teste",
                EnderecoDeEmail.Criar(email),
                HashPadrao.Value,
                perfil,
                agora);

            contexto.Usuarios.Add(usuario);
            contas.Add(new ContaDeTeste(usuario.Id, organizacao.Id, slug, email, SenhaPadrao, perfil));
        }

        await contexto.SaveChangesAsync();
        return new OrganizacaoDeTeste(organizacao.Id, slug, contas);
    }

    /// <summary>Desativa uma conta direto no banco, para preparar cenário de conta inativa.</summary>
    public Task DesativarDiretamenteAsync(Guid usuarioId) =>
        banco.ExecutarAsync("UPDATE usuarios SET ativo = false WHERE id = @id", ("id", usuarioId));
}
