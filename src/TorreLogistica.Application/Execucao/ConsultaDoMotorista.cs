using Microsoft.EntityFrameworkCore;
using TorreLogistica.Application.Cadastros;
using TorreLogistica.Application.Entregas;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Frota;
using TorreLogistica.Domain.Rotas;

namespace TorreLogistica.Application.Execucao;

/// <summary>Rota na lista do motorista.</summary>
public sealed record RotaDoMotoristaItem(
    Guid Id,
    string Codigo,
    DateOnly Data,
    StatusDaRota Status,
    DateTimeOffset? SaidaPlanejada,
    DateTimeOffset? IniciadaEm,
    int TotalDeParadas,
    int ParadasPendentes);

/// <summary>Veículo, como o motorista precisa reconhecê-lo.</summary>
public sealed record VeiculoDoMotoristaResumo(string Placa, string Identificacao);

/// <summary>Parada, como o motorista vê na lista.</summary>
public sealed record ParadaDoMotoristaResumo(
    int Sequencia,
    Guid EntregaId,
    string CodigoDaEntrega,
    StatusDaEntrega Status,
    string DestinatarioNome,
    EnderecoResumo Endereco,
    JanelaResumo JanelaPrometida);

/// <summary>Rota do motorista, com as paradas em ordem.</summary>
public sealed record RotaDoMotoristaResumo(
    Guid Id,
    string Codigo,
    DateOnly Data,
    StatusDaRota Status,
    DateTimeOffset? SaidaPlanejada,
    DateTimeOffset? IniciadaEm,
    DateTimeOffset? ConcluidaEm,
    string? HubNome,
    VeiculoDoMotoristaResumo? Veiculo,
    IReadOnlyList<ParadaDoMotoristaResumo> Paradas);

/// <summary>Quem recebe, com o necessário para entregar.</summary>
public sealed record DestinatarioDoMotoristaResumo(string Nome, string? Telefone, string? InstrucoesDeEntrega);

/// <summary>Execução da entrega, do ponto de vista do motorista.</summary>
public sealed record ExecucaoDoMotoristaResumo(
    DateTimeOffset? SaiuParaRotaEm,
    DateTimeOffset? ChegadaRegistradaEm,
    DateTimeOffset? EntregueEm,
    int TentativasFrustradas,
    MotivoDeTentativaFrustrada? MotivoDaUltimaTentativa,
    DateTimeOffset? UltimaTentativaFrustradaEm);

/// <summary>Entrega do motorista.</summary>
public sealed record EntregaDoMotoristaResumo(
    Guid Id,
    string Codigo,
    StatusDaEntrega Status,
    Guid? RotaId,
    int? Sequencia,
    DestinatarioDoMotoristaResumo Destinatario,
    EnderecoResumo Endereco,
    CoordenadaResumo? Localizacao,
    JanelaResumo JanelaPrometida,
    string? Observacoes,
    ExecucaoDoMotoristaResumo Execucao);

/// <summary>
/// O que o motorista lê no aplicativo: as próprias rotas e as próprias entregas.
/// </summary>
/// <remarks>
/// <para>
/// Leitura separada da do console, com modelo próprio: o motorista vê o necessário para executar — ordem das
/// paradas, endereço, janela, contato e instruções do destinatário da entrega dele — e nada de cliente,
/// auditoria, versão ou dados de outras rotas. O motorista é resolvido pela conta da sessão; o que não é
/// dele responde como inexistente.
/// </para>
/// <para>
/// Contato e instruções só aparecem no detalhe da entrega, não na lista da rota: quem abre o detalhe está
/// indo fazer aquela entrega.
/// </para>
/// </remarks>
public sealed class ConsultaDoMotorista(SuporteDeCadastro suporte)
{
    private static readonly StatusDaRota[] Visiveis = [StatusDaRota.Planejada, StatusDaRota.EmAndamento];

    /// <summary>Rotas ativas do motorista: a em andamento primeiro, depois as planejadas pela data.</summary>
    public async Task<IReadOnlyList<RotaDoMotoristaItem>> ListarRotasAsync(CancellationToken cancelamento)
    {
        var motorista = await MotoristaDaSessaoAsync(cancelamento).ConfigureAwait(false);
        var contexto = suporte.Contexto;

        var rotas = await contexto.Rotas
            .AsNoTracking()
            .Include(rota => rota.Paradas.Where(parada => parada.Ativa))
            .Where(rota => rota.MotoristaId == motorista.Id && Visiveis.Contains(rota.Status))
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

        var ids = rotas.SelectMany(rota => rota.Paradas).Select(parada => parada.EntregaId).ToArray();
        var status = await contexto.Entregas
            .AsNoTracking()
            .Where(entrega => ids.Contains(entrega.Id))
            .ToDictionaryAsync(entrega => entrega.Id, entrega => entrega.Status, cancelamento)
            .ConfigureAwait(false);

        return
        [
            .. rotas
                .OrderByDescending(rota => rota.Status == StatusDaRota.EmAndamento)
                .ThenBy(rota => rota.Data)
                .ThenBy(rota => rota.SaidaPlanejada)
                .Select(rota => new RotaDoMotoristaItem(
                    rota.Id,
                    rota.Codigo,
                    rota.Data,
                    rota.Status,
                    rota.SaidaPlanejada,
                    rota.IniciadaEm,
                    rota.Paradas.Count,
                    rota.Paradas.Count(parada => !status.TryGetValue(parada.EntregaId, out var atual) || !RegrasDaEntrega.EstaResolvidaNaRota(atual)))),
        ];
    }

    /// <summary>Rota do motorista com as paradas em ordem.</summary>
    public async Task<RotaDoMotoristaResumo> ObterRotaAsync(Guid rotaId, CancellationToken cancelamento)
    {
        var motorista = await MotoristaDaSessaoAsync(cancelamento).ConfigureAwait(false);
        var contexto = suporte.Contexto;

        var rota = await contexto.Rotas
            .AsNoTracking()
            .Include(item => item.Paradas.Where(parada => parada.Ativa))
            .SingleOrDefaultAsync(item => item.Id == rotaId && item.MotoristaId == motorista.Id, cancelamento)
            .ConfigureAwait(false)
            ?? throw ExcecaoDeDominio.NaoEncontrado("rota_nao_encontrada", "Rota não encontrada.");

        var ids = rota.Paradas.Select(parada => parada.EntregaId).ToArray();
        var entregas = await (
                from entrega in contexto.Entregas.AsNoTracking()
                join destinatario in contexto.Destinatarios.AsNoTracking() on entrega.DestinatarioId equals destinatario.Id
                where ids.Contains(entrega.Id)
                select new { Entrega = entrega, DestinatarioNome = destinatario.Nome })
            .ToDictionaryAsync(linha => linha.Entrega.Id, cancelamento)
            .ConfigureAwait(false);

        var hubNome = rota.HubId is { } hubId
            ? await contexto.Hubs.AsNoTracking().Where(hub => hub.Id == hubId).Select(hub => hub.Nome).SingleOrDefaultAsync(cancelamento).ConfigureAwait(false)
            : null;

        var veiculo = rota.VeiculoId is { } veiculoId
            ? await contexto.Veiculos.AsNoTracking().Where(item => item.Id == veiculoId)
                .Select(item => new VeiculoDoMotoristaResumo(item.Placa, item.Identificacao))
                .SingleOrDefaultAsync(cancelamento)
                .ConfigureAwait(false)
            : null;

        return new RotaDoMotoristaResumo(
            rota.Id,
            rota.Codigo,
            rota.Data,
            rota.Status,
            rota.SaidaPlanejada,
            rota.IniciadaEm,
            rota.ConcluidaEm,
            hubNome,
            veiculo,
            [
                .. rota.ObterParadasAtivas().Select(parada =>
                {
                    var linha = entregas[parada.EntregaId];
                    return new ParadaDoMotoristaResumo(
                        parada.Sequencia,
                        parada.EntregaId,
                        linha.Entrega.Codigo,
                        linha.Entrega.Status,
                        linha.DestinatarioNome,
                        EnderecoResumo.De(linha.Entrega.Endereco),
                        new JanelaResumo(linha.Entrega.Janela.Inicio, linha.Entrega.Janela.Fim));
                }),
            ]);
    }

    /// <summary>
    /// Entrega do motorista. A que já foi dele e passou a outro responde <c>409 entrega_reatribuida</c>, para o
    /// aplicativo explicar o que aconteceu; a que nunca foi dele, como inexistente.
    /// </summary>
    public async Task<EntregaDoMotoristaResumo> ObterEntregaAsync(Guid entregaId, CancellationToken cancelamento)
    {
        var motorista = await MotoristaDaSessaoAsync(cancelamento).ConfigureAwait(false);
        var contexto = suporte.Contexto;

        var linha = await (
                from entrega in contexto.Entregas.AsNoTracking()
                join destinatario in contexto.Destinatarios.AsNoTracking() on entrega.DestinatarioId equals destinatario.Id
                where entrega.Id == entregaId
                select new { Entrega = entrega, destinatario.Nome, destinatario.Telefone, destinatario.InstrucoesDeEntrega })
            .SingleOrDefaultAsync(cancelamento)
            .ConfigureAwait(false);

        if (linha is null || linha.Entrega.MotoristaId != motorista.Id)
        {
            if (linha is not null && await contexto.MotoristaJaFoiAtribuidoAsync(entregaId, motorista.Id, cancelamento).ConfigureAwait(false))
            {
                throw ExcecaoDeDominio.Conflito("entrega_reatribuida", "Esta entrega foi passada a outro motorista.");
            }

            throw ExcecaoDeDominio.NaoEncontrado("entrega_nao_encontrada", "Entrega não encontrada.");
        }

        var parada = await contexto.Paradas
            .AsNoTracking()
            .Where(item => item.EntregaId == entregaId && item.Ativa)
            .Select(item => new { item.RotaId, item.Sequencia })
            .SingleOrDefaultAsync(cancelamento)
            .ConfigureAwait(false);

        var entregaDoMotorista = linha.Entrega;

        return new EntregaDoMotoristaResumo(
            entregaDoMotorista.Id,
            entregaDoMotorista.Codigo,
            entregaDoMotorista.Status,
            parada?.RotaId,
            parada?.Sequencia,
            new DestinatarioDoMotoristaResumo(linha.Nome, linha.Telefone, linha.InstrucoesDeEntrega),
            EnderecoResumo.De(entregaDoMotorista.Endereco),
            CoordenadaResumo.De(entregaDoMotorista.Localizacao),
            new JanelaResumo(entregaDoMotorista.Janela.Inicio, entregaDoMotorista.Janela.Fim),
            entregaDoMotorista.Observacoes,
            new ExecucaoDoMotoristaResumo(
                entregaDoMotorista.SaiuParaRotaEm,
                entregaDoMotorista.ChegadaRegistradaEm,
                entregaDoMotorista.EntregueEm,
                entregaDoMotorista.TentativasFrustradas,
                entregaDoMotorista.MotivoDaUltimaTentativa,
                entregaDoMotorista.UltimaTentativaFrustradaEm));
    }

    private async Task<Motorista> MotoristaDaSessaoAsync(CancellationToken cancelamento)
    {
        var usuarioId = suporte.UsuarioId;

        return await suporte.Contexto.Motoristas
            .AsNoTracking()
            .SingleOrDefaultAsync(motorista => motorista.UsuarioId == usuarioId, cancelamento)
            .ConfigureAwait(false)
            ?? throw ExcecaoDeDominio.NaoEncontrado("motorista_nao_associado", "Esta conta não está associada a um motorista.");
    }
}
