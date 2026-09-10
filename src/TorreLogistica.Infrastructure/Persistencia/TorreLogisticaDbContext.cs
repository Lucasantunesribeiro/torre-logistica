using Microsoft.EntityFrameworkCore;

namespace TorreLogistica.Infrastructure.Persistencia;

/// <summary>
/// Contexto de persistência do domínio operacional da Torre Logística.
/// </summary>
/// <remarks>
/// PostgreSQL é a fonte de verdade do sistema. Na Fase 0 o contexto não mapeia
/// nenhuma entidade de negócio: ele existe para fixar as decisões técnicas que toda
/// tabela futura vai herdar — extensão PostGIS habilitada, nomes físicos em
/// <c>snake_case</c> e instantes gravados em UTC.
/// </remarks>
public class TorreLogisticaDbContext(DbContextOptions<TorreLogisticaDbContext> opcoes)
    : DbContext(opcoes)
{
    /// <summary>Nome da extensão geoespacial exigida pelo domínio.</summary>
    public const string ExtensaoPostGis = "postgis";

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        // Sem PostGIS não existe distância, raio nem geofence: a extensão é parte do
        // schema, não um pré-requisito informal de ambiente.
        modelBuilder.HasPostgresExtension(ExtensaoPostGis);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TorreLogisticaDbContext).Assembly);
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        base.ConfigureConventions(configurationBuilder);

        // Todo instante do sistema é UTC. Fuso é assunto de apresentação.
        configurationBuilder.Properties<DateTimeOffset>().HaveColumnType("timestamptz");
        configurationBuilder.Properties<DateTime>().HaveColumnType("timestamptz");
    }
}
