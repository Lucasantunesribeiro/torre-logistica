using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using TorreLogistica.Application.Abstracoes.Identidade;
using TorreLogistica.Infrastructure.Persistencia;

namespace TorreLogistica.IntegrationTests.Infra;

/// <summary>
/// PostgreSQL com PostGIS real, criado uma vez por execução e compartilhado pela coleção.
/// </summary>
/// <remarks>
/// Nada de provedor em memória: as decisões que este projeto precisa provar — PostGIS,
/// constraints, transação, trava de linha, trigger — só existem no banco real. Cada teste
/// cria suas próprias organizações com identificadores únicos, então compartilhar o banco
/// não cria dependência entre testes.
/// </remarks>
public sealed class ContainerPostgis : IAsyncLifetime
{
    /// <summary>Imagem usada nos testes; mesma família da usada no docker-compose.</summary>
    public const string ImagemDoPostGis = "postgis/postgis:17-3.5";

    private readonly PostgreSqlContainer _banco = new PostgreSqlBuilder(ImagemDoPostGis)
        .WithDatabase("torre_logistica_teste")
        .WithUsername("torre_teste")
        .WithPassword(Guid.CreateVersion7().ToString("n"))
        .WithCleanUp(true)
        .Build();

    /// <summary>Cadeia de conexão do contêiner em execução.</summary>
    public string CadeiaDeConexao => _banco.GetConnectionString();

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        await _banco.StartAsync().ConfigureAwait(false);

        // A migration roda aqui, e não na subida da API: o teste é dono do schema e prova
        // que as migrations aplicam do zero contra PostGIS real.
        await using var contexto = CriarContexto(new ContextoDeTenantAusente());
        await contexto.Database.MigrateAsync().ConfigureAwait(false);
    }

    /// <summary>Contexto direto no banco, com o tenant informado.</summary>
    public TorreLogisticaDbContext CriarContexto(IContextoDoTenant tenant) =>
        new(
            new DbContextOptionsBuilder<TorreLogisticaDbContext>()
                .UseNpgsql(CadeiaDeConexao, npgsql => npgsql.UseNetTopologySuite())
                .UseSnakeCaseNamingConvention()
                .Options,
            tenant);

    /// <summary>Migrations já aplicadas.</summary>
    public async Task<IReadOnlyList<string>> MigrationsAplicadasAsync()
    {
        await using var contexto = CriarContexto(new ContextoDeTenantAusente());
        return [.. await contexto.Database.GetAppliedMigrationsAsync().ConfigureAwait(false)];
    }

    /// <summary>Executa um SQL escalar.</summary>
    public async Task<string?> ConsultarEscalarAsync(string sql, params (string Nome, object Valor)[] parametros)
    {
        await using var conexao = new NpgsqlConnection(CadeiaDeConexao);
        await conexao.OpenAsync().ConfigureAwait(false);
        await using var comando = CriarComando(conexao, sql, parametros);
        return (await comando.ExecuteScalarAsync().ConfigureAwait(false))?.ToString();
    }

    /// <summary>Executa um SQL sem retorno.</summary>
    public async Task<int> ExecutarAsync(string sql, params (string Nome, object Valor)[] parametros)
    {
        await using var conexao = new NpgsqlConnection(CadeiaDeConexao);
        await conexao.OpenAsync().ConfigureAwait(false);
        await using var comando = CriarComando(conexao, sql, parametros);
        return await comando.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _banco.DisposeAsync();

    private static NpgsqlCommand CriarComando(NpgsqlConnection conexao, string sql, (string Nome, object Valor)[] parametros)
    {
        var comando = new NpgsqlCommand(sql, conexao);
        foreach (var (nome, valor) in parametros)
        {
            comando.Parameters.AddWithValue(nome, valor);
        }

        return comando;
    }
}

/// <summary>Tenant fixo, para provar o filtro global diretamente no contexto.</summary>
public sealed class TenantFixo(Guid organizacaoId) : IContextoDoTenant
{
    /// <inheritdoc />
    public Guid? OrganizacaoId => organizacaoId;
}

/// <summary>Compartilha um único contêiner entre todas as classes de teste de integração.</summary>
[CollectionDefinition(Nome)]
public sealed class ColecaoDeIntegracao : ICollectionFixture<ContainerPostgis>
{
    /// <summary>Nome da coleção.</summary>
    public const string Nome = "integracao-api";
}
