using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Application.Cadastros;
using TorreLogistica.Application.Comum;
using TorreLogistica.Application.Entregas;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Abstracoes.Identificadores;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Rotas;

namespace TorreLogistica.Application.Rotas;

/// <summary>Filtro da lista de rotas.</summary>
/// <param name="Pagina">Página, a partir de 1.</param>
/// <param name="TamanhoDaPagina">Itens por página.</param>
/// <param name="Data">Só deste dia.</param>
/// <param name="Status">Status aceitos; vazio traz todos.</param>
/// <param name="MotoristaId">Só deste motorista.</param>
public sealed record FiltroDeRotas(
    int Pagina,
    int TamanhoDaPagina,
    DateOnly? Data,
    IReadOnlyList<StatusDaRota> Status,
    Guid? MotoristaId);

/// <summary>Referência a um cadastro, com nome para exibição.</summary>
public sealed record ReferenciaResumo(Guid Id, string Nome);

/// <summary>Veículo da rota.</summary>
public sealed record VeiculoDaRotaResumo(Guid Id, string Placa, string Identificacao);

/// <summary>Parada na sequência da rota.</summary>
public sealed record ParadaResumo(
    int Sequencia,
    Guid EntregaId,
    string CodigoDaEntrega,
    StatusDaEntrega StatusDaEntrega,
    string DestinatarioNome,
    EnderecoResumo Endereco,
    JanelaResumo JanelaPrometida);

/// <summary>Rota com a sequência de paradas.</summary>
public sealed record RotaResumo(
    Guid Id,
    string Codigo,
    DateOnly Data,
    StatusDaRota Status,
    ReferenciaResumo? Hub,
    ReferenciaResumo? Motorista,
    VeiculoDaRotaResumo? Veiculo,
    DateTimeOffset? SaidaPlanejada,
    int VersaoDaOrdem,
    IReadOnlyList<ParadaResumo> Paradas,
    DateTimeOffset CriadaEm,
    DateTimeOffset AtualizadaEm,
    DateTimeOffset? PlanejadaEm,
    DateTimeOffset? CanceladaEm,
    DateTimeOffset? IniciadaEm,
    DateTimeOffset? ConcluidaEm,
    uint Versao);

/// <summary>Rota na lista.</summary>
public sealed record RotaItemResumo(
    Guid Id,
    string Codigo,
    DateOnly Data,
    StatusDaRota Status,
    Guid? HubId,
    Guid? MotoristaId,
    Guid? VeiculoId,
    DateTimeOffset? SaidaPlanejada,
    int QuantidadeDeParadas,
    uint Versao);

/// <summary>Evento da timeline da rota.</summary>
public sealed record EventoDaRotaResumo(
    int Sequencia,
    TipoDeEventoDaRota Tipo,
    StatusDaRota StatusResultante,
    DateTimeOffset OcorridoEm,
    Guid? AutorUsuarioId,
    JsonElement Dados);

/// <summary>
/// Montagem de rotas: criar, incluir e retirar entregas, ordenar, atribuir motorista e veículo,
/// planejar saída, confirmar e cancelar.
/// </summary>
/// <remarks>
/// <para>
/// Coordena dois agregados na mesma gravação: a rota (paradas, ordem, atribuições) e cada
/// entrega afetada (planejada, atribuída, retirada). Os dois eventos — da rota e da entrega —
/// entram no mesmo commit.
/// </para>
/// <para>
/// Regras que atravessam rotas são conferidas antes, para a mensagem, e decididas pelos índices
/// únicos parciais do banco, para a corrida: entrega em duas rotas ativas, motorista ou veículo em
/// duas rotas ativas no mesmo dia.
/// </para>
/// </remarks>
public sealed class GestaoDeRotas(
    SuporteDeCadastro suporte,
    IGeradorDeIdentificador identificadores,
    ILogger<GestaoDeRotas> log)
{
    private const string Recurso = "rota";

    private static readonly StatusDaRota[] StatusAtivos =
        [.. Enum.GetValues<StatusDaRota>().Where(RegrasDaRota.EhAtiva)];

    /// <summary>Lista rotas, do dia mais antigo para o mais novo.</summary>
    public async Task<PaginaDeResultados<RotaItemResumo>> ListarAsync(FiltroDeRotas filtro, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(filtro);

        var consulta = suporte.Contexto.Rotas.AsNoTracking();

        if (filtro.Data is { } data)
        {
            consulta = consulta.Where(rota => rota.Data == data);
        }

        if (filtro.Status.Count > 0)
        {
            var status = filtro.Status.ToArray();
            consulta = consulta.Where(rota => status.Contains(rota.Status));
        }

        if (filtro.MotoristaId is { } motoristaId)
        {
            consulta = consulta.Where(rota => rota.MotoristaId == motoristaId);
        }

        var total = await consulta.CountAsync(cancelamento).ConfigureAwait(false);
        var itens = await consulta
            .OrderBy(rota => rota.Data)
            .ThenBy(rota => rota.Codigo)
            .Skip((filtro.Pagina - 1) * filtro.TamanhoDaPagina)
            .Take(filtro.TamanhoDaPagina)
            .Select(rota => new RotaItemResumo(
                rota.Id,
                rota.Codigo,
                rota.Data,
                rota.Status,
                rota.HubId,
                rota.MotoristaId,
                rota.VeiculoId,
                rota.SaidaPlanejada,
                rota.Paradas.Count(parada => parada.Ativa),
                rota.Versao))
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

        return new PaginaDeResultados<RotaItemResumo>(itens, filtro.Pagina, filtro.TamanhoDaPagina, total);
    }

    /// <summary>Rota com a sequência de paradas.</summary>
    public async Task<RotaResumo> ObterAsync(Guid id, CancellationToken cancelamento)
    {
        var rota = await suporte.Contexto.Rotas
            .AsNoTracking()
            .Include(item => item.Paradas.Where(parada => parada.Ativa))
            .SingleOrDefaultAsync(item => item.Id == id, cancelamento)
            .ConfigureAwait(false)
            ?? throw RotaNaoEncontrada();

        return await MontarResumoAsync(rota, cancelamento).ConfigureAwait(false);
    }

    /// <summary>Timeline da rota, em ordem.</summary>
    public async Task<IReadOnlyList<EventoDaRotaResumo>> ListarEventosAsync(Guid id, CancellationToken cancelamento)
    {
        var existe = await suporte.Contexto.Rotas.AnyAsync(rota => rota.Id == id, cancelamento).ConfigureAwait(false);
        if (!existe)
        {
            throw RotaNaoEncontrada();
        }

        var eventos = await suporte.Contexto.EventosDaRota
            .AsNoTracking()
            .Where(evento => evento.RotaId == id)
            .OrderBy(evento => evento.Sequencia)
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

        return [.. eventos.Select(ParaResumo)];
    }

    /// <summary>Cria uma rota em montagem com o próximo código da organização.</summary>
    public async Task<RotaResumo> CriarAsync(DateOnly data, Guid? hubId, CancellationToken cancelamento)
    {
        var contexto = suporte.Contexto;

        var (id, codigo) = await contexto.ExecutarEmTransacaoAsync(
            async cancelamentoDaTentativa =>
            {
                if (hubId is { } hub)
                {
                    await GarantirHubAtivoAsync(hub, cancelamentoDaTentativa).ConfigureAwait(false);
                }

                var agora = suporte.Agora;
                var ano = agora.UtcDateTime.Year;
                var numero = await contexto
                    .ReservarNumeroSequencialAsync(suporte.OrganizacaoId, CodigoDaRota.Serie, ano, cancelamentoDaTentativa)
                    .ConfigureAwait(false);

                var (rota, evento) = Rota.Criar(
                    identificadores.Novo(),
                    suporte.OrganizacaoId,
                    CodigoDaRota.Gerar(ano, numero),
                    data,
                    hubId,
                    identificadores,
                    suporte.UsuarioId,
                    agora);

                contexto.Rotas.Add(rota);
                contexto.EventosDaRota.Add(evento);
                suporte.Auditar(Recurso, AcoesDeRota.Criada, rota.Id, new { codigo = rota.Codigo });

                await contexto.SaveChangesAsync(cancelamentoDaTentativa).ConfigureAwait(false);
                return (rota.Id, rota.Codigo);
            },
            cancelamento).ConfigureAwait(false);

        log.LogInformation("Rota {RotaId} ({CodigoDaRota}) criada na organização {OrganizacaoId}.", id, codigo, suporte.OrganizacaoId);
        return await ObterAsync(id, cancelamento).ConfigureAwait(false);
    }

    /// <summary>Inclui entregas no fim da rota. Tudo ou nada.</summary>
    public async Task<RotaResumo> AdicionarEntregasAsync(Guid id, IReadOnlyList<Guid> entregaIds, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(entregaIds);

        var contexto = suporte.Contexto;
        var rota = await CarregarAsync(id, cancelamento).ConfigureAwait(false);
        var agora = suporte.Agora;

        // A rota valida lista, status e repetição antes de qualquer consulta às entregas.
        var eventoDaRota = rota.AdicionarParadas(entregaIds, identificadores, suporte.UsuarioId, agora);

        var ids = entregaIds.ToArray();
        var entregas = await contexto.Entregas
            .Where(entrega => ids.Contains(entrega.Id))
            .ToDictionaryAsync(entrega => entrega.Id, cancelamento)
            .ConfigureAwait(false);

        if (entregas.Count != ids.Length)
        {
            throw ExcecaoDeDominio.NaoEncontrado("entrega_nao_encontrada", "Entrega não encontrada.");
        }

        var emOutraRota = await contexto.Paradas
            .AnyAsync(parada => ids.Contains(parada.EntregaId) && parada.Ativa && parada.RotaId != id, cancelamento)
            .ConfigureAwait(false);

        if (emOutraRota)
        {
            throw EntregaEmOutraRota();
        }

        foreach (var entregaId in ids)
        {
            var entrega = entregas[entregaId];
            contexto.EventosDaEntrega.Add(entrega.Planejar(rota.Id, suporte.UsuarioId, identificadores.Novo(), agora));

            if (rota.MotoristaId is { } motoristaId)
            {
                contexto.EventosDaEntrega.Add(entrega.Atribuir(rota.Id, motoristaId, suporte.UsuarioId, identificadores.Novo(), agora));
            }
        }

        contexto.EventosDaRota.Add(eventoDaRota);
        await SalvarAsync(cancelamento).ConfigureAwait(false);

        return await ObterAsync(id, cancelamento).ConfigureAwait(false);
    }

    /// <summary>Retira uma entrega da rota; ela volta a ficar livre.</summary>
    public async Task<RotaResumo> RemoverEntregaAsync(Guid id, Guid entregaId, CancellationToken cancelamento)
    {
        var contexto = suporte.Contexto;
        var rota = await CarregarAsync(id, cancelamento).ConfigureAwait(false);
        var agora = suporte.Agora;

        var eventos = rota.RemoverParada(
            entregaId, MotivoDeRemocaoDeParada.DecisaoDoPlanejamento, identificadores, suporte.UsuarioId, agora);

        var entrega = await contexto.Entregas
            .SingleAsync(item => item.Id == entregaId, cancelamento)
            .ConfigureAwait(false);

        contexto.EventosDaEntrega.Add(entrega.RetirarDaRota(rota.Id, suporte.UsuarioId, identificadores.Novo(), agora));
        contexto.EventosDaRota.AddRange(eventos);
        await SalvarAsync(cancelamento).ConfigureAwait(false);

        return await ObterAsync(id, cancelamento).ConfigureAwait(false);
    }

    /// <summary>Reordena as paradas, exigindo a versão lida da rota.</summary>
    public async Task<RotaResumo> ReordenarAsync(Guid id, uint versao, IReadOnlyList<Guid> ordem, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(ordem);

        var rota = await CarregarAsync(id, cancelamento).ConfigureAwait(false);
        ExigirVersao(rota, versao);

        if (rota.Reordenar(ordem, identificadores, suporte.UsuarioId, suporte.Agora) is { } evento)
        {
            suporte.Contexto.EventosDaRota.Add(evento);
            await SalvarAsync(cancelamento).ConfigureAwait(false);
        }

        return await ObterAsync(id, cancelamento).ConfigureAwait(false);
    }

    /// <summary>
    /// Define ou troca o motorista. As entregas da rota passam a atribuídas a ele — e a troca fica
    /// registrada na timeline de cada uma.
    /// </summary>
    public async Task<RotaResumo> AtribuirMotoristaAsync(Guid id, Guid motoristaId, CancellationToken cancelamento)
    {
        var contexto = suporte.Contexto;
        var rota = await CarregarAsync(id, cancelamento).ConfigureAwait(false);
        var motorista = await contexto.Motoristas
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == motoristaId, cancelamento)
            .ConfigureAwait(false)
            ?? throw ExcecaoDeDominio.NaoEncontrado("motorista_nao_encontrado", "Motorista não encontrado.");

        var agora = suporte.Agora;
        if (rota.AtribuirMotorista(motorista, identificadores, suporte.UsuarioId, agora) is not { } evento)
        {
            return await ObterAsync(id, cancelamento).ConfigureAwait(false);
        }

        var ocupado = await contexto.Rotas
            .AnyAsync(
                outra => outra.Id != id && outra.MotoristaId == motoristaId && outra.Data == rota.Data && StatusAtivos.Contains(outra.Status),
                cancelamento)
            .ConfigureAwait(false);

        if (ocupado)
        {
            throw MotoristaJaEmRota();
        }

        var ids = rota.ObterParadasAtivas().Select(parada => parada.EntregaId).ToArray();
        var entregas = await contexto.Entregas
            .Where(entrega => ids.Contains(entrega.Id))
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

        foreach (var entrega in entregas)
        {
            // Antes da saída, a entrega passa a atribuída ao novo motorista; em execução, é
            // reatribuída. Entrega que já tem resultado na rota fica como está.
            var eventoDaEntrega = entrega.Status switch
            {
                StatusDaEntrega.Planejada or StatusDaEntrega.Atribuida =>
                    entrega.Atribuir(rota.Id, motoristaId, suporte.UsuarioId, identificadores.Novo(), agora),
                StatusDaEntrega.EmRota or StatusDaEntrega.ProximaDoDestino =>
                    entrega.Reatribuir(rota.Id, motoristaId, suporte.UsuarioId, identificadores.Novo(), agora),
                _ => null,
            };

            if (eventoDaEntrega is not null)
            {
                contexto.EventosDaEntrega.Add(eventoDaEntrega);
            }
        }

        contexto.EventosDaRota.Add(evento);
        suporte.Auditar(Recurso, AcoesDeRota.MotoristaAtribuido, rota.Id, new { motoristaId, entregas = ids.Length, rotaEmAndamento = rota.Status == StatusDaRota.EmAndamento });
        await SalvarAsync(cancelamento).ConfigureAwait(false);

        return await ObterAsync(id, cancelamento).ConfigureAwait(false);
    }

    /// <summary>Define ou troca o veículo.</summary>
    public async Task<RotaResumo> AtribuirVeiculoAsync(Guid id, Guid veiculoId, CancellationToken cancelamento)
    {
        var contexto = suporte.Contexto;
        var rota = await CarregarAsync(id, cancelamento).ConfigureAwait(false);
        var veiculo = await contexto.Veiculos
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == veiculoId, cancelamento)
            .ConfigureAwait(false)
            ?? throw ExcecaoDeDominio.NaoEncontrado("veiculo_nao_encontrado", "Veículo não encontrado.");

        if (rota.AtribuirVeiculo(veiculo, identificadores, suporte.UsuarioId, suporte.Agora) is not { } evento)
        {
            return await ObterAsync(id, cancelamento).ConfigureAwait(false);
        }

        var ocupado = await contexto.Rotas
            .AnyAsync(
                outra => outra.Id != id && outra.VeiculoId == veiculoId && outra.Data == rota.Data && StatusAtivos.Contains(outra.Status),
                cancelamento)
            .ConfigureAwait(false);

        if (ocupado)
        {
            throw VeiculoJaEmRota();
        }

        contexto.EventosDaRota.Add(evento);
        suporte.Auditar(Recurso, AcoesDeRota.VeiculoAtribuido, rota.Id, new { veiculoId });
        await SalvarAsync(cancelamento).ConfigureAwait(false);

        return await ObterAsync(id, cancelamento).ConfigureAwait(false);
    }

    /// <summary>Define a saída planejada.</summary>
    public async Task<RotaResumo> PlanejarSaidaAsync(Guid id, DateTimeOffset saida, CancellationToken cancelamento)
    {
        var rota = await CarregarAsync(id, cancelamento).ConfigureAwait(false);

        if (rota.PlanejarSaida(saida, identificadores, suporte.UsuarioId, suporte.Agora) is { } evento)
        {
            suporte.Contexto.EventosDaRota.Add(evento);
            await SalvarAsync(cancelamento).ConfigureAwait(false);
        }

        return await ObterAsync(id, cancelamento).ConfigureAwait(false);
    }

    /// <summary>Confirma a rota como planejada.</summary>
    public async Task<RotaResumo> PlanejarAsync(Guid id, CancellationToken cancelamento)
    {
        var rota = await CarregarAsync(id, cancelamento).ConfigureAwait(false);

        if (rota.Planejar(identificadores, suporte.UsuarioId, suporte.Agora) is { } evento)
        {
            suporte.Contexto.EventosDaRota.Add(evento);
            suporte.Auditar(Recurso, AcoesDeRota.Planejada, rota.Id, new { paradas = rota.ObterParadasAtivas().Count });
            await SalvarAsync(cancelamento).ConfigureAwait(false);

            log.LogInformation("Rota {RotaId} planejada na organização {OrganizacaoId}.", rota.Id, rota.OrganizacaoId);
        }

        return await ObterAsync(id, cancelamento).ConfigureAwait(false);
    }

    /// <summary>Cancela a rota antes da saída e devolve as entregas.</summary>
    public async Task<RotaResumo> CancelarAsync(Guid id, CancellationToken cancelamento)
    {
        var contexto = suporte.Contexto;
        var rota = await CarregarAsync(id, cancelamento).ConfigureAwait(false);
        var agora = suporte.Agora;
        var cancelamentoDaRota = rota.Cancelar(identificadores, suporte.UsuarioId, agora);

        if (cancelamentoDaRota.Evento is { } evento)
        {
            var ids = cancelamentoDaRota.EntregasLiberadas.ToArray();
            var entregas = await contexto.Entregas
                .Where(entrega => ids.Contains(entrega.Id))
                .ToListAsync(cancelamento)
                .ConfigureAwait(false);

            foreach (var entrega in entregas)
            {
                contexto.EventosDaEntrega.Add(entrega.RetirarDaRota(rota.Id, suporte.UsuarioId, identificadores.Novo(), agora));
            }

            contexto.EventosDaRota.Add(evento);
            suporte.Auditar(Recurso, AcoesDeRota.Cancelada, rota.Id, new { entregasLiberadas = ids.Length });
            await SalvarAsync(cancelamento).ConfigureAwait(false);

            log.LogInformation("Rota {RotaId} cancelada na organização {OrganizacaoId}.", rota.Id, rota.OrganizacaoId);
        }

        return await ObterAsync(id, cancelamento).ConfigureAwait(false);
    }

    private async Task<RotaResumo> MontarResumoAsync(Rota rota, CancellationToken cancelamento)
    {
        var contexto = suporte.Contexto;
        var paradas = rota.ObterParadasAtivas();
        var ids = paradas.Select(parada => parada.EntregaId).ToArray();

        var entregas = await (
                from entrega in contexto.Entregas.AsNoTracking()
                where ids.Contains(entrega.Id)
                join destinatario in contexto.Destinatarios on entrega.DestinatarioId equals destinatario.Id
                select new LinhaDeParada { Entrega = entrega, DestinatarioNome = destinatario.Nome })
            .ToDictionaryAsync(linha => linha.Entrega.Id, cancelamento)
            .ConfigureAwait(false);

        var hub = rota.HubId is { } hubId
            ? await contexto.Hubs.AsNoTracking().Where(item => item.Id == hubId)
                .Select(item => new ReferenciaResumo(item.Id, item.Nome)).SingleOrDefaultAsync(cancelamento).ConfigureAwait(false)
            : null;

        var motorista = rota.MotoristaId is { } motoristaId
            ? await contexto.Motoristas.AsNoTracking().Where(item => item.Id == motoristaId)
                .Select(item => new ReferenciaResumo(item.Id, item.Nome)).SingleOrDefaultAsync(cancelamento).ConfigureAwait(false)
            : null;

        var veiculo = rota.VeiculoId is { } veiculoId
            ? await contexto.Veiculos.AsNoTracking().Where(item => item.Id == veiculoId)
                .Select(item => new VeiculoDaRotaResumo(item.Id, item.Placa, item.Identificacao)).SingleOrDefaultAsync(cancelamento).ConfigureAwait(false)
            : null;

        return new RotaResumo(
            rota.Id,
            rota.Codigo,
            rota.Data,
            rota.Status,
            hub,
            motorista,
            veiculo,
            rota.SaidaPlanejada,
            rota.VersaoDaOrdem,
            [.. paradas.Select(parada =>
            {
                var linha = entregas[parada.EntregaId];
                return new ParadaResumo(
                    parada.Sequencia,
                    parada.EntregaId,
                    linha.Entrega.Codigo,
                    linha.Entrega.Status,
                    linha.DestinatarioNome,
                    EnderecoResumo.De(linha.Entrega.Endereco),
                    new JanelaResumo(linha.Entrega.Janela.Inicio, linha.Entrega.Janela.Fim));
            })],
            rota.CriadaEm,
            rota.AtualizadaEm,
            rota.PlanejadaEm,
            rota.CanceladaEm,
            rota.IniciadaEm,
            rota.ConcluidaEm,
            rota.Versao);
    }

    private async Task GarantirHubAtivoAsync(Guid hubId, CancellationToken cancelamento)
    {
        var hub = await suporte.Contexto.Hubs
            .AsNoTracking()
            .Where(item => item.Id == hubId)
            .Select(item => new { item.Ativo })
            .SingleOrDefaultAsync(cancelamento)
            .ConfigureAwait(false)
            ?? throw ExcecaoDeDominio.NaoEncontrado("hub_nao_encontrado", "Hub não encontrado.");

        ExcecaoDeDominio.LancarSe(!hub.Ativo, "hub_inativo", "Hub inativo não recebe nova rota.");
    }

    private async Task<Rota> CarregarAsync(Guid id, CancellationToken cancelamento) =>
        await suporte.Contexto.Rotas
            .Include(rota => rota.Paradas)
            .SingleOrDefaultAsync(rota => rota.Id == id, cancelamento)
            .ConfigureAwait(false)
        ?? throw RotaNaoEncontrada();

    private void ExigirVersao(Rota rota, uint versaoInformada)
    {
        if (rota.Versao != versaoInformada)
        {
            throw ConflitoDeVersao();
        }

        suporte.Contexto.DefinirVersaoEsperada(rota, versaoInformada);
    }

    private Task SalvarAsync(CancellationToken cancelamento) =>
        suporte.SalvarAsync(
            cancelamento,
            (NomesDeRestricoes.ParadaAtivaDaEntrega, EntregaEmOutraRota),
            (NomesDeRestricoes.MotoristaEmRotaAtivaNoDia, MotoristaJaEmRota),
            (NomesDeRestricoes.VeiculoEmRotaAtivaNoDia, VeiculoJaEmRota),
            (NomesDeRestricoes.SequenciaDoEventoDaRota, ConflitoDeVersao),
            (NomesDeRestricoes.SequenciaDoEventoDaEntrega, ConflitoDeVersao));

    private static ExcecaoDeDominio RotaNaoEncontrada() =>
        ExcecaoDeDominio.NaoEncontrado("rota_nao_encontrada", "Rota não encontrada.");

    private static ExcecaoDeDominio EntregaEmOutraRota() =>
        ExcecaoDeDominio.Conflito("entrega_em_outra_rota", "A entrega já está em outra rota ativa.");

    private static ExcecaoDeDominio MotoristaJaEmRota() =>
        ExcecaoDeDominio.Conflito("motorista_ja_em_rota", "O motorista já está em outra rota ativa neste dia.");

    private static ExcecaoDeDominio VeiculoJaEmRota() =>
        ExcecaoDeDominio.Conflito("veiculo_ja_em_rota", "O veículo já está em outra rota ativa neste dia.");

    private static ExcecaoDeDominio ConflitoDeVersao() =>
        ExcecaoDeDominio.Conflito("conflito_de_versao", "A rota foi alterada por outra pessoa. Recarregue e tente de novo.");

    private static EventoDaRotaResumo ParaResumo(EventoDaRota evento)
    {
        using var documento = JsonDocument.Parse(evento.Dados);

        return new EventoDaRotaResumo(
            evento.Sequencia, evento.Tipo, evento.StatusResultante, evento.OcorridoEm, evento.AutorUsuarioId, documento.RootElement.Clone());
    }

    private sealed class LinhaDeParada
    {
        public required Entrega Entrega { get; init; }

        public required string DestinatarioNome { get; init; }
    }
}

/// <summary>Ações de rota registradas na trilha de auditoria.</summary>
public static class AcoesDeRota
{
    /// <summary>Criação.</summary>
    public const string Criada = "criada";

    /// <summary>Motorista definido ou trocado.</summary>
    public const string MotoristaAtribuido = "motorista_atribuido";

    /// <summary>Veículo definido ou trocado.</summary>
    public const string VeiculoAtribuido = "veiculo_atribuido";

    /// <summary>Confirmação do planejamento.</summary>
    public const string Planejada = "planejada";

    /// <summary>Cancelamento.</summary>
    public const string Cancelada = "cancelada";
}
