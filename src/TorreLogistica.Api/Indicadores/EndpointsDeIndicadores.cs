using TorreLogistica.Api.Autenticacao;
using TorreLogistica.Application.Indicadores;
using TorreLogistica.Domain.Abstracoes.Tempo;

namespace TorreLogistica.Api.Indicadores;

/// <summary>
/// Indicadores operacionais do período, para o console.
/// </summary>
/// <remarks>
/// Só leitura e só agregado: a resposta traz números e recortes, nunca a lista de entregas por trás deles,
/// nem endereço, nem coordenada. Cada indicador vem acompanhado da própria definição, para que a tela
/// mostre junto como o número foi calculado — número sem definição é palpite com aparência de fato.
/// </remarks>
public static class EndpointsDeIndicadores
{
    /// <summary>Registra os endpoints.</summary>
    public static IEndpointRouteBuilder MapearEndpointsDeIndicadores(this IEndpointRouteBuilder rotas)
    {
        ArgumentNullException.ThrowIfNull(rotas);

        rotas.MapGet(
                "/api/indicadores",
                (
                    DateTimeOffset? de,
                    DateTimeOffset? ate,
                    ConsultaDeIndicadores consulta,
                    IRelogio relogio,
                    CancellationToken cancelamento) =>
                    consulta.ObterAsync(PeriodoDoIndicador.Criar(de, ate, relogio.AgoraUtc), cancelamento))
            .WithTags("Indicadores")
            .RequireAuthorization(Politicas.LeituraDaOperacao);

        return rotas;
    }
}
