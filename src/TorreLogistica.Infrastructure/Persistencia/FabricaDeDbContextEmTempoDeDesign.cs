using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

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

        return new TorreLogisticaDbContext(opcoes);
    }
}
