using System.ComponentModel.DataAnnotations;

namespace TorreLogistica.Infrastructure.Configuracao;

/// <summary>
/// Configuração tipada do acesso ao PostgreSQL/PostGIS.
/// </summary>
/// <remarks>
/// A cadeia de conexão nunca é versionada: em desenvolvimento vem de variável de
/// ambiente ou user-secrets, em produção de um gerenciador de segredos.
/// Ver <c>.env.example</c> e <c>docs/seguranca/gestao-de-segredos.md</c>.
/// </remarks>
public sealed class OpcoesDeBancoDeDados
{
    /// <summary>Seção correspondente no arquivo/variáveis de configuração.</summary>
    public const string Secao = "Torre:BancoDeDados";

    /// <summary>Cadeia de conexão do PostgreSQL. Obrigatória.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "A cadeia de conexão do banco é obrigatória.")]
    public string CadeiaDeConexao { get; set; } = string.Empty;

    /// <summary>Tempo máximo, em segundos, para um comando SQL.</summary>
    [Range(1, 300)]
    public int TimeoutDeComandoEmSegundos { get; set; } = 30;

    /// <summary>Número de novas tentativas em falha transitória de conexão.</summary>
    [Range(0, 10)]
    public int TentativasEmFalhaTransitoria { get; set; } = 3;

    /// <summary>
    /// Aplica migrations pendentes durante a inicialização.
    /// </summary>
    /// <remarks>
    /// Conveniência de desenvolvimento e de teste. Em produção a migration é um passo
    /// explícito de deploy: aplicar schema no start faz várias instâncias competirem
    /// pela mesma alteração e esconde a falha dentro do processo que deveria servir.
    /// </remarks>
    public bool AplicarMigrationsAoIniciar { get; set; }
}
