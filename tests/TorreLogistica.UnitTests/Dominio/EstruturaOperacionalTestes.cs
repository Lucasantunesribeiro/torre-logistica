using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Clientes;
using TorreLogistica.Domain.Comum;
using TorreLogistica.Domain.Frota;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.Domain.Operacao;

namespace TorreLogistica.UnitTests.Dominio;

public sealed class TextoNormalizadoTestes
{
    [Theory]
    [InlineData("  Hub   São José  ", "hub sao jose")]
    [InlineData("AÇÃO Logística", "acao logistica")]
    [InlineData("Ñandú Über", "nandu uber")]
    [InlineData("", "")]
    public void FormaDeBuscaIgnoraAcentoCaixaEEspacos(string texto, string esperado)
    {
        Assert.Equal(esperado, TextoNormalizado.ParaBusca(texto));
    }

    [Theory]
    [InlineData("Nome\nForjado")]
    [InlineData("Nome\r\nForjado")]
    [InlineData("Nome\0Nulo")]
    [InlineData("Nome\u001bEscape")]
    public void TextoComCaractereDeControleEhRecusado(string texto)
    {
        Assert.Throws<ExcecaoDeDominio>(() => TextoNormalizado.Obrigatorio(texto, 50, "c", "Campo"));
    }

    [Fact]
    public void TabulacaoViraEspaco()
    {
        // Chega de planilha colada; não quebra linha em log.
        Assert.Equal("Tab no meio", TextoNormalizado.Obrigatorio("Tab\tno meio", 50, "c", "Campo"));
    }
}

public sealed class TelefoneTestes
{
    [Theory]
    [InlineData("(11) 98765-4321", "+5511987654321")]
    [InlineData("11 3456-7890", "+551134567890")]
    [InlineData("+55 21 99876-5432", "+5521998765432")]
    [InlineData("+351 912 345 678", "+351912345678")]
    public void TelefoneValidoEhNormalizadoEmE164(string texto, string esperado)
    {
        Assert.Equal(esperado, Telefone.Criar(texto).Valor);
    }

    [Theory]
    [InlineData("1234")]
    [InlineData("(00) 98765-4321")]
    [InlineData("11 88765-4321")]
    [InlineData("+0 11 98765-4321")]
    [InlineData("11 98765-4321 ramal 2")]
    [InlineData("+1234567890123456")]
    public void TelefoneInvalidoEhRecusado(string texto)
    {
        Assert.False(Telefone.TentarCriar(texto, out _));
    }

    [Fact]
    public void TelefoneOpcionalVazioViraNulo()
    {
        Assert.Null(Telefone.CriarOpcional("   "));
    }
}

public sealed class EnderecoTestes
{
    [Fact]
    public void EnderecoValidoEhNormalizado()
    {
        var endereco = Endereco.Criar(" Av.  Paulista ", "1578", "", "Bela Vista", "São Paulo", "sp", "01310-200");

        Assert.Equal("Av. Paulista", endereco.Logradouro);
        Assert.Null(endereco.Complemento);
        Assert.Equal("SP", endereco.Uf);
        Assert.Equal("01310200", endereco.Cep);
    }

    [Theory]
    [InlineData("0131020", "cep_invalido")]
    [InlineData("01310-20A", "cep_invalido")]
    [InlineData("00000-000", "cep_invalido")]
    public void CepInvalidoEhRecusado(string cep, string codigo)
    {
        var erro = Assert.Throws<ExcecaoDeDominio>(() => Endereco.Criar("Rua A", "1", null, "Centro", "Campinas", "SP", cep));
        Assert.Equal(codigo, erro.Codigo);
    }

    [Theory]
    [InlineData("XX")]
    [InlineData("S")]
    [InlineData("")]
    public void UfInexistenteEhRecusada(string uf)
    {
        var erro = Assert.Throws<ExcecaoDeDominio>(() => Endereco.Criar("Rua A", "1", null, "Centro", "Campinas", uf, "13010-000"));
        Assert.Equal("uf_invalida", erro.Codigo);
    }

    [Fact]
    public void EnderecoSemBairroEhIncompleto()
    {
        var erro = Assert.Throws<ExcecaoDeDominio>(() => Endereco.Criar("Rua A", "1", null, " ", "Campinas", "SP", "13010-000"));
        Assert.Equal("endereco_incompleto", erro.Codigo);
    }

    [Fact]
    public void EnderecosIguaisSaoIguais()
    {
        Assert.Equal(
            Endereco.Criar("Rua A", "1", null, "Centro", "Campinas", "SP", "13010-000"),
            Endereco.Criar("Rua  A ", "1", "", "Centro", "Campinas", "sp", "13010000"));
    }
}

public sealed class CoordenadaGeograficaTestes
{
    [Fact]
    public void CoordenadaValidaEhAceita()
    {
        var ponto = CoordenadaGeografica.Criar(-23.5505, -46.6339);

        Assert.Equal(-23.5505, ponto.Latitude);
        Assert.Equal(-46.6339, ponto.Longitude);
    }

    [Theory]
    [InlineData(91, 0.1)]
    [InlineData(-90.0001, 10)]
    [InlineData(10, 180.0001)]
    [InlineData(double.NaN, 10)]
    [InlineData(10, double.PositiveInfinity)]
    [InlineData(0, 0)]
    public void CoordenadaForaDaFaixaOuNulaEhRecusada(double latitude, double longitude)
    {
        var erro = Assert.Throws<ExcecaoDeDominio>(() => CoordenadaGeografica.Criar(latitude, longitude));
        Assert.Equal("coordenada_invalida", erro.Codigo);
    }

    [Fact]
    public void MeiaCoordenadaEhRecusada()
    {
        Assert.Throws<ExcecaoDeDominio>(() => CoordenadaGeografica.CriarOpcional(-23.5, null));
        Assert.Null(CoordenadaGeografica.CriarOpcional(null, null));
    }
}

public sealed class CnpjTestes
{
    [Theory]
    [InlineData("11.222.333/0001-81", "11222333000181")]
    [InlineData("12.ABC.345/01DE-35", "12ABC34501DE35")]
    [InlineData("12abc34501de35", "12ABC34501DE35")]
    public void CnpjNumericoEAlfanumericoValidoEhAceito(string texto, string esperado)
    {
        var cnpj = Cnpj.Criar(texto);

        Assert.Equal(esperado, cnpj.Valor);
    }

    [Fact]
    public void CnpjTemFormaDeExibicao()
    {
        Assert.Equal("12.ABC.345/01DE-35", Cnpj.Criar("12ABC34501DE35").Formatado);
    }

    [Theory]
    [InlineData("11.222.333/0001-82")]
    [InlineData("12.ABC.345/01DE-36")]
    [InlineData("11111111111111")]
    [InlineData("12ABC34501DE3A")]
    [InlineData("1122233300018")]
    [InlineData("12.AB!.345/01DE-35")]
    public void CnpjInvalidoEhRecusado(string texto)
    {
        Assert.False(Cnpj.TentarCriar(texto, out _));
    }
}

public sealed class PlacaDeVeiculoTestes
{
    [Theory]
    [InlineData("abc-1234", "ABC1234", false)]
    [InlineData("BRA2E19", "BRA2E19", true)]
    [InlineData(" rio 2a18 ", "RIO2A18", true)]
    public void PlacaValidaEhNormalizada(string texto, string esperado, bool mercosul)
    {
        var placa = PlacaDeVeiculo.Criar(texto);

        Assert.Equal(esperado, placa.Valor);
        Assert.Equal(mercosul, placa.EhMercosul);
    }

    [Theory]
    [InlineData("AB1234")]
    [InlineData("ABCD123")]
    [InlineData("1BC1234")]
    [InlineData("ABC12E4")]
    [InlineData("ÁBC1234")]
    public void PlacaInvalidaEhRecusada(string texto)
    {
        Assert.False(PlacaDeVeiculo.TentarCriar(texto, out _));
    }
}

public sealed class CadastrosOperacionaisTestes
{
    private static readonly DateTimeOffset Agora = Fabrica.Agora;
    private static readonly Guid Organizacao = Guid.CreateVersion7();

    [Fact]
    public void MotoristaInativoNaoRecebeAtribuicao()
    {
        var motorista = Motorista.Criar(Guid.CreateVersion7(), Organizacao, "Rafael Mendes", null, Agora);
        motorista.GarantirAptoParaAtribuicao();

        motorista.Inativar(Agora.AddMinutes(1));

        var erro = Assert.Throws<ExcecaoDeDominio>(motorista.GarantirAptoParaAtribuicao);
        Assert.Equal("motorista_inativo", erro.Codigo);
    }

    [Fact]
    public void VeiculoInativoNaoIniciaRota()
    {
        var veiculo = Veiculo.Criar(Guid.CreateVersion7(), Organizacao, PlacaDeVeiculo.Criar("BRA2E19"), "Fiorino 03", TipoDeVeiculo.Utilitario, 650, Agora);
        veiculo.GarantirAptoParaIniciarRota();

        veiculo.Inativar(Agora.AddMinutes(1));

        var erro = Assert.Throws<ExcecaoDeDominio>(veiculo.GarantirAptoParaIniciarRota);
        Assert.Equal("veiculo_inativo", erro.Codigo);
    }

    [Fact]
    public void AtivarEInativarSaoIdempotentes()
    {
        var hub = Hub.Criar(Guid.CreateVersion7(), Organizacao, "Hub Centro", EnderecoDeTeste(), CoordenadaGeografica.Criar(-23.55, -46.63), Agora);

        Assert.False(hub.Ativar(Agora.AddMinutes(1)));
        Assert.True(hub.Inativar(Agora.AddMinutes(2)));
        Assert.False(hub.Inativar(Agora.AddMinutes(3)));
        Assert.Equal(Agora.AddMinutes(2), hub.AtualizadoEm);
    }

    [Fact]
    public void AtualizacaoInformaSoOsCamposQueMudaram()
    {
        var destinatario = Destinatario.Criar(
            Guid.CreateVersion7(), Organizacao, "Carla Nunes", Telefone.Criar("11987654321"), EnderecoDeTeste(), null, null, Agora);

        var campos = destinatario.AtualizarDados(
            "Carla Nunes", Telefone.Criar("(11) 98765-4321"), EnderecoDeTeste(), CoordenadaGeografica.Criar(-23.5, -46.6), "Portão azul", Agora.AddMinutes(1));

        Assert.Equal(["localizacao", "instrucoesDeEntrega"], campos);
    }

    [Fact]
    public void AtualizacaoSemMudancaNaoTocaNoInstante()
    {
        var cliente = Cliente.Criar(Guid.CreateVersion7(), Organizacao, "Mercado Horizonte", Cnpj.Criar("11222333000181"), Agora);

        var campos = cliente.AtualizarDados(" Mercado  Horizonte ", Cnpj.Criar("11.222.333/0001-81"), Agora.AddHours(1));

        Assert.Empty(campos);
        Assert.Equal(Agora, cliente.AtualizadoEm);
    }

    [Fact]
    public void VeiculoRecusaCapacidadeForaDaFaixa()
    {
        var erro = Assert.Throws<ExcecaoDeDominio>(() =>
            Veiculo.Criar(Guid.CreateVersion7(), Organizacao, PlacaDeVeiculo.Criar("ABC1234"), "Moto 1", TipoDeVeiculo.Motocicleta, 0, Agora));

        Assert.Equal("capacidade_invalida", erro.Codigo);
    }

    [Fact]
    public void MotoristaAceitaSoContaAtivaDePerfilMotoristaDaMesmaOrganizacao()
    {
        var motorista = Motorista.Criar(Guid.CreateVersion7(), Organizacao, "Rafael Mendes", null, Agora);

        Assert.Equal("conta_nao_e_de_motorista", Assert.Throws<ExcecaoDeDominio>(() =>
            motorista.AssociarConta(Fabrica.Usuario(Perfil.Operador, Organizacao), Agora)).Codigo);

        Assert.Equal("usuario_nao_encontrado", Assert.Throws<ExcecaoDeDominio>(() =>
            motorista.AssociarConta(Fabrica.Usuario(Perfil.Motorista), Agora)).Codigo);

        var inativa = Fabrica.Usuario(Perfil.Motorista, Organizacao);
        inativa.Desativar(Agora);
        Assert.Equal("conta_inativa", Assert.Throws<ExcecaoDeDominio>(() =>
            motorista.AssociarConta(inativa, Agora)).Codigo);
    }

    [Fact]
    public void TrocarContaDiretamenteEhConflitoEReassociarAMesmaNaoMudaNada()
    {
        var motorista = Motorista.Criar(Guid.CreateVersion7(), Organizacao, "Rafael Mendes", null, Agora);
        var conta = Fabrica.Usuario(Perfil.Motorista, Organizacao);

        Assert.True(motorista.AssociarConta(conta, Agora));
        Assert.False(motorista.AssociarConta(conta, Agora));

        var erro = Assert.Throws<ExcecaoDeDominio>(() =>
            motorista.AssociarConta(Fabrica.Usuario(Perfil.Motorista, Organizacao), Agora));
        Assert.Equal(CategoriaDeErroDeDominio.Conflito, erro.Categoria);

        Assert.True(motorista.DesassociarConta(Agora));
        Assert.False(motorista.DesassociarConta(Agora));
    }

    private static Endereco EnderecoDeTeste() =>
        Endereco.Criar("Rua das Palmeiras", "120", null, "Centro", "Campinas", "SP", "13010-000");
}
