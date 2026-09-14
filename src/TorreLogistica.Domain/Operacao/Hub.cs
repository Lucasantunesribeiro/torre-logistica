using TorreLogistica.Domain.Abstracoes.Cadastro;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Comum;

namespace TorreLogistica.Domain.Operacao;

/// <summary>
/// Hub: centro de distribuição de onde as rotas saem.
/// </summary>
/// <remarks>
/// A localização é obrigatória. Hub sem coordenada não serve de origem para cálculo de
/// distância, ETA ou geofence — e essas são as razões de o hub existir no sistema.
/// </remarks>
public sealed class Hub : IRecursoAtivavel
{
    /// <summary>Tamanho máximo do nome.</summary>
    public const int TamanhoMaximoDoNome = 80;

    private Hub()
    {
        Nome = string.Empty;
        NomeNormalizado = string.Empty;
        Endereco = null!;
        Localizacao = null!;
    }

    /// <inheritdoc />
    public Guid Id { get; private set; }

    /// <inheritdoc />
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Nome. Único por organização, sem diferença de acento ou caixa.</summary>
    public string Nome { get; private set; }

    /// <summary>Nome normalizado, usado na unicidade e na busca.</summary>
    public string NomeNormalizado { get; private set; }

    /// <summary>Endereço postal.</summary>
    public Endereco Endereco { get; private set; }

    /// <summary>Ponto geográfico, gravado como <c>geography(Point,4326)</c>.</summary>
    public CoordenadaGeografica Localizacao { get; private set; }

    /// <inheritdoc />
    public bool Ativo { get; private set; }

    /// <summary>Instante de criação.</summary>
    public DateTimeOffset CriadoEm { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset AtualizadoEm { get; private set; }

    /// <inheritdoc />
    public uint Versao { get; private set; }

    /// <summary>Cadastra um hub ativo.</summary>
    public static Hub Criar(
        Guid id,
        Guid organizacaoId,
        string? nome,
        Endereco endereco,
        CoordenadaGeografica localizacao,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(endereco);
        ArgumentNullException.ThrowIfNull(localizacao);
        ExcecaoDeDominio.LancarSe(id == Guid.Empty, "identificador_invalido", "Identificador inválido.");
        ExcecaoDeDominio.LancarSe(organizacaoId == Guid.Empty, "organizacao_invalida", "Organização inválida.");

        var nomeValido = TextoNormalizado.Obrigatorio(nome, TamanhoMaximoDoNome, "nome_invalido", "O nome");
        var instante = agora.ToUniversalTime();

        return new Hub
        {
            Id = id,
            OrganizacaoId = organizacaoId,
            Nome = nomeValido,
            NomeNormalizado = TextoNormalizado.ParaBusca(nomeValido),
            Endereco = endereco,
            Localizacao = localizacao,
            Ativo = true,
            CriadoEm = instante,
            AtualizadoEm = instante,
        };
    }

    /// <summary>Atualiza os dados.</summary>
    /// <returns>Nomes dos campos que mudaram.</returns>
    public IReadOnlyList<string> AtualizarDados(
        string? nome,
        Endereco endereco,
        CoordenadaGeografica localizacao,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(endereco);
        ArgumentNullException.ThrowIfNull(localizacao);

        var nomeValido = TextoNormalizado.Obrigatorio(nome, TamanhoMaximoDoNome, "nome_invalido", "O nome");
        var alteracoes = new RegistroDeAlteracoes();

        alteracoes.Aplicar("nome", Nome, nomeValido, valor =>
        {
            Nome = valor;
            NomeNormalizado = TextoNormalizado.ParaBusca(valor);
        });
        alteracoes.Aplicar("endereco", Endereco, endereco, valor => Endereco = valor);
        alteracoes.Aplicar("localizacao", Localizacao, localizacao, valor => Localizacao = valor);

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
