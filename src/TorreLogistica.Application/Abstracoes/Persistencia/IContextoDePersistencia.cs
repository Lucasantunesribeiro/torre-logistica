using Microsoft.EntityFrameworkCore;
using TorreLogistica.Domain.Alertas;
using TorreLogistica.Domain.Auditoria;
using TorreLogistica.Domain.Clientes;
using TorreLogistica.Domain.Comum;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Frota;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.Domain.Operacao;
using TorreLogistica.Domain.Previsao;
using TorreLogistica.Domain.Rastreamento;
using TorreLogistica.Domain.Rotas;
using TorreLogistica.Domain.Sincronizacao;

namespace TorreLogistica.Application.Abstracoes.Persistencia;

/// <summary>Resultado da gravação de uma posição.</summary>
/// <param name="Inserida">Entrou no histórico; <see langword="false"/> quando era duplicata.</param>
/// <param name="AtualizouPosicaoAtual">Avançou a posição atual do motorista.</param>
public sealed record GravacaoDePosicao(bool Inserida, bool AtualizouPosicaoAtual);

/// <summary>Distância de um ponto ao destino de uma entrega.</summary>
/// <param name="EntregaId">Entrega.</param>
/// <param name="DistanciaEmMetros">Distância geodésica.</param>
/// <param name="DentroDoRaio">A no máximo o raio.</param>
/// <param name="DentroDaMargemDeSaida">A no máximo o raio mais a histerese.</param>
public sealed record DistanciaAoDestino(Guid EntregaId, double DistanciaEmMetros, bool DentroDoRaio, bool DentroDaMargemDeSaida);

/// <summary>
/// Acesso da camada de aplicação ao armazenamento operacional.
/// </summary>
/// <remarks>
/// <para>
/// Expõe os <see cref="DbSet{TEntity}"/> diretamente, sem repositório genérico por cima:
/// um repositório que só repassa chamadas ao EF Core não isola nada e esconde LINQ,
/// projeção e <c>ExecuteUpdate</c>. Ver <c>docs/adr/0008-application-usa-ef-core-sem-repositorio.md</c>.
/// </para>
/// <para>
/// Os conjuntos de entidades pertencentes a um tenant chegam aqui já filtrados pela
/// organização da sessão autenticada. Consultar outro tenant exige
/// <c>IgnoreQueryFilters()</c> explícito — e só os fluxos que ainda não têm sessão
/// (login, renovação, validação de sessão) fazem isso.
/// </para>
/// </remarks>
public interface IContextoDePersistencia
{
    /// <summary>Organizações. Não é filtrado: é o próprio tenant.</summary>
    DbSet<Organizacao> Organizacoes { get; }

    /// <summary>Contas, filtradas pelo tenant.</summary>
    DbSet<Usuario> Usuarios { get; }

    /// <summary>Sessões, filtradas pelo tenant.</summary>
    DbSet<Sessao> Sessoes { get; }

    /// <summary>Tokens de renovação, filtrados pelo tenant.</summary>
    DbSet<TokenDeRenovacao> TokensDeRenovacao { get; }

    /// <summary>Trilha de auditoria, filtrada pelo tenant.</summary>
    DbSet<EventoDeAuditoria> EventosDeAuditoria { get; }

    /// <summary>Motoristas, filtrados pelo tenant.</summary>
    DbSet<Motorista> Motoristas { get; }

    /// <summary>Veículos, filtrados pelo tenant.</summary>
    DbSet<Veiculo> Veiculos { get; }

    /// <summary>Hubs, filtrados pelo tenant.</summary>
    DbSet<Hub> Hubs { get; }

    /// <summary>Clientes, filtrados pelo tenant.</summary>
    DbSet<Cliente> Clientes { get; }

    /// <summary>Destinatários, filtrados pelo tenant.</summary>
    DbSet<Destinatario> Destinatarios { get; }

    /// <summary>Entregas, filtradas pelo tenant.</summary>
    DbSet<Entrega> Entregas { get; }

    /// <summary>Timeline das entregas, filtrada pelo tenant. Somente-inserção.</summary>
    DbSet<EventoDaEntrega> EventosDaEntrega { get; }

    /// <summary>Rotas, filtradas pelo tenant.</summary>
    DbSet<Rota> Rotas { get; }

    /// <summary>Paradas das rotas, filtradas pelo tenant.</summary>
    DbSet<Parada> Paradas { get; }

    /// <summary>Timeline das rotas, filtrada pelo tenant. Somente-inserção.</summary>
    DbSet<EventoDaRota> EventosDaRota { get; }

    /// <summary>Histórico de posições GPS, filtrado pelo tenant. Somente leitura por aqui.</summary>
    DbSet<PosicaoDoMotorista> Posicoes { get; }

    /// <summary>Posição atual de cada motorista, filtrada pelo tenant. Somente leitura por aqui.</summary>
    DbSet<PosicaoAtual> PosicoesAtuais { get; }

    /// <summary>Estado da geofence do destino por entrega, filtrado pelo tenant.</summary>
    DbSet<EstadoDeGeofence> EstadosDeGeofence { get; }

    /// <summary>Entradas e saídas das geofences, filtradas pelo tenant. Somente-inserção.</summary>
    DbSet<EventoDeGeofence> EventosDeGeofence { get; }

    /// <summary>Previsão atual de chegada e situação do SLA por entrega, filtrada pelo tenant.</summary>
    DbSet<PrevisaoDaEntrega> PrevisoesDaEntrega { get; }

    /// <summary>Histórico de previsões, filtrado pelo tenant. Somente-inserção.</summary>
    DbSet<RegistroDePrevisao> RegistrosDePrevisao { get; }

    /// <summary>Alertas operacionais, filtrados pelo tenant.</summary>
    DbSet<AlertaOperacional> AlertasOperacionais { get; }

    /// <summary>Ciclo de vida dos alertas, filtrado pelo tenant. Somente-inserção.</summary>
    DbSet<EventoDoAlerta> EventosDeAlerta { get; }

    /// <summary>Operações feitas no aparelho do motorista, pelo identificador do aparelho. Somente-inserção.</summary>
    DbSet<OperacaoDoCliente> OperacoesDoCliente { get; }

    /// <summary>Organização que o filtro de tenant deste contexto enxerga, ou <see langword="null"/>.</summary>
    Guid? OrganizacaoDoTenant { get; }

    /// <summary>
    /// Quanto tempo o motorista está no mesmo lugar: a sequência mais recente de posições confiáveis, desde
    /// <paramref name="desde"/>, a no máximo <paramref name="raioEmMetros"/> da posição atual — pelo PostGIS
    /// (<c>ST_DWithin</c> sobre <c>geography</c>).
    /// </summary>
    /// <returns>A permanência, ou <see langword="null"/> sem posição atual.</returns>
    Task<PermanenciaNoLocal?> AvaliarPermanenciaAsync(Guid motoristaId, DateTimeOffset desde, double raioEmMetros, CancellationToken cancelamento);

    /// <summary>
    /// Distância geodésica entre cada par de pontos consecutivos, em metros — calculada pelo PostGIS
    /// (<c>ST_Distance</c> sobre <c>geography</c>). Um valor a menos que a quantidade de pontos.
    /// </summary>
    Task<IReadOnlyList<double>> CalcularDistanciasDoTrajetoAsync(IReadOnlyList<CoordenadaGeografica> pontos, CancellationToken cancelamento);

    /// <summary>
    /// Distância geodésica do ponto a cada destino de entrega, e se ele está dentro do raio e da margem
    /// de saída — calculadas pelo PostGIS (<c>ST_Distance</c>, <c>ST_DWithin</c> sobre <c>geography</c>).
    /// </summary>
    /// <remarks>Entregas sem coordenada de destino, ou de outra organização, não aparecem.</remarks>
    Task<IReadOnlyList<DistanciaAoDestino>> CalcularDistanciasAosDestinosAsync(
        IReadOnlyCollection<Guid> entregaIds,
        double latitude,
        double longitude,
        double raioEmMetros,
        double raioDeSaidaEmMetros,
        CancellationToken cancelamento);

    /// <summary>
    /// Grava a posição no histórico e, se ela for confiável e mais recente, avança a posição
    /// atual — num único comando atômico.
    /// </summary>
    /// <remarks>
    /// Exige transação aberta. A duplicata (mesmo evento do mesmo motorista) é descartada pelo
    /// índice único, e a posição atual só avança por (captura, sequência) maior que a vigente:
    /// dois lotes simultâneos do mesmo motorista não conseguem regredi-la.
    /// </remarks>
    Task<GravacaoDePosicao> RegistrarPosicaoAsync(PosicaoDoMotorista posicao, CancellationToken cancelamento);

    /// <summary>
    /// Serializa, até o fim da transação, a gravação de posições do mesmo motorista.
    /// </summary>
    /// <remarks>
    /// Dois lotes do mesmo motorista com posições em comum se travariam um ao outro: cada um
    /// esperando a posição inserida pelo outro e a linha da posição atual já travada. Telemetria de
    /// um aparelho é sequencial por natureza; enfileirar os lotes dele elimina o impasse.
    /// </remarks>
    Task SerializarRastreamentoDoMotoristaAsync(Guid motoristaId, CancellationToken cancelamento);

    /// <summary>
    /// Enfileira, até o fim da transação, a sincronização de operações do motorista.
    /// </summary>
    /// <remarks>
    /// A mesma operação reenviada enquanto a primeira ainda está gravando espera e encontra o registro
    /// confirmado — em vez de executar o comando em paralelo e perder a corrida no banco. As ações de um
    /// aparelho são sequenciais por natureza; enfileirá-las não custa nada ao motorista.
    /// </remarks>
    Task SerializarSincronizacaoDoMotoristaAsync(Guid motoristaId, CancellationToken cancelamento);

    /// <summary>
    /// Reserva o próximo número de uma série de código humano, por organização e ano.
    /// </summary>
    /// <remarks>
    /// Exige transação aberta: a reserva e a gravação do registro que usa o número precisam
    /// confirmar juntas. Se a gravação falhar, a reserva volta atrás e o número não se perde.
    /// Criações simultâneas na mesma organização se enfileiram na linha do contador.
    /// </remarks>
    Task<long> ReservarNumeroSequencialAsync(Guid organizacaoId, string serie, int ano, CancellationToken cancelamento);

    /// <summary>Grava as alterações pendentes.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Executa a operação dentro de uma transação, com a estratégia de nova tentativa
    /// do provedor.
    /// </summary>
    /// <remarks>
    /// A operação pode rodar mais de uma vez quando a conexão falha de forma transitória.
    /// Por isso ela precisa recarregar o que usa, e as alterações pendentes são
    /// descartadas antes de cada tentativa.
    /// </remarks>
    Task<T> ExecutarEmTransacaoAsync<T>(
        Func<CancellationToken, Task<T>> operacao,
        CancellationToken cancelamento);

    /// <summary>
    /// Trava a linha da sessão até o fim da transação (<c>SELECT … FOR UPDATE</c>),
    /// serializando renovações e encerramentos da mesma família.
    /// </summary>
    Task BloquearSessaoAsync(Guid sessaoId, CancellationToken cancelamento);

    /// <summary>
    /// Trava a linha da organização até o fim da transação, serializando alterações que
    /// dependem do conjunto de administradores.
    /// </summary>
    Task BloquearOrganizacaoAsync(Guid organizacaoId, CancellationToken cancelamento);

    /// <summary>
    /// Declara a versão que o cliente leu. Se a linha mudou desde então, a gravação falha
    /// com conflito de concorrência em vez de sobrescrever.
    /// </summary>
    void DefinirVersaoEsperada(object entidade, uint versao);

    /// <summary>
    /// Indica se o motorista já foi atribuído à entrega em algum momento da timeline.
    /// </summary>
    /// <remarks>
    /// Distingue, para o motorista, "esta entrega foi passada a outro motorista" (409, estado
    /// recuperável no aplicativo) de "esta entrega nunca foi sua" (404, nada a revelar).
    /// </remarks>
    Task<bool> MotoristaJaFoiAtribuidoAsync(Guid entregaId, Guid motoristaId, CancellationToken cancelamento);

    /// <summary>Indica se a exceção é violação da restrição de unicidade informada.</summary>
    bool EhViolacaoDeUnicidade(Exception excecao, string nomeDaRestricao);
}
