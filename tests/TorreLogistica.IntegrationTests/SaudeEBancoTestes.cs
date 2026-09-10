using System.Net;
using System.Text.Json;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

[Collection(ColecaoDeIntegracao.Nome)]
public sealed class SaudeEBancoTestes(FabricaDaApi fabrica)
{
    private readonly FabricaDaApi _fabrica = fabrica;

    [Fact]
    public async Task VivacidadeRespondeSaudavelSemConsultarDependencia()
    {
        using var cliente = _fabrica.CreateClient();

        using var resposta = await cliente.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);
        var corpo = await resposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);

        using var json = JsonDocument.Parse(corpo);
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());

        // Se /live consultasse o banco, uma queda momentânea dele faria o
        // orquestrador matar processos saudáveis.
        Assert.Empty(json.RootElement.GetProperty("verificacoes").EnumerateArray());
    }

    [Fact]
    public async Task ProntidaoVerificaOBancoDeDados()
    {
        using var cliente = _fabrica.CreateClient();

        using var resposta = await cliente.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);
        var corpo = await resposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);

        using var json = JsonDocument.Parse(corpo);
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());

        var nomes = json.RootElement.GetProperty("verificacoes")
            .EnumerateArray()
            .Select(item => item.GetProperty("nome").GetString())
            .ToList();

        Assert.Contains("banco-de-dados", nomes);
    }

    /// <summary>
    /// O endpoint é anônimo: descrição e exceção de uma verificação carregariam
    /// host, banco e usuário da cadeia de conexão para quem quisesse ler.
    /// </summary>
    [Fact]
    public async Task ProntidaoNaoExpoeDetalheDaCadeiaDeConexao()
    {
        using var cliente = _fabrica.CreateClient();

        using var resposta = await cliente.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);
        var corpo = await resposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("Host=", corpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Username", corpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("torre_teste", corpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", corpo, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaudeNaoEhArmazenadaEmCache()
    {
        using var cliente = _fabrica.CreateClient();

        using var resposta = await cliente.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.NotNull(resposta.Headers.CacheControl);
        Assert.True(resposta.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task MigrationDeFundacaoFoiAplicadaNoBancoReal()
    {
        var aplicadas = await _fabrica.MigrationsAplicadasAsync();

        Assert.Contains(aplicadas, migration => migration.EndsWith("Fundacao", StringComparison.Ordinal));
    }

    /// <summary>
    /// PostGIS é parte do schema, não um pré-requisito informal de ambiente:
    /// a migration precisa deixá-lo instalado sozinha.
    /// </summary>
    [Fact]
    public async Task ExtensaoPostGisFicaDisponivelDepoisDaMigration()
    {
        var versao = await _fabrica.ConsultarEscalarAsync("select postgis_version();");

        Assert.False(string.IsNullOrWhiteSpace(versao));
    }

    [Fact]
    public async Task FuncoesGeoespaciaisRespondemNoBancoDeTeste()
    {
        // Distância entre a Praça da Sé e o Parque Ibirapuera, em metros.
        const string sql = """
            select round(st_distance(
                st_setsrid(st_makepoint(-46.6339, -23.5505), 4326)::geography,
                st_setsrid(st_makepoint(-46.6570, -23.5874), 4326)::geography)::numeric, 0);
            """;

        var distancia = await _fabrica.ConsultarEscalarAsync(sql);

        Assert.True(double.TryParse(distancia, out var metros));
        Assert.InRange(metros, 4_000, 6_000);
    }

    [Fact]
    public async Task NomesFisicosDoBancoUsamSnakeCase()
    {
        var coluna = await _fabrica.ConsultarEscalarAsync("""
            select column_name from information_schema.columns
            where table_name = '__EFMigrationsHistory' and column_name = 'migration_id';
            """);

        Assert.Equal("migration_id", coluna);
    }
}
