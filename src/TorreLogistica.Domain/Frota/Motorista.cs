using TorreLogistica.Domain.Abstracoes.Cadastro;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Comum;
using TorreLogistica.Domain.Identidade;

namespace TorreLogistica.Domain.Frota;

/// <summary>
/// Motorista como recurso da operação: quem pode receber rota.
/// </summary>
/// <remarks>
/// <para>
/// É diferente da conta de acesso. A conta (<see cref="Usuario"/> com perfil Motorista)
/// autentica na PWA; o motorista é o cadastro operacional. Um motorista pode existir antes
/// de ter conta, e a associação é explícita.
/// </para>
/// <para>
/// Dados mínimos de propósito: nome e telefone de contato. CPF, CNH e endereço residencial
/// não servem a nenhum fluxo do produto e, portanto, não são coletados.
/// </para>
/// </remarks>
public sealed class Motorista : IRecursoAtivavel
{
    /// <summary>Tamanho máximo do nome.</summary>
    public const int TamanhoMaximoDoNome = 120;

    private Motorista()
    {
        Nome = string.Empty;
        NomeNormalizado = string.Empty;
    }

    /// <inheritdoc />
    public Guid Id { get; private set; }

    /// <inheritdoc />
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Nome de exibição.</summary>
    public string Nome { get; private set; }

    /// <summary>Nome sem acento e em minúsculas, para busca.</summary>
    public string NomeNormalizado { get; private set; }

    /// <summary>Telefone de contato em E.164.</summary>
    public string? Telefone { get; private set; }

    /// <summary>Conta de acesso associada, se houver.</summary>
    public Guid? UsuarioId { get; private set; }

    /// <inheritdoc />
    public bool Ativo { get; private set; }

    /// <summary>Instante de criação.</summary>
    public DateTimeOffset CriadoEm { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset AtualizadoEm { get; private set; }

    /// <inheritdoc />
    public uint Versao { get; private set; }

    /// <summary>Cadastra um motorista ativo, sem conta associada.</summary>
    public static Motorista Criar(Guid id, Guid organizacaoId, string? nome, Telefone? telefone, DateTimeOffset agora)
    {
        ExcecaoDeDominio.LancarSe(id == Guid.Empty, "identificador_invalido", "Identificador inválido.");
        ExcecaoDeDominio.LancarSe(organizacaoId == Guid.Empty, "organizacao_invalida", "Organização inválida.");

        var nomeValido = TextoNormalizado.Obrigatorio(nome, TamanhoMaximoDoNome, "nome_invalido", "O nome");
        var instante = agora.ToUniversalTime();

        return new Motorista
        {
            Id = id,
            OrganizacaoId = organizacaoId,
            Nome = nomeValido,
            NomeNormalizado = TextoNormalizado.ParaBusca(nomeValido),
            Telefone = telefone?.Valor,
            Ativo = true,
            CriadoEm = instante,
            AtualizadoEm = instante,
        };
    }

    /// <summary>Atualiza os dados cadastrais.</summary>
    /// <returns>Nomes dos campos que mudaram.</returns>
    public IReadOnlyList<string> AtualizarDados(string? nome, Telefone? telefone, DateTimeOffset agora)
    {
        var nomeValido = TextoNormalizado.Obrigatorio(nome, TamanhoMaximoDoNome, "nome_invalido", "O nome");
        var alteracoes = new RegistroDeAlteracoes();

        alteracoes.Aplicar("nome", Nome, nomeValido, valor =>
        {
            Nome = valor;
            NomeNormalizado = TextoNormalizado.ParaBusca(valor);
        });
        alteracoes.Aplicar("telefone", Telefone, telefone?.Valor, valor => Telefone = valor);

        if (alteracoes.HouveAlteracao)
        {
            AtualizadoEm = agora.ToUniversalTime();
        }

        return alteracoes.Campos;
    }

    /// <summary>
    /// Associa a conta de acesso do motorista.
    /// </summary>
    /// <remarks>
    /// Trocar a conta de um motorista que já tem uma exige desassociar antes. Troca direta
    /// transferiria, numa única chamada, a identidade de quem executa as entregas dele.
    /// </remarks>
    /// <returns><see langword="true"/> se a associação mudou.</returns>
    public bool AssociarConta(Usuario conta, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(conta);

        // Chegar aqui com conta de outra organização é defeito de quem chamou: a consulta da
        // conta já é restrita ao tenant. A regra existe mesmo assim, no próprio domínio.
        ExcecaoDeDominio.LancarSe(
            conta.OrganizacaoId != OrganizacaoId,
            "usuario_nao_encontrado",
            "Usuário não encontrado.");
        ExcecaoDeDominio.LancarSe(
            conta.Perfil != Perfil.Motorista,
            "conta_nao_e_de_motorista",
            "Só uma conta com perfil Motorista pode ser associada a um motorista.");
        ExcecaoDeDominio.LancarSe(!conta.Ativo, "conta_inativa", "A conta está inativa.");

        if (UsuarioId == conta.Id)
        {
            return false;
        }

        if (UsuarioId is not null)
        {
            throw ExcecaoDeDominio.Conflito(
                "motorista_ja_possui_conta",
                "Este motorista já tem uma conta associada. Desassocie antes de associar outra.");
        }

        UsuarioId = conta.Id;
        AtualizadoEm = agora.ToUniversalTime();
        return true;
    }

    /// <summary>Remove a associação com a conta de acesso.</summary>
    /// <returns><see langword="true"/> se havia conta associada.</returns>
    public bool DesassociarConta(DateTimeOffset agora)
    {
        if (UsuarioId is null)
        {
            return false;
        }

        UsuarioId = null;
        AtualizadoEm = agora.ToUniversalTime();
        return true;
    }

    /// <inheritdoc />
    public bool Ativar(DateTimeOffset agora) => MudarSituacao(true, agora);

    /// <inheritdoc />
    public bool Inativar(DateTimeOffset agora) => MudarSituacao(false, agora);

    /// <summary>
    /// Regra da Fase 2, usada a partir da Fase 4: motorista inativo não recebe nova atribuição.
    /// </summary>
    public void GarantirAptoParaAtribuicao() =>
        ExcecaoDeDominio.LancarSe(!Ativo, "motorista_inativo", "Motorista inativo não recebe nova atribuição.");

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
