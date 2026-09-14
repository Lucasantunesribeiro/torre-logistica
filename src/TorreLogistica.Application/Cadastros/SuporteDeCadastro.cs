using Microsoft.EntityFrameworkCore;
using TorreLogistica.Application.Abstracoes.Identidade;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Application.Auditoria;
using TorreLogistica.Domain.Abstracoes.Cadastro;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Abstracoes.Identificadores;
using TorreLogistica.Domain.Abstracoes.Tempo;
using TorreLogistica.Domain.Auditoria;
using TorreLogistica.Domain.Comum;

namespace TorreLogistica.Application.Cadastros;

/// <summary>Filtro de listagem comum aos cadastros.</summary>
/// <param name="Pagina">Página, a partir de 1.</param>
/// <param name="TamanhoDaPagina">Itens por página.</param>
/// <param name="Ativo">Filtra por situação; <see langword="null"/> traz todos.</param>
/// <param name="Busca">Texto procurado no nome, sem diferença de acento ou caixa.</param>
public sealed record FiltroDeCadastro(int Pagina, int TamanhoDaPagina, bool? Ativo, string? Busca)
{
    /// <summary>Busca normalizada, ou <see langword="null"/> quando vazia.</summary>
    public string? BuscaNormalizada => string.IsNullOrWhiteSpace(Busca) ? null : TextoNormalizado.ParaBusca(Busca);
}

/// <summary>Endereço como recebido do cliente.</summary>
public sealed record DadosDeEndereco(
    string? Logradouro,
    string? Numero,
    string? Complemento,
    string? Bairro,
    string? Cidade,
    string? Uf,
    string? Cep)
{
    /// <summary>Valida e converte para o tipo de domínio.</summary>
    public Endereco ParaDominio() => Endereco.Criar(Logradouro, Numero, Complemento, Bairro, Cidade, Uf, Cep);
}

/// <summary>Endereço como devolvido pela API.</summary>
public sealed record EnderecoResumo(
    string Logradouro,
    string Numero,
    string? Complemento,
    string Bairro,
    string Cidade,
    string Uf,
    string Cep)
{
    /// <summary>Converte a partir do tipo de domínio.</summary>
    public static EnderecoResumo De(Endereco endereco)
    {
        ArgumentNullException.ThrowIfNull(endereco);
        return new(endereco.Logradouro, endereco.Numero, endereco.Complemento, endereco.Bairro, endereco.Cidade, endereco.Uf, endereco.Cep);
    }
}

/// <summary>Coordenada como devolvida pela API.</summary>
public sealed record CoordenadaResumo(double Latitude, double Longitude)
{
    /// <summary>Converte a partir do tipo de domínio.</summary>
    public static CoordenadaResumo? De(CoordenadaGeografica? coordenada) =>
        coordenada is null ? null : new(coordenada.Latitude, coordenada.Longitude);
}

/// <summary>
/// O que os cinco cadastros operacionais têm em comum: tenant, relógio, auditoria,
/// concorrência otimista e ciclo ativo/inativo.
/// </summary>
/// <remarks>
/// Não é repositório genérico: não esconde consulta nenhuma. Cada serviço de cadastro
/// continua escrevendo o próprio LINQ; aqui fica só o que seria copiado cinco vezes e que,
/// copiado, divergiria — em especial a tradução de conflito de versão e de unicidade.
/// </remarks>
public sealed class SuporteDeCadastro(
    IContextoDePersistencia contexto,
    IContextoDoUsuario usuarioAtual,
    IGeradorDeIdentificador identificadores,
    IRelogio relogio)
{
    /// <summary>Contexto de persistência, já filtrado pelo tenant.</summary>
    public IContextoDePersistencia Contexto => contexto;

    /// <summary>Organização da sessão.</summary>
    public Guid OrganizacaoId => usuarioAtual.OrganizacaoIdAutenticada;

    /// <summary>Conta autenticada que executa a ação.</summary>
    public Guid UsuarioId => usuarioAtual.UsuarioId;

    /// <summary>Instante atual.</summary>
    public DateTimeOffset Agora => relogio.AgoraUtc;

    /// <summary>Novo identificador UUIDv7.</summary>
    public Guid NovoIdentificador() => identificadores.Novo();

    /// <summary>Registra um evento de cadastro na trilha de auditoria.</summary>
    public void Auditar(string recurso, string acao, Guid alvoId, object dados) =>
        contexto.EventosDeAuditoria.Add(RegistroDeAuditoria.Criar(
            identificadores,
            relogio.AgoraUtc,
            usuarioAtual.OrganizacaoIdAutenticada,
            TiposDeEventoDeAuditoria.DeCadastro(recurso, acao),
            usuarioAtual.UsuarioId,
            recurso,
            alvoId,
            dados));

    /// <summary>Carrega um cadastro do tenant para alteração, ou lança 404.</summary>
    public static async Task<T> CarregarAsync<T>(
        IQueryable<T> conjunto,
        Guid id,
        string codigoNaoEncontrado,
        string mensagemNaoEncontrado,
        CancellationToken cancelamento)
        where T : class, IRecursoAtivavel =>
        await conjunto
            .SingleOrDefaultAsync(item => item.Id == id, cancelamento)
            .ConfigureAwait(false)
        ?? throw ExcecaoDeDominio.NaoEncontrado(codigoNaoEncontrado, mensagemNaoEncontrado);

    /// <summary>
    /// Confere a versão que o cliente leu e a registra para a gravação.
    /// </summary>
    /// <remarks>
    /// Duas camadas. A comparação imediata cobre quem chega com versão antiga; a versão
    /// esperada registrada no contexto cobre quem leu a mesma versão que outra requisição
    /// e perdeu a corrida entre a leitura e a gravação.
    /// </remarks>
    public void ExigirVersao(IRecursoAtivavel entidade, uint versaoInformada)
    {
        ArgumentNullException.ThrowIfNull(entidade);

        if (entidade.Versao != versaoInformada)
        {
            throw ConflitoDeVersao();
        }

        contexto.DefinirVersaoEsperada(entidade, versaoInformada);
    }

    /// <summary>
    /// Grava, traduzindo conflito de versão e violação de unicidade em erro de domínio.
    /// </summary>
    public async Task SalvarAsync(
        CancellationToken cancelamento,
        params (string Restricao, Func<ExcecaoDeDominio> Erro)[] unicidades)
    {
        try
        {
            await contexto.SaveChangesAsync(cancelamento).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException excecao)
        {
            throw new ExcecaoDeDominio(
                "conflito_de_versao",
                "O registro foi alterado por outra pessoa. Recarregue e tente de novo.",
                CategoriaDeErroDeDominio.Conflito,
                excecao);
        }
        catch (DbUpdateException excecao)
        {
            foreach (var (restricao, erro) in unicidades)
            {
                if (contexto.EhViolacaoDeUnicidade(excecao, restricao))
                {
                    throw erro();
                }
            }

            throw;
        }
    }

    /// <summary>Ativa ou inativa um cadastro. Repetir não gera efeito nem auditoria.</summary>
    public async Task<T> AlterarSituacaoAsync<T>(
        IQueryable<T> conjunto,
        Guid id,
        bool ativar,
        string recurso,
        string codigoNaoEncontrado,
        string mensagemNaoEncontrado,
        CancellationToken cancelamento)
        where T : class, IRecursoAtivavel
    {
        var entidade = await CarregarAsync(conjunto, id, codigoNaoEncontrado, mensagemNaoEncontrado, cancelamento)
            .ConfigureAwait(false);

        var mudou = ativar ? entidade.Ativar(Agora) : entidade.Inativar(Agora);

        if (mudou)
        {
            Auditar(recurso, ativar ? AcoesDeCadastro.Ativado : AcoesDeCadastro.Inativado, entidade.Id, new { });
            await SalvarAsync(cancelamento).ConfigureAwait(false);
        }

        return entidade;
    }

    /// <summary>Página de uma consulta já filtrada e ordenada.</summary>
    public static async Task<(IReadOnlyList<T> Itens, int Total)> PaginarAsync<T>(
        IQueryable<T> consulta,
        FiltroDeCadastro filtro,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(filtro);

        var total = await consulta.CountAsync(cancelamento).ConfigureAwait(false);
        var itens = await consulta
            .Skip((filtro.Pagina - 1) * filtro.TamanhoDaPagina)
            .Take(filtro.TamanhoDaPagina)
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

        return (itens, total);
    }

    private static ExcecaoDeDominio ConflitoDeVersao() =>
        ExcecaoDeDominio.Conflito(
            "conflito_de_versao",
            "O cadastro foi alterado por outra pessoa. Recarregue e tente de novo.");
}

/// <summary>Ações registradas na trilha de auditoria dos cadastros.</summary>
public static class AcoesDeCadastro
{
    /// <summary>Criação.</summary>
    public const string Criado = "criado";

    /// <summary>Alteração de dados.</summary>
    public const string Alterado = "alterado";

    /// <summary>Ativação.</summary>
    public const string Ativado = "ativado";

    /// <summary>Inativação.</summary>
    public const string Inativado = "inativado";

    /// <summary>Conta de acesso associada ao motorista.</summary>
    public const string ContaAssociada = "conta_associada";

    /// <summary>Conta de acesso desassociada do motorista.</summary>
    public const string ContaDesassociada = "conta_desassociada";
}
