using System.ComponentModel.DataAnnotations;
using TorreLogistica.Domain.Identidade;

namespace TorreLogistica.Application.Identidade;

/// <summary>
/// Prazos e limites de autenticação.
/// </summary>
/// <remarks>
/// Todos os valores têm faixa validada na subida. Um prazo zerado ou gigantesco por erro
/// de configuração é falha de segurança silenciosa; recusar a inicialização é melhor.
/// </remarks>
public sealed class OpcoesDeAutenticacao
{
    /// <summary>Seção correspondente na configuração.</summary>
    public const string Secao = "Torre:Autenticacao";

    /// <summary>Validade do token de acesso.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "01:00:00")]
    public TimeSpan DuracaoDoTokenDeAcesso { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Prazo absoluto de uma sessão do console.</summary>
    [Range(typeof(TimeSpan), "00:15:00", "1.00:00:00")]
    public TimeSpan DuracaoDaSessaoNoConsole { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    /// Prazo absoluto de uma sessão do aplicativo do motorista. Maior que o do console
    /// porque o motorista passa o turno inteiro com conexão intermitente.
    /// </summary>
    [Range(typeof(TimeSpan), "00:15:00", "14.00:00:00")]
    public TimeSpan DuracaoDaSessaoNoAplicativoDoMotorista { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Janela em que reapresentar um token recém-trocado é tratado como retry.</summary>
    [Range(typeof(TimeSpan), "00:00:00", "00:02:00")]
    public TimeSpan JanelaDeToleranciaDaRenovacao { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Falhas de senha consecutivas que bloqueiam a conta temporariamente.</summary>
    [Range(3, 20)]
    public int LimiteDeFalhasDeLogin { get; set; } = 5;

    /// <summary>Duração do bloqueio temporário.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "1.00:00:00")]
    public TimeSpan DuracaoDoBloqueio { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Prazo absoluto de sessão para o canal.</summary>
    public TimeSpan DuracaoDaSessao(CanalDeAcesso canal) => canal switch
    {
        CanalDeAcesso.Operacao => DuracaoDaSessaoNoConsole,
        CanalDeAcesso.Motorista => DuracaoDaSessaoNoAplicativoDoMotorista,
        _ => throw new ArgumentOutOfRangeException(nameof(canal), canal, "Canal desconhecido."),
    };
}
