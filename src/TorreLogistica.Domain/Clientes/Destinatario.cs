using TorreLogistica.Domain.Abstracoes.Cadastro;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Comum;

namespace TorreLogistica.Domain.Clientes;

/// <summary>
/// Destinatário: quem recebe a entrega.
/// </summary>
/// <remarks>
/// <para>
/// É o cadastro com mais dado pessoal do sistema, então é o mais enxuto: nome, endereço,
/// telefone opcional e instruções de acesso. Sem CPF, sem e-mail, sem data de nascimento —
/// nenhum fluxo de entrega precisa disso.
/// </para>
/// <para>
/// A coordenada é opcional: nem todo endereço chega geocodificado. Sem ela, geofence e ETA
/// daquele destino ficam indisponíveis até a coordenada ser informada.
/// </para>
/// </remarks>
public sealed class Destinatario : IRecursoAtivavel
{
    /// <summary>Tamanho máximo do nome.</summary>
    public const int TamanhoMaximoDoNome = 120;

    /// <summary>Tamanho máximo das instruções de entrega.</summary>
    public const int TamanhoMaximoDasInstrucoes = 280;

    private Destinatario()
    {
        Nome = string.Empty;
        NomeNormalizado = string.Empty;
        Endereco = null!;
    }

    /// <inheritdoc />
    public Guid Id { get; private set; }

    /// <inheritdoc />
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Nome de quem recebe.</summary>
    public string Nome { get; private set; }

    /// <summary>Nome normalizado, para busca.</summary>
    public string NomeNormalizado { get; private set; }

    /// <summary>Telefone de contato em E.164, opcional.</summary>
    public string? Telefone { get; private set; }

    /// <summary>Endereço de entrega.</summary>
    public Endereco Endereco { get; private set; }

    /// <summary>Ponto do endereço, quando conhecido.</summary>
    public CoordenadaGeografica? Localizacao { get; private set; }

    /// <summary>Orientação de acesso: "portão lateral, interfone 12".</summary>
    public string? InstrucoesDeEntrega { get; private set; }

    /// <inheritdoc />
    public bool Ativo { get; private set; }

    /// <summary>Instante de criação.</summary>
    public DateTimeOffset CriadoEm { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset AtualizadoEm { get; private set; }

    /// <inheritdoc />
    public uint Versao { get; private set; }

    /// <summary>Cadastra um destinatário ativo.</summary>
    public static Destinatario Criar(
        Guid id,
        Guid organizacaoId,
        string? nome,
        Telefone? telefone,
        Endereco endereco,
        CoordenadaGeografica? localizacao,
        string? instrucoesDeEntrega,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(endereco);
        ExcecaoDeDominio.LancarSe(id == Guid.Empty, "identificador_invalido", "Identificador inválido.");
        ExcecaoDeDominio.LancarSe(organizacaoId == Guid.Empty, "organizacao_invalida", "Organização inválida.");

        var nomeValido = TextoNormalizado.Obrigatorio(nome, TamanhoMaximoDoNome, "nome_invalido", "O nome");
        var instante = agora.ToUniversalTime();

        return new Destinatario
        {
            Id = id,
            OrganizacaoId = organizacaoId,
            Nome = nomeValido,
            NomeNormalizado = TextoNormalizado.ParaBusca(nomeValido),
            Telefone = telefone?.Valor,
            Endereco = endereco,
            Localizacao = localizacao,
            InstrucoesDeEntrega = ValidarInstrucoes(instrucoesDeEntrega),
            Ativo = true,
            CriadoEm = instante,
            AtualizadoEm = instante,
        };
    }

    /// <summary>Atualiza os dados.</summary>
    /// <returns>Nomes dos campos que mudaram.</returns>
    public IReadOnlyList<string> AtualizarDados(
        string? nome,
        Telefone? telefone,
        Endereco endereco,
        CoordenadaGeografica? localizacao,
        string? instrucoesDeEntrega,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(endereco);

        var nomeValido = TextoNormalizado.Obrigatorio(nome, TamanhoMaximoDoNome, "nome_invalido", "O nome");
        var instrucoes = ValidarInstrucoes(instrucoesDeEntrega);
        var alteracoes = new RegistroDeAlteracoes();

        alteracoes.Aplicar("nome", Nome, nomeValido, valor =>
        {
            Nome = valor;
            NomeNormalizado = TextoNormalizado.ParaBusca(valor);
        });
        alteracoes.Aplicar("telefone", Telefone, telefone?.Valor, valor => Telefone = valor);
        alteracoes.Aplicar("endereco", Endereco, endereco, valor => Endereco = valor);
        alteracoes.Aplicar("localizacao", Localizacao, localizacao, valor => Localizacao = valor);
        alteracoes.Aplicar("instrucoesDeEntrega", InstrucoesDeEntrega, instrucoes, valor => InstrucoesDeEntrega = valor);

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

    private static string? ValidarInstrucoes(string? instrucoes) =>
        TextoNormalizado.Opcional(instrucoes, TamanhoMaximoDasInstrucoes, "instrucoes_invalidas", "As instruções de entrega");

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
