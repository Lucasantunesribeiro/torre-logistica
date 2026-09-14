using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Application.Cadastros;
using TorreLogistica.Application.Entregas;
using TorreLogistica.Application.Rotas;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Abstracoes.Identificadores;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Frota;
using TorreLogistica.Domain.Rotas;

namespace TorreLogistica.Application.Execucao;

/// <summary>
/// Comandos do motorista sobre a própria rota e as próprias entregas: sair, chegar, concluir,
/// registrar tentativa sem sucesso e encerrar a rota.
/// </summary>
/// <remarks>
/// <para>
/// O motorista é resolvido pela conta da sessão (associação da Fase 2). Rota ou entrega que não
/// está com ele responde como inexistente — exceto a entrega que <b>já foi dele</b> e passou a
/// outro motorista: essa responde <c>409 entrega_reatribuida</c>, para o aplicativo mostrar o que
/// aconteceu em vez de um "não encontrado" sem explicação.
/// </para>
/// <para>
/// Todos os comandos são idempotentes pela máquina de estados: repetir um envio cujo resultado
/// já está aplicado responde 200 com o estado atual, sem novo evento. Concorrência com a operação
/// é decidida pela versão da linha: quem grava primeiro prevalece, o outro recebe 409.
/// </para>
/// </remarks>
public sealed class ExecucaoPeloMotorista(
    SuporteDeCadastro suporte,
    IGeradorDeIdentificador identificadores,
    GestaoDeRotas rotas,
    GestaoDeEntregas entregas,
    ILogger<ExecucaoPeloMotorista> log)
{
    /// <summary>Saída para a rota: rota em andamento e entregas atribuídas em rota.</summary>
    public async Task<RotaResumo> IniciarRotaAsync(Guid rotaId, CancellationToken cancelamento)
    {
        var contexto = suporte.Contexto;
        var motorista = await MotoristaDaSessaoAsync(cancelamento).ConfigureAwait(false);
        var rota = await RotaDoMotoristaAsync(rotaId, motorista, cancelamento).ConfigureAwait(false);
        var veiculo = rota.VeiculoId is { } veiculoId
            ? await contexto.Veiculos.AsNoTracking().SingleOrDefaultAsync(item => item.Id == veiculoId, cancelamento).ConfigureAwait(false)
            : null;

        var agora = suporte.Agora;
        if (rota.Iniciar(motorista, veiculo, identificadores, suporte.UsuarioId, agora) is { } evento)
        {
            var ids = rota.ObterParadasAtivas().Select(parada => parada.EntregaId).ToArray();
            var daRota = await contexto.Entregas
                .Where(entrega => ids.Contains(entrega.Id))
                .ToListAsync(cancelamento)
                .ConfigureAwait(false);

            foreach (var entrega in daRota)
            {
                if (entrega.IniciarRota(rota.Id, suporte.UsuarioId, identificadores.Novo(), agora) is { } saida)
                {
                    contexto.EventosDaEntrega.Add(saida);
                }
            }

            contexto.EventosDaRota.Add(evento);
            await SalvarAsync(cancelamento).ConfigureAwait(false);

            log.LogInformation("Rota {RotaId} iniciada pelo motorista {MotoristaId}.", rota.Id, motorista.Id);
        }

        return await rotas.ObterAsync(rotaId, cancelamento).ConfigureAwait(false);
    }

    /// <summary>Encerra a rota quando todas as entregas têm resultado.</summary>
    public async Task<RotaResumo> ConcluirRotaAsync(Guid rotaId, CancellationToken cancelamento)
    {
        var contexto = suporte.Contexto;
        var motorista = await MotoristaDaSessaoAsync(cancelamento).ConfigureAwait(false);
        var rota = await RotaDoMotoristaAsync(rotaId, motorista, cancelamento).ConfigureAwait(false);

        var ids = rota.ObterParadasAtivas().Select(parada => parada.EntregaId).ToArray();
        var statusDasEntregas = await contexto.Entregas
            .AsNoTracking()
            .Where(entrega => ids.Contains(entrega.Id))
            .ToDictionaryAsync(entrega => entrega.Id, entrega => entrega.Status, cancelamento)
            .ConfigureAwait(false);

        if (rota.Concluir(statusDasEntregas, identificadores, suporte.UsuarioId, suporte.Agora) is { } evento)
        {
            contexto.EventosDaRota.Add(evento);
            await SalvarAsync(cancelamento).ConfigureAwait(false);

            log.LogInformation("Rota {RotaId} concluída pelo motorista {MotoristaId}.", rota.Id, motorista.Id);
        }

        return await rotas.ObterAsync(rotaId, cancelamento).ConfigureAwait(false);
    }

    /// <summary>Chegada ao destino.</summary>
    public Task<EntregaResumo> RegistrarChegadaAsync(Guid entregaId, CancellationToken cancelamento) =>
        AplicarNaEntregaAsync(
            entregaId,
            (entrega, agora) => entrega.RegistrarChegada(suporte.UsuarioId, identificadores.Novo(), agora),
            cancelamento);

    /// <summary>Entrega feita.</summary>
    public Task<EntregaResumo> ConcluirEntregaAsync(Guid entregaId, CancellationToken cancelamento) =>
        AplicarNaEntregaAsync(
            entregaId,
            (entrega, agora) => entrega.Concluir(suporte.UsuarioId, identificadores.Novo(), agora),
            cancelamento);

    /// <summary>Tentativa sem sucesso.</summary>
    public Task<EntregaResumo> RegistrarTentativaFrustradaAsync(
        Guid entregaId,
        MotivoDeTentativaFrustrada motivo,
        CancellationToken cancelamento) =>
        AplicarNaEntregaAsync(
            entregaId,
            (entrega, agora) => entrega.RegistrarTentativaFrustrada(motivo, suporte.UsuarioId, identificadores.Novo(), agora),
            cancelamento);

    private async Task<EntregaResumo> AplicarNaEntregaAsync(
        Guid entregaId,
        Func<Entrega, DateTimeOffset, EventoDaEntrega?> comando,
        CancellationToken cancelamento)
    {
        var motorista = await MotoristaDaSessaoAsync(cancelamento).ConfigureAwait(false);
        var entrega = await EntregaDoMotoristaAsync(entregaId, motorista, cancelamento).ConfigureAwait(false);

        if (comando(entrega, suporte.Agora) is { } evento)
        {
            suporte.Contexto.EventosDaEntrega.Add(evento);
            await SalvarAsync(cancelamento).ConfigureAwait(false);

            log.LogInformation(
                "Entrega {EntregaId} ({CodigoDaEntrega}): {TipoDeEvento} pelo motorista {MotoristaId}.",
                entrega.Id, entrega.Codigo, evento.Tipo, motorista.Id);
        }

        return await entregas.ObterAsync(entregaId, cancelamento).ConfigureAwait(false);
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

    private async Task<Rota> RotaDoMotoristaAsync(Guid rotaId, Motorista motorista, CancellationToken cancelamento) =>
        await suporte.Contexto.Rotas
            .Include(rota => rota.Paradas)
            .SingleOrDefaultAsync(rota => rota.Id == rotaId && rota.MotoristaId == motorista.Id, cancelamento)
            .ConfigureAwait(false)
        ?? throw ExcecaoDeDominio.NaoEncontrado("rota_nao_encontrada", "Rota não encontrada.");

    private async Task<Entrega> EntregaDoMotoristaAsync(Guid entregaId, Motorista motorista, CancellationToken cancelamento)
    {
        var contexto = suporte.Contexto;
        var entrega = await contexto.Entregas
            .SingleOrDefaultAsync(item => item.Id == entregaId, cancelamento)
            .ConfigureAwait(false);

        if (entrega is not null && entrega.MotoristaId == motorista.Id)
        {
            return entrega;
        }

        if (entrega is not null
            && await contexto.MotoristaJaFoiAtribuidoAsync(entregaId, motorista.Id, cancelamento).ConfigureAwait(false))
        {
            throw ExcecaoDeDominio.Conflito(
                "entrega_reatribuida",
                "Esta entrega foi passada a outro motorista.");
        }

        throw ExcecaoDeDominio.NaoEncontrado("entrega_nao_encontrada", "Entrega não encontrada.");
    }

    private Task SalvarAsync(CancellationToken cancelamento) =>
        suporte.SalvarAsync(
            cancelamento,
            (NomesDeRestricoes.SequenciaDoEventoDaEntrega, ConflitoDeVersao),
            (NomesDeRestricoes.SequenciaDoEventoDaRota, ConflitoDeVersao));

    private static ExcecaoDeDominio ConflitoDeVersao() =>
        ExcecaoDeDominio.Conflito("conflito_de_versao", "O registro foi alterado ao mesmo tempo. Recarregue e tente de novo.");
}
