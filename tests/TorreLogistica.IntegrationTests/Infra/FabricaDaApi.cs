using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using TorreLogistica.Infrastructure.Persistencia;

namespace TorreLogistica.IntegrationTests.Infra;

/// <summary>
/// Sobe a API real contra um PostgreSQL com PostGIS de verdade.
/// </summary>
/// <remarks>
/// Nada de provedor em memória: as decisões que este projeto precisa provar —
/// PostGIS, constraints, transação, concorrência, índice — só existem no banco real.
/// O contêiner é criado uma vez por execução e descartado no fim.
/// </remarks>
public sealed class FabricaDaApi : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Imagem usada nos testes; mesma família da usada no docker-compose.</summary>
    public const string ImagemDoPostGis = "postgis/postgis:17-3.5";

    /// <summary>Origem que os testes tratam como autorizada a chamar a API.</summary>
    public const string OrigemAutorizada = "https://console.torre.teste";

    /// <summary>Origem que os testes tratam como não autorizada.</summary>
    public const string OrigemNaoAutorizada = "https://site-qualquer.teste";

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

        // Aplicar a migration aqui, e não na subida da API, mantém o teste como dono
        // do próprio schema e prova que a migration roda contra PostGIS real.
        await using var escopo = Services.CreateAsyncScope();
        var contexto = escopo.ServiceProvider.GetRequiredService<TorreLogisticaDbContext>();
        await contexto.Database.MigrateAsync().ConfigureAwait(false);
    }

    /// <summary>Migrations já aplicadas no banco de teste.</summary>
    public async Task<IReadOnlyList<string>> MigrationsAplicadasAsync()
    {
        await using var escopo = Services.CreateAsyncScope();
        var contexto = escopo.ServiceProvider.GetRequiredService<TorreLogisticaDbContext>();

        var aplicadas = await contexto.Database.GetAppliedMigrationsAsync().ConfigureAwait(false);
        return [.. aplicadas];
    }

    /// <summary>Executa um SQL escalar no banco de teste.</summary>
    public async Task<string?> ConsultarEscalarAsync(string sql)
    {
        await using var escopo = Services.CreateAsyncScope();
        var contexto = escopo.ServiceProvider.GetRequiredService<TorreLogisticaDbContext>();

        await using var conexao = contexto.Database.GetDbConnection();
        await conexao.OpenAsync().ConfigureAwait(false);

        await using var comando = conexao.CreateCommand();
        comando.CommandText = sql;

        var resultado = await comando.ExecuteScalarAsync().ConfigureAwait(false);
        return resultado?.ToString();
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // "Testing" e não "Development": assim o teste exercita o caminho de
        // produção — sem página de exceção do desenvolvedor e sem CORS de localhost.
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration(configuracao =>
            configuracao.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Torre:BancoDeDados:CadeiaDeConexao"] = _banco.GetConnectionString(),
                ["Torre:BancoDeDados:AplicarMigrationsAoIniciar"] = "false",
                ["Torre:Cors:OrigensPermitidas:0"] = OrigemAutorizada,

                // Só aviso e erro no console do teste: o log de requisição da API é
                // verboso e esconderia a falha que a execução precisa mostrar.
                ["Serilog:MinimumLevel:Default"] = "Warning",
                ["Serilog:MinimumLevel:Override:Microsoft"] = "Warning",
                ["Serilog:MinimumLevel:Override:Serilog"] = "Warning",
            }));

        builder.ConfigureTestServices(servicos =>
            servicos.AddSingleton<IStartupFilter, FiltroDeEndpointsDeTeste>());
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        await _banco.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Compartilha um único contêiner entre todas as classes de teste de integração.
/// </summary>
[CollectionDefinition(Nome)]
public sealed class ColecaoDeIntegracao : ICollectionFixture<FabricaDaApi>
{
    /// <summary>Nome da coleção.</summary>
    public const string Nome = "integracao-api";
}
