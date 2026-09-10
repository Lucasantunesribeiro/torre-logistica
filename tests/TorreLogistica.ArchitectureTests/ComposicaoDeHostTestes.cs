namespace TorreLogistica.ArchitectureTests;

/// <summary>
/// Guarda a ancoragem do content root nos hosts que não são web.
/// </summary>
/// <remarks>
/// <para>
/// <c>Host.CreateApplicationBuilder</c> usa o diretório de trabalho do processo como
/// content root. Iniciado de outra pasta — um serviço do Windows, um contêiner, um
/// agendador, ou simplesmente <c>dotnet run</c> a partir da raiz do repositório — o
/// host não encontra o <c>appsettings.json</c>.
/// </para>
/// <para>
/// O que torna isso perigoso é o sintoma: não há exceção. O Serilog configurado por
/// arquivo fica sem sink algum e o processo roda em silêncio absoluto, aparentando
/// saúde. Foi exatamente o que aconteceu com o simulador durante a Fase 0, e só
/// apareceu porque alguém foi conferir a saída esperada.
/// </para>
/// <para>
/// A verificação é por leitura do código-fonte porque não há como observar a ausência
/// de log: um teste de comportamento não distingue "não logou nada" de "não tinha nada
/// para logar".
/// </para>
/// </remarks>
public sealed class ComposicaoDeHostTestes
{
    private const string AncoragemEsperada = "ContentRootPath = AppContext.BaseDirectory";

    [Theory]
    [InlineData("TorreLogistica.Workers")]
    [InlineData("TorreLogistica.Simulator")]
    public void HostNaoWebAncoraOContentRootNoProprioExecutavel(string nomeDoProjeto)
    {
        var programa = Repositorio.ArquivosDeCodigo(nomeDoProjeto)
            .SingleOrDefault(arquivo =>
                string.Equals(Path.GetFileName(arquivo), "Program.cs", StringComparison.Ordinal));

        Assert.NotNull(programa);

        var conteudo = File.ReadAllText(programa);

        Assert.Contains(AncoragemEsperada, conteudo, StringComparison.Ordinal);
    }
}
