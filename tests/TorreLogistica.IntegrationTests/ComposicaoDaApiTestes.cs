using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// O grafo de dependências que a API monta.
/// </summary>
/// <remarks>
/// <para>
/// A contraparte de <c>ComposicaoDoProcessoDeTrabalhoTestes</c>, do outro lado da divisão. Aqui sobe o
/// <c>Program.cs</c> de verdade — não uma reconstrução dele —, em <c>Development</c> e com
/// <c>ValidateOnBuild</c> e <c>ValidateScopes</c> ligados à força.
/// </para>
/// <para>
/// Ligados à força de propósito: a suíte roda em <c>Testing</c>, onde a validação fica desligada, e foi
/// exatamente essa diferença entre ambientes que escondeu o defeito do processo de trabalho até ele ser
/// executado à mão em desenvolvimento.
/// </para>
/// </remarks>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class ComposicaoDaApiTestes(ContainerPostgis banco)
{
    /// <summary>A API sobe em Development com o contêiner validando cada descritor.</summary>
    [Fact]
    public void ApiConstroiOProvedorEmDevelopmentComValidacaoLigada()
    {
        using var fabrica = new FabricaDaApi(
            banco,
            ambiente: "Development",
            validarContainer: true,
            registrarLacoDePrevisao: false);

        Assert.NotNull(fabrica.Services.GetRequiredService<IConfiguration>());
    }

    /// <summary>
    /// A API não hospeda os laços de fundo — é essa ausência que separa os dois processos.
    /// </summary>
    /// <remarks>
    /// O laço de previsão fica de fora da fábrica neste teste. Nos demais testes a suíte o registra de
    /// propósito, para provar o efeito no banco com um processo só; aqui o que se quer ver é a API como o
    /// <c>Program.cs</c> a compõe.
    /// </remarks>
    [Fact]
    public void ApiNaoHospedaOsLacosDeFundo()
    {
        using var fabrica = new FabricaDaApi(
            banco,
            ambiente: "Development",
            validarContainer: true,
            registrarLacoDePrevisao: false);

        var hospedados = fabrica.Services
            .GetServices<IHostedService>()
            .Select(servico => servico.GetType().Name)
            .ToList();

        foreach (var laco in new[]
        {
            "ProcessadorDePrevisoes",
            "ProcessadorDeWebhooks",
            "ProcessadorDeRetencao",
            "ProcessadorDeMedidas",
        })
        {
            Assert.DoesNotContain(laco, hospedados);
        }
    }
}
