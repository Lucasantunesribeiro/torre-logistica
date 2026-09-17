using System.Security.Cryptography;
using System.Text;
using TorreLogistica.Application.Integracoes;
using TorreLogistica.Domain.Integracoes;

namespace TorreLogistica.UnitTests.Aplicacao;

public sealed class SegredosDeIntegracaoTestes
{
    [Fact]
    public void GeraChaveComPrefixoIdentificadorESegredo()
    {
        var emitida = SegredosDeIntegracao.Gerar();

        Assert.True(SegredosDeIntegracao.TentarAnalisar(emitida.Chave, out var identificador, out var segredo));
        Assert.Equal(emitida.IdentificadorPublico, identificador);
        Assert.StartsWith("tlog.", emitida.Chave, StringComparison.Ordinal);
        Assert.Equal(PoliticaDeIntegracao.TamanhoDoHashEmBytes, emitida.HashDoSegredo.Length);

        // O que o banco guarda é o hash do segredo — e ele confere contra o segredo entregue.
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(segredo)), emitida.HashDoSegredo);
        Assert.True(SegredosDeIntegracao.Confere(emitida.HashDoSegredo, segredo));
    }

    [Fact]
    public void CadaChaveEhDiferenteDaAnterior()
    {
        var chaves = Enumerable.Range(0, 50).Select(_ => SegredosDeIntegracao.Gerar().Chave).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(50, chaves.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("chave-qualquer")]
    [InlineData("outro.aaaaaaaaaaaaaaaa.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("tlog.curto.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("tlog.aaaaaaaaaaaaaaaa.curto")]
    [InlineData("tlog.aaaaaaaaaaaaaaaa")]
    [InlineData("tlog.aaaaaaaaaaaaaaaa.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.sobra")]
    public void ChaveComFormatoErradoEhRecusadaSemConsultarNada(string? chave) =>
        Assert.False(SegredosDeIntegracao.TentarAnalisar(chave, out _, out _));

    [Fact]
    public void SegredoErradoNaoConfere()
    {
        var emitida = SegredosDeIntegracao.Gerar();
        var outra = SegredosDeIntegracao.Gerar();

        SegredosDeIntegracao.TentarAnalisar(outra.Chave, out _, out var segredoDeOutra);

        Assert.False(SegredosDeIntegracao.Confere(emitida.HashDoSegredo, segredoDeOutra));
        Assert.False(SegredosDeIntegracao.Confere(null, segredoDeOutra));
    }
}

public sealed class LeitorDeCsvTestes
{
    [Fact]
    public void SeparaCamposSimples()
    {
        var linhas = LeitorDeCsv.Ler("a,b,c\n1,2,3");

        Assert.Equal(2, linhas.Count);
        Assert.Equal(["1", "2", "3"], linhas[1]);
    }

    [Fact]
    public void VirgulaDentroDeAspasNaoSeparaCampo()
    {
        var linhas = LeitorDeCsv.Ler("a,b\n\"Rua das Flores, 100\",Campinas");

        Assert.Equal(["Rua das Flores, 100", "Campinas"], linhas[1]);
    }

    [Fact]
    public void AspasDobradasViramUmaAspaLiteral()
    {
        var linhas = LeitorDeCsv.Ler("a\n\"Bloco \"\"B\"\"\"");

        Assert.Equal(["Bloco \"B\""], linhas[1]);
    }

    /// <summary>Planilha do Windows grava BOM e CRLF; sem tratá-los, o cabeçalho nunca casa.</summary>
    [Fact]
    public void BomECrlfNaoAtrapalham()
    {
        var linhas = LeitorDeCsv.Ler("﻿identificador_externo,cliente_id\r\nPED-1,x\r\n");

        Assert.Equal(["identificador_externo", "cliente_id"], linhas[0]);
        Assert.Equal(["PED-1", "x"], linhas[1]);
    }

    [Fact]
    public void LinhaVaziaEhIgnorada() => Assert.Equal(2, LeitorDeCsv.Ler("a\n\n\nb\n").Count);

    [Fact]
    public void ConteudoVazioNaoProduzLinha() => Assert.Empty(LeitorDeCsv.Ler("   "));
}

public sealed class PreviaDaImportacaoTestes
{
    private static readonly string Cabecalho = string.Join(',', ImportacaoDeEntregas.Cabecalho);
    private static readonly Guid Cliente = Guid.CreateVersion7();
    private static readonly Guid Destinatario = Guid.CreateVersion7();

    [Fact]
    public void ArquivoValidoNaoAcusaErro()
    {
        var previa = ImportacaoDeEntregas.Analisar($"{Cabecalho}\n{Linha("PED-1")}\n{Linha("PED-2")}");

        Assert.Empty(previa.Erros);
        Assert.Equal(2, previa.TotalDeLinhas);
        Assert.Equal(2, previa.LinhasValidas);
        Assert.Equal(64, previa.HashDoArquivo.Length);
    }

    [Fact]
    public void ArquivoVazioEhRecusado() =>
        Assert.Equal("arquivo_vazio", Assert.Single(ImportacaoDeEntregas.Analisar("").Erros).Codigo);

    [Fact]
    public void CabecalhoDiferenteEhRecusadoAntesDasLinhas()
    {
        var previa = ImportacaoDeEntregas.Analisar($"cliente_id,destinatario_id\n{Linha("PED-1")}");

        Assert.Equal("cabecalho_invalido", Assert.Single(previa.Erros).Codigo);
        Assert.Equal(0, previa.TotalDeLinhas);
    }

    [Fact]
    public void ColunaFaltandoEhRelatadaComONumeroDaLinha()
    {
        var previa = ImportacaoDeEntregas.Analisar($"{Cabecalho}\nPED-1,{Cliente}");

        var erro = Assert.Single(previa.Erros);
        Assert.Equal("colunas_invalidas", erro.Codigo);
        Assert.Equal(2, erro.Linha);
    }

    [Fact]
    public void IdentificadorRepetidoNoMesmoArquivoEhAcusadoNaPrevia()
    {
        var previa = ImportacaoDeEntregas.Analisar($"{Cabecalho}\n{Linha("PED-1")}\n{Linha("PED-1")}");

        var erro = Assert.Single(previa.Erros);
        Assert.Equal("identificador_repetido", erro.Codigo);
        Assert.Equal(3, erro.Linha);
    }

    [Fact]
    public void IdentificadorEmBrancoEhRecusado() =>
        Assert.Contains(
            ImportacaoDeEntregas.Analisar($"{Cabecalho}\n{Linha("  ")}").Erros,
            erro => erro.Codigo == "campo_obrigatorio" && erro.Campo == "identificador_externo");

    [Fact]
    public void CampoComTipoErradoEhRelatadoPeloNome()
    {
        var previa = ImportacaoDeEntregas.Analisar(
            $"{Cabecalho}\nPED-1,nao-e-guid,{Destinatario},2026-10-01T09:00:00+00:00,2026-10-01T12:00:00+00:00,Rua A,100,,Centro,Campinas,SP,13010-000,,,");

        Assert.Contains(previa.Erros, erro => erro.Codigo == "identificador_invalido" && erro.Campo == "cliente_id");
    }

    [Fact]
    public void DataSemFusoOuInvalidaEhRelatada()
    {
        var previa = ImportacaoDeEntregas.Analisar(
            $"{Cabecalho}\nPED-1,{Cliente},{Destinatario},ontem,2026-10-01T12:00:00+00:00,Rua A,100,,Centro,Campinas,SP,13010-000,,,");

        Assert.Contains(previa.Erros, erro => erro.Codigo == "data_invalida" && erro.Campo == "prometida_de");
    }

    [Fact]
    public void CoordenadaComVirgulaDecimalEhRelatada()
    {
        var previa = ImportacaoDeEntregas.Analisar(
            $"{Cabecalho}\nPED-1,{Cliente},{Destinatario},2026-10-01T09:00:00+00:00,2026-10-01T12:00:00+00:00,Rua A,100,,Centro,Campinas,SP,13010-000,\"-22,9\",-47.06,");

        Assert.Contains(previa.Erros, erro => erro.Codigo == "numero_invalido" && erro.Campo == "latitude");
    }

    [Fact]
    public void ArquivoAcimaDoLimiteDeLinhasEhRecusado()
    {
        var linhas = Enumerable.Range(1, ImportacaoDeEntregas.LimiteDeLinhas + 1).Select(numero => Linha($"PED-{numero}"));
        var previa = ImportacaoDeEntregas.Analisar($"{Cabecalho}\n{string.Join('\n', linhas)}");

        Assert.Equal("arquivo_grande_demais", Assert.Single(previa.Erros).Codigo);
    }

    /// <summary>O mesmo arquivo produz o mesmo hash: é dele que sai a chave de idempotência de cada linha.</summary>
    [Fact]
    public void HashDoArquivoEhEstavel()
    {
        var conteudo = $"{Cabecalho}\n{Linha("PED-1")}";

        Assert.Equal(
            ImportacaoDeEntregas.Analisar(conteudo).HashDoArquivo,
            ImportacaoDeEntregas.Analisar(conteudo).HashDoArquivo);

        Assert.NotEqual(
            ImportacaoDeEntregas.Analisar(conteudo).HashDoArquivo,
            ImportacaoDeEntregas.Analisar($"{conteudo}\n{Linha("PED-2")}").HashDoArquivo);
    }

    private static string Linha(string identificador) =>
        $"{identificador},{Cliente},{Destinatario},2026-10-01T09:00:00+00:00,2026-10-01T12:00:00+00:00,"
        + "Rua das Flores,100,,Centro,Campinas,SP,13010-000,,,";
}
