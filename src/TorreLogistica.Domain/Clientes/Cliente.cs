using TorreLogistica.Domain.Abstracoes.Cadastro;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Comum;

namespace TorreLogistica.Domain.Clientes;

/// <summary>
/// Cliente: a empresa contratante, origem operacional das entregas.
/// </summary>
/// <remarks>
/// Deliberadamente pequeno. A Torre Logística não é CRM (CLAUDE.md, seção 5): contato
/// comercial, contrato e histórico de negociação não pertencem a este cadastro.
/// </remarks>
public sealed class Cliente : IRecursoAtivavel
{
    /// <summary>Tamanho máximo do nome.</summary>
    public const int TamanhoMaximoDoNome = 150;

    private Cliente()
    {
        Nome = string.Empty;
        NomeNormalizado = string.Empty;
    }

    /// <inheritdoc />
    public Guid Id { get; private set; }

    /// <inheritdoc />
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Nome da empresa.</summary>
    public string Nome { get; private set; }

    /// <summary>Nome normalizado, para busca.</summary>
    public string NomeNormalizado { get; private set; }

    /// <summary>CNPJ normalizado, quando informado. Único por organização.</summary>
    public string? Cnpj { get; private set; }

    /// <inheritdoc />
    public bool Ativo { get; private set; }

    /// <summary>Instante de criação.</summary>
    public DateTimeOffset CriadoEm { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset AtualizadoEm { get; private set; }

    /// <inheritdoc />
    public uint Versao { get; private set; }

    /// <summary>Cadastra um cliente ativo.</summary>
    public static Cliente Criar(Guid id, Guid organizacaoId, string? nome, Cnpj? cnpj, DateTimeOffset agora)
    {
        ExcecaoDeDominio.LancarSe(id == Guid.Empty, "identificador_invalido", "Identificador inválido.");
        ExcecaoDeDominio.LancarSe(organizacaoId == Guid.Empty, "organizacao_invalida", "Organização inválida.");

        var nomeValido = TextoNormalizado.Obrigatorio(nome, TamanhoMaximoDoNome, "nome_invalido", "O nome");
        var instante = agora.ToUniversalTime();

        return new Cliente
        {
            Id = id,
            OrganizacaoId = organizacaoId,
            Nome = nomeValido,
            NomeNormalizado = TextoNormalizado.ParaBusca(nomeValido),
            Cnpj = cnpj?.Valor,
            Ativo = true,
            CriadoEm = instante,
            AtualizadoEm = instante,
        };
    }

    /// <summary>Atualiza os dados.</summary>
    /// <returns>Nomes dos campos que mudaram.</returns>
    public IReadOnlyList<string> AtualizarDados(string? nome, Cnpj? cnpj, DateTimeOffset agora)
    {
        var nomeValido = TextoNormalizado.Obrigatorio(nome, TamanhoMaximoDoNome, "nome_invalido", "O nome");
        var alteracoes = new RegistroDeAlteracoes();

        alteracoes.Aplicar("nome", Nome, nomeValido, valor =>
        {
            Nome = valor;
            NomeNormalizado = TextoNormalizado.ParaBusca(valor);
        });
        alteracoes.Aplicar("cnpj", Cnpj, cnpj?.Valor, valor => Cnpj = valor);

        if (alteracoes.HouveAlteracao)
        {
            AtualizadoEm = agora.ToUniversalTime();
        }

        return alteracoes.Campos;
    }

    /// <inheritdoc />
    public bool Ativar(DateTimeOffset agora) => MudarSituacao(true, agora);

    /// <inheritdoc />
    public bool Inativar(DateTimeOffset agora) => MudarSituacao(false, agora);

    private bool MudarSituacao(bool ativo, DateTimeOffset agora)
    {
        if (Ativo == ativo)
        {
            return false;
        }

        Ativo = ativo;
        AtualizadoEm = agora.ToUniversalTime();
        return true;
    }
}
