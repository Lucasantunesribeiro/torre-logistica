using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TorreLogistica.Application.Abstracoes.Identidade;
using TorreLogistica.Application.Abstracoes.Seguranca;
using TorreLogistica.Application.Alertas;
using TorreLogistica.Application.Identidade;
using TorreLogistica.Application.Observabilidade;
using TorreLogistica.Application.Previsao;
using TorreLogistica.Application.Webhooks;
using TorreLogistica.Workers;

namespace TorreLogistica.ArchitectureTests;

/// <summary>
/// O grafo de dependências que o processo de trabalho monta.
/// </summary>
/// <remarks>
/// <para>
/// Estes testes existem por causa de um defeito real: o processo de trabalho registrava a camada de
/// aplicação inteira para usar seis casos de uso, e arrastava junto os que dependem de
/// <see cref="IContextoDoUsuario"/> e <see cref="IEmissorDeTokenDeAcesso"/> — abstrações que a borda HTTP
/// implementa e que processo de fundo nenhum pode satisfazer.
/// </para>
/// <para>
/// O sintoma aparecia só em desenvolvimento, porque é lá que o contêiner valida cada descritor na
/// construção. Em produção, que não valida, o processo subia carregando um grafo impossível e só quebraria
/// se alguém resolvesse um daqueles serviços. Por isso os testes aqui ligam <c>ValidateOnBuild</c> e
/// <c>ValidateScopes</c> <b>explicitamente</b>: o que eles provam não pode depender do ambiente em que a
/// suíte roda.
/// </para>
/// </remarks>
public sealed class ComposicaoDoProcessoDeTrabalhoTestes
{
    /// <summary>Configuração mínima. Validar o grafo não abre conexão com nada.</summary>
    private static IConfiguration Configuracao => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Torre:BancoDeDados:CadeiaDeConexao"] = "Host=localhost;Database=validacao;Username=validacao;Password=validacao",
            ["Torre:Armazenamento:EnderecoBase"] = "http://localhost:5080",
        })
        .Build();

    /// <summary>O processo monta um grafo válido, nos dois ambientes, com validação total.</summary>
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void ConstroiOProvedorComValidacaoLigada(string ambiente)
    {
        using var host = ConstruirValidando(ambiente);

        Assert.NotNull(host.Services);
    }

    /// <summary>
    /// Nada exclusivo da sessão humana entra no grafo.
    /// </summary>
    /// <remarks>
    /// A checagem é sobre o que está <b>registrado</b>, não sobre o que falha ao resolver: um serviço que
    /// ninguém resolve hoje mas está no grafo volta a ser o defeito de ontem no dia em que alguém o
    /// resolver.
    /// </remarks>
    [Fact]
    public void NaoRegistraNadaDaSessaoHumana()
    {
        var registrados = new ServiceCollection()
            .AdicionarProcessoDeTrabalho(Configuracao)
            .Select(descritor => descritor.ServiceType)
            .ToHashSet();

        Type[] exclusivosDaBorda =
        [
            typeof(IContextoDoUsuario),
            typeof(IEmissorDeTokenDeAcesso),
            typeof(AutenticarUsuario),
            typeof(RenovarSessao),
            typeof(EncerrarSessao),
            typeof(ValidarSessaoAtiva),
        ];

        var intrusos = exclusivosDaBorda.Where(registrados.Contains).Select(tipo => tipo.Name).ToList();

        Assert.True(
            intrusos.Count == 0,
            "O processo de trabalho não deve conhecer a sessão humana, e registrou: " + string.Join(", ", intrusos));
    }

    /// <summary>
    /// Nenhum tipo do grafo pede, no construtor, algo que só a borda HTTP implementa.
    /// </summary>
    /// <remarks>
    /// A prova anterior olha o que está registrado; esta olha o que os registrados <b>exigem</b>. Juntas,
    /// fecham os dois caminhos pelos quais uma dependência de requisição voltaria a entrar aqui.
    /// </remarks>
    [Fact]
    public void NenhumServicoRegistradoExigeDependenciaDeRequisicao()
    {
        Type[] proibidas = [typeof(IContextoDoUsuario), typeof(IEmissorDeTokenDeAcesso)];

        var culpados = new ServiceCollection()
            .AdicionarProcessoDeTrabalho(Configuracao)
            .Select(descritor => descritor.ImplementationType)
            .Where(tipo => tipo is not null)
            .Distinct()
            .SelectMany(tipo => tipo!.GetConstructors().SelectMany(construtor => construtor.GetParameters())
                .Where(parametro => proibidas.Contains(parametro.ParameterType))
                .Select(parametro => $"{tipo.Name}({parametro.ParameterType.Name})"))
            .ToList();

        Assert.True(culpados.Count == 0, "Dependência de requisição no grafo de fundo: " + string.Join(", ", culpados));
    }

    /// <summary>O que os laços resolvem continua lá — a divisão não pode ter cortado demais.</summary>
    [Fact]
    public void ResolveOsCasosDeUsoQueOsLacosExecutam()
    {
        using var host = ConstruirValidando(Environments.Development);
        using var escopo = host.Services.CreateScope();
        var servicos = escopo.ServiceProvider;

        // Exatamente os que os laços resolvem. Um deles faltando é o processo subindo para morrer na
        // primeira rodada — que foi como este projeto descobriu o problema da primeira vez.
        Assert.NotNull(servicos.GetRequiredService<RecalculoDePrevisoes>());
        Assert.NotNull(servicos.GetRequiredService<MonitoramentoOperacional>());
        Assert.NotNull(servicos.GetRequiredService<DespachoDeWebhooks>());
        Assert.NotNull(servicos.GetRequiredService<EntregaDeWebhooks>());
        Assert.NotNull(servicos.GetRequiredService<LeituraDoEstadoDaOperacao>());
        Assert.NotNull(host.Services.GetRequiredService<MedidasDaOperacao>());
    }

    /// <summary>Os laços de fundo estão hospedados aqui — e são estes.</summary>
    [Fact]
    public void HospedaOsLacosEsperados()
    {
        var hospedados = new ServiceCollection()
            .AdicionarProcessoDeTrabalho(Configuracao)
            .Where(descritor => descritor.ServiceType == typeof(IHostedService))
            .Select(descritor => descritor.ImplementationType?.Name ?? string.Empty)
            .ToList();

        foreach (var esperado in new[]
        {
            "ProcessadorDePrevisoes",
            "ProcessadorDeWebhooks",
            "ProcessadorDeRetencao",
            "ProcessadorDeMedidas",
            "ServicoDeVerificacaoDeInfraestrutura",
        })
        {
            Assert.Contains(esperado, hospedados);
        }
    }

    /// <summary>
    /// Monta o host de verdade — o mesmo <c>Host.CreateApplicationBuilder</c> do <c>Program.cs</c> — com a
    /// validação do contêiner ligada à força.
    /// </summary>
    /// <remarks>
    /// Um <c>ServiceCollection</c> avulso não serviria: faltariam os serviços que o host fornece
    /// (<c>IHostApplicationLifetime</c>, <c>IHostEnvironment</c>), e o teste acabaria registrando dublês
    /// deles só para o grafo fechar — exatamente o tipo de remendo que este trabalho veio eliminar.
    /// </remarks>
    private static IHost ConstruirValidando(string ambiente)
    {
        var construtor = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = ambiente,
            ApplicationName = "TorreLogistica.Composicao",
            ContentRootPath = AppContext.BaseDirectory,
        });

        construtor.Configuration.AddConfiguration(Configuracao);
        construtor.Services.AdicionarProcessoDeTrabalho(construtor.Configuration);

        // A régua, ligada explicitamente: o que este teste prova não pode depender do ambiente em que a
        // suíte roda, e é justamente a diferença entre ambientes que escondia o defeito.
        construtor.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        }));

        return construtor.Build();
    }
}
