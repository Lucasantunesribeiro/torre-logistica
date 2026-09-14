using System.Reflection;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Rotas;

namespace TorreLogistica.ArchitectureTests;

/// <summary>
/// Critério de aceite da Fase 5, do lado do domínio: toda mudança de status passa por comando
/// explícito, e não existe mutador genérico de status.
/// </summary>
public sealed class ComandosDeStatusTestes
{
    [Theory]
    [InlineData(typeof(Entrega))]
    [InlineData(typeof(Rota))]
    public void StatusNaoTemSetterNemMetodoGenerico(Type agregado)
    {
        ArgumentNullException.ThrowIfNull(agregado);

        var status = agregado.GetProperty("Status")!;
        Assert.False(status.SetMethod?.IsPublic ?? false, $"{agregado.Name}.Status tem setter público.");

        var genericos = agregado
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(metodo => !metodo.IsSpecialName && metodo.Name.Contains("Status", StringComparison.OrdinalIgnoreCase))
            .Select(metodo => metodo.Name)
            .ToList();

        Assert.True(genericos.Count == 0, $"{agregado.Name} expõe método genérico de status: {string.Join(", ", genericos)}");
    }

    /// <summary>
    /// Os métodos públicos que produzem evento de mudança na entrega são exatamente os comandos
    /// da máquina de estados, mais criar e alterar dados. Um método novo que mude status sem
    /// estar na máquina faz este teste falhar.
    /// </summary>
    [Fact]
    public void MetodosQueProduzemEventoSaoOsComandosDaMaquina()
    {
        var produzemEvento = typeof(Entrega)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(metodo => metodo.ReturnType == typeof(EventoDaEntrega)
                || metodo.ReturnType == typeof(AlteracaoDaEntrega)
                || metodo.ReturnType == typeof((Entrega, EventoDaEntrega)))
            .Select(metodo => metodo.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        var esperados = Enum.GetNames<ComandoDaEntrega>()
            .Append(nameof(Entrega.Criar))
            .Append(nameof(Entrega.AtualizarDados))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(esperados, produzemEvento);
    }
}
