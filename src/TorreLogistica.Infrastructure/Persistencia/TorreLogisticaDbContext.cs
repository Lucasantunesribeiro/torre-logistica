using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using TorreLogistica.Application.Abstracoes.Identidade;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Domain.Auditoria;
using TorreLogistica.Domain.Clientes;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Frota;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.Domain.Operacao;
using TorreLogistica.Domain.Rastreamento;
using TorreLogistica.Domain.Rotas;

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
    IContextoDoTenant contextoDoTenant)
    : DbContext(opcoes), IContextoDePersistencia
{
    /// <summary>Nome da extensão geoespacial exigida pelo domínio.</summary>
    public const string ExtensaoPostGis = "postgis";

    private readonly IContextoDoTenant _contextoDoTenant =
        contextoDoTenant ?? throw new ArgumentNullException(nameof(contextoDoTenant));

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

        return await estrategia.ExecuteAsync(
            async cancelamentoDaTentativa =>
            {
                ChangeTracker.Clear();

                await using var transacao = await Database
                    .BeginTransactionAsync(cancelamentoDaTentativa)
                    .ConfigureAwait(false);

                var resultado = await operacao(cancelamentoDaTentativa).ConfigureAwait(false);

                await transacao.CommitAsync(cancelamentoDaTentativa).ConfigureAwait(false);
                return resultado;
            },
            cancelamento).ConfigureAwait(false);
    }

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
        return new GravacaoDePosicao(leitor.GetBoolean(0), leitor.GetBoolean(1));

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
