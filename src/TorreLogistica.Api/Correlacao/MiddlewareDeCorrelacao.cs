using System.Diagnostics;
using Microsoft.Extensions.Primitives;
using TorreLogistica.Domain.Abstracoes.Identificadores;

namespace TorreLogistica.Api.Correlacao;

/// <summary>
/// Garante que toda requisição tenha um identificador de correlação.
/// </summary>
/// <remarks>
/// Reaproveita o identificador enviado pelo cliente quando ele é aceitável, para
/// que a chamada feita pelo console operacional e o processamento no servidor
/// apareçam sob o mesmo nome na investigação. Caso contrário gera um UUIDv7 novo.
/// O valor volta sempre no cabeçalho da resposta e entra no escopo de log.
/// </remarks>
public sealed class MiddlewareDeCorrelacao(
    RequestDelegate proximo,
    IGeradorDeIdentificador geradorDeIdentificador,
    ILogger<MiddlewareDeCorrelacao> log)
{
    private readonly RequestDelegate _proximo =
        proximo ?? throw new ArgumentNullException(nameof(proximo));

    private readonly IGeradorDeIdentificador _geradorDeIdentificador =
        geradorDeIdentificador ?? throw new ArgumentNullException(nameof(geradorDeIdentificador));

    private readonly ILogger<MiddlewareDeCorrelacao> _log =
        log ?? throw new ArgumentNullException(nameof(log));

    /// <summary>Executa o middleware.</summary>
    public async Task InvokeAsync(HttpContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var idDeCorrelacao = ResolverIdentificador(contexto);

        contexto.Items[CorrelacaoHttp.ChaveNoContexto] = idDeCorrelacao;

        // Gravar o cabeçalho em OnStarting, e não agora: o middleware de tratamento
        // de erro limpa a resposta antes de escrever o ProblemDetails, e um cabeçalho
        // gravado de imediato desapareceria justamente na resposta de falha — a que
        // mais precisa do identificador para investigação.
        contexto.Response.OnStarting(static estado =>
        {
            var (resposta, identificador) = ((HttpResponse, string))estado;
            resposta.Headers[CorrelacaoHttp.NomeDoCabecalho] = new StringValues(identificador);
            return Task.CompletedTask;
        }, (contexto.Response, idDeCorrelacao));

        // O rastro entra no escopo junto com a correlação: é o que permite sair de uma linha de log
        // para o trace inteiro, e voltar. Sem isso, log e trace seriam dois relatos do mesmo fato sem
        // nada que os ligasse.
        var escopo = new Dictionary<string, object>
        {
            ["IdDeCorrelacao"] = idDeCorrelacao,
        };

        if (Activity.Current is { } rastro)
        {
            escopo["TraceId"] = rastro.TraceId.ToString();
            escopo["SpanId"] = rastro.SpanId.ToString();
        }

        using (_log.BeginScope(escopo))
        {
            await _proximo(contexto).ConfigureAwait(false);
        }
    }

    private string ResolverIdentificador(HttpContext contexto)
    {
        if (contexto.Request.Headers.TryGetValue(CorrelacaoHttp.NomeDoCabecalho, out var recebido))
        {
            var candidato = recebido.Count > 0 ? recebido[0] : null;
            if (CorrelacaoHttp.EhIdentificadorAceitavel(candidato))
            {
                return candidato!;
            }
        }

        return _geradorDeIdentificador.Novo().ToString("n");
    }
}
