using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using TorreLogistica.Domain.Abstracoes.Erros;

namespace TorreLogistica.IntegrationTests.Infra;

/// <summary>
/// Rotas que só existem durante o teste, para provocar falhas de propósito.
/// </summary>
/// <remarks>
/// Elas ficam no projeto de teste — nunca na API — e são acrescentadas ao fim do
/// pipeline real por um <see cref="IStartupFilter"/>. Assim a requisição atravessa
/// exatamente os mesmos middlewares de produção (cabeçalhos, correlação, CORS e
/// tratamento de erro) antes de falhar, que é o que dá valor à prova.
/// </remarks>
public sealed class FiltroDeEndpointsDeTeste : IStartupFilter
{
    /// <summary>Rota que lança uma exceção não prevista.</summary>
    public const string RotaDeErroInesperado = "/__teste/erro-inesperado";

    /// <summary>Rota que lança erro de domínio de regra violada.</summary>
    public const string RotaDeRegraViolada = "/__teste/regra-violada";

    /// <summary>Rota que lança erro de domínio de conflito.</summary>
    public const string RotaDeConflito = "/__teste/conflito";

    /// <summary>Rota que lança erro de domínio de recurso não encontrado.</summary>
    public const string RotaDeNaoEncontrado = "/__teste/nao-encontrado";

    /// <summary>Mensagem usada para checar que nada dela vaza para o cliente.</summary>
    public const string SegredoDaExcecao =
        "detalhe-interno-que-nunca-pode-sair-na-resposta";

    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return aplicacao =>
        {
            // Primeiro o pipeline real da API, depois estas rotas: elas precisam
            // estar por dentro do tratamento de erro para que ele as capture.
            next(aplicacao);

            aplicacao.Use(async (contexto, proximo) =>
            {
                var caminho = contexto.Request.Path.Value;

                switch (caminho)
                {
                    case RotaDeErroInesperado:
                        throw new InvalidOperationException(SegredoDaExcecao);

                    case RotaDeRegraViolada:
                        throw ExcecaoDeDominio.RegraViolada(
                            "regra_de_teste", "Regra de teste violada.");

                    case RotaDeConflito:
                        throw ExcecaoDeDominio.Conflito(
                            "conflito_de_teste", "Conflito de teste.");

                    case RotaDeNaoEncontrado:
                        throw ExcecaoDeDominio.NaoEncontrado(
                            "recurso_de_teste", "Recurso de teste não encontrado.");

                    default:
                        await proximo().ConfigureAwait(false);
                        break;
                }
            });
        };
    }
}
