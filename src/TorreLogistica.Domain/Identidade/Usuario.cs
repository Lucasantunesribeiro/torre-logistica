using TorreLogistica.Domain.Abstracoes.Erros;

namespace TorreLogistica.Domain.Identidade;

/// <summary>
/// Conta de acesso de uma pessoa dentro de uma organização.
/// </summary>
/// <remarks>
/// <para>
/// O e-mail é único <b>por organização</b>, não globalmente. A escolha é de segurança:
/// com unicidade global, um administrador do tenant A que tentasse cadastrar um e-mail já
/// usado no tenant B receberia um conflito — e acabaria de descobrir que aquela pessoa é
/// cliente de outra organização. Ver <c>docs/adr/0010-multi-tenancy-e-isolamento.md</c>.
/// </para>
/// <para>
/// Nenhuma propriedade tem setter público: estado muda por operação com intenção.
/// </para>
/// </remarks>
public sealed class Usuario
{
    /// <summary>Tamanho máximo do nome.</summary>
    public const int TamanhoMaximoDoNome = 120;

    private Usuario()
    {
        Nome = string.Empty;
        Email = string.Empty;
        EmailNormalizado = string.Empty;
        HashDaSenha = string.Empty;
    }

    /// <summary>Identificador interno (UUIDv7).</summary>
    public Guid Id { get; private set; }

    /// <summary>Organização dona da conta. Nunca muda.</summary>
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Nome de exibição.</summary>
    public string Nome { get; private set; }

    /// <summary>E-mail como cadastrado.</summary>
    public string Email { get; private set; }

    /// <summary>E-mail normalizado, usado no login e na unicidade.</summary>
    public string EmailNormalizado { get; private set; }

    /// <summary>Hash da senha. Nunca a senha.</summary>
    public string HashDaSenha { get; private set; }

    /// <summary>Perfil de acesso.</summary>
    public Perfil Perfil { get; private set; }

    /// <summary>Conta inativa não autentica e perde as sessões abertas.</summary>
    public bool Ativo { get; private set; }

    /// <summary>Falhas de senha consecutivas desde o último sucesso ou bloqueio.</summary>
    public int TentativasDeLoginFalhas { get; private set; }

    /// <summary>Até quando a conta está temporariamente bloqueada.</summary>
    public DateTimeOffset? BloqueadoAte { get; private set; }

    /// <summary>Instante de criação.</summary>
    public DateTimeOffset CriadoEm { get; private set; }

    /// <summary>Instante da última alteração administrativa.</summary>
    public DateTimeOffset AtualizadoEm { get; private set; }

    /// <summary>Canal pelo qual esta conta se autentica.</summary>
    public CanalDeAcesso Canal => Perfil.Canal();

    /// <summary>Cria uma conta ativa.</summary>
    public static Usuario Criar(
        Guid id,
        Guid organizacaoId,
        string? nome,
        EnderecoDeEmail email,
        string hashDaSenha,
        Perfil perfil,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(email);
        ExcecaoDeDominio.LancarSe(id == Guid.Empty, "identificador_invalido", "Identificador inválido.");
        ExcecaoDeDominio.LancarSe(
            organizacaoId == Guid.Empty, "organizacao_invalida", "Organização inválida.");
        ExcecaoDeDominio.LancarSe(!perfil.EhDefinido(), "perfil_invalido", "Perfil inválido.");
        ExcecaoDeDominio.LancarSe(
            string.IsNullOrWhiteSpace(hashDaSenha), "senha_invalida", "Senha inválida.");

        var nomeAparado = (nome ?? string.Empty).Trim();
        ExcecaoDeDominio.LancarSe(
            nomeAparado.Length is 0 or > TamanhoMaximoDoNome,
            "nome_invalido",
            $"O nome deve ter entre 1 e {TamanhoMaximoDoNome} caracteres.");

        var instante = agora.ToUniversalTime();

        return new Usuario
        {
            Id = id,
            OrganizacaoId = organizacaoId,
            Nome = nomeAparado,
            Email = email.Valor,
            EmailNormalizado = email.Normalizado,
            HashDaSenha = hashDaSenha,
            Perfil = perfil,
            Ativo = true,
            CriadoEm = instante,
            AtualizadoEm = instante,
        };
    }

    /// <summary>Indica se o bloqueio temporário ainda vale.</summary>
    public bool EstaBloqueado(DateTimeOffset agora) => BloqueadoAte is { } ate && ate > agora;

    /// <summary>
    /// Troca o perfil. Mudar de canal (console ↔ motorista) é proibido.
    /// </summary>
    /// <remarks>
    /// Transformar uma conta de motorista em operador seria conceder acesso administrativo
    /// a uma credencial emitida para outra finalidade. Quem precisa dos dois acessos tem
    /// duas contas.
    /// </remarks>
    /// <returns><see langword="true"/> se o perfil mudou.</returns>
    public bool AlterarPerfil(Perfil novoPerfil, DateTimeOffset agora)
    {
        ExcecaoDeDominio.LancarSe(!novoPerfil.EhDefinido(), "perfil_invalido", "Perfil inválido.");
        ExcecaoDeDominio.LancarSe(
            novoPerfil.Canal() != Perfil.Canal(),
            "perfil_incompativel",
            "Não é possível trocar uma conta entre o console e o aplicativo do motorista.");

        if (novoPerfil == Perfil)
        {
            return false;
        }

        Perfil = novoPerfil;
        AtualizadoEm = agora.ToUniversalTime();
        return true;
    }

    /// <summary>Desativa a conta. Repetir a operação não tem efeito.</summary>
    /// <returns><see langword="true"/> se a conta estava ativa.</returns>
    public bool Desativar(DateTimeOffset agora)
    {
        if (!Ativo)
        {
            return false;
        }

        Ativo = false;
        AtualizadoEm = agora.ToUniversalTime();
        return true;
    }
}
