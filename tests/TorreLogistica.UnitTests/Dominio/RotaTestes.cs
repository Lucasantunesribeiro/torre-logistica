using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Abstracoes.Identificadores;
using TorreLogistica.Domain.Comum;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Frota;
using TorreLogistica.Domain.Rotas;

namespace TorreLogistica.UnitTests.Dominio;

public sealed class RotaTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Amanha = new(2026, 9, 15);
    private static readonly Guid Organizacao = Guid.CreateVersion7();
    private static readonly Guid Autor = Guid.CreateVersion7();
    private static readonly GeradorDeTeste Ids = new();

    [Fact]
    public void CriarGeraRotaEmMontagemEOPrimeiroEvento()
    {
        var (rota, evento) = Rota.Criar(Guid.CreateVersion7(), Organizacao, "ROT-2026-0001", Amanha, null, Ids, Autor, Agora);

        Assert.Equal(StatusDaRota.EmMontagem, rota.Status);
        Assert.Equal(0, rota.VersaoDaOrdem);
        Assert.Empty(rota.Paradas);
        Assert.Equal(1, evento.Sequencia);
        Assert.Equal(TipoDeEventoDaRota.Criada, evento.Tipo);
        Assert.Contains("2026-09-15", evento.Dados, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(61)]
    public void DataForaDaJanelaEhRecusada(int dias)
    {
        var erro = Assert.Throws<ExcecaoDeDominio>(() =>
            Rota.Criar(Guid.CreateVersion7(), Organizacao, "ROT-2026-0001", new DateOnly(2026, 9, 14).AddDays(dias), null, Ids, Autor, Agora));

        Assert.Equal("data_invalida", erro.Codigo);
    }

    [Fact]
    public void OntemEmUtcEhAceitoPorCausaDoFuso()
    {
        var (rota, _) = Rota.Criar(Guid.CreateVersion7(), Organizacao, "ROT-2026-0001", new DateOnly(2026, 9, 13), null, Ids, Autor, Agora);

        Assert.Equal(new DateOnly(2026, 9, 13), rota.Data);
    }

    [Fact]
    public void InclusaoNumeraNaOrdemInformadaEVersionaAOrdem()
    {
        var rota = NovaRota();
        var (a, b, c) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        rota.AdicionarParadas([a, b], Ids, Autor, Agora);
        var evento = rota.AdicionarParadas([c], Ids, Autor, Agora);

        Assert.Equal(new[] { a, b, c }, rota.ObterParadasAtivas().Select(parada => parada.EntregaId));
        Assert.Equal(Enumerable.Range(1, 3), rota.ObterParadasAtivas().Select(parada => parada.Sequencia));
        Assert.Equal(2, rota.VersaoDaOrdem);
        Assert.Equal(TipoDeEventoDaRota.ParadasAdicionadas, evento.Tipo);
        Assert.Equal(3, evento.Sequencia);
    }

    [Fact]
    public void EntregaRepetidaNaRotaEhRecusadaSemIncluirNada()
    {
        var rota = NovaRota();
        var a = Guid.CreateVersion7();
        rota.AdicionarParadas([a], Ids, Autor, Agora);

        var erro = Assert.Throws<ExcecaoDeDominio>(() => rota.AdicionarParadas([Guid.CreateVersion7(), a], Ids, Autor, Agora));

        Assert.Equal("entrega_ja_na_rota", erro.Codigo);
        Assert.Single(rota.ObterParadasAtivas());
        Assert.Equal(1, rota.VersaoDaOrdem);
    }

    [Fact]
    public void ListaVaziaOuComRepeticaoEhRecusada()
    {
        var rota = NovaRota();
        var a = Guid.CreateVersion7();

        foreach (var lista in new[] { Array.Empty<Guid>(), [a, a], [Guid.Empty] })
        {
            var erro = Assert.Throws<ExcecaoDeDominio>(() => rota.AdicionarParadas(lista, Ids, Autor, Agora));
            Assert.Equal("lista_de_entregas_invalida", erro.Codigo);
        }
    }

    [Fact]
    public void RemocaoDesativaGuardaMotivoERenumera()
    {
        var rota = NovaRota();
        var (a, b, c) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        rota.AdicionarParadas([a, b, c], Ids, Autor, Agora);

        var eventos = rota.RemoverParada(b, MotivoDeRemocaoDeParada.DecisaoDoPlanejamento, Ids, Autor, Agora);

        Assert.Equal(new[] { a, c }, rota.ObterParadasAtivas().Select(parada => parada.EntregaId));
        Assert.Equal(Enumerable.Range(1, 2), rota.ObterParadasAtivas().Select(parada => parada.Sequencia));
        var removida = Assert.Single(rota.Paradas, parada => !parada.Ativa);
        Assert.Equal(MotivoDeRemocaoDeParada.DecisaoDoPlanejamento, removida.MotivoDaRemocao);
        Assert.Equal(Agora, removida.RemovidaEm);
        Assert.Equal(TipoDeEventoDaRota.ParadaRemovida, Assert.Single(eventos).Tipo);
        Assert.Equal(2, rota.VersaoDaOrdem);

        var erro = Assert.Throws<ExcecaoDeDominio>(() =>
            rota.RemoverParada(b, MotivoDeRemocaoDeParada.DecisaoDoPlanejamento, Ids, Autor, Agora));
        Assert.Equal("parada_nao_encontrada", erro.Codigo);
    }

    [Fact]
    public void EntregaRetiradaPodeVoltarParaAMesmaRota()
    {
        var rota = NovaRota();
        var a = Guid.CreateVersion7();
        rota.AdicionarParadas([a], Ids, Autor, Agora);
        rota.RemoverParada(a, MotivoDeRemocaoDeParada.DecisaoDoPlanejamento, Ids, Autor, Agora);

        rota.AdicionarParadas([a], Ids, Autor, Agora);

        Assert.Single(rota.ObterParadasAtivas());
        Assert.Equal(2, rota.Paradas.Count);
    }

    [Fact]
    public void ReordenacaoAceitaSoPermutacaoExataERegistraAsDuasOrdens()
    {
        var rota = NovaRota();
        var (a, b, c) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        rota.AdicionarParadas([a, b, c], Ids, Autor, Agora);

        foreach (var invalida in new[] { new[] { a, b }, [a, b, b], [a, b, Guid.CreateVersion7()], [a, b, c, Guid.CreateVersion7()] })
        {
            var erro = Assert.Throws<ExcecaoDeDominio>(() => rota.Reordenar(invalida, Ids, Autor, Agora));
            Assert.Equal("ordem_invalida", erro.Codigo);
        }

        Assert.Null(rota.Reordenar([a, b, c], Ids, Autor, Agora));

        var evento = rota.Reordenar([c, a, b], Ids, Autor, Agora);

        Assert.NotNull(evento);
        Assert.Equal(new[] { c, a, b }, rota.ObterParadasAtivas().Select(parada => parada.EntregaId));
        Assert.Equal(Enumerable.Range(1, 3), rota.ObterParadasAtivas().Select(parada => parada.Sequencia));
        Assert.Equal(2, rota.VersaoDaOrdem);
        Assert.Contains("ordemAnterior", evento.Dados, StringComparison.Ordinal);
        Assert.Contains(c.ToString(), evento.Dados, StringComparison.Ordinal);
    }

    [Fact]
    public void MotoristaEVeiculoInativosNaoSaoAtribuidos()
    {
        var rota = NovaRota();
        var motorista = Motorista.Criar(Guid.CreateVersion7(), Organizacao, "Rafael Souza", null, Agora);
        var veiculo = Veiculo.Criar(Guid.CreateVersion7(), Organizacao, PlacaDeVeiculo.Criar("ABC1D23"), "Fiorino 03", TipoDeVeiculo.Utilitario, 650, Agora);
        motorista.Inativar(Agora);
        veiculo.Inativar(Agora);

        Assert.Equal("motorista_inativo", Assert.Throws<ExcecaoDeDominio>(() => rota.AtribuirMotorista(motorista, Ids, Autor, Agora)).Codigo);
        Assert.Equal("veiculo_inativo", Assert.Throws<ExcecaoDeDominio>(() => rota.AtribuirVeiculo(veiculo, Ids, Autor, Agora)).Codigo);
        Assert.Null(rota.MotoristaId);
        Assert.Null(rota.VeiculoId);
    }

    [Fact]
    public void CadastroDeOutraOrganizacaoRespondeComoInexistente()
    {
        var rota = NovaRota();
        var deOutra = Motorista.Criar(Guid.CreateVersion7(), Guid.CreateVersion7(), "Rafael Souza", null, Agora);

        var erro = Assert.Throws<ExcecaoDeDominio>(() => rota.AtribuirMotorista(deOutra, Ids, Autor, Agora));

        Assert.Equal("motorista_nao_encontrado", erro.Codigo);
        Assert.Equal(CategoriaDeErroDeDominio.NaoEncontrado, erro.Categoria);
    }

    [Fact]
    public void TrocaDeMotoristaRegistraOAnteriorERepetirNaoTemEfeito()
    {
        var rota = NovaRota();
        var primeiro = Motorista.Criar(Guid.CreateVersion7(), Organizacao, "Rafael Souza", null, Agora);
        var segundo = Motorista.Criar(Guid.CreateVersion7(), Organizacao, "Márcia Lopes", null, Agora);

        rota.AtribuirMotorista(primeiro, Ids, Autor, Agora);
        Assert.Null(rota.AtribuirMotorista(primeiro, Ids, Autor, Agora));
        var troca = rota.AtribuirMotorista(segundo, Ids, Autor, Agora);

        Assert.Equal(segundo.Id, rota.MotoristaId);
        Assert.NotNull(troca);
        Assert.Contains(primeiro.Id.ToString(), troca.Dados, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1, "saida_no_passado")]
    [InlineData(60, "saida_fora_da_data")]
    public void SaidaPlanejadaPrecisaSerFuturaENoDiaDaRota(int horas, string codigo)
    {
        var rota = NovaRota();

        var erro = Assert.Throws<ExcecaoDeDominio>(() => rota.PlanejarSaida(Agora.AddHours(horas), Ids, Autor, Agora));

        Assert.Equal(codigo, erro.Codigo);
    }

    [Fact]
    public void PlanejarExigeParadaMotoristaVeiculoESaida()
    {
        var rota = NovaRota();

        void EsperarCodigo(string codigo) =>
            Assert.Equal(codigo, Assert.Throws<ExcecaoDeDominio>(() => rota.Planejar(Ids, Autor, Agora)).Codigo);

        EsperarCodigo("rota_sem_paradas");
        rota.AdicionarParadas([Guid.CreateVersion7()], Ids, Autor, Agora);
        EsperarCodigo("rota_sem_motorista");
        rota.AtribuirMotorista(Motorista.Criar(Guid.CreateVersion7(), Organizacao, "Rafael Souza", null, Agora), Ids, Autor, Agora);
        EsperarCodigo("rota_sem_veiculo");
        rota.AtribuirVeiculo(
            Veiculo.Criar(Guid.CreateVersion7(), Organizacao, PlacaDeVeiculo.Criar("ABC1D23"), "Fiorino 03", TipoDeVeiculo.Utilitario, 650, Agora),
            Ids,
            Autor,
            Agora);
        EsperarCodigo("rota_sem_saida_planejada");
        rota.PlanejarSaida(Agora.AddHours(20), Ids, Autor, Agora);

        var evento = rota.Planejar(Ids, Autor, Agora);

        Assert.NotNull(evento);
        Assert.Equal(StatusDaRota.Planejada, rota.Status);
        Assert.Equal(StatusDaRota.Planejada, evento.StatusResultante);
        Assert.Null(rota.Planejar(Ids, Autor, Agora));

        // Saída que ficou no passado impede confirmar de novo depois de voltar para montagem.
        var depois = Agora.AddDays(2);
        rota.RemoverParada(rota.ObterParadasAtivas()[0].EntregaId, MotivoDeRemocaoDeParada.DecisaoDoPlanejamento, Ids, Autor, depois);
        rota.AdicionarParadas([Guid.CreateVersion7()], Ids, Autor, depois);
        Assert.Equal("saida_no_passado", Assert.Throws<ExcecaoDeDominio>(() => rota.Planejar(Ids, Autor, depois)).Codigo);
    }

    [Fact]
    public void RotaPlanejadaQueFicaSemParadaVoltaParaMontagem()
    {
        var rota = RotaPlanejada(out var entregaId);

        var eventos = rota.RemoverParada(entregaId, MotivoDeRemocaoDeParada.EntregaCancelada, Ids, Autor, Agora);

        Assert.Equal(StatusDaRota.EmMontagem, rota.Status);
        Assert.Null(rota.PlanejadaEm);
        Assert.Equal(
            new[] { TipoDeEventoDaRota.ParadaRemovida, TipoDeEventoDaRota.RetornouParaMontagem },
            eventos.Select(evento => evento.Tipo));
    }

    [Fact]
    public void CancelamentoLiberaAsEntregasEEncerraAEstrutura()
    {
        var rota = NovaRota();
        var (a, b) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        rota.AdicionarParadas([a, b], Ids, Autor, Agora);

        var cancelamento = rota.Cancelar(Ids, Autor, Agora);

        Assert.NotNull(cancelamento.Evento);
        Assert.Equal(new[] { a, b }, cancelamento.EntregasLiberadas);
        Assert.Equal(StatusDaRota.Cancelada, rota.Status);
        Assert.Empty(rota.ObterParadasAtivas());
        Assert.All(rota.Paradas, parada => Assert.Equal(MotivoDeRemocaoDeParada.RotaCancelada, parada.MotivoDaRemocao));

        var repeticao = rota.Cancelar(Ids, Autor, Agora);
        Assert.Null(repeticao.Evento);
        Assert.Empty(repeticao.EntregasLiberadas);

        foreach (var alteracao in new Action[]
        {
            () => rota.AdicionarParadas([Guid.CreateVersion7()], Ids, Autor, Agora),
            () => rota.Reordenar([], Ids, Autor, Agora),
            () => rota.PlanejarSaida(Agora.AddHours(20), Ids, Autor, Agora),
            () => rota.Planejar(Ids, Autor, Agora),
        })
        {
            Assert.Equal("rota_nao_editavel", Assert.Throws<ExcecaoDeDominio>(alteracao).Codigo);
        }
    }

    private static Rota NovaRota() =>
        Rota.Criar(Guid.CreateVersion7(), Organizacao, "ROT-2026-0001", Amanha, null, Ids, Autor, Agora).Rota;

    private static Rota RotaPlanejada(out Guid entregaId)
    {
        var rota = NovaRota();
        entregaId = Guid.CreateVersion7();
        rota.AdicionarParadas([entregaId], Ids, Autor, Agora);
        rota.AtribuirMotorista(Motorista.Criar(Guid.CreateVersion7(), Organizacao, "Rafael Souza", null, Agora), Ids, Autor, Agora);
        rota.AtribuirVeiculo(
            Veiculo.Criar(Guid.CreateVersion7(), Organizacao, PlacaDeVeiculo.Criar("ABC1D23"), "Fiorino 03", TipoDeVeiculo.Utilitario, 650, Agora),
            Ids,
            Autor,
            Agora);
        rota.PlanejarSaida(Agora.AddHours(20), Ids, Autor, Agora);
        rota.Planejar(Ids, Autor, Agora);
        return rota;
    }

    private sealed class GeradorDeTeste : IGeradorDeIdentificador
    {
        public Guid Novo() => Guid.CreateVersion7();
    }
}

/// <summary>Regras de status da rota como especificação.</summary>
public sealed class RegrasDaRotaTestes
{
    //                                                     EM     Pl     EA     Co     Ca
    private static readonly bool[] Estrutural = [true, true, false, false, false];
    private static readonly bool[] Ativa = [true, true, true, false, false];
    private static readonly bool[] Cancelavel = [true, true, false, false, false];

    public static TheoryData<StatusDaRota> TodosOsStatus() => new(Enum.GetValues<StatusDaRota>());

    [Fact]
    public void TabelasCobremTodoStatus()
    {
        var quantidade = Enum.GetValues<StatusDaRota>().Length;
        Assert.All(new[] { Estrutural, Ativa, Cancelavel }, tabela => Assert.Equal(quantidade, tabela.Length));
    }

    [Theory]
    [MemberData(nameof(TodosOsStatus))]
    public void RegrasPorStatus(StatusDaRota status)
    {
        var indice = Array.IndexOf(Enum.GetValues<StatusDaRota>(), status);

        Assert.Equal(Estrutural[indice], RegrasDaRota.PermiteAlteracaoEstrutural(status));
        Assert.Equal(Ativa[indice], RegrasDaRota.EhAtiva(status));
        Assert.Equal(Cancelavel[indice], RegrasDaRota.PermiteCancelamento(status));

        // Rota concluída ou cancelada não aceita alteração estrutural e não ocupa ninguém.
        if (RegrasDaRota.EhFinal(status))
        {
            Assert.False(RegrasDaRota.PermiteAlteracaoEstrutural(status));
            Assert.False(RegrasDaRota.EhAtiva(status));
        }
    }
}

/// <summary>Transições da entrega provocadas pela rota.</summary>
public sealed class EntregaEmRotaTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Rota = Guid.CreateVersion7();
    private static readonly Guid Motorista = Guid.CreateVersion7();

    [Fact]
    public void PlanejarAtribuirERetirarPercorremOsStatusComEventos()
    {
        var entrega = NovaEntrega();

        var planejada = entrega.Planejar(Rota, null, Guid.CreateVersion7(), Agora);
        Assert.Equal(StatusDaEntrega.Planejada, entrega.Status);
        Assert.Equal(TipoDeEventoDaEntrega.Planejada, planejada.Tipo);
        Assert.Contains(Rota.ToString(), planejada.Dados, StringComparison.Ordinal);

        var atribuida = entrega.Atribuir(Rota, Motorista, null, Guid.CreateVersion7(), Agora);
        Assert.Equal(StatusDaEntrega.Atribuida, entrega.Status);
        Assert.Contains(Motorista.ToString(), atribuida.Dados, StringComparison.Ordinal);

        // Troca de motorista: nova atribuição registrada.
        var reatribuida = entrega.Atribuir(Rota, Guid.CreateVersion7(), null, Guid.CreateVersion7(), Agora);
        Assert.Equal(4, reatribuida.Sequencia);

        var retirada = entrega.RetirarDaRota(Rota, null, Guid.CreateVersion7(), Agora);
        Assert.Equal(StatusDaEntrega.Criada, entrega.Status);
        Assert.Equal(TipoDeEventoDaEntrega.RetiradaDaRota, retirada.Tipo);
        Assert.Contains("Atribuida", retirada.Dados, StringComparison.Ordinal);
    }

    [Fact]
    public void EntregaForaDaRegraNaoEntraNemRecebeMotorista()
    {
        var entrega = NovaEntrega();

        Assert.Equal("entrega_nao_planejada", Assert.Throws<ExcecaoDeDominio>(() =>
            entrega.Atribuir(Rota, Motorista, null, Guid.CreateVersion7(), Agora)).Codigo);
        Assert.Equal("entrega_fora_de_rota", Assert.Throws<ExcecaoDeDominio>(() =>
            entrega.RetirarDaRota(Rota, null, Guid.CreateVersion7(), Agora)).Codigo);

        entrega.Planejar(Rota, null, Guid.CreateVersion7(), Agora);
        Assert.Equal("entrega_inelegivel_para_rota", Assert.Throws<ExcecaoDeDominio>(() =>
            entrega.Planejar(Guid.CreateVersion7(), null, Guid.CreateVersion7(), Agora)).Codigo);

        entrega.Cancelar(MotivoDeCancelamento.SolicitacaoDoCliente, null, null, Guid.CreateVersion7(), Agora);
        Assert.Equal("entrega_inelegivel_para_rota", Assert.Throws<ExcecaoDeDominio>(() =>
            entrega.Planejar(Rota, null, Guid.CreateVersion7(), Agora)).Codigo);
    }

    [Fact]
    public void EntregaPlanejadaTemClienteCongeladoMasDestinoEditavel()
    {
        var entrega = NovaEntrega();
        entrega.Planejar(Rota, null, Guid.CreateVersion7(), Agora);

        var comOutroCliente = new DadosDaEntrega(
            Guid.CreateVersion7(), entrega.DestinatarioId, entrega.Endereco, entrega.Localizacao, entrega.Janela, entrega.Observacoes);
        Assert.Equal("campo_nao_editavel", Assert.Throws<ExcecaoDeDominio>(() =>
            entrega.AtualizarDados(comOutroCliente, null, Guid.CreateVersion7(), Agora)).Codigo);

        var comObservacao = new DadosDaEntrega(
            entrega.ClienteId, entrega.DestinatarioId, entrega.Endereco, entrega.Localizacao, entrega.Janela, "Portaria 24h");
        Assert.Equal(new[] { CamposDaEntrega.Observacoes }, entrega.AtualizarDados(comObservacao, null, Guid.CreateVersion7(), Agora).Campos);
    }

    [Theory]
    [InlineData(StatusDaEntrega.Criada, true, false)]
    [InlineData(StatusDaEntrega.Planejada, false, true)]
    [InlineData(StatusDaEntrega.Atribuida, false, true)]
    [InlineData(StatusDaEntrega.EmRota, false, false)]
    [InlineData(StatusDaEntrega.ProximaDoDestino, false, false)]
    [InlineData(StatusDaEntrega.Entregue, false, false)]
    [InlineData(StatusDaEntrega.TentativaFrustrada, false, false)]
    [InlineData(StatusDaEntrega.Reagendada, true, false)]
    [InlineData(StatusDaEntrega.Cancelada, false, false)]
    public void RegrasDeRotaPorStatusDaEntrega(StatusDaEntrega status, bool podeEntrar, bool emRotaNaoIniciada)
    {
        Assert.Equal(podeEntrar, RegrasDaEntrega.PodeEntrarEmRota(status));
        Assert.Equal(emRotaNaoIniciada, RegrasDaEntrega.EstaEmRotaNaoIniciada(status));
    }

    private static Entrega NovaEntrega() =>
        Entrega.Criar(
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
}
