using System.ComponentModel.DataAnnotations;

namespace TorreLogistica.Simulator.Configuracao;

/// <summary>
/// Configuração do simulador da operação.
/// </summary>
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
}
