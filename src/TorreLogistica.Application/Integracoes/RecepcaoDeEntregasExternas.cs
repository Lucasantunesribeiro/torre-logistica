using Microsoft.EntityFrameworkCore;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Application.Cadastros;
using TorreLogistica.Application.Entregas;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Integracoes;

namespace TorreLogistica.Application.Integracoes;

/// <summary>Entrega recebida de um sistema externo.</summary>
/// <param name="Entrega">A entrega, nova ou a que já existia.</param>
/// <param name="JaExistia">
/// A requisição não criou nada: era reenvio da mesma chave, ou o mesmo pedido de origem já tinha entrega.
/// </param>
public sealed record RecebimentoDeEntrega(EntregaResumo Entrega, bool JaExistia);

/// <summary>
/// Criação de entregas por sistema externo, com efeito exatamente uma vez.
/// </summary>
/// <remarks>
/// <para>
/// Duas guardas, com propósitos diferentes. A <b>chave de idempotência</b> protege contra o reenvio do
/// mesmo pedido — resposta perdida, timeout, retentativa automática — e por isso guarda também o hash do
/// corpo: a mesma chave com corpo diferente é defeito do cliente, e responder o resultado antigo o
/// esconderia. O <b>identificador de origem</b> protege contra o mesmo pedido chegar duas vezes com chaves
/// diferentes, que é o caso do ERP que reprocessa uma fila.
/// </para>
/// <para>
/// Os dois registros são gravados no mesmo commit da entrega, pelo gancho de
/// <see cref="GestaoDeEntregas.CriarAsync"/>: entrega criada sem registro de idempotência seria duplicada
/// no próximo reenvio.
/// </para>
/// </remarks>
public sealed class RecepcaoDeEntregasExternas(SuporteDeCadastro suporte, GestaoDeEntregas entregas)
{
    private const string RecursoDaRequisicao = "entrega";

    /// <summary>Recebe uma entrega de um sistema externo.</summary>
    /// <param name="integracaoId">Credencial que autenticou a requisição.</param>
    /// <param name="chaveDeIdempotencia">Valor do cabeçalho <c>Idempotency-Key</c>.</param>
    /// <param name="hashDoCorpo">SHA-256 do corpo recebido.</param>
    /// <param name="identificadorExterno">Identificador da entrega no sistema de origem, quando houver.</param>
    /// <param name="dados">Dados da entrega.</param>
    /// <param name="cancelamento">Cancelamento.</param>
    public async Task<RecebimentoDeEntrega> ReceberAsync(
        Guid integracaoId,
        string? chaveDeIdempotencia,
        byte[] hashDoCorpo,
        string? identificadorExterno,
        DadosDeEntrega dados,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(hashDoCorpo);
        ArgumentNullException.ThrowIfNull(dados);

        ExcecaoDeDominio.LancarSe(
            string.IsNullOrWhiteSpace(chaveDeIdempotencia),
            "chave_de_idempotencia_obrigatoria",
            "Informe o cabeçalho Idempotency-Key.");

        ExcecaoDeDominio.LancarSe(
            chaveDeIdempotencia!.Length > PoliticaDeIntegracao.TamanhoMaximoDaChaveDeIdempotencia,
            "chave_de_idempotencia_invalida",
            $"A chave de idempotência passa de {PoliticaDeIntegracao.TamanhoMaximoDaChaveDeIdempotencia} caracteres.");

        if (await ResultadoAnteriorAsync(integracaoId, chaveDeIdempotencia, hashDoCorpo, cancelamento).ConfigureAwait(false)
            is { } jaProcessada)
        {
            return jaProcessada;
        }

        if (identificadorExterno is not null
            && await EntregaDaOrigemAsync(integracaoId, identificadorExterno, cancelamento).ConfigureAwait(false)
            is { } jaRecebida)
        {
            return jaRecebida;
        }

        try
        {
            var entrega = await entregas
                .CriarAsync(dados, cancelamento, (entregaId, cancelamentoDaTentativa) =>
                {
                    Registrar(integracaoId, chaveDeIdempotencia, hashDoCorpo, identificadorExterno, entregaId);
                    return Task.CompletedTask;
                })
                .ConfigureAwait(false);

            return new RecebimentoDeEntrega(entrega, JaExistia: false);
        }
        catch (DbUpdateException excecao) when (
            suporte.Contexto.EhViolacaoDeUnicidade(excecao, NomesDeRestricoes.ChaveDeIdempotenciaPorIntegracao)
            || suporte.Contexto.EhViolacaoDeUnicidade(excecao, NomesDeRestricoes.ReferenciaExternaPorIntegracao))
        {
            // Duas cópias do mesmo pedido chegaram juntas: uma gravou, a outra bateu no índice. Quem
            // perdeu a corrida devolve o resultado de quem ganhou, em vez de um erro que o ERP não saberia
            // interpretar.
            var vencedora = await ResultadoAnteriorAsync(integracaoId, chaveDeIdempotencia, hashDoCorpo, cancelamento)
                .ConfigureAwait(false);

            if (vencedora is null && identificadorExterno is not null)
            {
                vencedora = await EntregaDaOrigemAsync(integracaoId, identificadorExterno, cancelamento).ConfigureAwait(false);
            }

            if (vencedora is null)
            {
                // Bateu num índice único que não é destes dois, ou a gravação vencedora ainda não está
                // visível: erro de verdade, e esconder seria pior.
                throw;
            }

            return vencedora;
        }
    }

    private void Registrar(
        Guid integracaoId,
        string chaveDeIdempotencia,
        byte[] hashDoCorpo,
        string? identificadorExterno,
        Guid entregaId)
    {
        suporte.Contexto.RequisicoesDeIntegracao.Add(RequisicaoDeIntegracao.Registrar(
            suporte.NovoIdentificador(),
            suporte.OrganizacaoId,
            integracaoId,
            chaveDeIdempotencia,
            hashDoCorpo,
            RecursoDaRequisicao,
            entregaId,
            suporte.Agora));

        if (identificadorExterno is not null)
        {
            suporte.Contexto.ReferenciasExternasDeEntrega.Add(ReferenciaExternaDaEntrega.Registrar(
                suporte.NovoIdentificador(),
                suporte.OrganizacaoId,
                integracaoId,
                entregaId,
                identificadorExterno,
                suporte.Agora));
        }
    }

    private async Task<RecebimentoDeEntrega?> ResultadoAnteriorAsync(
        Guid integracaoId,
        string chave,
        byte[] hashDoCorpo,
        CancellationToken cancelamento)
    {
        var anterior = await suporte.Contexto.RequisicoesDeIntegracao
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.IntegracaoId == integracaoId && item.Chave == chave,
                cancelamento)
            .ConfigureAwait(false);

        if (anterior is null)
        {
            return null;
        }

        // Mesma chave, corpo diferente: o cliente reaproveitou a chave. Devolver o resultado antigo
        // esconderia o defeito e faria o pedido novo desaparecer em silêncio.
        if (!anterior.HashDaRequisicao.AsSpan().SequenceEqual(hashDoCorpo))
        {
            throw ExcecaoDeDominio.Conflito(
                "chave_de_idempotencia_reutilizada",
                "Esta chave de idempotência já foi usada com outro conteúdo.");
        }

        return new RecebimentoDeEntrega(
            await entregas.ObterAsync(anterior.RecursoId, cancelamento).ConfigureAwait(false),
            JaExistia: true);
    }

    private async Task<RecebimentoDeEntrega?> EntregaDaOrigemAsync(
        Guid integracaoId,
        string identificadorExterno,
        CancellationToken cancelamento)
    {
        var referencia = await suporte.Contexto.ReferenciasExternasDeEntrega
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.IntegracaoId == integracaoId && item.IdentificadorExterno == identificadorExterno,
                cancelamento)
            .ConfigureAwait(false);

        return referencia is null
            ? null
            : new RecebimentoDeEntrega(
                await entregas.ObterAsync(referencia.EntregaId, cancelamento).ConfigureAwait(false),
                JaExistia: true);
    }
}
