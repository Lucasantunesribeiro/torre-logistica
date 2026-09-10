namespace TorreLogistica.Application.Abstracoes.Correlacao;

/// <summary>
/// Identificador que amarra tudo o que aconteceu por causa de uma mesma requisição.
/// </summary>
/// <remarks>
/// A implementação vive na borda que conhece o transporte (HTTP hoje; fila e worker
/// depois). A camada de aplicação só precisa saber ler o valor para carimbá-lo em
/// log, evento e mensagem — é o que torna possível reconstruir um incidente inteiro
/// a partir de um único identificador.
/// </remarks>
public interface IContextoDeCorrelacao
{
    /// <summary>Identificador de correlação em vigor. Nunca vazio.</summary>
    string IdDeCorrelacao { get; }
}
