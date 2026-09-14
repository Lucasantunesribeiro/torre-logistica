using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TorreLogistica.Application.Abstracoes.Identidade;

namespace TorreLogistica.Infrastructure.Persistencia;

/// <summary>
/// Permite que as ferramentas do EF Core criem migrations sem subir a API.
/// </summary>
/// <remarks>
/// Gerar uma migration não abre conexão com o banco: a cadeia usada aqui serve só
/// para o provedor Npgsql se configurar. Por isso ela é um destino local sem
/// credencial — nenhum segredo é versionado. Para aplicar a migration em um banco
/// real, defina a variável de ambiente <c>TORRE_BANCO_CADEIA_DE_CONEXAO</c>.
/// </remarks>
public sealed class FabricaDeDbContextEmTempoDeDesign
    : IDesignTimeDbContextFactory<TorreLogisticaDbContext>
{
    internal const string VariavelDeAmbienteDaCadeia = "TORRE_BANCO_CADEIA_DE_CONEXAO";

    private const string CadeiaApenasParaScaffolding =
        "Host=localhost;Port=5432;Database=torre_logistica;Username=torre";

    /// <inheritdoc />
    public TorreLogisticaDbContext CreateDbContext(string[] args)
    {
        var cadeia = Environment.GetEnvironmentVariable(VariavelDeAmbienteDaCadeia);
        if (string.IsNullOrWhiteSpace(cadeia))
        {
            cadeia = CadeiaApenasParaScaffolding;
        }

        var opcoes = new DbContextOptionsBuilder<TorreLogisticaDbContext>()
            .UseNpgsql(cadeia, npgsql => npgsql.MigrationsAssembly(
                typeof(TorreLogisticaDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new TorreLogisticaDbContext(opcoes, new ContextoDeTenantAusente());
    }
}

/// <summary>
/// Contexto sem tenant: usado por processos que não atendem requisição autenticada.
/// </summary>
/// <remarks>
/// Com ele, os filtros de tenant não enxergam dado algum. Um worker que precise operar
/// sobre uma organização terá de receber essa autoridade de forma explícita.
/// </remarks>
public sealed class ContextoDeTenantAusente : IContextoDoTenant
{
    /// <inheritdoc />
    public Guid? OrganizacaoId => null;
}
