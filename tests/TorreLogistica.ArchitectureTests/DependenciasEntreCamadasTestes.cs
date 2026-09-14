using TorreLogistica.Domain.Abstracoes.Erros;

namespace TorreLogistica.ArchitectureTests;

/// <summary>
/// Guarda a direção das dependências declarada no CLAUDE.md, seção 7.
/// </summary>
public sealed class DependenciasEntreCamadasTestes
{
    private const string Domain = "TorreLogistica.Domain";
    private const string Application = "TorreLogistica.Application";
    private const string Infrastructure = "TorreLogistica.Infrastructure";
    private const string Api = "TorreLogistica.Api";
    private const string Workers = "TorreLogistica.Workers";
    private const string Simulator = "TorreLogistica.Simulator";

    /// <summary>
    /// Pacotes que arrastariam infraestrutura para dentro do domínio.
    /// </summary>
    private static readonly string[] PacotesProibidosNoDominio =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Microsoft.Extensions.Hosting",
        "Npgsql",
        "AWSSDK",
        "Serilog",
        "EFCore.NamingConventions",
        "NetTopologySuite",
    ];

    [Fact]
    public void DominioNaoReferenciaNenhumOutroProjeto()
    {
        var referencias = Repositorio.ReferenciasDeProjeto(Domain);

        Assert.Empty(referencias);
    }

    [Fact]
    public void DominioNaoReferenciaPacoteDeInfraestrutura()
    {
        var pacotes = Repositorio.ReferenciasDePacote(Domain);

        var proibidos = pacotes
            .Where(pacote => PacotesProibidosNoDominio.Any(proibido =>
                pacote.StartsWith(proibido, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(
            proibidos.Count == 0,
            $"O domínio referencia pacote de infraestrutura: {string.Join(", ", proibidos)}.");
    }

    [Fact]
    public void DominioCompiladoNaoCarregaAssemblyDeInfraestrutura()
    {
        var referenciados = typeof(ExcecaoDeDominio).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .ToList();

        var proibidos = referenciados
            .Where(nome => PacotesProibidosNoDominio.Any(proibido =>
                nome.StartsWith(proibido, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(
            proibidos.Count == 0,
            $"O assembly do domínio carrega infraestrutura: {string.Join(", ", proibidos)}.");
    }

    [Fact]
    public void ApplicationNaoReferenciaInfrastructureNemApi()
    {
        var referencias = Repositorio.ReferenciasDeProjeto(Application);

        Assert.DoesNotContain(Infrastructure, referencias);
        Assert.DoesNotContain(Api, referencias);
        Assert.Contains(Domain, referencias);
    }

    [Fact]
    public void InfrastructureNaoReferenciaApiNemWorkers()
    {
        var referencias = Repositorio.ReferenciasDeProjeto(Infrastructure);

        Assert.DoesNotContain(Api, referencias);
        Assert.DoesNotContain(Workers, referencias);
    }

    [Fact]
    public void InfrastructureNaoReferenciaAspNetCore()
    {
        var pacotes = Repositorio.ReferenciasDePacote(Infrastructure);

        var aspNet = pacotes
            .Where(pacote => pacote.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(
            aspNet.Count == 0,
            $"Infrastructure referencia ASP.NET Core: {string.Join(", ", aspNet)}.");
    }

    /// <summary>
    /// O simulador precisa se comportar como cliente externo. Sem referência a
    /// Domain, Application ou Infrastructure ele fica impossibilitado, por
    /// construção, de escrever direto no banco para "fingir" operação.
    /// </summary>
    [Fact]
    public void SimuladorNaoAlcancaONucleoDaAplicacao()
    {
        var referencias = Repositorio.ReferenciasDeProjeto(Simulator);

        Assert.Empty(referencias);
    }

    [Fact]
    public void SimuladorNaoReferenciaPacoteDeBancoDeDados()
    {
        var pacotes = Repositorio.ReferenciasDePacote(Simulator);

        var deBanco = pacotes
            .Where(pacote =>
                pacote.StartsWith("Npgsql", StringComparison.OrdinalIgnoreCase)
                || pacote.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)
                || pacote.StartsWith("Dapper", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(
            deBanco.Count == 0,
            $"O simulador referencia acesso a banco: {string.Join(", ", deBanco)}.");
    }

    [Fact]
    public void ComposicaoDeApiEWorkersUsaApplicationEInfrastructure()
    {
        foreach (var raiz in new[] { Api, Workers })
        {
            var referencias = Repositorio.ReferenciasDeProjeto(raiz);

            Assert.Contains(Application, referencias);
            Assert.Contains(Infrastructure, referencias);
        }
    }
}
