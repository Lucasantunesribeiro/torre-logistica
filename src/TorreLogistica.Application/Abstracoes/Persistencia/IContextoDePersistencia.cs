using Microsoft.EntityFrameworkCore;
using TorreLogistica.Domain.Auditoria;
using TorreLogistica.Domain.Clientes;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Frota;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.Domain.Operacao;
using TorreLogistica.Domain.Rastreamento;
using TorreLogistica.Domain.Rotas;

namespace TorreLogistica.Application.Abstracoes.Persistencia;

/// <summary>Resultado da gravação de uma posição.</summary>
/// <param name="Inserida">Entrou no histórico; <see langword="false"/> quando era duplicata.</param>
/// <param name="AtualizouPosicaoAtual">Avançou a posição atual do motorista.</param>
public sealed record GravacaoDePosicao(bool Inserida, bool AtualizouPosicaoAtual);

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
