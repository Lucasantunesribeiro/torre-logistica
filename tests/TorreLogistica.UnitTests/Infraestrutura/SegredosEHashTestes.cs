using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using TorreLogistica.Application.Abstracoes.Seguranca;
using TorreLogistica.Application.Identidade;
using TorreLogistica.Infrastructure.Seguranca;

namespace TorreLogistica.UnitTests.Infraestrutura;

public sealed class SegredosDeRenovacaoTestes
{
    [Fact]
    public void TokenGeradoTemFormatoEsperado()
    {
        var (cru, hash) = SegredosDeRenovacao.Gerar();

        Assert.Equal(SegredosDeRenovacao.TamanhoCodificado, cru.Length);
        Assert.True(SegredosDeRenovacao.FormatoEhPlausivel(cru));
        Assert.Equal(32, hash.Length);
    }

    [Fact]
    public void HashEhDeterministicoParaOMesmoToken()
    {
        var (cru, hash) = SegredosDeRenovacao.Gerar();

        Assert.Equal(hash, SegredosDeRenovacao.CalcularHash(cru));
    }

    [Fact]
    public void TokensGeradosNaoSeRepetem()
    {
        var tokens = Enumerable.Range(0, 1_000).Select(_ => SegredosDeRenovacao.Gerar().TokenCru).ToHashSet();

        Assert.Equal(1_000, tokens.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("curto")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa!")]
    public void ValorSemFormatoDeTokenEhDescartadoAntesDoBanco(string? valor)
    {
        Assert.False(SegredosDeRenovacao.FormatoEhPlausivel(valor));
    }
}

public sealed class HasherDeSenhaTestes
{
    private static readonly HasherDeSenha Hasher = new();

    [Fact]
    public void SenhaCorretaConfere()
    {
        var hash = Hasher.GerarHash("uma senha longa o bastante");

        Assert.Equal(ResultadoDaVerificacaoDeSenha.Valida, Hasher.Verificar(hash, "uma senha longa o bastante"));
    }

    [Fact]
    public void SenhaIncorretaNaoConfere()
    {
        var hash = Hasher.GerarHash("uma senha longa o bastante");

        Assert.Equal(ResultadoDaVerificacaoDeSenha.Invalida, Hasher.Verificar(hash, "Uma senha longa o bastante"));
    }

    [Fact]
    public void MesmaSenhaGeraHashesDiferentes()
    {
        Assert.NotEqual(Hasher.GerarHash("mesma senha de teste"), Hasher.GerarHash("mesma senha de teste"));
    }

    [Fact]
    public void HashNaoContemASenha()
    {
        Assert.DoesNotContain("senha-reconhecivel", Hasher.GerarHash("senha-reconhecivel"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nao-e-um-hash")]
    [InlineData("AQAAAAIAAYagAAAAEA==")]
    public void HashCorrompidoNaoConfereENaoLanca(string hash)
    {
        Assert.Equal(ResultadoDaVerificacaoDeSenha.Invalida, Hasher.Verificar(hash, "qualquer senha"));
    }

    /// <summary>
    /// Subir o custo do hash no futuro não pode invalidar as senhas existentes: o hash
    /// antigo confere e sinaliza que precisa ser recalculado no próximo login.
    /// </summary>
    [Fact]
    public void HashComMenosIteracoesPedeRecalculo()
    {
        var antigo = new PasswordHasher<object>(Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
            IterationCount = 10_000,
        })).HashPassword(new object(), "senha de outra época");

        Assert.Equal(ResultadoDaVerificacaoDeSenha.ValidaComRecalculo, Hasher.Verificar(antigo, "senha de outra época"));
    }

    [Fact]
    public void ConferenciaFicticiaNaoLanca()
    {
        Hasher.VerificarContraHashFicticio("qualquer coisa");
    }
}
