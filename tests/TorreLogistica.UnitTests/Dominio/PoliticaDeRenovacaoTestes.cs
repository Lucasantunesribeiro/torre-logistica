using TorreLogistica.Domain.Identidade;

namespace TorreLogistica.UnitTests.Dominio;

/// <summary>
/// Cobre cada ramo da decisão sobre um token de renovação apresentado.
/// </summary>
/// <remarks>
/// É a regra em que um erro ou mantém um invasor logado, ou derruba o usuário legítimo a
/// cada rede ruim. Por isso cada combinação relevante tem um teste com nome próprio.
/// </remarks>
public sealed class PoliticaDeRenovacaoTestes
{
    private static readonly DateTimeOffset Inicio = new(2026, 9, 14, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Janela = TimeSpan.FromSeconds(20);

    [Fact]
    public void TokenDisponivelEhRotacionado()
    {
        var (sessao, token) = SessaoComToken();

        var decisao = Avaliar(token, null, sessao, Inicio.AddMinutes(5));

        Assert.Equal(DecisaoDeRenovacao.Rotacionar, decisao);
    }

    [Fact]
    public void TokenVencidoSemUsoEhRecusadoSemRevogar()
    {
        var (sessao, token) = SessaoComToken(validadeDoToken: TimeSpan.FromMinutes(10));

        var decisao = Avaliar(token, null, sessao, Inicio.AddMinutes(11));

        Assert.Equal(DecisaoDeRenovacao.Recusar, decisao);
    }

    [Fact]
    public void SessaoRevogadaRecusaQualquerToken()
    {
        var (sessao, token) = SessaoComToken();
        sessao.Revogar(MotivoDeRevogacao.Logout, Inicio.AddMinutes(1));

        Assert.Equal(DecisaoDeRenovacao.Recusar, Avaliar(token, null, sessao, Inicio.AddMinutes(2)));
    }

    [Fact]
    public void SessaoAlemDoPrazoAbsolutoRecusaToken()
    {
        var (sessao, token) = SessaoComToken(duracaoDaSessao: TimeSpan.FromHours(1));

        Assert.Equal(DecisaoDeRenovacao.Recusar, Avaliar(token, null, sessao, Inicio.AddHours(1)));
    }

    /// <summary>
    /// O cookie do console nunca vai para a rota do motorista, mas a regra não depende
    /// disso: token apresentado pelo canal errado é recusado.
    /// </summary>
    [Fact]
    public void TokenApresentadoPeloOutroCanalEhRecusado()
    {
        var (sessao, token) = SessaoComToken();

        var decisao = PoliticaDeRenovacao.Avaliar(
            token, null, sessao, CanalDeAcesso.Motorista, Inicio.AddMinutes(1), Janela);

        Assert.Equal(DecisaoDeRenovacao.Recusar, decisao);
    }

    [Fact]
    public void TokenDeOutraSessaoEhRecusado()
    {
        var (_, token) = SessaoComToken();
        var (outraSessao, _) = SessaoComToken();

        Assert.Equal(DecisaoDeRenovacao.Recusar, Avaliar(token, null, outraSessao, Inicio.AddMinutes(1)));
    }

    [Fact]
    public void TokenUsadoReapresentadoDentroDaJanelaComSucessorIntactoEhRetry()
    {
        var (sessao, token) = SessaoComToken();
        var sucessor = Suceder(sessao, token, Inicio.AddMinutes(10));

        var decisao = Avaliar(token, sucessor, sessao, Inicio.AddMinutes(10).AddSeconds(5));

        Assert.Equal(DecisaoDeRenovacao.RotacionarNovamenteNaJanelaDeTolerancia, decisao);
    }

    [Fact]
    public void TokenUsadoReapresentadoNoLimiteExatoDaJanelaAindaEhRetry()
    {
        var (sessao, token) = SessaoComToken();
        var sucessor = Suceder(sessao, token, Inicio.AddMinutes(10));

        var decisao = Avaliar(token, sucessor, sessao, Inicio.AddMinutes(10) + Janela);

        Assert.Equal(DecisaoDeRenovacao.RotacionarNovamenteNaJanelaDeTolerancia, decisao);
    }

    [Fact]
    public void TokenUsadoReapresentadoForaDaJanelaEhReuso()
    {
        var (sessao, token) = SessaoComToken();
        var sucessor = Suceder(sessao, token, Inicio.AddMinutes(10));

        var decisao = Avaliar(token, sucessor, sessao, Inicio.AddMinutes(10) + Janela + TimeSpan.FromSeconds(1));

        Assert.Equal(DecisaoDeRenovacao.ReusoDetectado, decisao);
    }

    /// <summary>
    /// Se o sucessor já foi usado, alguém seguiu em frente com a cadeia. Reapresentar o
    /// antecessor não é retry de rede — é o sinal clássico de token copiado.
    /// </summary>
    [Fact]
    public void TokenUsadoComSucessorJaUsadoEhReusoMesmoDentroDaJanela()
    {
        var (sessao, token) = SessaoComToken();
        var sucessor = Suceder(sessao, token, Inicio.AddMinutes(10));
        Suceder(sessao, sucessor, Inicio.AddMinutes(10).AddSeconds(2));

        var decisao = Avaliar(token, sucessor, sessao, Inicio.AddMinutes(10).AddSeconds(5));

        Assert.Equal(DecisaoDeRenovacao.ReusoDetectado, decisao);
    }

    [Fact]
    public void TokenUsadoComSucessorDescartadoEhReusoMesmoDentroDaJanela()
    {
        var (sessao, token) = SessaoComToken();
        var sucessor = Suceder(sessao, token, Inicio.AddMinutes(10));
        sucessor.Invalidar(Inicio.AddMinutes(10).AddSeconds(1));

        var decisao = Avaliar(token, sucessor, sessao, Inicio.AddMinutes(10).AddSeconds(5));

        Assert.Equal(DecisaoDeRenovacao.ReusoDetectado, decisao);
    }

    [Fact]
    public void TokenUsadoSemSucessorCarregadoEhReuso()
    {
        var (sessao, token) = SessaoComToken();
        Suceder(sessao, token, Inicio.AddMinutes(10));

        var decisao = Avaliar(token, null, sessao, Inicio.AddMinutes(10).AddSeconds(5));

        Assert.Equal(DecisaoDeRenovacao.ReusoDetectado, decisao);
    }

    /// <summary>
    /// Token descartado sem uso só aparece nas mãos de quem ficou para trás numa corrida
    /// com um invasor. Nos dois casos, a família inteira precisa cair.
    /// </summary>
    [Fact]
    public void TokenDescartadoApresentadoEhReuso()
    {
        var (sessao, token) = SessaoComToken();
        token.Invalidar(Inicio.AddMinutes(1));

        Assert.Equal(DecisaoDeRenovacao.ReusoDetectado, Avaliar(token, null, sessao, Inicio.AddMinutes(1).AddSeconds(1)));
    }

    [Fact]
    public void JanelaZeradaTransformaQualquerReapresentacaoEmReuso()
    {
        var (sessao, token) = SessaoComToken();
        var sucessor = Suceder(sessao, token, Inicio.AddMinutes(10));

        var decisao = PoliticaDeRenovacao.Avaliar(
            token, sucessor, sessao, CanalDeAcesso.Operacao, Inicio.AddMinutes(10).AddMilliseconds(1), TimeSpan.Zero);

        Assert.Equal(DecisaoDeRenovacao.ReusoDetectado, decisao);
    }

    private static DecisaoDeRenovacao Avaliar(
        TokenDeRenovacao token, TokenDeRenovacao? sucessor, Sessao sessao, DateTimeOffset agora) =>
        PoliticaDeRenovacao.Avaliar(token, sucessor, sessao, CanalDeAcesso.Operacao, agora, Janela);

    private static (Sessao Sessao, TokenDeRenovacao Token) SessaoComToken(
        TimeSpan? duracaoDaSessao = null,
        TimeSpan? validadeDoToken = null)
    {
        var usuario = Fabrica.Usuario(Perfil.Operador);
        var sessao = Sessao.Abrir(Guid.CreateVersion7(), usuario, Inicio, duracaoDaSessao ?? TimeSpan.FromHours(12));
        var token = TokenDeRenovacao.Emitir(
            Guid.CreateVersion7(), sessao, Fabrica.Hash(), Inicio, validadeDoToken ?? TimeSpan.FromHours(12));

        return (sessao, token);
    }

    private static TokenDeRenovacao Suceder(Sessao sessao, TokenDeRenovacao anterior, DateTimeOffset agora)
    {
        var sucessor = TokenDeRenovacao.Emitir(Guid.CreateVersion7(), sessao, Fabrica.Hash(), agora, TimeSpan.FromHours(12));
        anterior.MarcarComoUsado(sucessor.Id, agora);
        return sucessor;
    }
}
