using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Application.Cadastros;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Sincronizacao;

namespace TorreLogistica.Application.Execucao;

/// <summary>Operação como enviada pelo aparelho. Campos opcionais para recusar item a item.</summary>
public sealed record OperacaoEnviada(
    Guid? OperacaoDoClienteId,
    TipoDeOperacaoDoCliente? Tipo,
    Guid? AlvoId,
    MotivoDeTentativaFrustrada? Motivo,
    DateTimeOffset? CriadaEm);

/// <summary>Desfecho de uma operação na sincronização.</summary>
public enum DesfechoDaSincronizacao
{
    /// <summary>Aplicada. Não reenviar.</summary>
    Aplicada = 1,

    /// <summary>O estado mudou; a operação não vale mais. Não reenviar; mostrar ao motorista.</summary>
    Conflito = 2,

    /// <summary>Recusada por regra. Não reenviar; mostrar ao motorista.</summary>
    Recusada = 3,

    /// <summary>Gravação concorrente na mesma entrega; nada foi aplicado. Reenviar.</summary>
    TentarDeNovo = 4,
}

/// <summary>Resultado de uma operação, na ordem do envio.</summary>
/// <param name="OperacaoDoClienteId">Identificador enviado.</param>
/// <param name="Desfecho">Desfecho.</param>
/// <param name="Repetida">Já tinha sido processada antes: o desfecho é o registrado, e nada foi executado de novo.</param>
/// <param name="Codigo">Código do conflito ou da recusa.</param>
/// <param name="Mensagem">Mensagem para o motorista.</param>
public sealed record ResultadoDeOperacaoSincronizada(
    Guid? OperacaoDoClienteId,
    DesfechoDaSincronizacao Desfecho,
    bool Repetida,
    string? Codigo,
    string? Mensagem);

/// <summary>Resultado do lote.</summary>
public sealed record ResultadoDaSincronizacao(IReadOnlyList<ResultadoDeOperacaoSincronizada> Resultados);

/// <summary>
/// Aplica as operações feitas no aparelho, cada uma exatamente uma vez.
/// </summary>
/// <remarks>
/// <para>
/// Cada operação roda na própria transação, na ordem do envio — saída antes da chegada, chegada antes da
/// conclusão. O registro da operação é gravado junto com o efeito: ou os dois ficam, ou nenhum. Repetição
/// encontra o registro e devolve o mesmo desfecho, sem executar de novo.
/// </para>
/// <para>
/// O comando em si é o mesmo do caminho online (<see cref="ExecucaoPeloMotorista"/>): mesma máquina de
/// estados, mesmas regras de propriedade. Operação feita offline não tem atalho: se a entrega foi
/// cancelada ou passada a outro motorista enquanto o aparelho estava sem conexão, o desfecho é conflito, e
/// a mudança da operação prevalece.
/// </para>
/// </remarks>
public sealed class SincronizacaoDoMotorista(
    SuporteDeCadastro suporte,
    ExecucaoPeloMotorista execucao,
    MetricasDeSincronizacao metricas,
    ILogger<SincronizacaoDoMotorista> log)
{
    /// <summary>Processa o lote.</summary>
    public async Task<ResultadoDaSincronizacao> ProcessarAsync(IReadOnlyList<OperacaoEnviada> operacoes, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(operacoes);

        var usuarioId = suporte.UsuarioId;
        var motoristaId = await suporte.Contexto.Motoristas
            .AsNoTracking()
            .Where(motorista => motorista.UsuarioId == usuarioId)
            .Select(motorista => (Guid?)motorista.Id)
            .SingleOrDefaultAsync(cancelamento)
            .ConfigureAwait(false)
            ?? throw ExcecaoDeDominio.NaoEncontrado("motorista_nao_associado", "Esta conta não está associada a um motorista.");

        var resultados = new List<ResultadoDeOperacaoSincronizada>(operacoes.Count);
        foreach (var operacao in operacoes)
        {
            var resultado = await ProcessarComNovasTentativasAsync(operacao, motoristaId, cancelamento).ConfigureAwait(false);
            metricas.Registrar(resultado);
            resultados.Add(resultado);
        }

        log.LogInformation(
            "Sincronização do motorista {MotoristaId}: {Quantidade} operação(ões), {Aplicadas} aplicada(s), {Repetidas} repetida(s), {Conflitos} conflito(s), {Recusadas} recusada(s).",
            motoristaId,
            resultados.Count,
            resultados.Count(resultado => resultado.Desfecho == DesfechoDaSincronizacao.Aplicada && !resultado.Repetida),
            resultados.Count(resultado => resultado.Repetida),
            resultados.Count(resultado => resultado.Desfecho == DesfechoDaSincronizacao.Conflito),
            resultados.Count(resultado => resultado.Desfecho == DesfechoDaSincronizacao.Recusada));

        return new ResultadoDaSincronizacao(resultados);
    }

    /// <summary>
    /// Conflito de versão significa que nada foi aplicado nem registrado: outra gravação na mesma entrega
    /// venceu a corrida — muitas vezes a própria operação repetida pelo aparelho. Reprocessar é seguro e
    /// encontra o registro, se foi isso; só depois de algumas voltas o aparelho é mandado reenviar.
    /// </summary>
    private async Task<ResultadoDeOperacaoSincronizada> ProcessarComNovasTentativasAsync(
        OperacaoEnviada enviada,
        Guid motoristaId,
        CancellationToken cancelamento)
    {
        const int TentativasEmConflitoDeVersao = 3;

        var resultado = await ProcessarAsync(enviada, motoristaId, cancelamento).ConfigureAwait(false);
        for (var tentativa = 1; tentativa < TentativasEmConflitoDeVersao && resultado.Desfecho == DesfechoDaSincronizacao.TentarDeNovo; tentativa++)
        {
            resultado = await ProcessarAsync(enviada, motoristaId, cancelamento).ConfigureAwait(false);
        }

        return resultado;
    }

    private async Task<ResultadoDeOperacaoSincronizada> ProcessarAsync(OperacaoEnviada enviada, Guid motoristaId, CancellationToken cancelamento)
    {
        if (enviada is not { OperacaoDoClienteId: { } id, Tipo: { } tipo, AlvoId: { } alvoId, CriadaEm: { } criadaEm })
        {
            return new ResultadoDeOperacaoSincronizada(
                enviada.OperacaoDoClienteId,
                DesfechoDaSincronizacao.Recusada,
                false,
                "campo_obrigatorio",
                "Informe identificador da operação, tipo, alvo e instante de criação.");
        }

        var contexto = suporte.Contexto;
        var recebidaEm = suporte.Agora;

        var registrada = await BuscarAsync(motoristaId, id, cancelamento).ConfigureAwait(false);
        if (registrada is not null)
        {
            return DoRegistro(registrada, tipo, alvoId, enviada.Motivo);
        }

        try
        {
            OperacaoDoCliente.Validar(id, tipo, alvoId, enviada.Motivo, criadaEm, recebidaEm);
        }
        catch (ExcecaoDeDominio invalida)
        {
            // Operação malformada não é registrada: não há o que proteger de repetição.
            return new ResultadoDeOperacaoSincronizada(id, DesfechoDaSincronizacao.Recusada, false, invalida.Codigo, invalida.Message);
        }

        try
        {
            var jaRegistrada = await contexto.ExecutarEmTransacaoAsync(
                async cancelamentoDaTentativa =>
                {
                    var repeticao = await RepeticaoNaFilaAsync(motoristaId, id, tipo, alvoId, enviada.Motivo, cancelamentoDaTentativa)
                        .ConfigureAwait(false);
                    if (repeticao is not null)
                    {
                        return repeticao;
                    }

                    contexto.OperacoesDoCliente.Add(OperacaoDoCliente.Registrar(
                        suporte.NovoIdentificador(), suporte.OrganizacaoId, motoristaId, id, tipo, alvoId, enviada.Motivo, criadaEm, recebidaEm,
                        ResultadoDaOperacaoDoCliente.Aplicada));

                    await ExecutarAsync(tipo, alvoId, enviada.Motivo, cancelamentoDaTentativa).ConfigureAwait(false);

                    // Comando que encontra o efeito já aplicado não grava nada; o registro da operação, sim.
                    await contexto.SaveChangesAsync(cancelamentoDaTentativa).ConfigureAwait(false);
                    return null;
                },
                cancelamento).ConfigureAwait(false);

            return jaRegistrada ?? new ResultadoDeOperacaoSincronizada(id, DesfechoDaSincronizacao.Aplicada, false, null, null);
        }
        catch (ExcecaoDeDominio versao) when (versao.Codigo == "conflito_de_versao")
        {
            // Gravação concorrente na mesma entrega: nada foi aplicado nem registrado; o aparelho reenvia.
            return new ResultadoDeOperacaoSincronizada(id, DesfechoDaSincronizacao.TentarDeNovo, false, versao.Codigo, versao.Message);
        }
        catch (ExcecaoDeDominio recusa)
        {
            return await RegistrarDesfechoNegativoAsync(recusa, motoristaId, id, tipo, alvoId, enviada.Motivo, criadaEm, recebidaEm, cancelamento)
                .ConfigureAwait(false);
        }
        catch (DbUpdateException duplicada) when (contexto.EhViolacaoDeUnicidade(duplicada, NomesDeRestricoes.OperacaoDoCliente))
        {
            // Outra requisição com a mesma operação gravou primeiro; esta transação foi desfeita inteira.
            return await RepetidaAsync(motoristaId, id, tipo, alvoId, enviada.Motivo, cancelamento).ConfigureAwait(false);
        }
    }

    private async Task<ResultadoDeOperacaoSincronizada> RegistrarDesfechoNegativoAsync(
        ExcecaoDeDominio recusa,
        Guid motoristaId,
        Guid id,
        TipoDeOperacaoDoCliente tipo,
        Guid alvoId,
        MotivoDeTentativaFrustrada? motivo,
        DateTimeOffset criadaEm,
        DateTimeOffset recebidaEm,
        CancellationToken cancelamento)
    {
        var contexto = suporte.Contexto;
        var resultado = recusa.Categoria == CategoriaDeErroDeDominio.Conflito
            ? ResultadoDaOperacaoDoCliente.Conflito
            : ResultadoDaOperacaoDoCliente.Recusada;

        try
        {
            var jaRegistrada = await contexto.ExecutarEmTransacaoAsync(
                async cancelamentoDaTentativa =>
                {
                    var repeticao = await RepeticaoNaFilaAsync(motoristaId, id, tipo, alvoId, motivo, cancelamentoDaTentativa)
                        .ConfigureAwait(false);
                    if (repeticao is not null)
                    {
                        return repeticao;
                    }

                    contexto.OperacoesDoCliente.Add(OperacaoDoCliente.Registrar(
                        suporte.NovoIdentificador(), suporte.OrganizacaoId, motoristaId, id, tipo, alvoId, motivo, criadaEm, recebidaEm,
                        resultado, recusa.Codigo, recusa.Message));
                    await contexto.SaveChangesAsync(cancelamentoDaTentativa).ConfigureAwait(false);
                    return null;
                },
                cancelamento).ConfigureAwait(false);

            if (jaRegistrada is not null)
            {
                return jaRegistrada;
            }
        }
        catch (DbUpdateException duplicada) when (contexto.EhViolacaoDeUnicidade(duplicada, NomesDeRestricoes.OperacaoDoCliente))
        {
            return await RepetidaAsync(motoristaId, id, tipo, alvoId, motivo, cancelamento).ConfigureAwait(false);
        }

        log.LogInformation(
            "Operação {OperacaoDoClienteId} ({Tipo}) do motorista {MotoristaId}: {Resultado} {Codigo}.",
            id, tipo, motoristaId, resultado, recusa.Codigo);

        return new ResultadoDeOperacaoSincronizada(
            id,
            resultado == ResultadoDaOperacaoDoCliente.Conflito ? DesfechoDaSincronizacao.Conflito : DesfechoDaSincronizacao.Recusada,
            false,
            recusa.Codigo,
            recusa.Message);
    }

    private async Task<ResultadoDeOperacaoSincronizada> RepetidaAsync(
        Guid motoristaId,
        Guid id,
        TipoDeOperacaoDoCliente tipo,
        Guid alvoId,
        MotivoDeTentativaFrustrada? motivo,
        CancellationToken cancelamento)
    {
        var registrada = await BuscarAsync(motoristaId, id, cancelamento).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Violação de unicidade sem registro correspondente.");

        return DoRegistro(registrada, tipo, alvoId, motivo);
    }

    /// <summary>
    /// Entra na fila do motorista e confere, já na vez, se a operação foi registrada por uma requisição que
    /// estava na frente — o aparelho que reenviou antes da primeira resposta chegar.
    /// </summary>
    private async Task<ResultadoDeOperacaoSincronizada?> RepeticaoNaFilaAsync(
        Guid motoristaId,
        Guid id,
        TipoDeOperacaoDoCliente tipo,
        Guid alvoId,
        MotivoDeTentativaFrustrada? motivo,
        CancellationToken cancelamento)
    {
        await suporte.Contexto.SerializarSincronizacaoDoMotoristaAsync(motoristaId, cancelamento).ConfigureAwait(false);

        var registrada = await BuscarAsync(motoristaId, id, cancelamento).ConfigureAwait(false);
        return registrada is null ? null : DoRegistro(registrada, tipo, alvoId, motivo);
    }

    private Task<OperacaoDoCliente?> BuscarAsync(Guid motoristaId, Guid id, CancellationToken cancelamento) =>
        suporte.Contexto.OperacoesDoCliente
            .AsNoTracking()
            .SingleOrDefaultAsync(operacao => operacao.MotoristaId == motoristaId && operacao.OperacaoDoClienteId == id, cancelamento);

    private static ResultadoDeOperacaoSincronizada DoRegistro(
        OperacaoDoCliente registrada,
        TipoDeOperacaoDoCliente tipo,
        Guid alvoId,
        MotivoDeTentativaFrustrada? motivo)
    {
        if (!registrada.MesmoPedido(tipo, alvoId, motivo))
        {
            return new ResultadoDeOperacaoSincronizada(
                registrada.OperacaoDoClienteId,
                DesfechoDaSincronizacao.Recusada,
                false,
                "operacao_divergente",
                "Este identificador de operação já foi usado para outra ação.");
        }

        var desfecho = registrada.Resultado switch
        {
            ResultadoDaOperacaoDoCliente.Aplicada => DesfechoDaSincronizacao.Aplicada,
            ResultadoDaOperacaoDoCliente.Conflito => DesfechoDaSincronizacao.Conflito,
            _ => DesfechoDaSincronizacao.Recusada,
        };

        return new ResultadoDeOperacaoSincronizada(registrada.OperacaoDoClienteId, desfecho, true, registrada.Codigo, registrada.Mensagem);
    }

    private Task ExecutarAsync(TipoDeOperacaoDoCliente tipo, Guid alvoId, MotivoDeTentativaFrustrada? motivo, CancellationToken cancelamento) =>
        tipo switch
        {
            TipoDeOperacaoDoCliente.IniciarRota => execucao.IniciarRotaAsync(alvoId, cancelamento),
            TipoDeOperacaoDoCliente.ConcluirRota => execucao.ConcluirRotaAsync(alvoId, cancelamento),
            TipoDeOperacaoDoCliente.RegistrarChegada => execucao.RegistrarChegadaAsync(alvoId, cancelamento),
            TipoDeOperacaoDoCliente.ConcluirEntrega => execucao.ConcluirEntregaAsync(alvoId, cancelamento),
            TipoDeOperacaoDoCliente.RegistrarTentativaFrustrada => execucao.RegistrarTentativaFrustradaAsync(alvoId, motivo!.Value, cancelamento),
            _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "Tipo de operação desconhecido."),
        };
}

/// <summary>Métricas de sincronização offline, no medidor <see cref="NomeDoMedidor"/>.</summary>
public sealed class MetricasDeSincronizacao : IDisposable
{
    /// <summary>Nome do medidor.</summary>
    public const string NomeDoMedidor = "TorreLogistica.Sincronizacao";

    private readonly Meter _medidor = new(NomeDoMedidor);
    private readonly Counter<long> _operacoes;

    /// <summary>Cria os instrumentos.</summary>
    public MetricasDeSincronizacao() =>
        _operacoes = _medidor.CreateCounter<long>("sync.operations", "{operacao}", "Operações do aparelho sincronizadas, por desfecho e repetição.");

    /// <summary>Conta uma operação.</summary>
    public void Registrar(ResultadoDeOperacaoSincronizada resultado)
    {
        ArgumentNullException.ThrowIfNull(resultado);

        _operacoes.Add(
            1,
            new KeyValuePair<string, object?>("desfecho", resultado.Desfecho.ToString()),
            new KeyValuePair<string, object?>("repetida", resultado.Repetida));
    }

    /// <inheritdoc />
    public void Dispose() => _medidor.Dispose();
}
