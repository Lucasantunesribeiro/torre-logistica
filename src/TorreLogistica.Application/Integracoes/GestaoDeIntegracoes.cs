using Microsoft.EntityFrameworkCore;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Application.Cadastros;
using TorreLogistica.Application.Comum;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Integracoes;

namespace TorreLogistica.Application.Integracoes;

/// <summary>Credencial recém-emitida. A chave aparece aqui e em nenhum outro lugar.</summary>
public sealed record IntegracaoEmitida(Guid Id, string Nome, string Chave, DateTimeOffset CriadaEm);

/// <summary>Credencial como devolvida pela API. Nunca inclui a chave nem o hash.</summary>
public sealed record IntegracaoResumo(
    Guid Id,
    string Nome,
    string IdentificadorPublico,
    bool Ativa,
    DateTimeOffset CriadaEm,
    DateTimeOffset? RevogadaEm,
    DateTimeOffset? UltimoUsoEm);

/// <summary>
/// Credenciais de integração, administradas pelo console.
/// </summary>
/// <remarks>
/// A chave só existe no momento da emissão: o banco guarda o identificador público e o SHA-256 do
/// segredo. Credencial perdida é revogada e substituída — nunca recuperada.
/// </remarks>
public sealed class GestaoDeIntegracoes(SuporteDeCadastro suporte)
{
    private const string Recurso = "integracao";

    /// <summary>Emite uma credencial e devolve a chave, uma única vez.</summary>
    public async Task<IntegracaoEmitida> EmitirAsync(string? nome, CancellationToken cancelamento)
    {
        var emitida = SegredosDeIntegracao.Gerar();

        var integracao = Integracao.Emitir(
            suporte.NovoIdentificador(),
            suporte.OrganizacaoId,
            nome,
            emitida.IdentificadorPublico,
            emitida.HashDoSegredo,
            suporte.AutorUsuarioId,
            suporte.Agora);

        suporte.Contexto.Integracoes.Add(integracao);

        // Auditoria registra o ato e o identificador público — nunca a chave, que é o segredo.
        suporte.Auditar(Recurso, "emitida", integracao.Id, new
        {
            integracao.Nome,
            integracao.IdentificadorPublico,
        });

        await suporte.SalvarAsync(
            cancelamento,
            (NomesDeRestricoes.IdentificadorPublicoDaIntegracao,
                () => ExcecaoDeDominio.Conflito("integracao_em_emissao", "Não foi possível emitir a credencial. Tente de novo.")))
            .ConfigureAwait(false);

        return new IntegracaoEmitida(integracao.Id, integracao.Nome, emitida.Chave, integracao.CriadaEm);
    }

    /// <summary>Lista as credenciais da organização, das mais novas para as mais antigas.</summary>
    public async Task<PaginaDeResultados<IntegracaoResumo>> ListarAsync(
        int pagina,
        int tamanhoDaPagina,
        CancellationToken cancelamento)
    {
        var consulta = suporte.Contexto.Integracoes.AsNoTracking();

        var total = await consulta.CountAsync(cancelamento).ConfigureAwait(false);

        var itens = await consulta
            .OrderByDescending(integracao => integracao.CriadaEm)
            .ThenBy(integracao => integracao.Id)
            .Skip((pagina - 1) * tamanhoDaPagina)
            .Take(tamanhoDaPagina)
            .Select(integracao => new IntegracaoResumo(
                integracao.Id,
                integracao.Nome,
                integracao.IdentificadorPublico,
                integracao.RevogadaEm == null,
                integracao.CriadaEm,
                integracao.RevogadaEm,
                integracao.UltimoUsoEm))
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

        return new PaginaDeResultados<IntegracaoResumo>(itens, pagina, tamanhoDaPagina, total);
    }

    /// <summary>Uma credencial da organização.</summary>
    public async Task<IntegracaoResumo> ObterAsync(Guid id, CancellationToken cancelamento) =>
        await suporte.Contexto.Integracoes
            .AsNoTracking()
            .Where(integracao => integracao.Id == id)
            .Select(integracao => new IntegracaoResumo(
                integracao.Id,
                integracao.Nome,
                integracao.IdentificadorPublico,
                integracao.RevogadaEm == null,
                integracao.CriadaEm,
                integracao.RevogadaEm,
                integracao.UltimoUsoEm))
            .SingleOrDefaultAsync(cancelamento)
            .ConfigureAwait(false)
        ?? throw NaoEncontrada();

    /// <summary>Revoga a credencial. Revogar de novo devolve o mesmo estado.</summary>
    public async Task<IntegracaoResumo> RevogarAsync(Guid id, CancellationToken cancelamento)
    {
        var integracao = await suporte.Contexto.Integracoes
            .SingleOrDefaultAsync(item => item.Id == id, cancelamento)
            .ConfigureAwait(false)
            ?? throw NaoEncontrada();

        if (integracao.EstaAtiva)
        {
            integracao.Revogar(suporte.AutorUsuarioId, suporte.Agora);
            suporte.Auditar(Recurso, "revogada", integracao.Id, new { integracao.Nome, integracao.IdentificadorPublico });
            await suporte.SalvarAsync(cancelamento).ConfigureAwait(false);
        }

        return await ObterAsync(id, cancelamento).ConfigureAwait(false);
    }

    private static ExcecaoDeDominio NaoEncontrada() =>
        ExcecaoDeDominio.NaoEncontrado("integracao_nao_encontrada", "Credencial de integração não encontrada.");
}
