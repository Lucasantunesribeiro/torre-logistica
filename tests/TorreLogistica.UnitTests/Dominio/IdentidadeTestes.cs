using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Identidade;

namespace TorreLogistica.UnitTests.Dominio;

public sealed class OrganizacaoTestes
{
    [Theory]
    [InlineData("transportadora-aurora")]
    [InlineData("abc")]
    [InlineData("frota-2026")]
    [InlineData("  Logistica-Boreal  ")]
    public void SlugValidoEhAceitoENormalizado(string slug)
    {
        var organizacao = Organizacao.Criar(Guid.CreateVersion7(), "Organização", slug, Fabrica.Agora);

        Assert.Equal(slug.Trim().ToLowerInvariant(), organizacao.Slug);
        Assert.True(organizacao.Ativa);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("-aurora")]
    [InlineData("aurora-")]
    [InlineData("aurora--boreal")]
    [InlineData("aurora boreal")]
    [InlineData("aurora_boreal")]
    [InlineData("aurorá")]
    public void SlugForaDoFormatoEhRecusado(string slug)
    {
        var erro = Assert.Throws<ExcecaoDeDominio>(() =>
            Organizacao.Criar(Guid.CreateVersion7(), "Organização", slug, Fabrica.Agora));

        Assert.Equal("slug_invalido", erro.Codigo);
    }

    [Fact]
    public void SlugAcimaDoTamanhoMaximoEhRecusado()
    {
        var slug = new string('a', Organizacao.TamanhoMaximoDoSlug + 1);

        Assert.Throws<ExcecaoDeDominio>(() => Organizacao.Criar(Guid.CreateVersion7(), "Organização", slug, Fabrica.Agora));
    }
}

public sealed class EnderecoDeEmailTestes
{
    [Fact]
    public void NormalizacaoIgnoraCaixaEEspacosNasPontas()
    {
        var email = EnderecoDeEmail.Criar("  Helena.Duarte@Aurora.TEST ");

        Assert.Equal("Helena.Duarte@Aurora.TEST", email.Valor);
        Assert.Equal("helena.duarte@aurora.test", email.Normalizado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("helena")]
    [InlineData("helena@aurora")]
    [InlineData("helena duarte@aurora.test")]
    [InlineData("Helena <helena@aurora.test>")]
    [InlineData("@aurora.test")]
    public void EnderecoInvalidoEhRecusado(string? texto)
    {
        Assert.False(EnderecoDeEmail.TentarCriar(texto, out _));
    }

    [Fact]
    public void EnderecoAcimaDoTamanhoMaximoEhRecusado()
    {
        var texto = new string('a', EnderecoDeEmail.TamanhoMaximo) + "@aurora.test";

        Assert.False(EnderecoDeEmail.TentarCriar(texto, out _));
    }
}

public sealed class UsuarioTestes
{
    [Theory]
    [InlineData(Perfil.Administrador, Perfil.Operador)]
    [InlineData(Perfil.Operador, Perfil.Supervisor)]
    [InlineData(Perfil.Supervisor, Perfil.Administrador)]
    public void PerfilMudaDentroDoConsole(Perfil de, Perfil para)
    {
        var usuario = Fabrica.Usuario(de);

        Assert.True(usuario.AlterarPerfil(para, Fabrica.Agora.AddMinutes(1)));
        Assert.Equal(para, usuario.Perfil);
        Assert.Equal(Fabrica.Agora.AddMinutes(1), usuario.AtualizadoEm);
    }

    [Fact]
    public void AlterarParaOMesmoPerfilNaoEhMudanca()
    {
        var usuario = Fabrica.Usuario(Perfil.Operador);

        Assert.False(usuario.AlterarPerfil(Perfil.Operador, Fabrica.Agora.AddMinutes(1)));
        Assert.Equal(Fabrica.Agora, usuario.AtualizadoEm);
    }

    /// <summary>
    /// Motorista não herda permissão administrativa, nem por promoção. A conta nasceu para
    /// outro canal; quem precisa dos dois acessos tem duas contas.
    /// </summary>
    [Theory]
    [InlineData(Perfil.Motorista, Perfil.Operador)]
    [InlineData(Perfil.Motorista, Perfil.Administrador)]
    [InlineData(Perfil.Operador, Perfil.Motorista)]
    [InlineData(Perfil.Administrador, Perfil.Motorista)]
    public void PerfilNaoAtravessaCanal(Perfil de, Perfil para)
    {
        var usuario = Fabrica.Usuario(de);

        var erro = Assert.Throws<ExcecaoDeDominio>(() => usuario.AlterarPerfil(para, Fabrica.Agora));

        Assert.Equal("perfil_incompativel", erro.Codigo);
        Assert.Equal(de, usuario.Perfil);
    }

    [Fact]
    public void DesativarRepetidamenteSoTemEfeitoNaPrimeiraVez()
    {
        var usuario = Fabrica.Usuario(Perfil.Operador);

        Assert.True(usuario.Desativar(Fabrica.Agora.AddMinutes(1)));
        Assert.False(usuario.Desativar(Fabrica.Agora.AddMinutes(2)));
        Assert.False(usuario.Ativo);
        Assert.Equal(Fabrica.Agora.AddMinutes(1), usuario.AtualizadoEm);
    }

    [Theory]
    [InlineData(Perfil.Administrador, CanalDeAcesso.Operacao)]
    [InlineData(Perfil.Supervisor, CanalDeAcesso.Operacao)]
    [InlineData(Perfil.Operador, CanalDeAcesso.Operacao)]
    [InlineData(Perfil.Motorista, CanalDeAcesso.Motorista)]
    public void CadaPerfilPertenceAUmCanal(Perfil perfil, CanalDeAcesso canal)
    {
        Assert.Equal(canal, perfil.Canal());
    }

    [Fact]
    public void ContaNovaNaoEstaBloqueada()
    {
        Assert.False(Fabrica.Usuario(Perfil.Operador).EstaBloqueado(Fabrica.Agora));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NomeVazioEhRecusado(string nome)
    {
        var erro = Assert.Throws<ExcecaoDeDominio>(() => Usuario.Criar(
            Guid.CreateVersion7(), Guid.CreateVersion7(), nome,
            EnderecoDeEmail.Criar("a@b.test"), "hash", Perfil.Operador, Fabrica.Agora));

        Assert.Equal("nome_invalido", erro.Codigo);
    }

    [Fact]
    public void PerfilNaoDeclaradoEhRecusado()
    {
        var erro = Assert.Throws<ExcecaoDeDominio>(() => Usuario.Criar(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "Nome",
            EnderecoDeEmail.Criar("a@b.test"), "hash", (Perfil)99, Fabrica.Agora));

        Assert.Equal("perfil_invalido", erro.Codigo);
    }
}

public sealed class SessaoETokenTestes
{
    [Fact]
    public void SessaoHerdaOrganizacaoECanalDaConta()
    {
        var usuario = Fabrica.Usuario(Perfil.Motorista);

        var sessao = Sessao.Abrir(Guid.CreateVersion7(), usuario, Fabrica.Agora, TimeSpan.FromDays(7));

        Assert.Equal(usuario.OrganizacaoId, sessao.OrganizacaoId);
        Assert.Equal(CanalDeAcesso.Motorista, sessao.Canal);
        Assert.Equal(Fabrica.Agora.AddDays(7), sessao.ExpiraEm);
    }

    [Fact]
    public void SessaoDeixaDeEstarAtivaNoInstanteExatoDoPrazo()
    {
        var sessao = Sessao.Abrir(Guid.CreateVersion7(), Fabrica.Usuario(Perfil.Operador), Fabrica.Agora, TimeSpan.FromHours(1));

        Assert.True(sessao.EstaAtiva(Fabrica.Agora.AddHours(1).AddTicks(-1)));
        Assert.False(sessao.EstaAtiva(Fabrica.Agora.AddHours(1)));
    }

    [Fact]
    public void PrimeiraRevogacaoPrevalece()
    {
        var sessao = Sessao.Abrir(Guid.CreateVersion7(), Fabrica.Usuario(Perfil.Operador), Fabrica.Agora, TimeSpan.FromHours(1));

        Assert.True(sessao.Revogar(MotivoDeRevogacao.ReusoDeToken, Fabrica.Agora.AddMinutes(1)));
        Assert.False(sessao.Revogar(MotivoDeRevogacao.Logout, Fabrica.Agora.AddMinutes(2)));

        Assert.Equal(MotivoDeRevogacao.ReusoDeToken, sessao.MotivoDaRevogacao);
        Assert.Equal(Fabrica.Agora.AddMinutes(1), sessao.RevogadaEm);
    }

    [Fact]
    public void TokenNuncaSobreviveASessao()
    {
        var sessao = Sessao.Abrir(Guid.CreateVersion7(), Fabrica.Usuario(Perfil.Operador), Fabrica.Agora, TimeSpan.FromHours(1));

        var token = TokenDeRenovacao.Emitir(Guid.CreateVersion7(), sessao, Fabrica.Hash(), Fabrica.Agora, TimeSpan.FromDays(30));

        Assert.Equal(sessao.ExpiraEm, token.ExpiraEm);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(64)]
    public void HashDeTokenPrecisaTer32Bytes(int tamanho)
    {
        var sessao = Sessao.Abrir(Guid.CreateVersion7(), Fabrica.Usuario(Perfil.Operador), Fabrica.Agora, TimeSpan.FromHours(1));

        Assert.Throws<ExcecaoDeDominio>(() =>
            TokenDeRenovacao.Emitir(Guid.CreateVersion7(), sessao, new byte[tamanho], Fabrica.Agora, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void MarcarComoUsadoPreservaOPrimeiroInstanteDeUso()
    {
        var sessao = Sessao.Abrir(Guid.CreateVersion7(), Fabrica.Usuario(Perfil.Operador), Fabrica.Agora, TimeSpan.FromHours(1));
        var token = TokenDeRenovacao.Emitir(Guid.CreateVersion7(), sessao, Fabrica.Hash(), Fabrica.Agora, TimeSpan.FromHours(1));
        var primeiro = Guid.CreateVersion7();
        var segundo = Guid.CreateVersion7();

        token.MarcarComoUsado(primeiro, Fabrica.Agora.AddMinutes(1));
        token.MarcarComoUsado(segundo, Fabrica.Agora.AddMinutes(2));

        Assert.Equal(Fabrica.Agora.AddMinutes(1), token.UsadoEm);
        Assert.Equal(segundo, token.SubstitutoId);
        Assert.False(token.EstaDisponivel(Fabrica.Agora.AddMinutes(3)));
    }
}
