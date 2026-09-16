using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TorreLogistica.Application.Cadastros;
using TorreLogistica.Application.Comum;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Comum;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Ocorrencias;

namespace TorreLogistica.Application.Ocorrencias;

/// <summary>Filtro da lista de ocorrências.</summary>
/// <param name="Pagina">Página, a partir de 1.</param>
/// <param name="TamanhoDaPagina">Itens por página.</param>
/// <param name="EntregaId">Só desta entrega.</param>
/// <param name="RotaId">Só desta rota.</param>
/// <param name="MotoristaId">Só deste motorista.</param>
/// <param name="Tipos">Tipos aceitos; vazio traz todos.</param>
/// <param name="Severidade">Só desta severidade.</param>
/// <param name="APartirDe">Ocorridas a partir deste instante.</param>
/// <param name="Ate">Ocorridas até este instante.</param>
public sealed record FiltroDeOcorrencias(
    int Pagina,
    int TamanhoDaPagina,
    Guid? EntregaId,
    Guid? RotaId,
    Guid? MotoristaId,
    IReadOnlyList<TipoDeOcorrencia> Tipos,
    SeveridadeDaOcorrencia? Severidade,
    DateTimeOffset? APartirDe,
    DateTimeOffset? Ate);

/// <summary>Ocorrência como a operação a informa.</summary>
/// <param name="Tipo">Tipo.</param>
/// <param name="Severidade">Severidade; sem ela, vale a padrão do tipo.</param>
/// <param name="Motivo">Motivo tipado, só na tentativa de entrega.</param>
/// <param name="Observacao">Descrição complementar; obrigatória quando tipo ou motivo é "Outro".</param>
/// <param name="Latitude">Latitude de onde aconteceu.</param>
/// <param name="Longitude">Longitude de onde aconteceu.</param>
/// <param name="OcorridaEm">Quando aconteceu; sem ela, vale o instante do registro.</param>
public sealed record DadosDeOcorrencia(
    TipoDeOcorrencia Tipo,
    SeveridadeDaOcorrencia? Severidade,
    MotivoDeTentativaFrustrada? Motivo,
    string? Observacao,
    double? Latitude,
    double? Longitude,
    DateTimeOffset? OcorridaEm);

/// <summary>Ocorrência como devolvida pela API.</summary>
public sealed record OcorrenciaResumo(
    Guid Id,
    Guid EntregaId,
    string CodigoDaEntrega,
    Guid? RotaId,
    Guid? MotoristaId,
    string? NomeDoMotorista,
    TipoDeOcorrencia Tipo,
    SeveridadeDaOcorrencia Severidade,
    MotivoDeTentativaFrustrada? MotivoDaTentativa,
    string? Observacao,
    CoordenadaResumo? Localizacao,
    DateTimeOffset OcorridaEm,
    DateTimeOffset RegistradaEm,
    OrigemDaOcorrencia Origem,
    Guid AutorUsuarioId);

/// <summary>
/// Ocorrências da operação: o registro de tudo que deu errado na última milha.
/// </summary>
/// <remarks>
/// <para>
/// Somente-inserção: registrar é o único comando. Uma ocorrência errada é corrigida registrando outra — as
/// duas ficam, e é isso que dá rastreabilidade.
/// </para>
/// <para>
/// A tentativa de entrega não entra por aqui: ela muda o status da entrega, então vem pelo comando do
/// motorista (<c>ExecucaoPeloMotorista</c>), que grava a mudança, o evento da timeline e a ocorrência no
/// mesmo commit. Aqui ficam as ocorrências que não mudam o status — veículo, mercadoria, incidente, acesso.
/// </para>
/// </remarks>
public sealed class GestaoDeOcorrencias(SuporteDeCadastro suporte, ILogger<GestaoDeOcorrencias> log)
{
    private const string Recurso = "ocorrencia";

    /// <summary>Registra uma ocorrência pelo console.</summary>
    public async Task<OcorrenciaResumo> RegistrarAsync(Guid entregaId, DadosDeOcorrencia dados, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(dados);

        ExcecaoDeDominio.LancarSe(
            dados.Tipo == TipoDeOcorrencia.TentativaDeEntrega,
            "tentativa_pelo_motorista",
            "A tentativa de entrega é registrada pelo motorista, porque muda o status da entrega.");

        var contexto = suporte.Contexto;
        var entrega = await contexto.Entregas
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == entregaId, cancelamento)
            .ConfigureAwait(false)
            ?? throw EntregaNaoEncontrada();

        var ocorrencia = await MontarAsync(
            entrega,
            dados,
            OrigemDaOcorrencia.Operacao,
            entrega.MotoristaId,
            cancelamento).ConfigureAwait(false);

        contexto.Ocorrencias.Add(ocorrencia);
        suporte.Auditar(Recurso, AcoesDeOcorrencia.Registrada, ocorrencia.Id, new
        {
            entregaId,
            tipo = ocorrencia.Tipo.ToString(),
            severidade = ocorrencia.Severidade.ToString(),
        });

        await suporte.SalvarAsync(cancelamento).ConfigureAwait(false);

        log.LogInformation(
            "Ocorrência {OcorrenciaId} ({Tipo}, {Severidade}) registrada na entrega {EntregaId} pela operação.",
            ocorrencia.Id, ocorrencia.Tipo, ocorrencia.Severidade, entregaId);

        return await ObterAsync(ocorrencia.Id, cancelamento).ConfigureAwait(false);
    }

    /// <summary>
    /// Monta a ocorrência e resolve a rota ativa da entrega — sem gravar. Usado por quem já está gravando a
    /// mudança da entrega no mesmo commit.
    /// </summary>
    public async Task<Ocorrencia> MontarAsync(
        Entrega entrega,
        DadosDeOcorrencia dados,
        OrigemDaOcorrencia origem,
        Guid? motoristaId,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(entrega);
        ArgumentNullException.ThrowIfNull(dados);

        var rotaId = await suporte.Contexto.Paradas
            .AsNoTracking()
            .Where(parada => parada.EntregaId == entrega.Id && parada.Ativa)
            .Select(parada => (Guid?)parada.RotaId)
            .FirstOrDefaultAsync(cancelamento)
            .ConfigureAwait(false);

        return Ocorrencia.Registrar(
            suporte.NovoIdentificador(),
            suporte.OrganizacaoId,
            entrega.Id,
            rotaId,
            motoristaId,
            dados.Tipo,
            dados.Severidade,
            dados.Motivo,
            dados.Observacao,
            CoordenadaGeografica.CriarOpcional(dados.Latitude, dados.Longitude),
            dados.OcorridaEm,
            origem,
            suporte.UsuarioId,
            suporte.Agora);
    }

    /// <summary>Obtém uma ocorrência.</summary>
    public async Task<OcorrenciaResumo> ObterAsync(Guid id, CancellationToken cancelamento)
    {
        var linha = await Projetar(suporte.Contexto.Ocorrencias.AsNoTracking().Where(ocorrencia => ocorrencia.Id == id))
            .SingleOrDefaultAsync(cancelamento)
            .ConfigureAwait(false)
            ?? throw ExcecaoDeDominio.NaoEncontrado("ocorrencia_nao_encontrada", "Ocorrência não encontrada.");

        return Resumir(linha);
    }

    /// <summary>Lista ocorrências, das mais recentes para as mais antigas.</summary>
    public async Task<PaginaDeResultados<OcorrenciaResumo>> ListarAsync(FiltroDeOcorrencias filtro, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(filtro);

        var consulta = suporte.Contexto.Ocorrencias.AsNoTracking();

        if (filtro.EntregaId is { } entregaId)
        {
            consulta = consulta.Where(ocorrencia => ocorrencia.EntregaId == entregaId);
        }

        if (filtro.RotaId is { } rotaId)
        {
            consulta = consulta.Where(ocorrencia => ocorrencia.RotaId == rotaId);
        }

        if (filtro.MotoristaId is { } motoristaId)
        {
            consulta = consulta.Where(ocorrencia => ocorrencia.MotoristaId == motoristaId);
        }

        if (filtro.Tipos.Count > 0)
        {
            var tipos = filtro.Tipos.ToArray();
            consulta = consulta.Where(ocorrencia => tipos.Contains(ocorrencia.Tipo));
        }

        if (filtro.Severidade is { } severidade)
        {
            consulta = consulta.Where(ocorrencia => ocorrencia.Severidade == severidade);
        }

        if (filtro.APartirDe is { } aPartirDe)
        {
            var instante = aPartirDe.ToUniversalTime();
            consulta = consulta.Where(ocorrencia => ocorrencia.OcorridaEm >= instante);
        }

        if (filtro.Ate is { } ate)
        {
            var instante = ate.ToUniversalTime();
            consulta = consulta.Where(ocorrencia => ocorrencia.OcorridaEm <= instante);
        }

        var total = await consulta.CountAsync(cancelamento).ConfigureAwait(false);
        var linhas = await Projetar(consulta)
            .OrderByDescending(linha => linha.Ocorrencia.OcorridaEm)
            .ThenByDescending(linha => linha.Ocorrencia.Id)
            .Skip((filtro.Pagina - 1) * filtro.TamanhoDaPagina)
            .Take(filtro.TamanhoDaPagina)
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

        return new PaginaDeResultados<OcorrenciaResumo>(
            [.. linhas.Select(Resumir)], filtro.Pagina, filtro.TamanhoDaPagina, total);
    }

    /// <summary>Ocorrências de uma entrega, da mais recente para a mais antiga.</summary>
    public async Task<IReadOnlyList<OcorrenciaResumo>> ListarDaEntregaAsync(Guid entregaId, CancellationToken cancelamento)
    {
        var existe = await suporte.Contexto.Entregas
            .AnyAsync(entrega => entrega.Id == entregaId, cancelamento)
            .ConfigureAwait(false);

        if (!existe)
        {
            throw EntregaNaoEncontrada();
        }

        var linhas = await Projetar(suporte.Contexto.Ocorrencias.AsNoTracking().Where(ocorrencia => ocorrencia.EntregaId == entregaId))
            .OrderByDescending(linha => linha.Ocorrencia.OcorridaEm)
            .ThenByDescending(linha => linha.Ocorrencia.Id)
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

        return [.. linhas.Select(Resumir)];
    }

    private static ExcecaoDeDominio EntregaNaoEncontrada() =>
        ExcecaoDeDominio.NaoEncontrado("entrega_nao_encontrada", "Entrega não encontrada.");

    private IQueryable<LinhaDeOcorrencia> Projetar(IQueryable<Ocorrencia> ocorrencias)
    {
        var contexto = suporte.Contexto;

        return ocorrencias.Select(ocorrencia => new LinhaDeOcorrencia
        {
            Ocorrencia = ocorrencia,
            CodigoDaEntrega = contexto.Entregas.Where(entrega => entrega.Id == ocorrencia.EntregaId).Select(entrega => entrega.Codigo).FirstOrDefault()!,
            NomeDoMotorista = contexto.Motoristas.Where(motorista => motorista.Id == ocorrencia.MotoristaId).Select(motorista => motorista.Nome).FirstOrDefault(),
        });
    }

    private static OcorrenciaResumo Resumir(LinhaDeOcorrencia linha)
    {
        var ocorrencia = linha.Ocorrencia;

        return new OcorrenciaResumo(
            ocorrencia.Id,
            ocorrencia.EntregaId,
            linha.CodigoDaEntrega,
            ocorrencia.RotaId,
            ocorrencia.MotoristaId,
            linha.NomeDoMotorista,
            ocorrencia.Tipo,
            ocorrencia.Severidade,
            ocorrencia.MotivoDaTentativa,
            ocorrencia.Observacao,
            CoordenadaResumo.De(ocorrencia.Localizacao),
            ocorrencia.OcorridaEm,
            ocorrencia.RegistradaEm,
            ocorrencia.Origem,
            ocorrencia.AutorUsuarioId);
    }

    private sealed class LinhaDeOcorrencia
    {
        public required Ocorrencia Ocorrencia { get; init; }

        public required string CodigoDaEntrega { get; init; }

        public string? NomeDoMotorista { get; init; }
    }
}

/// <summary>Ações de ocorrência registradas na trilha de auditoria.</summary>
public static class AcoesDeOcorrencia
{
    /// <summary>Registro de uma ocorrência.</summary>
    public const string Registrada = "registrada";
}
