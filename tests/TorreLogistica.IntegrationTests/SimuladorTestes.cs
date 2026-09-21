using Microsoft.Extensions.Logging.Abstractions;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.IntegrationTests.Infra;
using TorreLogistica.Simulator.Cenario;
using TorreLogistica.Simulator.Cliente;
using TorreLogistica.Simulator.Configuracao;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// A demonstração, encenada contra a API real.
/// </summary>
/// <remarks>
/// <para>
/// O simulador é exercitado aqui como um integrador qualquer: só HTTP, autenticado, sem acesso ao banco —
/// o projeto dele nem referencia a persistência, e um teste de arquitetura guarda isso. O que se prova
/// nestes testes é o critério de aceite da fase: <b>resetar e reproduzir leva ao mesmo storytelling</b>.
/// </para>
/// <para>
/// O tempo do roteiro roda muito comprimido para o teste caber em segundos. Isso tem consequência
/// honesta: as histórias que dependem de o servidor reagir sozinho — risco de atraso e motorista
/// offline — não têm tempo de reagir aqui, e terminam em <c>EmRota</c>. Isso vale igualmente nas duas
/// execuções, então não atrapalha a comparação; o que essas histórias provam de verdade só aparece numa
/// demonstração em ritmo de apresentação, e está documentado no README do simulador.
/// </para>
/// </remarks>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class SimuladorTestes(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    /// <summary>Critério de aceite da Fase 23.</summary>
    [Fact]
    public async Task MesmaSementeContaAMesmaHistoria()
    {
        var primeira = await EncenarAsync(semente: 2026);
        var segunda = await EncenarAsync(semente: 2026);

        Assert.Equal(6, primeira.Count);

        // O storytelling é a sequência: quais histórias, em que ordem, terminando em que estado, com que
        // eventos. Identificador e código humano mudam — eles são de cada execução, não da narrativa.
        Assert.Equal(Narrativa(primeira), Narrativa(segunda));
    }

    /// <summary>As seis histórias que a fase exige, cada uma terminando onde deve.</summary>
    [Fact]
    public async Task AsSeisHistoriasAcontecemEChegamAoDesfechoEsperado()
    {
        var desfechos = await EncenarAsync(semente: 7);
        var porHistoria = desfechos.ToDictionary(desfecho => desfecho.Historia);

        Assert.Equal(6, porHistoria.Count);

        // A — operação normal: sai, chega e entrega.
        Assert.Equal("Entregue", porHistoria[Historia.OperacaoNormal].StatusFinal);
        Assert.Contains("ChegadaRegistrada", porHistoria[Historia.OperacaoNormal].Eventos);

        // D — tentativa frustrada: a porta fechada vira estado, não desaparece.
        Assert.Equal("TentativaFrustrada", porHistoria[Historia.TentativaFrustrada].StatusFinal);
        Assert.Contains("TentativaFrustrada", porHistoria[Historia.TentativaFrustrada].Eventos);

        // E — geofence: ninguém chamou "chegada"; o domínio decidiu pela distância.
        Assert.Equal("ProximaDoDestino", porHistoria[Historia.EntradaNoGeofence].StatusFinal);
        Assert.Contains("ProximidadeDetectada", porHistoria[Historia.EntradaNoGeofence].Eventos);
        Assert.DoesNotContain("ChegadaRegistrada", porHistoria[Historia.EntradaNoGeofence].Eventos);

        // E a distinção entre A e E é real: na normal, quem registra a chegada é o motorista, e o roteiro
        // para fora do raio justamente para não deixar o sistema responder no lugar dele.
        Assert.DoesNotContain("ProximidadeDetectada", porHistoria[Historia.OperacaoNormal].Eventos);

        // F — prova de entrega: conclusão com comprovante.
        Assert.Equal("Entregue", porHistoria[Historia.ProvaDeEntrega].StatusFinal);

        // B e C dependem de o servidor reagir sozinho, o que não cabe no ritmo do teste. O que se afirma
        // aqui é que elas foram encenadas e saíram para rota — o resto é da demonstração em tempo real.
        Assert.Contains("SaiuParaRota", porHistoria[Historia.RiscoDeAtraso].Eventos);
        Assert.Contains("SaiuParaRota", porHistoria[Historia.MotoristaOffline].Eventos);
    }

    /// <summary>Semente diferente, operação diferente — senão a semente seria enfeite.</summary>
    [Fact]
    public async Task SementeDiferenteMudaAOperacao()
    {
        var comUma = new RoteiroDaDemonstracao(2026);
        var comOutra = new RoteiroDaDemonstracao(7);

        Assert.NotEqual(comUma.Etiqueta, comOutra.Etiqueta);
        Assert.NotEqual(comUma.EmailDoMotorista, comOutra.EmailDoMotorista);
        Assert.NotEqual(
            comUma.Capitulos.Select(capitulo => capitulo.Destino.Nome),
            comOutra.Capitulos.Select(capitulo => capitulo.Destino.Nome));

        // A ordem das histórias, porém, é do roteiro e não do sorteio: ela conta uma progressão.
        Assert.Equal(
            comUma.Capitulos.Select(capitulo => capitulo.Historia),
            comOutra.Capitulos.Select(capitulo => capitulo.Historia));

        await Task.CompletedTask;
    }

    private static IReadOnlyList<string> Narrativa(IReadOnlyList<DesfechoDaHistoria> desfechos) =>
        [.. desfechos.Select(desfecho =>
            $"{desfecho.Historia}|{desfecho.Titulo}|{desfecho.StatusFinal}|{string.Join(",", desfecho.Eventos)}")];

    private async Task<IReadOnlyList<DesfechoDaHistoria>> EncenarAsync(int semente)
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(Perfil.Administrador);
        var administrador = organizacao.Com(Perfil.Administrador);

        var http = Fabrica.Cliente();
        var opcoes = new OpcoesDoSimulador
        {
            UrlBaseDaApi = http.BaseAddress!.ToString(),
            Organizacao = organizacao.Slug,
            EmailDoAdministrador = administrador.Email,
            Senha = Infra.Cenario.SenhaPadrao,
            OrigemDeclarada = FabricaDaApi.OrigemAutorizada,
            Semente = semente,

            // O tempo do roteiro corre muito mais rápido do que numa apresentação: o que o teste mede é
            // a narrativa, não a paciência de quem assiste.
            MultiplicadorDeTempo = 600,
            IntervaloEntrePosicoes = TimeSpan.FromSeconds(6),
            EsperaPelaTorre = TimeSpan.FromSeconds(6),
        };

        var encenacao = new EncenacaoDaDemonstracao(
            new ClienteDaApiDaTorre(http, opcoes.OrigemDeclarada),
            opcoes,
            NullLogger<EncenacaoDaDemonstracao>.Instance);

        return await encenacao.EncenarAsync(Cancelamento);
    }
}
