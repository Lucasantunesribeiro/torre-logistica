using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TorreLogistica.Api.Autenticacao;
using TorreLogistica.Api.Cadastros;
using TorreLogistica.Api.Comum;
using TorreLogistica.Application.Comum;
using TorreLogistica.Application.Entregas;
using TorreLogistica.Application.Integracoes;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Integracoes;

namespace TorreLogistica.Api.Integracoes;

/// <summary>Emissão de credencial de integração.</summary>
public sealed record RequisicaoDeIntegracaoNova(
    [property: Required(ErrorMessage = "Informe o nome da integração.")]
    [property: StringLength(PoliticaDeIntegracao.TamanhoMaximoDoNome)]
    string? Nome);

/// <summary>
/// Entrega recebida de um sistema externo.
/// </summary>
/// <remarks>
/// Espelha o contrato do console, com um campo a mais: o identificador do pedido na origem, que é o que
/// impede o mesmo pedido de virar duas entregas quando o ERP reprocessa a fila dele.
/// </remarks>
public sealed record RequisicaoDeEntregaExterna(
    [property: StringLength(PoliticaDeIntegracao.TamanhoMaximoDoIdentificadorExterno)] string? IdentificadorExterno,
    [property: Required(ErrorMessage = "Informe o cliente.")] Guid? ClienteId,
    [property: Required(ErrorMessage = "Informe o destinatário.")] Guid? DestinatarioId,
    RequisicaoDeEndereco? Endereco,
    double? Latitude,
    double? Longitude,
    [property: Required(ErrorMessage = "Informe o início da janela prometida.")] DateTimeOffset? PrometidaDe,
    [property: Required(ErrorMessage = "Informe o fim da janela prometida.")] DateTimeOffset? PrometidaAte,
    [property: StringLength(Entrega.TamanhoMaximoDasObservacoes)] string? Observacoes);

/// <summary>
/// Credenciais de integração (console) e a API versionada que os sistemas externos consomem.
/// </summary>
/// <remarks>
/// <para>
/// A administração das credenciais é do administrador: emitir e revogar integração é decisão de segurança,
/// da mesma natureza que criar ou desativar conta.
/// </para>
/// <para>
/// A API externa tem versão no caminho (<c>/v1/</c>) porque o contrato dela é público para terceiros: o
/// console pode mudar junto com o frontend na mesma entrega, um ERP de cliente não.
/// </para>
/// </remarks>
public static class EndpointsDeIntegracoes
{
    private static readonly JsonSerializerOptions Canonica = new(JsonSerializerDefaults.Web);

    /// <summary>Registra os endpoints.</summary>
    public static IEndpointRouteBuilder MapearEndpointsDeIntegracoes(this IEndpointRouteBuilder rotas)
    {
        ArgumentNullException.ThrowIfNull(rotas);

        MapearAdministracao(rotas.MapGroup("/api/integracoes").WithTags("Integrações"));
        MapearApiExterna(rotas.MapGroup("/api/integracoes/v1").WithTags("API de integração v1"));

        return rotas;
    }

    private static void MapearAdministracao(RouteGroupBuilder grupo)
    {
        // A chave aparece só nesta resposta. Guardamos o hash: perdida, a credencial é revogada e outra
        // é emitida — nunca recuperada.
        grupo.MapPost("/", async (
                RequisicaoDeIntegracaoNova requisicao,
                GestaoDeIntegracoes gestao,
                CancellationToken cancelamento) =>
            {
                var emitida = await gestao.EmitirAsync(requisicao.Nome, cancelamento).ConfigureAwait(false);
                return Results.Created($"/api/integracoes/{emitida.Id}", emitida);
            })
            .RequireAuthorization(Politicas.GestaoDeUsuarios)
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDeIntegracaoNova>>();

        grupo.MapGet("/", (
                int? pagina,
                int? tamanhoDaPagina,
                GestaoDeIntegracoes gestao,
                CancellationToken cancelamento) =>
                gestao.ListarAsync(
                    pagina is > 0 ? pagina.Value : 1,
                    Math.Clamp(
                        tamanhoDaPagina ?? LimitesDePaginacao.TamanhoPadrao, 1, LimitesDePaginacao.TamanhoMaximo),
                    cancelamento))
            .RequireAuthorization(Politicas.GestaoDeUsuarios);

        grupo.MapGet("/{id:guid}", (Guid id, GestaoDeIntegracoes gestao, CancellationToken cancelamento) =>
                gestao.ObterAsync(id, cancelamento))
            .RequireAuthorization(Politicas.GestaoDeUsuarios);

        grupo.MapPost("/{id:guid}/revogacao", (Guid id, GestaoDeIntegracoes gestao, CancellationToken cancelamento) =>
                gestao.RevogarAsync(id, cancelamento))
            .RequireAuthorization(Politicas.GestaoDeUsuarios);
    }

    private static void MapearApiExterna(RouteGroupBuilder grupo)
    {
        grupo.MapPost("/entregas", async (
                RequisicaoDeEntregaExterna requisicao,
                [FromHeader(Name = "Idempotency-Key")] string? chaveDeIdempotencia,
                HttpContext http,
                RecepcaoDeEntregasExternas recepcao,
                CancellationToken cancelamento) =>
            {
                // O hash sai da re-serialização do contrato, não dos bytes crus: reenvio com espaçamento
                // ou ordem de campos diferente é o mesmo pedido, e tratá-lo como outro criaria duplicata.
                var hash = SegredosDeIntegracao.CalcularHashDoCorpo(
                    JsonSerializer.SerializeToUtf8Bytes(requisicao, Canonica));

                var recebida = await recepcao
                    .ReceberAsync(
                        IntegracaoDaRequisicao(http),
                        chaveDeIdempotencia,
                        hash,
                        requisicao.IdentificadorExterno,
                        new DadosDeEntrega(
                            requisicao.ClienteId!.Value,
                            requisicao.DestinatarioId!.Value,
                            requisicao.Endereco?.ParaDados(),
                            requisicao.Latitude,
                            requisicao.Longitude,
                            requisicao.PrometidaDe!.Value,
                            requisicao.PrometidaAte!.Value,
                            requisicao.Observacoes),
                        cancelamento)
                    .ConfigureAwait(false);

                // 200 no reenvio, 201 só quando algo nasceu: o cliente distingue sem precisar comparar
                // identificadores.
                return recebida.JaExistia
                    ? Results.Ok(recebida.Entrega)
                    : Results.Created($"/api/integracoes/v1/entregas/{recebida.Entrega.Id}", recebida.Entrega);
            })
            .RequireAuthorization(Politicas.Integracao)
            .RequireRateLimiting(PoliticasDeLimite.Integracao)
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDeEntregaExterna>>();

        // Sem isto, integrar exigiria abrir o console para saber o que aconteceu com a entrega enviada.
        grupo.MapGet("/entregas/{id:guid}", (Guid id, GestaoDeEntregas gestao, CancellationToken cancelamento) =>
                gestao.ObterAsync(id, cancelamento))
            .RequireAuthorization(Politicas.Integracao)
            .RequireRateLimiting(PoliticasDeLimite.Integracao);

        // Confere o arquivo inteiro sem gravar nada. É o passo que permite ao operador do ERP corrigir a
        // planilha antes de qualquer entrega existir.
        grupo.MapPost("/importacoes/previa", async (HttpContext http, CancellationToken cancelamento) =>
            {
                var conteudo = await LerArquivoAsync(http, cancelamento).ConfigureAwait(false);
                var previa = ImportacaoDeEntregas.Analisar(conteudo);

                return previa.Erros.Count > 0
                    ? Results.UnprocessableEntity(previa)
                    : Results.Ok(previa);
            })
            .RequireAuthorization(Politicas.Integracao)
            .RequireRateLimiting(PoliticasDeLimite.Integracao);

        grupo.MapPost("/importacoes", async (
                HttpContext http,
                ImportacaoDeEntregas importacao,
                CancellationToken cancelamento) =>
            {
                var conteudo = await LerArquivoAsync(http, cancelamento).ConfigureAwait(false);

                var resultado = await importacao
                    .ImportarAsync(IntegracaoDaRequisicao(http), conteudo, cancelamento)
                    .ConfigureAwait(false);

                // Erro de formato recusa o lote antes de gravar; erro de regra numa linha aparece no
                // relatório com as demais já importadas. Em nenhum dos casos o resultado é silencioso.
                return resultado.Importadas.Count == 0 && resultado.Erros.Count > 0
                    ? Results.UnprocessableEntity(resultado)
                    : Results.Ok(resultado);
            })
            .RequireAuthorization(Politicas.Integracao)
            .RequireRateLimiting(PoliticasDeLimite.Integracao);
    }

    private static async Task<string> LerArquivoAsync(HttpContext http, CancellationToken cancelamento)
    {
        using var leitor = new StreamReader(http.Request.Body, Encoding.UTF8);
        return await leitor.ReadToEndAsync(cancelamento).ConfigureAwait(false);
    }

    private static Guid IntegracaoDaRequisicao(HttpContext http) =>
        Guid.TryParse(http.User.FindFirstValue(ReivindicacoesDaTorre.Integracao), out var id)
            ? id
            // Inalcançável com a política aplicada: chegar aqui é defeito de configuração do endpoint.
            : throw new InvalidOperationException("Requisição de integração sem credencial reconhecida.");
}
