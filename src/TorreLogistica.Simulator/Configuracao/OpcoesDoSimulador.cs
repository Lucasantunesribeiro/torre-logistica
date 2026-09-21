using System.ComponentModel.DataAnnotations;

namespace TorreLogistica.Simulator.Configuracao;

/// <summary>
/// Configuração do simulador da operação.
/// </summary>
/// <remarks>
/// O simulador entra na API com credenciais de uma organização que já existe — ele não cria organização
/// nem conta de administrador, porque isso seria poder demais para um cliente externo. Em desenvolvimento,
/// quem semeia essa organização é <c>Torre:Desenvolvimento:Semeadura</c>.
/// </remarks>
public sealed class OpcoesDoSimulador
{
    /// <summary>Seção correspondente na configuração.</summary>
    public const string Secao = "Torre:Simulador";

    /// <summary>Endereço base da API que o simulador vai exercitar.</summary>
    [Required(AllowEmptyStrings = false)]
    [Url]
    public string UrlBaseDaApi { get; set; } = "http://localhost:5080";

    /// <summary>Tempo máximo, em segundos, de cada chamada HTTP.</summary>
    [Range(1, 120)]
    public int TimeoutEmSegundos { get; set; } = 15;

    /// <summary>Identificador da organização onde a demonstração acontece.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Organizacao { get; set; } = "transportadora-aurora";

    /// <summary>Conta administrativa usada para montar o palco.</summary>
    [Required(AllowEmptyStrings = false)]
    [EmailAddress]
    public string EmailDoAdministrador { get; set; } = "helena.duarte@aurora.test";

    /// <summary>
    /// Senha das contas da demonstração.
    /// </summary>
    /// <remarks>
    /// Vazia de propósito: vem de variável de ambiente ou do gerenciador de segredos, nunca do
    /// repositório. Sem ela, o simulador recusa começar em vez de tentar uma senha adivinhada.
    /// </remarks>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Informe a senha das contas da demonstração.")]
    public string Senha { get; set; } = string.Empty;

    /// <summary>
    /// Origem declarada nas chamadas, para o login do console aceitar.
    /// </summary>
    /// <remarks>
    /// O login humano só é aceito a partir das aplicações da Torre, conferido pelo cabeçalho
    /// <c>Origin</c> — é a defesa contra um site terceiro fazer o navegador de alguém autenticar sem
    /// querer. O simulador não é navegador e não tem vítima a proteger, mas também não passa por cima da
    /// regra: ele declara quem é, e quem opera decide se aquela origem entra na lista de permitidas. Vazio
    /// significa não declarar nada, e aí o login é recusado — que é o padrão correto.
    /// </remarks>
    public string? OrigemDeclarada { get; set; }

    /// <summary>
    /// Semente do roteiro. A mesma semente produz a mesma operação.
    /// </summary>
    [Range(1, 9_999)]
    public int Semente { get; set; } = 2026;

    /// <summary>
    /// Quantas vezes o tempo do roteiro corre mais rápido que o relógio de parede.
    /// </summary>
    /// <remarks>
    /// A janela prometida de cada entrega é criada na mesma escala. Comprimir a espera sem comprimir a
    /// promessa faria toda entrega nascer atrasada, e a demonstração contaria uma história falsa.
    /// </remarks>
    [Range(1, 600)]
    public int MultiplicadorDeTempo { get; set; } = 60;

    /// <summary>Intervalo entre posições, no tempo do roteiro.</summary>
    public TimeSpan IntervaloEntrePosicoes { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Quanto o roteiro espera a torre reagir sozinha, no tempo do roteiro.
    /// </summary>
    /// <remarks>
    /// Risco de atraso, motorista offline e entrada em geofence não são encenados: eles nascem no
    /// servidor, e o roteiro só dá tempo para isso acontecer. Se o simulador os escrevesse, a
    /// demonstração provaria que o simulador sabe escrever.
    /// </remarks>
    public TimeSpan EsperaPelaTorre { get; set; } = TimeSpan.FromMinutes(15);
}
