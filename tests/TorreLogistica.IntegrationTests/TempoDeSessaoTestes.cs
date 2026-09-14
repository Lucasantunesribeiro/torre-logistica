using System.Net;
using Microsoft.Extensions.Time.Testing;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// Prazos de token, sessão e bloqueio, com o relógio da API sob controle do teste.
/// </summary>
/// <remarks>
/// Emissão, validação do token, sessão e bloqueio leem o mesmo relógio. É o que permite
/// avançar 15 minutos sem esperar 15 minutos — e o que prova que nenhuma dessas regras
/// consulta o relógio do sistema por fora.
/// </remarks>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class TempoDeSessaoTestes(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    private readonly FakeTimeProvider _tempo = new(DateTimeOffset.UtcNow);

    protected override TimeProvider Relogio => _tempo;

    protected override IReadOnlyDictionary<string, string?> ConfiguracaoAdicional => new Dictionary<string, string?>
    {
        ["Torre:Autenticacao:DuracaoDaSessaoNoConsole"] = "00:15:00",
    };

    [Fact]
    public async Task TokenDeAcessoVenceNoPrazo()
    {
        var conta = (await Cenario.CriarOrganizacaoAsync(Perfil.Operador)).Com(Perfil.Operador);
        using var cliente = Cliente();
        var sessao = await EntrarAsync(cliente, conta);

        _tempo.Advance(TimeSpan.FromMinutes(14));
        using (var dentroDoPrazo = await EnviarAsync(cliente, HttpMethod.Get, "/api/autenticacao/eu", sessao.TokenDeAcesso))
        {
            Assert.Equal(HttpStatusCode.OK, dentroDoPrazo.StatusCode);
        }

        // Um minuto até o vencimento, mais a tolerância de relógio de 30 segundos.
        _tempo.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(31));
        using var vencido = await EnviarAsync(cliente, HttpMethod.Get, "/api/autenticacao/eu", sessao.TokenDeAcesso);
        Assert.Equal(HttpStatusCode.Unauthorized, vencido.StatusCode);
    }

    [Fact]
    public async Task RenovacaoNaoEstendeASessaoAlemDoPrazoAbsoluto()
    {
        var conta = (await Cenario.CriarOrganizacaoAsync(Perfil.Operador)).Com(Perfil.Operador);
        using var cliente = Cliente();
        var sessao = await EntrarAsync(cliente, conta);

        _tempo.Advance(TimeSpan.FromMinutes(10));
        using var renovacao = await RenovarAsync(cliente, sessao.CookieDeRenovacao);
        var renovada = await LerSessaoAsync(renovacao, CanalDeAcesso.Operacao);

        _tempo.Advance(TimeSpan.FromMinutes(6));
        using var alemDoPrazo = await RenovarAsync(cliente, renovada.CookieDeRenovacao);
        Assert.Equal(HttpStatusCode.Unauthorized, alemDoPrazo.StatusCode);
    }

    [Fact]
    public async Task BloqueioTemporarioBarraASenhaCertaELiberaSozinho()
    {
        var conta = (await Cenario.CriarOrganizacaoAsync(Perfil.Operador)).Com(Perfil.Operador);
        using var cliente = Cliente();

        for (var tentativa = 0; tentativa < 5; tentativa++)
        {
            using var errada = await LoginAsync(cliente, conta.OrganizacaoSlug, conta.Email, "senha-errada-mas-comprida");
            Assert.Equal(HttpStatusCode.Unauthorized, errada.StatusCode);
        }

        using (var bloqueada = await LoginAsync(cliente, conta.OrganizacaoSlug, conta.Email, conta.Senha))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, bloqueada.StatusCode);
            Assert.Equal("credenciais_invalidas", await CodigoDoErroAsync(bloqueada));
        }

        _tempo.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));

        using var liberada = await LoginAsync(cliente, conta.OrganizacaoSlug, conta.Email, conta.Senha);
        Assert.Equal(HttpStatusCode.OK, liberada.StatusCode);
    }
}
