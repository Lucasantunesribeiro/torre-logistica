using System.Reflection;
using TorreLogistica.Domain.Abstracoes.Erros;

namespace TorreLogistica.ArchitectureTests;

public sealed class RegrasDoDominioEDaApplicationTestes
{
    /// <summary>
    /// A Application pode usar o núcleo do EF Core (ADR 0008), mas nunca o provedor nem a
    /// camada relacional: SQL específico de PostgreSQL mora na Infrastructure.
    /// </summary>
    [Fact]
    public void ApplicationUsaSoONucleoDoEfCore()
    {
        var pacotes = Repositorio.ReferenciasDePacote("TorreLogistica.Application");

        var proibidos = pacotes
            .Where(pacote =>
                pacote.StartsWith("Npgsql", StringComparison.OrdinalIgnoreCase)
                || pacote.Equals("Microsoft.EntityFrameworkCore.Relational", StringComparison.OrdinalIgnoreCase)
                || pacote.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase)
                || pacote.StartsWith("Microsoft.IdentityModel", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(proibidos.Count == 0, $"A Application referencia: {string.Join(", ", proibidos)}.");
    }

    /// <summary>
    /// CLAUDE.md, seção 9: estado não muda por atribuição arbitrária. Nenhuma propriedade
    /// de tipo do domínio pode ter setter — nem <c>init</c> — público.
    /// </summary>
    [Fact]
    public void TiposDoDominioNaoExpoemSetterPublico()
    {
        var infratores = typeof(ExcecaoDeDominio).Assembly
            .GetTypes()
            .Where(tipo => tipo is { IsPublic: true, IsClass: true } && !typeof(Exception).IsAssignableFrom(tipo))
            .SelectMany(tipo => tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(propriedade => propriedade.SetMethod is { IsPublic: true })
            .Select(propriedade => $"{propriedade.DeclaringType!.Name}.{propriedade.Name}")
            .ToList();

        Assert.True(
            infratores.Count == 0,
            "Propriedades de domínio com setter público:" + Environment.NewLine + string.Join(Environment.NewLine, infratores));
    }

    /// <summary>
    /// Emissão e validação de token são da borda HTTP. A Infrastructure pode usar o
    /// hasher do Identity, mas não pacotes de token nem o ASP.NET Core.
    /// </summary>
    [Fact]
    public void InfrastructureNaoEmiteNemValidaToken()
    {
        var pacotes = Repositorio.ReferenciasDePacote("TorreLogistica.Infrastructure");

        var proibidos = pacotes
            .Where(pacote =>
                pacote.StartsWith("Microsoft.IdentityModel", StringComparison.OrdinalIgnoreCase)
                || pacote.StartsWith("System.IdentityModel", StringComparison.OrdinalIgnoreCase)
                || pacote.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(proibidos.Count == 0, $"A Infrastructure referencia: {string.Join(", ", proibidos)}.");
    }
}
