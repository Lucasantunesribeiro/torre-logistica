using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Abstracoes.Identificadores;
using TorreLogistica.Domain.Comum;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Frota;
using TorreLogistica.Domain.Rotas;

namespace TorreLogistica.UnitTests.Dominio;

/// <summary>
/// A máquina de estados da entrega como especificação: cada linha é um comando, cada coluna um
/// status de origem, e a célula é o status resultante — ou "·" quando a transição é proibida.
/// </summary>
public sealed class MaquinaDeEstadosDaEntregaTestes
{
    private static readonly Dictionary<string, StatusDaEntrega> Abreviacoes = new(StringComparer.Ordinal)
    {
        ["Cr"] = StatusDaEntrega.Criada,
        ["Pl"] = StatusDaEntrega.Planejada,
        ["At"] = StatusDaEntrega.Atribuida,
        ["ER"] = StatusDaEntrega.EmRota,
        ["PD"] = StatusDaEntrega.ProximaDoDestino,
        ["En"] = StatusDaEntrega.Entregue,
        ["TF"] = StatusDaEntrega.TentativaFrustrada,
        ["Re"] = StatusDaEntrega.Reagendada,
        ["Ca"] = StatusDaEntrega.Cancelada,
    };

    // Colunas, na ordem de declaração do enum:     Cr Pl At ER PD En TF Re Ca
    private static readonly Dictionary<ComandoDaEntrega, string> Tabela = new()
    {
        [ComandoDaEntrega.Planejar] = "                 Pl ·  ·  ·  ·  ·  ·  Pl ·",
        [ComandoDaEntrega.Atribuir] = "                 ·  At At ·  ·  ·  ·  ·  ·",
        [ComandoDaEntrega.Reatribuir] = "               ·  ·  ·  ER PD ·  ·  ·  ·",
        [ComandoDaEntrega.RetirarDaRota] = "            ·  Cr Cr ·  ·  ·  ·  ·  ·",
        [ComandoDaEntrega.IniciarRota] = "              ·  ·  ER ·  ·  ·  ·  ·  ·",
        [ComandoDaEntrega.RegistrarChegada] = "         ·  ·  ·  PD ·  ·  ·  ·  ·",
        [ComandoDaEntrega.Concluir] = "                 ·  ·  ·  En En ·  ·  ·  ·",
        [ComandoDaEntrega.RegistrarTentativaFrustrada] = "·  ·  ·  TF TF ·  ·  ·  ·",
        [ComandoDaEntrega.Reagendar] = "                ·  ·  ·  ·  ·  ·  Re Re ·",
        [ComandoDaEntrega.Cancelar] = "                 Ca Ca Ca ·  ·  ·  Ca Ca ·",
    };

    public static TheoryData<ComandoDaEntrega, StatusDaEntrega, StatusDaEntrega?> Transicoes()
    {
        var dados = new TheoryData<ComandoDaEntrega, StatusDaEntrega, StatusDaEntrega?>();
        var origens = Enum.GetValues<StatusDaEntrega>();

        foreach (var (comando, linha) in Tabela)
        {
            var celulas = linha.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var indice = 0; indice < origens.Length; indice++)
            {
                dados.Add(comando, origens[indice], celulas[indice] == "·" ? null : Abreviacoes[celulas[indice]]);
            }
        }

        return dados;
    }

    [Fact]
    public void TabelaCobreTodoComandoETodoStatus()
    {
        Assert.Equal(Enum.GetValues<ComandoDaEntrega>().Order(), Tabela.Keys.Order());
        Assert.All(Tabela.Values, linha =>
            Assert.Equal(Enum.GetValues<StatusDaEntrega>().Length, linha.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length));
    }

    [Theory]
    [MemberData(nameof(Transicoes))]
    public void TransicaoPorComandoEStatus(ComandoDaEntrega comando, StatusDaEntrega origem, StatusDaEntrega? esperado)
    {
        Assert.Equal(esperado, MaquinaDeEstadosDaEntrega.Destino(comando, origem));
    }

    [Theory]
    [InlineData(StatusDaEntrega.Entregue)]
    [InlineData(StatusDaEntrega.Cancelada)]
    public void NadaSaiDeStatusFinal(StatusDaEntrega final)
    {
        Assert.All(Enum.GetValues<ComandoDaEntrega>(), comando => Assert.Null(MaquinaDeEstadosDaEntrega.Destino(comando, final)));
    }

    [Fact]
    public void TodoStatusEhAlcancavelAPartirDeCriada()
    {
        var alcancados = new HashSet<StatusDaEntrega> { StatusDaEntrega.Criada };
        var fila = new Queue<StatusDaEntrega>(alcancados);

        while (fila.TryDequeue(out var atual))
        {
            foreach (var comando in Enum.GetValues<ComandoDaEntrega>())
            {
                if (MaquinaDeEstadosDaEntrega.Destino(comando, atual) is { } destino && alcancados.Add(destino))
                {
                    fila.Enqueue(destino);
                }
            }
        }

        Assert.Equal(Enum.GetValues<StatusDaEntrega>().Order(), alcancados.Order());
    }
}

/// <summary>Os comandos de execução da entrega, com eventos, idempotência e recusas.</summary>
public sealed class ExecucaoDaEntregaTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Rota = Guid.CreateVersion7();
    private static readonly Guid Motorista = Guid.CreateVersion7();

    [Fact]
    public void CaminhoFelizPercorreOsStatusComUmEventoPorPasso()
    {
        var entrega = EntregaAtribuida();

        Assert.NotNull(entrega.IniciarRota(Rota, null, Guid.CreateVersion7(), Agora.AddMinutes(1)));
        Assert.Equal(StatusDaEntrega.EmRota, entrega.Status);
        Assert.Equal(Agora.AddMinutes(1), entrega.SaiuParaRotaEm);

        Assert.NotNull(entrega.RegistrarChegada(null, Guid.CreateVersion7(), Agora.AddMinutes(30)));
        Assert.Equal(StatusDaEntrega.ProximaDoDestino, entrega.Status);

        var conclusao = entrega.Concluir(null, Guid.CreateVersion7(), Agora.AddMinutes(33));
        Assert.NotNull(conclusao);
        Assert.Equal(StatusDaEntrega.Entregue, entrega.Status);
        Assert.Equal(StatusDaEntrega.Entregue, conclusao.StatusResultante);
        Assert.Equal(TipoDeEventoDaEntrega.Entregue, conclusao.Tipo);
        Assert.Equal(Agora.AddMinutes(33), entrega.EntregueEm);
        Assert.Equal(6, entrega.UltimaSequenciaDeEvento);
        Assert.Equal(Motorista, entrega.MotoristaId);
    }

    [Fact]
    public void RepetirComandoJaAplicadoNaoGeraEventoNemErro()
    {
        var entrega = EntregaAtribuida();
        entrega.IniciarRota(Rota, null, Guid.CreateVersion7(), Agora);
        Assert.Null(entrega.IniciarRota(Rota, null, Guid.CreateVersion7(), Agora));

        entrega.RegistrarChegada(null, Guid.CreateVersion7(), Agora);
        Assert.Null(entrega.RegistrarChegada(null, Guid.CreateVersion7(), Agora));

        entrega.Concluir(null, Guid.CreateVersion7(), Agora);
        Assert.Null(entrega.Concluir(null, Guid.CreateVersion7(), Agora));

        Assert.Equal(6, entrega.UltimaSequenciaDeEvento);
    }

    [Fact]
    public void ConclusaoNaoExigeChegadaRegistrada()
    {
        var entrega = EntregaAtribuida();
        entrega.IniciarRota(Rota, null, Guid.CreateVersion7(), Agora);

        Assert.NotNull(entrega.Concluir(null, Guid.CreateVersion7(), Agora));
        Assert.Null(entrega.ChegadaRegistradaEm);
    }

    [Fact]
    public void TentativaFrustradaContaEReagendamentoDevolveAEntregaParaRota()
    {
        var entrega = EntregaAtribuida();
        entrega.IniciarRota(Rota, null, Guid.CreateVersion7(), Agora);

        var tentativa = entrega.RegistrarTentativaFrustrada(MotivoDeTentativaFrustrada.DestinatarioAusente, null, Guid.CreateVersion7(), Agora);
        Assert.NotNull(tentativa);
        Assert.Contains("DestinatarioAusente", tentativa.Dados, StringComparison.Ordinal);
        Assert.Equal(1, entrega.TentativasFrustradas);
        Assert.Null(entrega.RegistrarTentativaFrustrada(MotivoDeTentativaFrustrada.LocalFechado, null, Guid.CreateVersion7(), Agora));
        Assert.Equal(MotivoDeTentativaFrustrada.DestinatarioAusente, entrega.MotivoDaUltimaTentativa);

        var novaJanela = JanelaDeEntrega.Criar(Agora.AddDays(1), Agora.AddDays(1).AddHours(3));
        Assert.NotNull(entrega.Reagendar(novaJanela, null, Guid.CreateVersion7(), Agora));
        Assert.Equal(StatusDaEntrega.Reagendada, entrega.Status);
        Assert.Equal(novaJanela, entrega.Janela);
        Assert.Null(entrega.Reagendar(novaJanela, null, Guid.CreateVersion7(), Agora));

        // Nova rota: a entrega reagendada volta a ser planejada.
        entrega.Planejar(Guid.CreateVersion7(), null, Guid.CreateVersion7(), Agora);
        Assert.Equal(StatusDaEntrega.Planejada, entrega.Status);
        Assert.Null(entrega.MotoristaId);
    }

    [Fact]
    public void ReagendarParaJanelaPassadaEhRecusado()
    {
        var entrega = EntregaAtribuida();
        entrega.IniciarRota(Rota, null, Guid.CreateVersion7(), Agora);
        entrega.RegistrarTentativaFrustrada(MotivoDeTentativaFrustrada.AcessoImpedido, null, Guid.CreateVersion7(), Agora);

        var erro = Assert.Throws<ExcecaoDeDominio>(() =>
            entrega.Reagendar(JanelaDeEntrega.Criar(Agora.AddHours(-3), Agora.AddHours(-1)), null, Guid.CreateVersion7(), Agora));

        Assert.Equal("janela_no_passado", erro.Codigo);
        Assert.Equal(StatusDaEntrega.TentativaFrustrada, entrega.Status);
    }

    [Fact]
    public void ReatribuicaoEmExecucaoTrocaOMotoristaSemMudarOStatus()
    {
        var entrega = EntregaAtribuida();
        entrega.IniciarRota(Rota, null, Guid.CreateVersion7(), Agora);
        var novo = Guid.CreateVersion7();

        var evento = entrega.Reatribuir(Rota, novo, null, Guid.CreateVersion7(), Agora);

        Assert.NotNull(evento);
        Assert.Equal(StatusDaEntrega.EmRota, entrega.Status);
        Assert.Equal(novo, entrega.MotoristaId);
        Assert.Contains(Motorista.ToString(), evento.Dados, StringComparison.Ordinal);
        Assert.Null(entrega.Reatribuir(Rota, novo, null, Guid.CreateVersion7(), Agora));
    }

    public static TheoryData<string, string> ProibidasImportantes() => new()
    {
        { "concluir antes de sair", "transicao_invalida" },
        { "concluir depois de tentativa sem sucesso", "transicao_invalida" },
        { "chegar em entrega entregue", "transicao_invalida" },
        { "reagendar em rota", "transicao_invalida" },
        { "cancelar em rota", "cancelamento_nao_permitido" },
        { "cancelar entregue", "cancelamento_nao_permitido" },
        { "reatribuir antes de sair", "transicao_invalida" },
        { "sair de novo depois de entregue", "transicao_invalida" },
        { "voltar para rota depois de entregue", "entrega_inelegivel_para_rota" },
    };

    [Theory]
    [MemberData(nameof(ProibidasImportantes))]
    public void TransicaoProibidaEhRecusadaComConflitoSemMudarNada(string caso, string codigo)
    {
        var entrega = EntregaAtribuida();
        Action comando = caso switch
        {
            "concluir antes de sair" => () => entrega.Concluir(null, Guid.CreateVersion7(), Agora),
            "reatribuir antes de sair" => () => entrega.Reatribuir(Rota, Guid.CreateVersion7(), null, Guid.CreateVersion7(), Agora),
            "reagendar em rota" => EmRota(() => entrega.Reagendar(JanelaDeEntrega.Criar(Agora.AddDays(1), Agora.AddDays(2)), null, Guid.CreateVersion7(), Agora)),
            "cancelar em rota" => EmRota(() => entrega.Cancelar(MotivoDeCancelamento.SolicitacaoDoCliente, null, null, Guid.CreateVersion7(), Agora)),
            "concluir depois de tentativa sem sucesso" => EmRota(() =>
            {
                entrega.RegistrarTentativaFrustrada(MotivoDeTentativaFrustrada.LocalFechado, null, Guid.CreateVersion7(), Agora);
                Congelar();
                entrega.Concluir(null, Guid.CreateVersion7(), Agora);
            }),
            "chegar em entrega entregue" => Entregue(() => entrega.RegistrarChegada(null, Guid.CreateVersion7(), Agora)),
            "cancelar entregue" => Entregue(() => entrega.Cancelar(MotivoDeCancelamento.SolicitacaoDoCliente, null, null, Guid.CreateVersion7(), Agora)),
            "sair de novo depois de entregue" => Entregue(() => entrega.IniciarRota(Rota, null, Guid.CreateVersion7(), Agora)),
            "voltar para rota depois de entregue" => Entregue(() => entrega.Planejar(Guid.CreateVersion7(), null, Guid.CreateVersion7(), Agora)),
            _ => throw new ArgumentOutOfRangeException(nameof(caso), caso, "Caso desconhecido."),
        };

        var statusAntes = default(StatusDaEntrega);
        var sequenciaAntes = 0;

        var erro = Assert.Throws<ExcecaoDeDominio>(comando);

        Assert.Equal(codigo, erro.Codigo);
        Assert.Equal(CategoriaDeErroDeDominio.Conflito, erro.Categoria);
        if (sequenciaAntes > 0)
        {
            Assert.Equal(statusAntes, entrega.Status);
            Assert.Equal(sequenciaAntes, entrega.UltimaSequenciaDeEvento);
        }

        void Congelar()
        {
            statusAntes = entrega.Status;
            sequenciaAntes = entrega.UltimaSequenciaDeEvento;
        }

        Action EmRota(Action acao) => () =>
        {
            entrega.IniciarRota(Rota, null, Guid.CreateVersion7(), Agora);
            Congelar();
            acao();
        };

        Action Entregue(Action acao) => () =>
        {
            entrega.IniciarRota(Rota, null, Guid.CreateVersion7(), Agora);
            entrega.Concluir(null, Guid.CreateVersion7(), Agora);
            Congelar();
            acao();
        };
    }

    [Fact]
    public void EmRotaSoObservacoesMudam()
    {
        var entrega = EntregaAtribuida();
        entrega.IniciarRota(Rota, null, Guid.CreateVersion7(), Agora);

        var outroEndereco = new DadosDaEntrega(
            entrega.ClienteId,
            entrega.DestinatarioId,
            Endereco.Criar("Rua Nova", "1", null, "Centro", "Campinas", "SP", "13015904"),
            entrega.Localizacao,
            entrega.Janela,
            entrega.Observacoes);
        Assert.Equal("campo_nao_editavel", Assert.Throws<ExcecaoDeDominio>(() =>
            entrega.AtualizarDados(outroEndereco, null, Guid.CreateVersion7(), Agora)).Codigo);

        var comObservacao = new DadosDaEntrega(
            entrega.ClienteId, entrega.DestinatarioId, entrega.Endereco, entrega.Localizacao, entrega.Janela, "Interfone quebrado");
        Assert.Single(entrega.AtualizarDados(comObservacao, null, Guid.CreateVersion7(), Agora).Campos);
    }

    private static Entrega EntregaAtribuida()
    {
        var entrega = Entrega.Criar(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "ENT-2026-000001",
            new DadosDaEntrega(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Endereco.Criar("Rua das Palmeiras", "120", null, "Centro", "Campinas", "SP", "13010000"),
                null,
                JanelaDeEntrega.Criar(Agora.AddHours(1), Agora.AddHours(3)),
                null),
            null,
            Guid.CreateVersion7(),
            Agora).Entrega;

        entrega.Planejar(Rota, null, Guid.CreateVersion7(), Agora);
        entrega.Atribuir(Rota, Motorista, null, Guid.CreateVersion7(), Agora);
        return entrega;
    }
}

/// <summary>Início, conclusão e reatribuição da rota.</summary>
public sealed class ExecucaoDaRotaTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Organizacao = Guid.CreateVersion7();
    private static readonly GeradorDeTeste Ids = new();

    [Fact]
    public void SoOMotoristaDaRotaIniciaESoRotaPlanejadaSai()
    {
        var (rota, motorista, veiculo, _) = RotaPlanejada();
        var outro = Motorista.Criar(Guid.CreateVersion7(), Organizacao, "Márcia Lopes", null, Agora);

        Assert.Equal("rota_nao_encontrada", Assert.Throws<ExcecaoDeDominio>(() => rota.Iniciar(outro, veiculo, Ids, null, Agora)).Codigo);

        var evento = rota.Iniciar(motorista, veiculo, Ids, null, Agora);
        Assert.NotNull(evento);
        Assert.Equal(StatusDaRota.EmAndamento, rota.Status);
        Assert.Equal(Agora, rota.IniciadaEm);
        Assert.Null(rota.Iniciar(motorista, veiculo, Ids, null, Agora));

        var (emMontagem, motoristaDaMontagem, _, _) = RotaPlanejada(planejar: false);
        Assert.Equal("transicao_de_rota_invalida", Assert.Throws<ExcecaoDeDominio>(() =>
            emMontagem.Iniciar(motoristaDaMontagem, null, Ids, null, Agora)).Codigo);
    }

    [Fact]
    public void VeiculoInativoNaoSai()
    {
        var (rota, motorista, veiculo, _) = RotaPlanejada();
        veiculo.Inativar(Agora);

        Assert.Equal("veiculo_inativo", Assert.Throws<ExcecaoDeDominio>(() => rota.Iniciar(motorista, veiculo, Ids, null, Agora)).Codigo);
        Assert.Equal(StatusDaRota.Planejada, rota.Status);
    }

    [Fact]
    public void EmAndamentoTrocaMotoristaMasNaoMudaEstrutura()
    {
        var (rota, motorista, veiculo, entregaId) = RotaPlanejada();
        rota.Iniciar(motorista, veiculo, Ids, null, Agora);

        Assert.NotNull(rota.AtribuirMotorista(Motorista.Criar(Guid.CreateVersion7(), Organizacao, "Márcia Lopes", null, Agora), Ids, null, Agora));

        foreach (var estrutural in new Action[]
        {
            () => rota.AdicionarParadas([Guid.CreateVersion7()], Ids, null, Agora),
            () => rota.RemoverParada(entregaId, MotivoDeRemocaoDeParada.DecisaoDoPlanejamento, Ids, null, Agora),
            () => rota.AtribuirVeiculo(veiculo, Ids, null, Agora),
            () => rota.PlanejarSaida(Agora.AddHours(2), Ids, null, Agora),
            () => rota.Cancelar(Ids, null, Agora),
        })
        {
            Assert.IsType<ExcecaoDeDominio>(Record.Exception(estrutural));
        }
    }

    [Fact]
    public void ConclusaoExigeTodasAsEntregasResolvidasEEncerraAsParadas()
    {
        var (rota, motorista, veiculo, entregaId) = RotaPlanejada();

        Assert.Equal("transicao_de_rota_invalida", Assert.Throws<ExcecaoDeDominio>(() =>
            rota.Concluir(new Dictionary<Guid, StatusDaEntrega>(), Ids, null, Agora)).Codigo);

        rota.Iniciar(motorista, veiculo, Ids, null, Agora);

        foreach (var pendente in new[] { StatusDaEntrega.EmRota, StatusDaEntrega.ProximaDoDestino })
        {
            Assert.Equal("rota_com_entregas_pendentes", Assert.Throws<ExcecaoDeDominio>(() =>
                rota.Concluir(new Dictionary<Guid, StatusDaEntrega> { [entregaId] = pendente }, Ids, null, Agora)).Codigo);
        }

        var evento = rota.Concluir(new Dictionary<Guid, StatusDaEntrega> { [entregaId] = StatusDaEntrega.Reagendada }, Ids, null, Agora);

        Assert.NotNull(evento);
        Assert.Equal(StatusDaRota.Concluida, rota.Status);
        Assert.Empty(rota.ObterParadasAtivas());
        Assert.Equal(MotivoDeRemocaoDeParada.RotaConcluida, Assert.Single(rota.Paradas).MotivoDaRemocao);
        Assert.Contains("\"semSucesso\":1", evento.Dados, StringComparison.Ordinal);
        Assert.Null(rota.Concluir(new Dictionary<Guid, StatusDaEntrega>(), Ids, null, Agora));
    }

    private static (Rota Rota, Motorista Motorista, Veiculo Veiculo, Guid EntregaId) RotaPlanejada(bool planejar = true)
    {
        var rota = Rota.Criar(Guid.CreateVersion7(), Organizacao, "ROT-2026-0001", new DateOnly(2026, 9, 15), null, Ids, null, Agora).Rota;
        var motorista = Motorista.Criar(Guid.CreateVersion7(), Organizacao, "Rafael Souza", null, Agora);
        var veiculo = Veiculo.Criar(Guid.CreateVersion7(), Organizacao, PlacaDeVeiculo.Criar("ABC1D23"), "Fiorino 03", TipoDeVeiculo.Utilitario, 650, Agora);
        var entregaId = Guid.CreateVersion7();

        rota.AdicionarParadas([entregaId], Ids, null, Agora);
        rota.AtribuirMotorista(motorista, Ids, null, Agora);
        rota.AtribuirVeiculo(veiculo, Ids, null, Agora);
        rota.PlanejarSaida(Agora.AddHours(20), Ids, null, Agora);
        if (planejar)
        {
            rota.Planejar(Ids, null, Agora);
        }

        return (rota, motorista, veiculo, entregaId);
    }

    private sealed class GeradorDeTeste : IGeradorDeIdentificador
    {
        public Guid Novo() => Guid.CreateVersion7();
    }
}
