using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using TorreLogistica.Application.Abstracoes.Identidade;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Application.Abstracoes.Previsao;
using TorreLogistica.Application.Abstracoes.TempoReal;
using TorreLogistica.Domain.Abstracoes.Identificadores;
using TorreLogistica.Domain.Alertas;
using TorreLogistica.Domain.Auditoria;
using TorreLogistica.Domain.Clientes;
using TorreLogistica.Domain.Comprovantes;
using TorreLogistica.Domain.Comum;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Frota;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.Domain.Integracoes;
using TorreLogistica.Domain.Ocorrencias;
using TorreLogistica.Domain.Operacao;
using TorreLogistica.Domain.Previsao;
using TorreLogistica.Domain.Rastreamento;
using TorreLogistica.Domain.Rotas;
using TorreLogistica.Domain.Sincronizacao;
using TorreLogistica.Domain.Webhooks;

namespace TorreLogistica.Infrastructure.Persistencia;

/// <summary>
/// Contexto de persistência do domínio operacional da Torre Logística.
/// </summary>
/// <remarks>
/// <para>
/// PostgreSQL é a fonte de verdade do sistema. Toda entidade pertencente a um tenant
/// recebe filtro global pela organização da sessão autenticada.
/// </para>
/// <para>
/// Sem sessão, o filtro compara com <see cref="Guid.Empty"/> — valor que nenhuma
/// organização tem —, então a consulta não enxerga nada. A falha é fechada: esquecer de
/// resolver o tenant produz lista vazia e 404, nunca dado de outra organização.
/// </para>
/// </remarks>
public class TorreLogisticaDbContext(
    DbContextOptions<TorreLogisticaDbContext> opcoes,
    IContextoDoTenant contextoDoTenant,
    IPublicadorDeTempoReal? publicadorDeTempoReal = null,
    ISolicitacoesDeRecalculoDePrevisao? solicitacoesDeRecalculo = null,
    IGeradorDeIdentificador? identificadores = null)
    : DbContext(opcoes), IContextoDePersistencia
{
    /// <summary>Nome da extensão geoespacial exigida pelo domínio.</summary>
    public const string ExtensaoPostGis = "postgis";

    // Corpo do evento é contrato público: camelCase, como o resto da API, e sem indentação, porque a
    // assinatura HMAC é calculada sobre estes bytes exatos.
    private static readonly JsonSerializerOptions OpcoesDeSerializacaoDoOutbox = new(JsonSerializerDefaults.Web);

    // Mudanças na execução da entrega que alteram a previsão das paradas da rota.
    private static readonly HashSet<TipoDeEventoDaEntrega> EventosQueMudamAPrevisao =
    [
        TipoDeEventoDaEntrega.SaiuParaRota,
        TipoDeEventoDaEntrega.ChegadaRegistrada,
        TipoDeEventoDaEntrega.ProximidadeDetectada,
        TipoDeEventoDaEntrega.Entregue,
        TipoDeEventoDaEntrega.TentativaFrustrada,
        TipoDeEventoDaEntrega.Reatribuida,
    ];

    private readonly IContextoDoTenant _contextoDoTenant =
        contextoDoTenant ?? throw new ArgumentNullException(nameof(contextoDoTenant));

    // Avisos, sessões revogadas e pedidos de recálculo confirmados por gravação, à espera do commit para sair.
    private readonly List<NotificacaoDaOperacao> _notificacoesPendentes = [];
    private readonly HashSet<Guid> _sessoesRevogadasPendentes = [];
    private readonly HashSet<SolicitacaoDeRecalculo> _recalculosPendentes = [];

    /// <inheritdoc />
    public DbSet<Organizacao> Organizacoes => Set<Organizacao>();

    /// <inheritdoc />
    public DbSet<Usuario> Usuarios => Set<Usuario>();

    /// <inheritdoc />
    public DbSet<Sessao> Sessoes => Set<Sessao>();

    /// <inheritdoc />
    public DbSet<TokenDeRenovacao> TokensDeRenovacao => Set<TokenDeRenovacao>();

    /// <inheritdoc />
    public DbSet<EventoDeAuditoria> EventosDeAuditoria => Set<EventoDeAuditoria>();

    /// <inheritdoc />
    public DbSet<Motorista> Motoristas => Set<Motorista>();

    /// <inheritdoc />
    public DbSet<Veiculo> Veiculos => Set<Veiculo>();

    /// <inheritdoc />
    public DbSet<Hub> Hubs => Set<Hub>();

    /// <inheritdoc />
    public DbSet<Cliente> Clientes => Set<Cliente>();

    /// <inheritdoc />
    public DbSet<Destinatario> Destinatarios => Set<Destinatario>();

    /// <inheritdoc />
    public DbSet<Entrega> Entregas => Set<Entrega>();

    /// <inheritdoc />
    public DbSet<EventoDaEntrega> EventosDaEntrega => Set<EventoDaEntrega>();

    /// <inheritdoc />
    public DbSet<Rota> Rotas => Set<Rota>();

    /// <inheritdoc />
    public DbSet<Parada> Paradas => Set<Parada>();

    /// <inheritdoc />
    public DbSet<EventoDaRota> EventosDaRota => Set<EventoDaRota>();

    /// <inheritdoc />
    public DbSet<PosicaoDoMotorista> Posicoes => Set<PosicaoDoMotorista>();

    /// <inheritdoc />
    public DbSet<PosicaoAtual> PosicoesAtuais => Set<PosicaoAtual>();

    /// <inheritdoc />
    public DbSet<EstadoDeGeofence> EstadosDeGeofence => Set<EstadoDeGeofence>();

    /// <inheritdoc />
    public DbSet<EventoDeGeofence> EventosDeGeofence => Set<EventoDeGeofence>();

    /// <inheritdoc />
    public DbSet<PrevisaoDaEntrega> PrevisoesDaEntrega => Set<PrevisaoDaEntrega>();

    /// <inheritdoc />
    public DbSet<RegistroDePrevisao> RegistrosDePrevisao => Set<RegistroDePrevisao>();

    /// <inheritdoc />
    public DbSet<AlertaOperacional> AlertasOperacionais => Set<AlertaOperacional>();

    /// <inheritdoc />
    public DbSet<EventoDoAlerta> EventosDeAlerta => Set<EventoDoAlerta>();

    /// <inheritdoc />
    public DbSet<OperacaoDoCliente> OperacoesDoCliente => Set<OperacaoDoCliente>();

    /// <inheritdoc />
    public DbSet<Ocorrencia> Ocorrencias => Set<Ocorrencia>();

    /// <inheritdoc />
    public DbSet<Comprovante> Comprovantes => Set<Comprovante>();

    /// <inheritdoc />
    public DbSet<ArquivoDoComprovante> ArquivosDoComprovante => Set<ArquivoDoComprovante>();

    /// <inheritdoc />
    public DbSet<TokenDeRastreamento> TokensDeRastreamento => Set<TokenDeRastreamento>();

    /// <inheritdoc />
    public DbSet<Integracao> Integracoes => Set<Integracao>();

    /// <inheritdoc />
    public DbSet<RequisicaoDeIntegracao> RequisicoesDeIntegracao => Set<RequisicaoDeIntegracao>();

    /// <inheritdoc />
    public DbSet<ReferenciaExternaDaEntrega> ReferenciasExternasDeEntrega => Set<ReferenciaExternaDaEntrega>();

    /// <inheritdoc />
    public DbSet<MensagemDoOutbox> Outbox => Set<MensagemDoOutbox>();

    /// <inheritdoc />
    public DbSet<AssinaturaDeWebhook> AssinaturasDeWebhook => Set<AssinaturaDeWebhook>();

    /// <inheritdoc />
    public DbSet<EntregaDeWebhook> EntregasDeWebhook => Set<EntregaDeWebhook>();

    /// <inheritdoc />
    public DbSet<TentativaDeWebhook> TentativasDeWebhook => Set<TentativaDeWebhook>();

    /// <inheritdoc />
    public Guid? OrganizacaoDoTenant => _contextoDoTenant.OrganizacaoId;

    /// <inheritdoc />
    public async Task<PermanenciaNoLocal?> AvaliarPermanenciaAsync(
        Guid motoristaId,
        DateTimeOffset desde,
        double raioEmMetros,
        CancellationToken cancelamento)
    {
        // A permanência atual é a sequência de posições confiáveis, depois da última posição fora do raio, que
        // ficam a no máximo o raio da posição atual. Consulta crua: a organização vai explícita na condição.
        await using var comando = Database.GetDbConnection().CreateCommand();
        comando.Transaction = Database.CurrentTransaction?.GetDbTransaction();
        comando.CommandText = """
            WITH atual AS (
                SELECT localizacao, capturada_em
                FROM posicoes_atuais
                WHERE motorista_id = @motorista AND organizacao_id = @organizacao
            ),
            historico AS (
                SELECT posicoes.capturada_em, ST_DWithin(posicoes.localizacao, atual.localizacao, @raio) AS dentro
                FROM posicoes, atual
                WHERE posicoes.motorista_id = @motorista
                  AND posicoes.organizacao_id = @organizacao
                  AND posicoes.qualidade = 'Confiavel'
                  AND posicoes.capturada_em >= @desde
                  AND posicoes.capturada_em <= atual.capturada_em
            ),
            ultima_fora AS (
                SELECT coalesce(max(capturada_em), '-infinity'::timestamptz) AS em FROM historico WHERE NOT dentro
            )
            SELECT atual.capturada_em, min(historico.capturada_em), count(historico.capturada_em)
            FROM atual
            CROSS JOIN ultima_fora
            LEFT JOIN historico ON historico.dentro AND historico.capturada_em > ultima_fora.em
            GROUP BY atual.capturada_em
            """;
        comando.Parameters.Add(new NpgsqlParameter("motorista", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = motoristaId });
        comando.Parameters.Add(new NpgsqlParameter("organizacao", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = OrganizacaoIdDoFiltro });
        comando.Parameters.Add(new NpgsqlParameter("raio", NpgsqlTypes.NpgsqlDbType.Double) { Value = raioEmMetros });
        comando.Parameters.Add(new NpgsqlParameter("desde", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = desde.ToUniversalTime() });

        await Database.OpenConnectionAsync(cancelamento).ConfigureAwait(false);
        try
        {
            await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
            if (!await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
            {
                return null;
            }

            var ultimaCaptura = leitor.GetFieldValue<DateTimeOffset>(0);
            var inicio = leitor.IsDBNull(1) ? ultimaCaptura : leitor.GetFieldValue<DateTimeOffset>(1);
            return new PermanenciaNoLocal(inicio, ultimaCaptura, (int)leitor.GetInt64(2));
        }
        finally
        {
            await Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DistanciaAoDestino>> CalcularDistanciasAosDestinosAsync(
        IReadOnlyCollection<Guid> entregaIds,
        double latitude,
        double longitude,
        double raioEmMetros,
        double raioDeSaidaEmMetros,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(entregaIds);

        if (entregaIds.Count == 0)
        {
            return [];
        }

        // geography: distância geodésica em metros sobre o elipsoide WGS 84. Consulta crua não passa
        // pelo filtro global, então a organização vai explícita na condição.
        await using var comando = Database.GetDbConnection().CreateCommand();
        comando.Transaction = Database.CurrentTransaction?.GetDbTransaction();
        comando.CommandText = """
            WITH ponto AS (
                SELECT ST_SetSRID(ST_MakePoint(@longitude, @latitude), 4326)::geography AS localizacao
            )
            SELECT
                entregas.id,
                ST_Distance(entregas.localizacao, ponto.localizacao),
                ST_DWithin(entregas.localizacao, ponto.localizacao, @raio),
                ST_DWithin(entregas.localizacao, ponto.localizacao, @raioDeSaida)
            FROM entregas, ponto
            WHERE entregas.id = ANY(@entregas)
              AND entregas.organizacao_id = @organizacao
              AND entregas.localizacao IS NOT NULL
            """;
        comando.Parameters.Add(new NpgsqlParameter("latitude", NpgsqlTypes.NpgsqlDbType.Double) { Value = latitude });
        comando.Parameters.Add(new NpgsqlParameter("longitude", NpgsqlTypes.NpgsqlDbType.Double) { Value = longitude });
        comando.Parameters.Add(new NpgsqlParameter("raio", NpgsqlTypes.NpgsqlDbType.Double) { Value = raioEmMetros });
        comando.Parameters.Add(new NpgsqlParameter("raioDeSaida", NpgsqlTypes.NpgsqlDbType.Double) { Value = raioDeSaidaEmMetros });
        comando.Parameters.Add(new NpgsqlParameter("entregas", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid) { Value = entregaIds.ToArray() });
        comando.Parameters.Add(new NpgsqlParameter("organizacao", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = OrganizacaoIdDoFiltro });

        var distancias = new List<DistanciaAoDestino>(entregaIds.Count);
        await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
        while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
        {
            distancias.Add(new DistanciaAoDestino(leitor.GetGuid(0), leitor.GetDouble(1), leitor.GetBoolean(2), leitor.GetBoolean(3)));
        }

        return distancias;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<double>> CalcularDistanciasDoTrajetoAsync(
        IReadOnlyList<CoordenadaGeografica> pontos,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(pontos);

        if (pontos.Count < 2)
        {
            return [];
        }

        // Pares consecutivos pela ordem dos arrays. Só coordenadas: nenhum dado de tenant é lido.
        await using var comando = Database.GetDbConnection().CreateCommand();
        comando.Transaction = Database.CurrentTransaction?.GetDbTransaction();
        comando.CommandText = """
            SELECT ST_Distance(
                ST_SetSRID(ST_MakePoint(origem.longitude, origem.latitude), 4326)::geography,
                ST_SetSRID(ST_MakePoint(destino.longitude, destino.latitude), 4326)::geography)
            FROM unnest(@latitudes, @longitudes) WITH ORDINALITY AS origem(latitude, longitude, ordem)
            JOIN unnest(@latitudes, @longitudes) WITH ORDINALITY AS destino(latitude, longitude, ordem)
              ON destino.ordem = origem.ordem + 1
            ORDER BY origem.ordem
            """;
        comando.Parameters.Add(new NpgsqlParameter("latitudes", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Double)
        {
            Value = pontos.Select(ponto => ponto.Latitude).ToArray(),
        });
        comando.Parameters.Add(new NpgsqlParameter("longitudes", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Double)
        {
            Value = pontos.Select(ponto => ponto.Longitude).ToArray(),
        });

        // O provedor simulado e a contingência rodam fora de transação: a conexão é aberta e devolvida aqui.
        await Database.OpenConnectionAsync(cancelamento).ConfigureAwait(false);
        try
        {
            var distancias = new List<double>(pontos.Count - 1);
            await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
            while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
            {
                distancias.Add(leitor.GetDouble(0));
            }

            return distancias;
        }
        finally
        {
            await Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Organização usada nos filtros globais. O EF Core lê esta propriedade a cada
    /// consulta, na instância do contexto — por isso ela precisa morar aqui.
    /// </summary>
    private Guid OrganizacaoIdDoFiltro => _contextoDoTenant.OrganizacaoId ?? Guid.Empty;

    /// <inheritdoc />
    public async Task<T> ExecutarEmTransacaoAsync<T>(
        Func<CancellationToken, Task<T>> operacao,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(operacao);

        // Com nova tentativa em falha transitória habilitada, transação aberta fora da
        // estratégia de execução é recusada pelo provedor — e com razão: a repetição
        // precisa reexecutar a unidade inteira, não só o comando que falhou.
        var estrategia = Database.CreateExecutionStrategy();

        T resultadoConfirmado;
        try
        {
            resultadoConfirmado = await estrategia.ExecuteAsync(
                async cancelamentoDaTentativa =>
                {
                    ChangeTracker.Clear();

                    // Aviso de tentativa desfeita não sai: a próxima tentativa recoleta o que confirmar.
                    DescartarAvisosPendentes();

                    await using var transacao = await Database
                        .BeginTransactionAsync(cancelamentoDaTentativa)
                        .ConfigureAwait(false);

                    var resultado = await operacao(cancelamentoDaTentativa).ConfigureAwait(false);

                    await transacao.CommitAsync(cancelamentoDaTentativa).ConfigureAwait(false);
                    return resultado;
                },
                cancelamento).ConfigureAwait(false);
        }
        catch
        {
            DescartarAvisosPendentes();
            throw;
        }

        await DespacharAvisosAsync().ConfigureAwait(false);
        return resultadoConfirmado;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Único ponto de saída dos efeitos pós-commit: avisos de tempo real, conexões de sessões revogadas e
    /// pedidos de recálculo de previsão. Antes de gravar, recolhe o que a gravação vai confirmar; depois de
    /// gravar, despacha na hora se não há transação aberta, ou espera o commit de
    /// <see cref="ExecutarEmTransacaoAsync{T}"/>. Nenhum caso de uso precisa lembrar — e nada sai de
    /// mudança que não foi confirmada.
    /// </remarks>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var avisos = RecolherAvisosDeEntrega();
        avisos.AddRange(RecolherAvisosDeRisco());
        avisos.AddRange(RecolherAvisosDeAlerta());
        avisos.AddRange(RecolherAvisosDeOcorrencia());
        var sessoesRevogadas = RecolherSessoesRevogadas();
        var recalculos = RecolherRecalculosPorEntrega();

        // Outbox antes de gravar, e não depois: o evento entra na MESMA transação do fato. É isto que
        // impede "entrega concluída, aviso perdido" quando o processo cai entre uma coisa e outra.
        var eventos = RecolherEventosParaOutbox();

        if (eventos.Count > 0)
        {
            Outbox.AddRange(eventos);
        }

        var gravados = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);

        _notificacoesPendentes.AddRange(avisos);
        _sessoesRevogadasPendentes.UnionWith(sessoesRevogadas);
        _recalculosPendentes.UnionWith(recalculos);

        if (Database.CurrentTransaction is null)
        {
            await DespacharAvisosAsync().ConfigureAwait(false);
        }

        return gravados;
    }

    private List<NotificacaoDaOperacao> RecolherAvisosDeEntrega() =>
    [
        .. ChangeTracker.Entries<EventoDaEntrega>()
            .Where(entrada => entrada.State == EntityState.Added)
            .Select(entrada => entrada.Entity)
            .Where(evento => evento.Tipo is not (TipoDeEventoDaEntrega.DadosAlterados or TipoDeEventoDaEntrega.Reatribuida))
            .OrderBy(evento => evento.EntregaId)
            .ThenBy(evento => evento.Sequencia)
            .Select(evento => new StatusDaEntregaAlterado(
                evento.OrganizacaoId, evento.EntregaId, evento.StatusResultante, evento.Tipo, evento.Sequencia, evento.OcorridoEm)),
    ];

    /// <summary>
    /// Registros de previsão que mudam a situação do SLA. A primeira previsão conta como mudança só se não
    /// for Normal: toda entrega nasce Normal para quem acompanha.
    /// </summary>
    private List<NotificacaoDaOperacao> RecolherAvisosDeRisco() =>
    [
        .. ChangeTracker.Entries<RegistroDePrevisao>()
            .Where(entrada => entrada.State == EntityState.Added)
            .Select(entrada => entrada.Entity)
            .Where(registro => registro.Tipo != TipoDeRegistroDePrevisao.Encerrada
                && (registro.SituacaoAnterior ?? SituacaoDoSla.Normal) != registro.Situacao)
            .OrderBy(registro => registro.EntregaId)
            .ThenBy(registro => registro.Sequencia)
            .Select(registro => new RiscoDaEntregaAlterado(
                registro.OrganizacaoId,
                registro.EntregaId,
                registro.SituacaoAnterior,
                registro.Situacao,
                registro.MotivoDaSituacao,
                registro.ChegadaPrevistaEm,
                registro.FolgaEmSegundos,
                registro.Sequencia,
                registro.RegistradoEm)),
    ];

    /// <summary>Abertura, reabertura e resolução de alertas.</summary>
    private List<NotificacaoDaOperacao> RecolherAvisosDeAlerta()
    {
        var alertas = ChangeTracker.Entries<AlertaOperacional>().ToDictionary(entrada => entrada.Entity.Id, entrada => entrada.Entity);

        return
        [
            .. ChangeTracker.Entries<EventoDoAlerta>()
                .Where(entrada => entrada.State == EntityState.Added && alertas.ContainsKey(entrada.Entity.AlertaId))
                .Select(entrada => entrada.Entity)
                .OrderBy(evento => evento.AlertaId)
                .ThenBy(evento => evento.Sequencia)
                .Select(evento =>
                {
                    var alerta = alertas[evento.AlertaId];
                    return evento.Tipo is TipoDeEventoDoAlerta.Aberto or TipoDeEventoDoAlerta.Reaberto
                        ? (NotificacaoDaOperacao)new AlertaCriado(
                            alerta.OrganizacaoId,
                            alerta.Id,
                            alerta.Tipo,
                            alerta.Severidade,
                            alerta.EntregaId,
                            alerta.MotoristaId,
                            alerta.RotaId,
                            evento.Tipo == TipoDeEventoDoAlerta.Reaberto,
                            evento.Sequencia,
                            evento.OcorridoEm)
                        : new AlertaResolvido(
                            alerta.OrganizacaoId,
                            alerta.Id,
                            alerta.Tipo,
                            evento.Tipo == TipoDeEventoDoAlerta.ResolvidoPeloOperador ? FormaDeResolucao.PeloOperador : FormaDeResolucao.Automatica,
                            evento.Sequencia,
                            evento.OcorridoEm);
                }),
        ];
    }

    /// <summary>Ocorrências registradas nesta gravação.</summary>
    private List<NotificacaoDaOperacao> RecolherAvisosDeOcorrencia() =>
    [
        .. ChangeTracker.Entries<Ocorrencia>()
            .Where(entrada => entrada.State == EntityState.Added)
            .Select(entrada => entrada.Entity)
            .OrderBy(ocorrencia => ocorrencia.OcorridaEm)
            .Select(ocorrencia => new OcorrenciaCriada(
                ocorrencia.OrganizacaoId,
                ocorrencia.Id,
                ocorrencia.EntregaId,
                ocorrencia.RotaId,
                ocorrencia.MotoristaId,
                ocorrencia.Tipo,
                ocorrencia.Severidade,
                ocorrencia.MotivoDaTentativa,
                ocorrencia.Origem,
                ocorrencia.OcorridaEm)),
    ];

    private List<Guid> RecolherSessoesRevogadas() =>
    [
        .. ChangeTracker.Entries<Sessao>()
            .Where(entrada => entrada.State == EntityState.Modified
                && entrada.Property(sessao => sessao.RevogadaEm).OriginalValue is null
                && entrada.Entity.RevogadaEm is not null)
            .Select(entrada => entrada.Entity.Id),
    ];

    private List<SolicitacaoDeRecalculo> RecolherRecalculosPorEntrega() =>
        solicitacoesDeRecalculo is null
            ? []
            :
            [
                .. ChangeTracker.Entries<EventoDaEntrega>()
                    .Where(entrada => entrada.State == EntityState.Added && EventosQueMudamAPrevisao.Contains(entrada.Entity.Tipo))
                    .Select(entrada => new SolicitacaoDeRecalculo(
                        entrada.Entity.OrganizacaoId, OrigemDoRecalculo.Entrega, null, entrada.Entity.EntregaId, null)),
            ];

    /// <summary>
    /// Traduz os fatos desta gravação nos eventos públicos do contrato de webhook.
    /// </summary>
    /// <remarks>
    /// O corpo carrega só o que já está em mãos aqui — identificador, situação e instante. Buscar o código
    /// humano ou o endereço exigiria consulta no meio do <c>SaveChanges</c>, e o assinante que precisa de
    /// detalhe consulta a API com a credencial dele.
    /// </remarks>
    private List<MensagemDoOutbox> RecolherEventosParaOutbox()
    {
        var mensagens = new List<MensagemDoOutbox>();

        foreach (var evento in ChangeTracker.Entries<EventoDaEntrega>()
            .Where(entrada => entrada.State == EntityState.Added)
            .Select(entrada => entrada.Entity)
            .OrderBy(evento => evento.EntregaId)
            .ThenBy(evento => evento.Sequencia))
        {
            var tipo = evento.Tipo switch
            {
                TipoDeEventoDaEntrega.SaiuParaRota => TiposDeEventoDeWebhook.EntregaIniciada,
                TipoDeEventoDaEntrega.TentativaFrustrada => TiposDeEventoDeWebhook.TentativaFrustrada,
                TipoDeEventoDaEntrega.Entregue => TiposDeEventoDeWebhook.EntregaConcluida,
                _ => null,
            };

            if (tipo is null)
            {
                continue;
            }

            mensagens.Add(MensagemDoOutbox.Criar(
                NovoIdentificador(),
                evento.OrganizacaoId,
                tipo,
                evento.EntregaId,
                JsonSerializer.Serialize(
                    new
                    {
                        deliveryId = evento.EntregaId,
                        status = evento.StatusResultante.ToString(),
                        sequence = evento.Sequencia,
                        occurredAt = evento.OcorridoEm,
                    },
                    OpcoesDeSerializacaoDoOutbox),
                evento.OcorridoEm,
                evento.OcorridoEm));
        }

        foreach (var registro in ChangeTracker.Entries<RegistroDePrevisao>()
            .Where(entrada => entrada.State == EntityState.Added)
            .Select(entrada => entrada.Entity)
            .Where(registro => registro.Situacao is SituacaoDoSla.Risco or SituacaoDoSla.Atrasada
                && (registro.SituacaoAnterior ?? SituacaoDoSla.Normal) != registro.Situacao)
            .OrderBy(registro => registro.EntregaId)
            .ThenBy(registro => registro.Sequencia))
        {
            mensagens.Add(MensagemDoOutbox.Criar(
                NovoIdentificador(),
                registro.OrganizacaoId,
                TiposDeEventoDeWebhook.EntregaEmRisco,
                registro.EntregaId,
                JsonSerializer.Serialize(
                    new
                    {
                        deliveryId = registro.EntregaId,
                        situation = registro.Situacao.ToString(),
                        reason = registro.MotivoDaSituacao.ToString(),
                        estimatedArrival = registro.ChegadaPrevistaEm,
                        occurredAt = registro.RegistradoEm,
                    },
                    OpcoesDeSerializacaoDoOutbox),
                registro.RegistradoEm,
                registro.RegistradoEm));
        }

        return mensagens;
    }

    private Guid NovoIdentificador() => identificadores?.Novo() ?? Guid.CreateVersion7();

    private void DescartarAvisosPendentes()
    {
        _notificacoesPendentes.Clear();
        _sessoesRevogadasPendentes.Clear();
        _recalculosPendentes.Clear();
    }

    private async Task DespacharAvisosAsync()
    {
        var avisos = _notificacoesPendentes.ToList();
        var sessoes = _sessoesRevogadasPendentes.ToList();
        var recalculos = _recalculosPendentes.ToList();
        DescartarAvisosPendentes();

        // A mudança já foi confirmada: o aviso sai mesmo se a requisição for cancelada agora.
        if (publicadorDeTempoReal is not null && avisos.Count > 0)
        {
            await publicadorDeTempoReal.PublicarAsync(avisos, CancellationToken.None).ConfigureAwait(false);
        }

        if (publicadorDeTempoReal is not null && sessoes.Count > 0)
        {
            await publicadorDeTempoReal.EncerrarConexoesDasSessoesAsync(sessoes, CancellationToken.None).ConfigureAwait(false);
        }

        if (solicitacoesDeRecalculo is not null && recalculos.Count > 0)
        {
            solicitacoesDeRecalculo.Solicitar(recalculos);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ReservarMensagensDoOutboxAsync(
        int limite,
        DateTimeOffset agora,
        CancellationToken cancelamento) =>
        await Database
            .SqlQuery<Guid>(
                $"""
                 SELECT id FROM outbox
                 WHERE despachada_em IS NULL AND disponivel_em <= {agora}
                 ORDER BY disponivel_em, criada_em
                 LIMIT {limite}
                 FOR UPDATE SKIP LOCKED
                 """)
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ReservarEntregasDeWebhookAsync(
        int limite,
        DateTimeOffset agora,
        CancellationToken cancelamento) =>
        await Database
            .SqlQuery<Guid>(
                $"""
                 SELECT id FROM entregas_de_webhook
                 WHERE estado = 'Pendente' AND disponivel_em <= {agora}
                 ORDER BY disponivel_em, criada_em
                 LIMIT {limite}
                 FOR UPDATE SKIP LOCKED
                 """)
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task BloquearSessaoAsync(Guid sessaoId, CancellationToken cancelamento) =>
        Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM sessoes WHERE id = {sessaoId} FOR UPDATE",
            cancelamento);

    /// <inheritdoc />
    public Task BloquearOrganizacaoAsync(Guid organizacaoId, CancellationToken cancelamento) =>
        Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM organizacoes WHERE id = {organizacaoId} FOR UPDATE",
            cancelamento);

    /// <inheritdoc />
    public async Task<long> ReservarNumeroSequencialAsync(
        Guid organizacaoId,
        string serie,
        int ano,
        CancellationToken cancelamento)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serie);

        var transacao = Database.CurrentTransaction
            ?? throw new InvalidOperationException("A reserva de número sequencial exige transação aberta.");

        // INSERT ... ON CONFLICT trava a linha do contador até o fim da transação: a segunda
        // criação simultânea espera a primeira confirmar e recebe o número seguinte.
        await using var comando = Database.GetDbConnection().CreateCommand();
        comando.Transaction = transacao.GetDbTransaction();
        comando.CommandText = """
            INSERT INTO sequencias_de_codigo (organizacao_id, serie, ano, ultimo_numero)
            VALUES (@organizacao, @serie, @ano, 1)
            ON CONFLICT (organizacao_id, serie, ano)
            DO UPDATE SET ultimo_numero = sequencias_de_codigo.ultimo_numero + 1
            RETURNING ultimo_numero
            """;
        comando.Parameters.Add(new NpgsqlParameter("organizacao", organizacaoId));
        comando.Parameters.Add(new NpgsqlParameter("serie", serie));
        comando.Parameters.Add(new NpgsqlParameter("ano", ano));

        var resultado = await comando.ExecuteScalarAsync(cancelamento).ConfigureAwait(false);
        return Convert.ToInt64(resultado, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public Task SerializarRastreamentoDoMotoristaAsync(Guid motoristaId, CancellationToken cancelamento) =>
        Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({motoristaId.ToString()}, 0))",
            cancelamento);

    /// <inheritdoc />
    public Task SerializarSincronizacaoDoMotoristaAsync(Guid motoristaId, CancellationToken cancelamento) =>
        Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"sincronizacao:" + motoristaId.ToString()}, 0))",
            cancelamento);

    /// <inheritdoc />
    public async Task<GravacaoDePosicao> RegistrarPosicaoAsync(PosicaoDoMotorista posicao, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(posicao);

        var transacao = Database.CurrentTransaction
            ?? throw new InvalidOperationException("A gravação de posição exige transação aberta.");

        // Um comando só, atômico:
        // 1. histórico: a duplicata (mesmo evento do mesmo motorista) é descartada pelo índice único;
        // 2. posição atual: só se a posição entrou, é confiável e é mais recente por (captura, sequência).
        //    A comparação fica no WHERE do ON CONFLICT — dois lotes simultâneos não regridem a posição.
        await using var comando = Database.GetDbConnection().CreateCommand();
        comando.Transaction = transacao.GetDbTransaction();
        comando.CommandText = """
            WITH nova AS (
                INSERT INTO posicoes (
                    id, organizacao_id, motorista_id, rota_id, evento_de_localizacao_id, sequencia, localizacao,
                    precisao_em_metros, velocidade_em_metros_por_segundo, direcao_em_graus,
                    capturada_em, recebida_em, qualidade)
                VALUES (
                    @id, @organizacao, @motorista, @rota, @evento, @sequencia,
                    ST_SetSRID(ST_MakePoint(@longitude, @latitude), 4326)::geography,
                    @precisao, @velocidade, @direcao, @capturada, @recebida, @qualidade)
                ON CONFLICT (organizacao_id, motorista_id, evento_de_localizacao_id) DO NOTHING
                RETURNING 1
            ),
            atual AS (
                INSERT INTO posicoes_atuais (
                    motorista_id, organizacao_id, rota_id, evento_de_localizacao_id, sequencia, localizacao,
                    precisao_em_metros, velocidade_em_metros_por_segundo, direcao_em_graus,
                    capturada_em, recebida_em, atualizada_em)
                SELECT
                    @motorista, @organizacao, @rota, @evento, @sequencia,
                    ST_SetSRID(ST_MakePoint(@longitude, @latitude), 4326)::geography,
                    @precisao, @velocidade, @direcao, @capturada, @recebida, @recebida
                WHERE @confiavel AND EXISTS (SELECT 1 FROM nova)
                ON CONFLICT (motorista_id) DO UPDATE SET
                    organizacao_id = EXCLUDED.organizacao_id,
                    rota_id = EXCLUDED.rota_id,
                    evento_de_localizacao_id = EXCLUDED.evento_de_localizacao_id,
                    sequencia = EXCLUDED.sequencia,
                    localizacao = EXCLUDED.localizacao,
                    precisao_em_metros = EXCLUDED.precisao_em_metros,
                    velocidade_em_metros_por_segundo = EXCLUDED.velocidade_em_metros_por_segundo,
                    direcao_em_graus = EXCLUDED.direcao_em_graus,
                    capturada_em = EXCLUDED.capturada_em,
                    recebida_em = EXCLUDED.recebida_em,
                    atualizada_em = EXCLUDED.atualizada_em
                WHERE (posicoes_atuais.capturada_em, posicoes_atuais.sequencia) < (EXCLUDED.capturada_em, EXCLUDED.sequencia)
                RETURNING 1
            )
            SELECT EXISTS (SELECT 1 FROM nova), EXISTS (SELECT 1 FROM atual)
            """;

        Parametro(comando, "id", NpgsqlTypes.NpgsqlDbType.Uuid, posicao.Id);
        Parametro(comando, "organizacao", NpgsqlTypes.NpgsqlDbType.Uuid, posicao.OrganizacaoId);
        Parametro(comando, "motorista", NpgsqlTypes.NpgsqlDbType.Uuid, posicao.MotoristaId);
        Parametro(comando, "rota", NpgsqlTypes.NpgsqlDbType.Uuid, posicao.RotaId);
        Parametro(comando, "evento", NpgsqlTypes.NpgsqlDbType.Uuid, posicao.EventoDeLocalizacaoId);
        Parametro(comando, "sequencia", NpgsqlTypes.NpgsqlDbType.Bigint, posicao.Sequencia);
        Parametro(comando, "latitude", NpgsqlTypes.NpgsqlDbType.Double, posicao.Localizacao.Latitude);
        Parametro(comando, "longitude", NpgsqlTypes.NpgsqlDbType.Double, posicao.Localizacao.Longitude);
        Parametro(comando, "precisao", NpgsqlTypes.NpgsqlDbType.Double, posicao.PrecisaoEmMetros);
        Parametro(comando, "velocidade", NpgsqlTypes.NpgsqlDbType.Double, posicao.VelocidadeEmMetrosPorSegundo);
        Parametro(comando, "direcao", NpgsqlTypes.NpgsqlDbType.Double, posicao.DirecaoEmGraus);
        Parametro(comando, "capturada", NpgsqlTypes.NpgsqlDbType.TimestampTz, posicao.CapturadaEm);
        Parametro(comando, "recebida", NpgsqlTypes.NpgsqlDbType.TimestampTz, posicao.RecebidaEm);
        Parametro(comando, "qualidade", NpgsqlTypes.NpgsqlDbType.Text, posicao.Qualidade.ToString());
        Parametro(comando, "confiavel", NpgsqlTypes.NpgsqlDbType.Boolean, posicao.Qualidade == QualidadeDaPosicao.Confiavel);

        await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
        await leitor.ReadAsync(cancelamento).ConfigureAwait(false);
        var gravacao = new GravacaoDePosicao(leitor.GetBoolean(0), leitor.GetBoolean(1));

        if (gravacao.AtualizouPosicaoAtual)
        {
            // Um aviso por motorista no lote: só a posição mais recente interessa ao console.
            _notificacoesPendentes.RemoveAll(aviso => aviso is PosicaoDoMotoristaAtualizada anterior && anterior.MotoristaId == posicao.MotoristaId);
            _notificacoesPendentes.Add(new PosicaoDoMotoristaAtualizada(
                posicao.OrganizacaoId,
                posicao.MotoristaId,
                posicao.RotaId,
                posicao.Localizacao.Latitude,
                posicao.Localizacao.Longitude,
                posicao.PrecisaoEmMetros,
                posicao.VelocidadeEmMetrosPorSegundo,
                posicao.DirecaoEmGraus,
                posicao.Sequencia,
                posicao.CapturadaEm));

            // A posição atual mudou: a previsão das paradas da rota do motorista também. O conjunto colapsa
            // o lote inteiro num pedido só.
            if (solicitacoesDeRecalculo is not null)
            {
                _recalculosPendentes.Add(new SolicitacaoDeRecalculo(
                    posicao.OrganizacaoId, OrigemDoRecalculo.Posicao, posicao.RotaId, null, posicao.RotaId is null ? posicao.MotoristaId : null));
            }
        }

        return gravacao;

        static void Parametro(System.Data.Common.DbCommand comando, string nome, NpgsqlTypes.NpgsqlDbType tipo, object? valor) =>
            comando.Parameters.Add(new NpgsqlParameter(nome, tipo) { Value = valor ?? DBNull.Value });
    }

    /// <inheritdoc />
    public Task<bool> MotoristaJaFoiAtribuidoAsync(Guid entregaId, Guid motoristaId, CancellationToken cancelamento)
    {
        // SQL explícito: contém em jsonb (@>) sobre {"motoristaId": "..."}, gravado nos eventos de
        // atribuição. Consulta crua não passa pelo filtro global, então a organização vai na condição.
        var organizacaoId = OrganizacaoIdDoFiltro;
        var atribuida = nameof(TipoDeEventoDaEntrega.Atribuida);
        var reatribuida = nameof(TipoDeEventoDaEntrega.Reatribuida);
        var motorista = motoristaId.ToString();

        return Database
            .SqlQuery<bool>($"""
                SELECT EXISTS (
                    SELECT 1
                    FROM eventos_da_entrega
                    WHERE entrega_id = {entregaId}
                      AND organizacao_id = {organizacaoId}
                      AND tipo IN ({atribuida}, {reatribuida})
                      AND dados @> jsonb_build_object('motoristaId', {motorista})
                ) AS "Value"
                """)
            .SingleAsync(cancelamento);
    }

    /// <inheritdoc />
    public void DefinirVersaoEsperada(object entidade, uint versao)
    {
        ArgumentNullException.ThrowIfNull(entidade);
        Entry(entidade).Property("Versao").OriginalValue = versao;
    }

    /// <inheritdoc />
    public bool EhViolacaoDeUnicidade(Exception excecao, string nomeDaRestricao)
    {
        for (var atual = excecao; atual is not null; atual = atual.InnerException)
        {
            if (atual is PostgresException
                {
                    SqlState: PostgresErrorCodes.UniqueViolation,
                } postgres
                && string.Equals(postgres.ConstraintName, nomeDaRestricao, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        // Sem PostGIS não existe distância, raio nem geofence: a extensão é parte do
        // schema, não um pré-requisito informal de ambiente.
        modelBuilder.HasPostgresExtension(ExtensaoPostGis);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TorreLogisticaDbContext).Assembly);

        // Filtros de tenant ficam aqui, e não nas classes de configuração, porque
        // precisam ler o tenant desta instância de contexto a cada consulta.
        modelBuilder.Entity<Usuario>()
            .HasQueryFilter(usuario => usuario.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<Sessao>()
            .HasQueryFilter(sessao => sessao.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<TokenDeRenovacao>()
            .HasQueryFilter(token => token.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<EventoDeAuditoria>()
            .HasQueryFilter(evento => evento.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<Motorista>()
            .HasQueryFilter(motorista => motorista.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<Veiculo>()
            .HasQueryFilter(veiculo => veiculo.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<Hub>()
            .HasQueryFilter(hub => hub.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<Cliente>()
            .HasQueryFilter(cliente => cliente.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<Destinatario>()
            .HasQueryFilter(destinatario => destinatario.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<Entrega>()
            .HasQueryFilter(entrega => entrega.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<EventoDaEntrega>()
            .HasQueryFilter(evento => evento.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<Rota>()
            .HasQueryFilter(rota => rota.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<Parada>()
            .HasQueryFilter(parada => parada.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<EventoDaRota>()
            .HasQueryFilter(evento => evento.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<PosicaoDoMotorista>()
            .HasQueryFilter(posicao => posicao.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<PosicaoAtual>()
            .HasQueryFilter(posicao => posicao.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<EstadoDeGeofence>()
            .HasQueryFilter(estado => estado.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<EventoDeGeofence>()
            .HasQueryFilter(evento => evento.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<PrevisaoDaEntrega>()
            .HasQueryFilter(previsao => previsao.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<RegistroDePrevisao>()
            .HasQueryFilter(registro => registro.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<AlertaOperacional>()
            .HasQueryFilter(alerta => alerta.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<EventoDoAlerta>()
            .HasQueryFilter(evento => evento.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<OperacaoDoCliente>()
            .HasQueryFilter(operacao => operacao.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<Ocorrencia>()
            .HasQueryFilter(ocorrencia => ocorrencia.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<Comprovante>()
            .HasQueryFilter(comprovante => comprovante.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<ArquivoDoComprovante>()
            .HasQueryFilter(arquivo => arquivo.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<TokenDeRastreamento>()
            .HasQueryFilter(token => token.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<Integracao>()
            .HasQueryFilter(integracao => integracao.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<RequisicaoDeIntegracao>()
            .HasQueryFilter(requisicao => requisicao.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<ReferenciaExternaDaEntrega>()
            .HasQueryFilter(referencia => referencia.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<AssinaturaDeWebhook>()
            .HasQueryFilter(assinatura => assinatura.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<EntregaDeWebhook>()
            .HasQueryFilter(entrega => entrega.OrganizacaoId == OrganizacaoIdDoFiltro);
        modelBuilder.Entity<TentativaDeWebhook>()
            .HasQueryFilter(tentativa => tentativa.OrganizacaoId == OrganizacaoIdDoFiltro);

        // O outbox NÃO leva filtro de tenant: quem o lê é um processo de fundo, sem sessão, e precisa
        // enxergar as mensagens de todas as organizações. A organização viaja dentro da mensagem e é ela
        // que decide o destino — mesma escolha do rastreamento público (ADR 0024).
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        base.ConfigureConventions(configurationBuilder);

        // Todo instante do sistema é UTC. Fuso é assunto de apresentação.
        configurationBuilder.Properties<DateTimeOffset>().HaveColumnType("timestamptz");
        configurationBuilder.Properties<DateTime>().HaveColumnType("timestamptz");
    }
}
