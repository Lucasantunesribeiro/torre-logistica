using TorreLogistica.Domain.Abstracoes.Erros;

namespace TorreLogistica.Domain.Identidade;

/// <summary>Por que uma sessão foi encerrada antes do prazo.</summary>
public enum MotivoDeRevogacao
{
    /// <summary>O próprio usuário saiu.</summary>
    Logout = 1,

    /// <summary>Um token de renovação já consumido foi apresentado de novo.</summary>
    ReusoDeToken = 2,

    /// <summary>A conta foi desativada.</summary>
    UsuarioDesativado = 3,

    /// <summary>O perfil mudou; o token antigo carrega permissões desatualizadas.</summary>
    PerfilAlterado = 4,
}

/// <summary>
/// Sessão de acesso: a "família" de tokens de renovação emitidos a partir de um login.
/// </summary>
/// <remarks>
/// A sessão é verificada a cada requisição autenticada, não só na renovação. Por isso
/// logout, desativação e detecção de reuso valem na hora, sem esperar o token de acesso
/// expirar. Ver <c>docs/adr/0009-autenticacao-e-sessao.md</c>.
/// </remarks>
public sealed class Sessao
{
    private Sessao()
    {
    }

    /// <summary>Identificador (UUIDv7). Vai no token de acesso como <c>sid</c>.</summary>
    public Guid Id { get; private set; }

    /// <summary>Organização da conta.</summary>
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Conta dona da sessão.</summary>
    public Guid UsuarioId { get; private set; }

    /// <summary>Canal em que a sessão foi aberta.</summary>
    public CanalDeAcesso Canal { get; private set; }

    /// <summary>Instante do login.</summary>
    public DateTimeOffset CriadaEm { get; private set; }

    /// <summary>Prazo absoluto: nenhuma renovação estende a sessão além disto.</summary>
    public DateTimeOffset ExpiraEm { get; private set; }

    /// <summary>Instante da última renovação bem-sucedida.</summary>
    public DateTimeOffset UltimaRenovacaoEm { get; private set; }

    /// <summary>Instante da revogação, se houve.</summary>
    public DateTimeOffset? RevogadaEm { get; private set; }

    /// <summary>Motivo da revogação, se houve.</summary>
    public MotivoDeRevogacao? MotivoDaRevogacao { get; private set; }

    /// <summary>Abre uma sessão para uma conta.</summary>
    public static Sessao Abrir(Guid id, Usuario usuario, DateTimeOffset agora, TimeSpan duracaoMaxima)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        ExcecaoDeDominio.LancarSe(id == Guid.Empty, "identificador_invalido", "Identificador inválido.");
        ExcecaoDeDominio.LancarSe(
            duracaoMaxima <= TimeSpan.Zero, "duracao_invalida", "Duração de sessão inválida.");

        var instante = agora.ToUniversalTime();

        return new Sessao
        {
            Id = id,
            OrganizacaoId = usuario.OrganizacaoId,
            UsuarioId = usuario.Id,
            Canal = usuario.Canal,
            CriadaEm = instante,
            UltimaRenovacaoEm = instante,
            ExpiraEm = instante + duracaoMaxima,
        };
    }

    /// <summary>Sessão não revogada e dentro do prazo absoluto.</summary>
    public bool EstaAtiva(DateTimeOffset agora) => RevogadaEm is null && agora < ExpiraEm;

    /// <summary>Revoga a sessão. A primeira revogação vale; as seguintes não mudam nada.</summary>
    /// <returns><see langword="true"/> se a sessão estava aberta.</returns>
    public bool Revogar(MotivoDeRevogacao motivo, DateTimeOffset agora)
    {
        if (RevogadaEm is not null)
        {
            return false;
        }

        RevogadaEm = agora.ToUniversalTime();
        MotivoDaRevogacao = motivo;
        return true;
    }

    /// <summary>Registra uma renovação.</summary>
    public void RegistrarRenovacao(DateTimeOffset agora) => UltimaRenovacaoEm = agora.ToUniversalTime();
}
