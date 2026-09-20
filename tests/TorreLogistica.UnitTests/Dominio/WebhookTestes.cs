using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Webhooks;

namespace TorreLogistica.UnitTests.Dominio;

public sealed class MensagemDoOutboxTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CriaOEventoDisponivelDeImediato()
    {
        var mensagem = Criar();

        Assert.Equal(TiposDeEventoDeWebhook.EntregaConcluida, mensagem.Tipo);
        Assert.Equal(Agora, mensagem.DisponivelEm);
        Assert.Null(mensagem.DespachadaEm);
        Assert.Equal(0, mensagem.TentativasDeDespacho);
    }

    [Fact]
    public void TipoForaDoVocabularioEhRecusado() =>
        Assert.Equal(
            "tipo_de_evento_invalido",
            Assert.Throws<ExcecaoDeDominio>(() => Criar(tipo: "delivery.whatever")).Codigo);

    [Fact]
    public void ConteudoVazioEhRecusado() =>
        Assert.Equal("conteudo_invalido", Assert.Throws<ExcecaoDeDominio>(() => Criar(conteudo: "  ")).Codigo);

    [Fact]
    public void DespacharDuasVezesGuardaOPrimeiroInstante()
    {
        var mensagem = Criar();

        mensagem.MarcarDespachada(Agora.AddMinutes(1));
        mensagem.MarcarDespachada(Agora.AddMinutes(9));

        Assert.Equal(Agora.AddMinutes(1), mensagem.DespachadaEm);
    }

    [Fact]
    public void FalhaNoDespachoAdiaComEsperaCrescente()
    {
        var mensagem = Criar();

        mensagem.AdiarPorFalha("banco indisponível", Agora);
        Assert.Equal(1, mensagem.TentativasDeDespacho);
        Assert.Equal(Agora.AddSeconds(30), mensagem.DisponivelEm);

        mensagem.AdiarPorFalha("de novo", Agora.AddSeconds(30));
        Assert.Equal(Agora.AddSeconds(30).AddMinutes(2), mensagem.DisponivelEm);
    }

    private static MensagemDoOutbox Criar(string tipo = TiposDeEventoDeWebhook.EntregaConcluida, string conteudo = "{}") =>
        MensagemDoOutbox.Criar(Guid.CreateVersion7(), Guid.CreateVersion7(), tipo, Guid.CreateVersion7(), conteudo, Agora, Agora);
}

public sealed class AssinaturaDeWebhookTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CriaAssinaturaAtivaComEventosEscolhidos()
    {
        var assinatura = Criar(eventos: [TiposDeEventoDeWebhook.EntregaConcluida]);

        Assert.True(assinatura.EstaAtiva);
        Assert.Equal([TiposDeEventoDeWebhook.EntregaConcluida], assinatura.Eventos);
        Assert.True(assinatura.Assina(TiposDeEventoDeWebhook.EntregaConcluida));
        Assert.False(assinatura.Assina(TiposDeEventoDeWebhook.EntregaIniciada));
    }

    [Fact]
    public void SemEventoEscolhidoAssinaTodos()
    {
        var assinatura = Criar(eventos: null);

        Assert.All(TiposDeEventoDeWebhook.Todos, evento => Assert.True(assinatura.Assina(evento)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nao-e-url")]
    [InlineData("ftp://arquivos.exemplo.test/hook")]
    [InlineData("/apenas/caminho")]
    public void UrlInvalidaEhRecusada(string? url) =>
        Assert.Equal("url_invalida", Assert.Throws<ExcecaoDeDominio>(() => Criar(url: url)).Codigo);

    [Fact]
    public void EventoDesconhecidoEhRecusado() =>
        Assert.Equal(
            "tipo_de_evento_invalido",
            Assert.Throws<ExcecaoDeDominio>(() => Criar(eventos: ["delivery.exploded"])).Codigo);

    [Fact]
    public void NomeEhObrigatorio() =>
        Assert.Equal("nome_invalido", Assert.Throws<ExcecaoDeDominio>(() => Criar(nome: "   ")).Codigo);

    [Fact]
    public void RevogarEhIdempotente()
    {
        var assinatura = Criar();

        assinatura.Revogar(Agora.AddHours(1));
        assinatura.Revogar(Agora.AddHours(5));

        Assert.False(assinatura.EstaAtiva);
        Assert.Equal(Agora.AddHours(1), assinatura.RevogadaEm);
    }

    private static AssinaturaDeWebhook Criar(
        string? nome = "ERP do cliente",
        string? url = "https://erp.exemplo.test/hooks/torre",
        IReadOnlyCollection<string>? eventos = null) =>
        AssinaturaDeWebhook.Criar(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            nome,
            url,
            new byte[48],
            eventos,
            Guid.CreateVersion7(),
            Agora);
}

public sealed class EntregaDeWebhookTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NasceDisponivelEPendente()
    {
        var entrega = Criar();

        Assert.Equal(EstadoDaEntregaDeWebhook.Pendente, entrega.Estado);
        Assert.Equal(Agora, entrega.DisponivelEm);
        Assert.Equal(0, entrega.Tentativas);
    }

    [Fact]
    public void SucessoEncerraAEntregaEGeraTentativa()
    {
        var entrega = Criar();

        var tentativa = entrega.RegistrarSucesso(Guid.CreateVersion7(), 204, 42, Agora.AddSeconds(1));

        Assert.Equal(EstadoDaEntregaDeWebhook.Entregue, entrega.Estado);
        Assert.Equal(204, entrega.UltimoStatus);
        Assert.Null(entrega.UltimoErro);
        Assert.Equal(Agora.AddSeconds(1), entrega.ConcluidaEm);
        Assert.Equal(1, tentativa.Numero);
        Assert.Equal(42, tentativa.DuracaoEmMilissegundos);
    }

    [Fact]
    public void FalhaAdiaComBackoffAteDesistir()
    {
        var entrega = Criar();
        var instante = Agora;

        for (var numero = 1; numero < PoliticaDeWebhook.MaximoDeTentativas; numero++)
        {
            entrega.RegistrarFalha(Guid.CreateVersion7(), 500, "erro do assinante", 10, instante);

            Assert.Equal(EstadoDaEntregaDeWebhook.Pendente, entrega.Estado);
            Assert.Equal(instante + PoliticaDeWebhook.EsperaAntesDaTentativa(numero + 1), entrega.DisponivelEm);

            instante = entrega.DisponivelEm;
        }

        entrega.RegistrarFalha(Guid.CreateVersion7(), 500, "última", 10, instante);

        // Esgotadas as tentativas, a entrega não some: fica visível no estado falhado.
        Assert.Equal(EstadoDaEntregaDeWebhook.Falhada, entrega.Estado);
        Assert.Equal(PoliticaDeWebhook.MaximoDeTentativas, entrega.Tentativas);
        Assert.Equal(instante, entrega.ConcluidaEm);
    }

    [Fact]
    public void ReenvioSoValeParaEntregaFalhada()
    {
        var pendente = Criar();
        Assert.Equal("entrega_nao_reenviavel", Assert.Throws<ExcecaoDeDominio>(() => pendente.Reenviar(Agora)).Codigo);

        var entregue = Criar();
        entregue.RegistrarSucesso(Guid.CreateVersion7(), 200, 10, Agora);
        Assert.Equal("entrega_nao_reenviavel", Assert.Throws<ExcecaoDeDominio>(() => entregue.Reenviar(Agora)).Codigo);

        var falhada = Criar();

        for (var numero = 0; numero < PoliticaDeWebhook.MaximoDeTentativas; numero++)
        {
            falhada.RegistrarFalha(Guid.CreateVersion7(), 500, "erro", 10, Agora);
        }

        falhada.Reenviar(Agora.AddDays(1));

        Assert.Equal(EstadoDaEntregaDeWebhook.Pendente, falhada.Estado);
        Assert.Equal(0, falhada.Tentativas);
        Assert.Null(falhada.ConcluidaEm);
        Assert.Equal(Agora.AddDays(1), falhada.DisponivelEm);
    }

    [Fact]
    public void ArrendamentoEmpurraADisponibilidade()
    {
        var entrega = Criar();

        entrega.AdiarPorArrendamento(Agora, TimeSpan.FromSeconds(30));

        Assert.Equal(Agora.AddSeconds(30), entrega.DisponivelEm);
        Assert.Equal(EstadoDaEntregaDeWebhook.Pendente, entrega.Estado);
    }

    private static EntregaDeWebhook Criar()
    {
        var organizacao = Guid.CreateVersion7();

        var assinatura = AssinaturaDeWebhook.Criar(
            Guid.CreateVersion7(),
            organizacao,
            "ERP",
            "https://erp.exemplo.test/hooks",
            new byte[48],
            null,
            null,
            Agora);

        var mensagem = MensagemDoOutbox.Criar(
            Guid.CreateVersion7(),
            organizacao,
            TiposDeEventoDeWebhook.EntregaConcluida,
            Guid.CreateVersion7(),
            "{}",
            Agora,
            Agora);

        return EntregaDeWebhook.Criar(Guid.CreateVersion7(), organizacao, assinatura, mensagem, Agora);
    }
}

public sealed class PoliticaDeWebhookTestes
{
    [Fact]
    public void APrimeiraTentativaEhImediataEAsSeguintesCrescem()
    {
        Assert.Equal(TimeSpan.Zero, PoliticaDeWebhook.EsperaAntesDaTentativa(1));

        var anterior = TimeSpan.Zero;

        for (var numero = 2; numero <= PoliticaDeWebhook.MaximoDeTentativas; numero++)
        {
            var espera = PoliticaDeWebhook.EsperaAntesDaTentativa(numero);
            Assert.True(espera > anterior, $"A espera da tentativa {numero} não cresceu.");
            anterior = espera;
        }
    }

    /// <summary>Somadas, as esperas cobrem horas — é o que dá tempo de um assinante voltar do ar.</summary>
    [Fact]
    public void AsEsperasSomadasCobremHoras()
    {
        var total = TimeSpan.Zero;

        for (var numero = 1; numero <= PoliticaDeWebhook.MaximoDeTentativas; numero++)
        {
            total += PoliticaDeWebhook.EsperaAntesDaTentativa(numero);
        }

        Assert.True(total >= TimeSpan.FromHours(2), $"Total de espera curto demais: {total}.");
    }
}
