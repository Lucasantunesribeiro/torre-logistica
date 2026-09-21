using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TorreLogistica.Application.Webhooks;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// O que acontece quando a infraestrutura pisca.
/// </summary>
/// <remarks>
/// Os outros modos de falha que a fase pede já têm dono: provedor de rotas que não responde
/// (<c>ProvedorQueNaoRespondeNaoSeguraAIngestaoECaiNaContingencia</c>), assinante de webhook fora do ar
/// (<c>AssinanteQuebradoEhRetentadoEDepoisDesisteDeFormaVisivel</c>) e mensagem repetida
/// (<c>SincronizacaoTestes</c>, idempotência por identificador do cliente). O que faltava era o caso mais
/// bruto: o banco sumir no meio do trabalho.
/// </remarks>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class ResilienciaTestes(ContainerPostgis banco) : TesteDeWebhook(banco)
{
    /// <summary>
    /// Conexão derrubada não vira erro para quem está operando.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reinício do banco, failover e queda de rede aparecem para a aplicação exatamente assim: a conexão
    /// que estava aberta morre.
    /// </para>
    /// <para>
    /// O teste afirma o que de fato acontece, e não o que seria bonito: a <b>primeira</b> operação depois
    /// da queda pode falhar, porque o PostgreSQL devolve <c>57P01</c> — encerramento administrativo — e o
    /// provedor não classifica esse código como falha transitória, então a estratégia de nova tentativa
    /// não entra. Da segunda em diante o pool já descartou a conexão morta e tudo volta sozinho, sem
    /// reiniciar processo. Ampliar a classificação para retentar <c>57P01</c> foi considerado e recusado:
    /// repetir escrita por conta própria custa mais do que um erro isolado num reinício planejado.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ConexaoDerrubadaNaoDerrubaAOperacao()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);

        var (clienteId, destinatarioId) = await CriarClienteEDestinatarioAsync(http, cenario.Administrador);

        await DerrubarConexoesAsync();

        // O pool guarda várias conexões, e cada requisição pega uma. As que estavam abertas quando o
        // banco caiu falham ao serem usadas pela primeira vez; o pool as descarta e abre novas. O que se
        // prova aqui é que isso se resolve sozinho, sem reiniciar processo e sem intervenção — não que
        // a primeira tentativa dê certo.
        var tentativasAteVoltar = await TentarAteVoltarAsync(async () =>
        {
            using var resposta = await EnviarAsync(
                http, HttpMethod.Post, "/api/entregas", cenario.Administrador, RoteirosDeEntrega.Corpo(clienteId, destinatarioId));
            return resposta.StatusCode == HttpStatusCode.Created;
        });

        Assert.True(tentativasAteVoltar > 0, "A escrita nunca voltou depois da queda.");

        var leituraVoltou = await TentarAteVoltarAsync(async () =>
        {
            using var resposta = await EnviarAsync(http, HttpMethod.Get, "/api/entregas?tamanhoDaPagina=10", cenario.Administrador);
            return resposta.StatusCode == HttpStatusCode.OK;
        });

        Assert.True(leituraVoltou > 0, "A leitura nunca voltou depois da queda.");

        // A sonda de prontidão volta a aprovar: ela consulta o banco de verdade.
        var prontidaoVoltou = await TentarAteVoltarAsync(async () =>
        {
            using var resposta = await http.GetAsync(new Uri("/health/ready", UriKind.Relative), Cancelamento);
            return resposta.StatusCode == HttpStatusCode.OK;
        });

        Assert.True(prontidaoVoltou > 0, "A sonda de prontidão nunca voltou a aprovar.");
    }

    /// <summary>
    /// Queda no meio do despacho não perde nem duplica evento.
    /// </summary>
    /// <remarks>
    /// É o cenário de reinício de worker: o processo some no meio da rodada. Como a criação das entregas de
    /// webhook e a marcação da mensagem acontecem na mesma transação, ou as duas valem ou nenhuma vale —
    /// e a rodada seguinte encontra o trabalho intacto, sem repetir o que já saiu.
    /// </remarks>
    [Fact]
    public async Task QuedaNoMeioDoDespachoNaoPerdeNemDuplicaEvento()
    {
        using var http = Cliente();
        var cenario = await CenarioAsync(http);
        await AssinarAsync(http, cenario);

        await OkAsync(http, HttpMethod.Post, $"/api/motorista/entregas/{cenario.Entrega}/conclusao", cenario.TokenDoMotorista, null);

        var pendentesAntes = await ContarAsync(
            "SELECT count(*) FROM outbox WHERE organizacao_id = @id AND despachada_em IS NULL", cenario.Organizacao.Id);
        Assert.True(pendentesAntes > 0, "A conclusão devia ter deixado evento no outbox.");

        // O processo cai: a conexão morre antes de a rodada confirmar.
        await DerrubarConexoesAsync();
        await DespacharTolerandoQuedaAsync();

        // Nada se perdeu: ou a rodada confirmou, ou a mensagem continua lá esperando.
        var pendentesDepois = await ContarAsync(
            "SELECT count(*) FROM outbox WHERE organizacao_id = @id AND despachada_em IS NULL", cenario.Organizacao.Id);
        var entregasDepois = await ContarAsync(
            "SELECT count(*) FROM entregas_de_webhook WHERE organizacao_id = @id", cenario.Organizacao.Id);
        Assert.True(
            pendentesDepois > 0 || entregasDepois > 0,
            "A queda não pode ter engolido a mensagem: ou ela continua pendente, ou virou entrega.");

        // Rodadas seguintes terminam o serviço, e o resultado é o mesmo de uma execução sem queda.
        await DespacharAsync(cenario.Organizacao.Id);

        Assert.Equal(0, await ContarAsync(
            "SELECT count(*) FROM outbox WHERE organizacao_id = @id AND despachada_em IS NULL", cenario.Organizacao.Id));

        var porMensagem = await Banco.ConsultarEscalarAsync(
            """
            SELECT coalesce(max(total)::text, '0') FROM (
                SELECT count(*) AS total
                FROM entregas_de_webhook
                WHERE organizacao_id = @id
                GROUP BY mensagem_id, assinatura_id
            ) AS agrupado
            """,
            ("id", cenario.Organizacao.Id));

        Assert.Equal("1", porMensagem);
    }

    /// <inheritdoc />
    /// <remarks>
    /// O pool do Npgsql é global por cadeia de conexão, e as conexões que esta classe mata continuariam
    /// nele. Limpar na entrada e na saída mantém o estrago dentro do teste que o causou — sem isso, o
    /// teste seguinte falharia ao montar o próprio cenário, por uma queda que não provocou.
    /// </remarks>
    public override async ValueTask InitializeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await base.InitializeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await base.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Repete a operação até ela voltar a funcionar, devolvendo em quantas tentativas foi.
    /// </summary>
    /// <remarks>
    /// Cada tentativa pode estourar exceção em vez de devolver 500: o servidor de teste relança o que o
    /// pipeline não tratou. Para o que se mede aqui os dois casos são a mesma coisa — não funcionou ainda.
    /// </remarks>
    private static async Task<int> TentarAteVoltarAsync(Func<Task<bool>> operacao)
    {
        for (var tentativa = 1; tentativa <= 10; tentativa++)
        {
            try
            {
                if (await operacao().ConfigureAwait(false))
                {
                    return tentativa;
                }
            }
            catch (Exception excecao) when (excecao is not OperationCanceledException)
            {
                // Conexão morta encontrada no pool: segue para a próxima tentativa.
            }
        }

        return 0;
    }

    private async Task DespacharTolerandoQuedaAsync()
    {
        try
        {
            using var escopo = Fabrica.Services.CreateScope();
            await escopo.ServiceProvider.GetRequiredService<DespachoDeWebhooks>().DespacharLoteAsync(Cancelamento);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException)
        {
            // Falhar aqui é o cenário, não o defeito: o que importa é o estado que sobrou no banco.
        }
    }

    /// <summary>
    /// Mata as conexões que a API sob teste mantém abertas, como um reinício do banco faria.
    /// </summary>
    /// <remarks>
    /// Só as dela, identificadas pelo nome da aplicação. Derrubar tudo atingiria as conexões que a
    /// própria suíte usa para montar cenário, e o teste seguinte quebraria por um estrago alheio.
    /// </remarks>
    private Task<int> DerrubarConexoesAsync() =>
        Banco.ExecutarAsync(
            """
            SELECT pg_terminate_backend(pid)
            FROM pg_stat_activity
            WHERE datname = current_database()
              AND application_name = @aplicacao
              AND pid <> pg_backend_pid()
            """,
            ("aplicacao", FabricaDaApi.NomeDaAplicacao));

    private async Task<int> ContarAsync(string sql, Guid organizacaoId) =>
        int.Parse(
            await Banco.ConsultarEscalarAsync(sql, ("id", organizacaoId)) ?? "0",
            System.Globalization.CultureInfo.InvariantCulture);
}
