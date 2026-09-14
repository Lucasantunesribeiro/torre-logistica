using System.Text.Json;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Abstracoes.Identificadores;
using TorreLogistica.Domain.Frota;

namespace TorreLogistica.Domain.Rotas;

/// <summary>Resultado do cancelamento de uma rota.</summary>
/// <param name="evento">Evento da timeline, ou <see langword="null"/> se já estava cancelada.</param>
/// <param name="entregasLiberadas">Entregas que estavam na rota e voltam a ficar livres.</param>
public sealed class CancelamentoDaRota(EventoDaRota? evento, IReadOnlyList<Guid> entregasLiberadas)
{
    /// <summary>Evento da timeline, ou <see langword="null"/> se já estava cancelada.</summary>
    public EventoDaRota? Evento { get; } = evento;

    /// <summary>Entregas que estavam na rota e voltam a ficar livres.</summary>
    public IReadOnlyList<Guid> EntregasLiberadas { get; } = entregasLiberadas;
}

/// <summary>
/// Rota: o agrupamento operacional de entregas para um motorista, um veículo e um dia.
/// </summary>
/// <remarks>
/// <para>
/// A rota é dona das paradas: incluir, retirar e reordenar passa por ela, que numera, versiona a
/// ordem e registra o evento. Toda mudança toca a linha da rota, então a versão da linha
/// (<c>xmin</c>) serializa mudanças concorrentes na mesma rota.
/// </para>
/// <para>
/// A rota não altera a <c>Entrega</c> — agregado diferente. Quem inclui uma entrega aqui também
/// chama <c>Entrega.Planejar</c>, na mesma transação; quem atribui motorista chama
/// <c>Entrega.Atribuir</c> para cada parada. A coordenação fica na aplicação.
/// </para>
/// <para>
/// O que atravessa rotas diferentes — entrega em duas rotas ativas, motorista ou veículo em duas
/// rotas ativas no mesmo dia — não cabe numa rota só. A aplicação confere antes, e índices únicos
/// parciais no banco decidem a corrida.
/// </para>
/// </remarks>
public sealed class Rota
{
    /// <summary>Até quantos dias à frente uma rota pode ser montada.</summary>
    public const int DiasMaximosDeAntecedencia = 60;

    private static readonly JsonSerializerOptions OpcoesDeJson = new(JsonSerializerDefaults.Web);

    private readonly List<Parada> _paradas = [];

    private Rota()
    {
        Codigo = string.Empty;
    }

    /// <summary>Identificador (UUIDv7).</summary>
    public Guid Id { get; private set; }

    /// <summary>Organização dona da rota.</summary>
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Código humano, único na organização.</summary>
    public string Codigo { get; private set; }

    /// <summary>Dia operacional da rota, como a organização o chama.</summary>
    public DateOnly Data { get; private set; }

    /// <summary>Hub de saída, quando houver.</summary>
    public Guid? HubId { get; private set; }

    /// <summary>Motorista.</summary>
    public Guid? MotoristaId { get; private set; }

    /// <summary>Veículo.</summary>
    public Guid? VeiculoId { get; private set; }

    /// <summary>Saída planejada, em UTC.</summary>
    public DateTimeOffset? SaidaPlanejada { get; private set; }

    /// <summary>Status atual.</summary>
    public StatusDaRota Status { get; private set; }

    /// <summary>Versão da ordem das paradas: sobe a cada inclusão, retirada ou reordenação.</summary>
    public int VersaoDaOrdem { get; private set; }

    /// <summary>Instante da criação.</summary>
    public DateTimeOffset CriadaEm { get; private set; }

    /// <summary>Instante da última mudança.</summary>
    public DateTimeOffset AtualizadaEm { get; private set; }

    /// <summary>Instante em que foi confirmada como planejada.</summary>
    public DateTimeOffset? PlanejadaEm { get; private set; }

    /// <summary>Instante do cancelamento.</summary>
    public DateTimeOffset? CanceladaEm { get; private set; }

    /// <summary>Sequência do último evento da timeline.</summary>
    public int UltimaSequenciaDeEvento { get; private set; }

    /// <summary>Versão da linha, para concorrência otimista.</summary>
    public uint Versao { get; private set; }

    /// <summary>Todas as paradas que a rota já teve, ativas e retiradas.</summary>
    public IReadOnlyList<Parada> Paradas => _paradas;

    /// <summary>Cria a rota em montagem e o primeiro evento da timeline.</summary>
    public static (Rota Rota, EventoDaRota Evento) Criar(
        Guid id,
        Guid organizacaoId,
        string codigo,
        DateOnly data,
        Guid? hubId,
        IGeradorDeIdentificador identificadores,
        Guid? autorUsuarioId,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(identificadores);
        ExcecaoDeDominio.LancarSe(id == Guid.Empty || hubId == Guid.Empty, "identificador_invalido", "Identificador inválido.");
        ExcecaoDeDominio.LancarSe(organizacaoId == Guid.Empty, "organizacao_invalida", "Organização inválida.");
        ExcecaoDeDominio.LancarSe(!CodigoDaRota.EhValido(codigo), "codigo_invalido", "Código de rota inválido.");

        var instante = agora.ToUniversalTime();
        var hoje = DateOnly.FromDateTime(instante.UtcDateTime);

        // Um dia de tolerância para trás: "hoje" da organização pode ainda ser "ontem" em UTC.
        ExcecaoDeDominio.LancarSe(
            data < hoje.AddDays(-1) || data > hoje.AddDays(DiasMaximosDeAntecedencia),
            "data_invalida",
            $"A data da rota vai de hoje até {DiasMaximosDeAntecedencia} dias à frente.");

        var rota = new Rota
        {
            Id = id,
            OrganizacaoId = organizacaoId,
            Codigo = codigo,
            Data = data,
            HubId = hubId,
            Status = StatusDaRota.EmMontagem,
            CriadaEm = instante,
            AtualizadaEm = instante,
        };

        var evento = rota.RegistrarEvento(
            TipoDeEventoDaRota.Criada,
            new { data = data.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), hubId },
            identificadores,
            autorUsuarioId,
            instante);

        return (rota, evento);
    }

    /// <summary>Paradas ativas, na ordem da rota.</summary>
    public IReadOnlyList<Parada> ObterParadasAtivas() =>
        [.. _paradas.Where(parada => parada.Ativa).OrderBy(parada => parada.Sequencia)];

    /// <summary>Inclui entregas no fim da rota, na ordem informada. Tudo ou nada.</summary>
    public EventoDaRota AdicionarParadas(
        IReadOnlyList<Guid> entregaIds,
        IGeradorDeIdentificador identificadores,
        Guid? autorUsuarioId,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(entregaIds);
        ArgumentNullException.ThrowIfNull(identificadores);
        GarantirAlteracaoEstrutural();

        ExcecaoDeDominio.LancarSe(
            entregaIds.Count is 0 or > RegrasDaRota.QuantidadeMaximaPorInclusao
                || entregaIds.Contains(Guid.Empty)
                || entregaIds.Distinct().Count() != entregaIds.Count,
            "lista_de_entregas_invalida",
            $"Informe de 1 a {RegrasDaRota.QuantidadeMaximaPorInclusao} entregas, sem repetição.");

        var ativas = ObterParadasAtivas();
        var jaNaRota = ativas.Select(parada => parada.EntregaId).ToHashSet();

        if (entregaIds.Any(jaNaRota.Contains))
        {
            throw ExcecaoDeDominio.Conflito("entrega_ja_na_rota", "A entrega já está nesta rota.");
        }

        ExcecaoDeDominio.LancarSe(
            ativas.Count + entregaIds.Count > RegrasDaRota.QuantidadeMaximaDeParadas,
            "limite_de_paradas",
            $"Uma rota tem no máximo {RegrasDaRota.QuantidadeMaximaDeParadas} paradas.");

        var instante = agora.ToUniversalTime();
        var proxima = ativas.Count;

        foreach (var entregaId in entregaIds)
        {
            _paradas.Add(Parada.Criar(identificadores.Novo(), OrganizacaoId, Id, entregaId, ++proxima, instante));
        }

        VersaoDaOrdem++;
        AtualizadaEm = instante;

        return RegistrarEvento(
            TipoDeEventoDaRota.ParadasAdicionadas,
            new { entregas = entregaIds, versaoDaOrdem = VersaoDaOrdem },
            identificadores,
            autorUsuarioId,
            instante);
    }

    /// <summary>
    /// Retira a entrega da rota e renumera as demais. Rota planejada que fica sem parada volta
    /// para montagem — planejada sem parada não é rota.
    /// </summary>
    public IReadOnlyList<EventoDaRota> RemoverParada(
        Guid entregaId,
        MotivoDeRemocaoDeParada motivo,
        IGeradorDeIdentificador identificadores,
        Guid? autorUsuarioId,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(identificadores);
        ExcecaoDeDominio.LancarSe(!Enum.IsDefined(motivo), "motivo_invalido", "Motivo de remoção inválido.");
        GarantirAlteracaoEstrutural();

        var parada = ObterParadasAtivas().SingleOrDefault(item => item.EntregaId == entregaId)
            ?? throw ExcecaoDeDominio.NaoEncontrado("parada_nao_encontrada", "A entrega não está nesta rota.");

        var instante = agora.ToUniversalTime();
        parada.Desativar(motivo, instante);
        Renumerar();
        VersaoDaOrdem++;
        AtualizadaEm = instante;

        var eventos = new List<EventoDaRota>
        {
            RegistrarEvento(
                TipoDeEventoDaRota.ParadaRemovida,
                new { entregaId, motivo = motivo.ToString(), versaoDaOrdem = VersaoDaOrdem },
                identificadores,
                autorUsuarioId,
                instante),
        };

        if (Status == StatusDaRota.Planejada && ObterParadasAtivas().Count == 0)
        {
            Status = StatusDaRota.EmMontagem;
            PlanejadaEm = null;
            eventos.Add(RegistrarEvento(
                TipoDeEventoDaRota.RetornouParaMontagem, new { motivo = "rota_sem_paradas" }, identificadores, autorUsuarioId, instante));
        }

        return eventos;
    }

    /// <summary>
    /// Reordena as paradas. A nova ordem precisa conter exatamente as entregas da rota.
    /// </summary>
    /// <returns>O evento, ou <see langword="null"/> se a ordem já era essa.</returns>
    public EventoDaRota? Reordenar(
        IReadOnlyList<Guid> ordem,
        IGeradorDeIdentificador identificadores,
        Guid? autorUsuarioId,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(ordem);
        ArgumentNullException.ThrowIfNull(identificadores);
        GarantirAlteracaoEstrutural();

        var ativas = ObterParadasAtivas();
        var ordemAtual = ativas.Select(parada => parada.EntregaId).ToList();

        ExcecaoDeDominio.LancarSe(
            ordem.Count != ordemAtual.Count
                || ordem.Distinct().Count() != ordem.Count
                || !ordem.ToHashSet().SetEquals(ordemAtual),
            "ordem_invalida",
            "A nova ordem precisa conter exatamente as entregas da rota, cada uma uma vez.");

        if (ordem.SequenceEqual(ordemAtual))
        {
            return null;
        }

        var porEntrega = ativas.ToDictionary(parada => parada.EntregaId);
        for (var indice = 0; indice < ordem.Count; indice++)
        {
            porEntrega[ordem[indice]].MoverPara(indice + 1);
        }

        var instante = agora.ToUniversalTime();
        VersaoDaOrdem++;
        AtualizadaEm = instante;

        return RegistrarEvento(
            TipoDeEventoDaRota.ParadasReordenadas,
            new { ordemAnterior = ordemAtual, ordemNova = ordem, versaoDaOrdem = VersaoDaOrdem },
            identificadores,
            autorUsuarioId,
            instante);
    }

    /// <summary>Define ou troca o motorista. Motorista inativo é recusado.</summary>
    /// <returns>O evento, ou <see langword="null"/> se já era este motorista.</returns>
    public EventoDaRota? AtribuirMotorista(
        Motorista motorista,
        IGeradorDeIdentificador identificadores,
        Guid? autorUsuarioId,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(motorista);
        ArgumentNullException.ThrowIfNull(identificadores);
        GarantirMesmaOrganizacao(motorista.OrganizacaoId, "motorista_nao_encontrado", "Motorista não encontrado.");
        GarantirAlteracaoEstrutural();
        motorista.GarantirAptoParaAtribuicao();

        if (MotoristaId == motorista.Id)
        {
            return null;
        }

        var anterior = MotoristaId;
        var instante = agora.ToUniversalTime();
        MotoristaId = motorista.Id;
        AtualizadaEm = instante;

        return RegistrarEvento(
            TipoDeEventoDaRota.MotoristaAtribuido,
            new { motoristaId = motorista.Id, motoristaAnteriorId = anterior },
            identificadores,
            autorUsuarioId,
            instante);
    }

    /// <summary>Define ou troca o veículo. Veículo inativo é recusado.</summary>
    /// <returns>O evento, ou <see langword="null"/> se já era este veículo.</returns>
    public EventoDaRota? AtribuirVeiculo(
        Veiculo veiculo,
        IGeradorDeIdentificador identificadores,
        Guid? autorUsuarioId,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(veiculo);
        ArgumentNullException.ThrowIfNull(identificadores);
        GarantirMesmaOrganizacao(veiculo.OrganizacaoId, "veiculo_nao_encontrado", "Veículo não encontrado.");
        GarantirAlteracaoEstrutural();
        veiculo.GarantirAptoParaIniciarRota();

        if (VeiculoId == veiculo.Id)
        {
            return null;
        }

        var anterior = VeiculoId;
        var instante = agora.ToUniversalTime();
        VeiculoId = veiculo.Id;
        AtualizadaEm = instante;

        return RegistrarEvento(
            TipoDeEventoDaRota.VeiculoAtribuido,
            new { veiculoId = veiculo.Id, veiculoAnteriorId = anterior },
            identificadores,
            autorUsuarioId,
            instante);
    }

    /// <summary>
    /// Define a saída planejada: no futuro e no dia da rota, com um dia de tolerância para o fuso.
    /// </summary>
    /// <returns>O evento, ou <see langword="null"/> se já era esta saída.</returns>
    public EventoDaRota? PlanejarSaida(
        DateTimeOffset saida,
        IGeradorDeIdentificador identificadores,
        Guid? autorUsuarioId,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(identificadores);
        GarantirAlteracaoEstrutural();

        var instante = agora.ToUniversalTime();
        var saidaUtc = TruncarAoSegundo(saida);

        ExcecaoDeDominio.LancarSe(saidaUtc <= instante, "saida_no_passado", "A saída planejada precisa estar no futuro.");
        ExcecaoDeDominio.LancarSe(
            Math.Abs(DateOnly.FromDateTime(saidaUtc.UtcDateTime).DayNumber - Data.DayNumber) > 1,
            "saida_fora_da_data",
            "A saída planejada precisa cair no dia da rota.");

        if (SaidaPlanejada == saidaUtc)
        {
            return null;
        }

        SaidaPlanejada = saidaUtc;
        AtualizadaEm = instante;

        return RegistrarEvento(
            TipoDeEventoDaRota.SaidaPlanejada, new { saidaPlanejada = saidaUtc }, identificadores, autorUsuarioId, instante);
    }

    /// <summary>
    /// Confirma a rota como planejada: com parada, motorista, veículo e saída futura.
    /// </summary>
    /// <returns>O evento, ou <see langword="null"/> se já estava planejada.</returns>
    public EventoDaRota? Planejar(IGeradorDeIdentificador identificadores, Guid? autorUsuarioId, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(identificadores);

        if (Status == StatusDaRota.Planejada)
        {
            return null;
        }

        if (Status != StatusDaRota.EmMontagem)
        {
            throw RotaNaoEditavel();
        }

        var instante = agora.ToUniversalTime();
        var paradas = ObterParadasAtivas().Count;

        ExcecaoDeDominio.LancarSe(paradas == 0, "rota_sem_paradas", "Inclua ao menos uma entrega antes de planejar.");
        ExcecaoDeDominio.LancarSe(MotoristaId is null, "rota_sem_motorista", "Defina o motorista antes de planejar.");
        ExcecaoDeDominio.LancarSe(VeiculoId is null, "rota_sem_veiculo", "Defina o veículo antes de planejar.");
        ExcecaoDeDominio.LancarSe(SaidaPlanejada is null, "rota_sem_saida_planejada", "Defina a saída antes de planejar.");
        ExcecaoDeDominio.LancarSe(SaidaPlanejada <= instante, "saida_no_passado", "A saída planejada precisa estar no futuro.");

        Status = StatusDaRota.Planejada;
        PlanejadaEm = instante;
        AtualizadaEm = instante;

        return RegistrarEvento(
            TipoDeEventoDaRota.Planejada,
            new { paradas, versaoDaOrdem = VersaoDaOrdem },
            identificadores,
            autorUsuarioId,
            instante);
    }

    /// <summary>
    /// Cancela antes da saída. As paradas ficam inativas e as entregas são devolvidas. Repetir não
    /// tem efeito.
    /// </summary>
    public CancelamentoDaRota Cancelar(IGeradorDeIdentificador identificadores, Guid? autorUsuarioId, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(identificadores);

        if (Status == StatusDaRota.Cancelada)
        {
            return new CancelamentoDaRota(null, []);
        }

        if (!RegrasDaRota.PermiteCancelamento(Status))
        {
            throw ExcecaoDeDominio.Conflito(
                "cancelamento_de_rota_nao_permitido",
                $"Rota no status {Status} não pode ser cancelada.");
        }

        var instante = agora.ToUniversalTime();
        var liberadas = new List<Guid>();

        foreach (var parada in ObterParadasAtivas())
        {
            parada.Desativar(MotivoDeRemocaoDeParada.RotaCancelada, instante);
            liberadas.Add(parada.EntregaId);
        }

        Status = StatusDaRota.Cancelada;
        CanceladaEm = instante;
        AtualizadaEm = instante;

        var evento = RegistrarEvento(
            TipoDeEventoDaRota.Cancelada, new { entregasLiberadas = liberadas }, identificadores, autorUsuarioId, instante);

        return new CancelamentoDaRota(evento, liberadas);
    }

    private static DateTimeOffset TruncarAoSegundo(DateTimeOffset instante)
    {
        var utc = instante.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
    }

    private void GarantirMesmaOrganizacao(Guid organizacaoId, string codigo, string mensagem)
    {
        // A aplicação só carrega cadastros do tenant; isto é a última barreira, e responde como
        // inexistente para não revelar nada.
        if (organizacaoId != OrganizacaoId)
        {
            throw ExcecaoDeDominio.NaoEncontrado(codigo, mensagem);
        }
    }

    private void GarantirAlteracaoEstrutural()
    {
        if (!RegrasDaRota.PermiteAlteracaoEstrutural(Status))
        {
            throw RotaNaoEditavel();
        }
    }

    private ExcecaoDeDominio RotaNaoEditavel() =>
        ExcecaoDeDominio.Conflito("rota_nao_editavel", $"Rota no status {Status} não aceita alteração estrutural.");

    private void Renumerar()
    {
        var sequencia = 0;
        foreach (var parada in ObterParadasAtivas())
        {
            parada.MoverPara(++sequencia);
        }
    }

    private EventoDaRota RegistrarEvento(
        TipoDeEventoDaRota tipo,
        object detalhes,
        IGeradorDeIdentificador identificadores,
        Guid? autorUsuarioId,
        DateTimeOffset instante)
    {
        UltimaSequenciaDeEvento++;

        return EventoDaRota.Registrar(
            identificadores.Novo(),
            OrganizacaoId,
            Id,
            UltimaSequenciaDeEvento,
            tipo,
            Status,
            autorUsuarioId,
            JsonSerializer.Serialize(detalhes, OpcoesDeJson),
            instante);
    }
}
