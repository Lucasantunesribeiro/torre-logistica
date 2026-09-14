using Microsoft.EntityFrameworkCore;
using Npgsql;
using TorreLogistica.Application.Abstracoes.Identidade;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Domain.Auditoria;
using TorreLogistica.Domain.Identidade;

namespace TorreLogistica.Infrastructure.Persistencia;

/// <summary>
/// Contexto de persistência do domínio operacional da Torre Logística.
/// </summary>
/// <remarks>
/// <para>
/// PostgreSQL é a fonte de verdade do sistema. Toda entidade pertencente a um tenant
/// recebe filtro global pela organização da sessão autenticada.
/// </para>
/// <para>
/// Sem sessão, o filtro compara com <see cref="Guid.Empty"/> — valor que nenhuma
/// organização tem —, então a consulta não enxerga nada. A falha é fechada: esquecer de
/// resolver o tenant produz lista vazia e 404, nunca dado de outra organização.
/// </para>
/// </remarks>
public class TorreLogisticaDbContext(
    DbContextOptions<TorreLogisticaDbContext> opcoes,
    IContextoDoTenant contextoDoTenant)
    : DbContext(opcoes), IContextoDePersistencia
{
    /// <summary>Nome da extensão geoespacial exigida pelo domínio.</summary>
    public const string ExtensaoPostGis = "postgis";

    private readonly IContextoDoTenant _contextoDoTenant =
        contextoDoTenant ?? throw new ArgumentNullException(nameof(contextoDoTenant));

    /// <inheritdoc />
    public DbSet<Organizacao> Organizacoes => Set<Organizacao>();

    /// <inheritdoc />
    public DbSet<Usuario> Usuarios => Set<Usuario>();

    /// <inheritdoc />
    public DbSet<Sessao> Sessoes => Set<Sessao>();

    /// <inheritdoc />
    public DbSet<TokenDeRenovacao> TokensDeRenovacao => Set<TokenDeRenovacao>();

    /// <inheritdoc />
    public DbSet<EventoDeAuditoria> EventosDeAuditoria => Set<EventoDeAuditoria>();

    /// <summary>
    /// Organização usada nos filtros globais. O EF Core lê esta propriedade a cada
    /// consulta, na instância do contexto — por isso ela precisa morar aqui.
    /// </summary>
    private Guid OrganizacaoIdDoFiltro => _contextoDoTenant.OrganizacaoId ?? Guid.Empty;

    /// <inheritdoc />
    public async Task<T> ExecutarEmTransacaoAsync<T>(
        Func<CancellationToken, Task<T>> operacao,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(operacao);

        // Com nova tentativa em falha transitória habilitada, transação aberta fora da
        // estratégia de execução é recusada pelo provedor — e com razão: a repetição
        // precisa reexecutar a unidade inteira, não só o comando que falhou.
        var estrategia = Database.CreateExecutionStrategy();

        return await estrategia.ExecuteAsync(
            async cancelamentoDaTentativa =>
            {
                ChangeTracker.Clear();

                await using var transacao = await Database
                    .BeginTransactionAsync(cancelamentoDaTentativa)
                    .ConfigureAwait(false);

                var resultado = await operacao(cancelamentoDaTentativa).ConfigureAwait(false);

                await transacao.CommitAsync(cancelamentoDaTentativa).ConfigureAwait(false);
                return resultado;
            },
            cancelamento).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task BloquearSessaoAsync(Guid sessaoId, CancellationToken cancelamento) =>
        Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM sessoes WHERE id = {sessaoId} FOR UPDATE",
            cancelamento);

    /// <inheritdoc />
    public Task BloquearOrganizacaoAsync(Guid organizacaoId, CancellationToken cancelamento) =>
        Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM organizacoes WHERE id = {organizacaoId} FOR UPDATE",
            cancelamento);

    /// <inheritdoc />
    public bool EhViolacaoDeUnicidade(Exception excecao, string nomeDaRestricao)
    {
        for (var atual = excecao; atual is not null; atual = atual.InnerException)
        {
            if (atual is PostgresException
                {
                    SqlState: PostgresErrorCodes.UniqueViolation,
                } postgres
                && string.Equals(postgres.ConstraintName, nomeDaRestricao, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        // Sem PostGIS não existe distância, raio nem geofence: a extensão é parte do
        // schema, não um pré-requisito informal de ambiente.
        modelBuilder.HasPostgresExtension(ExtensaoPostGis);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TorreLogisticaDbContext).Assembly);

        // Filtros de tenant ficam aqui, e não nas classes de configuração, porque
        // precisam ler o tenant desta instância de contexto a cada consulta.
        modelBuilder.Entity<Usuario>()
            .HasQueryFilter(usuario => usuario.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<Sessao>()
            .HasQueryFilter(sessao => sessao.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<TokenDeRenovacao>()
            .HasQueryFilter(token => token.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<EventoDeAuditoria>()
            .HasQueryFilter(evento => evento.OrganizacaoId == OrganizacaoIdDoFiltro);
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
