using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Application.Cadastros;
using TorreLogistica.Application.Comum;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Abstracoes.Identificadores;
using TorreLogistica.Domain.Clientes;
using TorreLogistica.Domain.Comum;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Rotas;

namespace TorreLogistica.Application.Entregas;

/// <summary>Filtro da lista de entregas.</summary>
/// <param name="Pagina">Página, a partir de 1.</param>
/// <param name="TamanhoDaPagina">Itens por página.</param>
/// <param name="Status">Status aceitos; vazio traz todos.</param>
/// <param name="ClienteId">Só deste cliente.</param>
/// <param name="DestinatarioId">Só deste destinatário.</param>
/// <param name="Codigo">Trecho do código humano.</param>
/// <param name="JanelaAPartirDe">Janela que termina a partir deste instante.</param>
/// <param name="JanelaAte">Janela que começa até este instante.</param>
public sealed record FiltroDeEntregas(
    int Pagina,
    int TamanhoDaPagina,
    IReadOnlyList<StatusDaEntrega> Status,
    Guid? ClienteId,
    Guid? DestinatarioId,
    string? Codigo,
    DateTimeOffset? JanelaAPartirDe,
    DateTimeOffset? JanelaAte);

/// <summary>Dados de entrega recebidos do cliente da API.</summary>
/// <param name="ClienteId">Cliente contratante.</param>
/// <param name="DestinatarioId">Quem recebe.</param>
/// <param name="Endereco">Endereço; sem ele, vale o do destinatário.</param>
/// <param name="Latitude">Latitude, só com endereço informado.</param>
/// <param name="Longitude">Longitude, só com endereço informado.</param>
/// <param name="PrometidaDe">Início da janela prometida.</param>
/// <param name="PrometidaAte">Fim da janela prometida.</param>
/// <param name="Observacoes">Observações operacionais.</param>
public sealed record DadosDeEntrega(
    Guid ClienteId,
    Guid DestinatarioId,
    DadosDeEndereco? Endereco,
    double? Latitude,
    double? Longitude,
    DateTimeOffset PrometidaDe,
    DateTimeOffset PrometidaAte,
    string? Observacoes);

/// <summary>Janela prometida como devolvida pela API.</summary>
public sealed record JanelaResumo(DateTimeOffset De, DateTimeOffset Ate);

/// <summary>Cancelamento como devolvido pela API.</summary>
public sealed record CancelamentoResumo(MotivoDeCancelamento Motivo, string? Descricao, DateTimeOffset CanceladaEm);

/// <summary>Execução da entrega pelo motorista.</summary>
public sealed record ExecucaoResumo(
    Guid? MotoristaId,
    DateTimeOffset? SaiuParaRotaEm,
    DateTimeOffset? ChegadaRegistradaEm,
    DateTimeOffset? EntregueEm,
    int TentativasFrustradas,
    MotivoDeTentativaFrustrada? MotivoDaUltimaTentativa,
    DateTimeOffset? UltimaTentativaFrustradaEm);

/// <summary>Entrega como devolvida pela API.</summary>
public sealed record EntregaResumo(
    Guid Id,
    string Codigo,
    StatusDaEntrega Status,
    Guid ClienteId,
    string ClienteNome,
    Guid DestinatarioId,
    string DestinatarioNome,
    EnderecoResumo Endereco,
    CoordenadaResumo? Localizacao,
    JanelaResumo JanelaPrometida,
    string? Observacoes,
    CancelamentoResumo? Cancelamento,
    ExecucaoResumo Execucao,
    DateTimeOffset CriadaEm,
    DateTimeOffset AtualizadaEm,
    uint Versao);

/// <summary>Evento da timeline como devolvido pela API.</summary>
public sealed record EventoDaEntregaResumo(
    int Sequencia,
    TipoDeEventoDaEntrega Tipo,
    StatusDaEntrega StatusResultante,
    DateTimeOffset OcorridoEm,
    Guid? AutorUsuarioId,
    JsonElement Dados);

/// <summary>
/// Entregas da organização autenticada: criar, consultar, listar, alterar e cancelar.
/// </summary>
/// <remarks>
/// <para>
/// Toda mudança grava, no mesmo commit, a entrega, o evento da timeline e o registro de
/// auditoria. Não há caminho que mude a entrega sem deixar o evento.
/// </para>
/// <para>
/// Cliente e destinatário são procurados nos conjuntos já filtrados pelo tenant: um
/// identificador de outra organização responde como inexistente.
/// </para>
/// </remarks>
public sealed class GestaoDeEntregas(
    SuporteDeCadastro suporte,
    IGeradorDeIdentificador identificadores,
    ILogger<GestaoDeEntregas> log)
{
    private const string Recurso = "entrega";

    /// <summary>Lista entregas, da janela que termina antes para a que termina depois.</summary>
    public async Task<PaginaDeResultados<EntregaResumo>> ListarAsync(FiltroDeEntregas filtro, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(filtro);

        var consulta = suporte.Contexto.Entregas.AsNoTracking();

        if (filtro.Status.Count > 0)
        {
            var status = filtro.Status.ToArray();
            consulta = consulta.Where(entrega => status.Contains(entrega.Status));
        }

        if (filtro.ClienteId is { } clienteId)
        {
            consulta = consulta.Where(entrega => entrega.ClienteId == clienteId);
        }

        if (filtro.DestinatarioId is { } destinatarioId)
        {
            consulta = consulta.Where(entrega => entrega.DestinatarioId == destinatarioId);
        }

        if (!string.IsNullOrWhiteSpace(filtro.Codigo))
        {
            var codigo = filtro.Codigo.Trim().ToUpperInvariant();
            consulta = consulta.Where(entrega => entrega.Codigo.Contains(codigo));
        }

        if (filtro.JanelaAPartirDe is { } aPartirDe)
        {
            var instante = aPartirDe.ToUniversalTime();
            consulta = consulta.Where(entrega => entrega.Janela.Fim >= instante);
        }

        if (filtro.JanelaAte is { } ate)
        {
            var instante = ate.ToUniversalTime();
            consulta = consulta.Where(entrega => entrega.Janela.Inicio <= instante);
        }

        var total = await consulta.CountAsync(cancelamento).ConfigureAwait(false);
        var linhas = await Projetar(consulta)
            .OrderBy(linha => linha.Entrega.Janela.Fim)
            .ThenBy(linha => linha.Entrega.Id)
            .Skip((filtro.Pagina - 1) * filtro.TamanhoDaPagina)
            .Take(filtro.TamanhoDaPagina)
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

        return new PaginaDeResultados<EntregaResumo>(
            [.. linhas.Select(ParaResumo)], filtro.Pagina, filtro.TamanhoDaPagina, total);
    }

    /// <summary>Obtém uma entrega.</summary>
    public async Task<EntregaResumo> ObterAsync(Guid id, CancellationToken cancelamento)
    {
        var linha = await Projetar(suporte.Contexto.Entregas.AsNoTracking().Where(entrega => entrega.Id == id))
            .SingleOrDefaultAsync(cancelamento)
            .ConfigureAwait(false);

        return linha is null ? throw EntregaNaoEncontrada() : ParaResumo(linha);
    }

    /// <summary>Timeline da entrega, em ordem.</summary>
    public async Task<IReadOnlyList<EventoDaEntregaResumo>> ListarEventosAsync(Guid id, CancellationToken cancelamento)
    {
        var existe = await suporte.Contexto.Entregas
            .AnyAsync(entrega => entrega.Id == id, cancelamento)
            .ConfigureAwait(false);

        if (!existe)
        {
            throw EntregaNaoEncontrada();
        }

        var eventos = await suporte.Contexto.EventosDaEntrega
            .AsNoTracking()
            .Where(evento => evento.EntregaId == id)
            .OrderBy(evento => evento.Sequencia)
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

        return [.. eventos.Select(ParaResumo)];
    }

    /// <summary>Cria uma entrega com o próximo código humano da organização.</summary>
    /// <param name="dados">Dados da entrega.</param>
    /// <param name="cancelamento">Cancelamento.</param>
    /// <param name="aoCriar">
    /// Trabalho extra a confirmar <b>no mesmo commit</b> da entrega, recebendo o identificador dela.
    /// </param>
    /// <remarks>
    /// O gancho existe porque a transação é aberta aqui dentro, e aninhar outra do lado de fora é recusado
    /// pelo provedor — a estratégia de nova tentativa precisa reexecutar a unidade inteira. Quem recebe
    /// entrega de um sistema externo grava por aqui o registro de idempotência e a referência de origem,
    /// em vez de tentar cercar esta chamada com uma transação própria.
    /// </remarks>
    public async Task<EntregaResumo> CriarAsync(
        DadosDeEntrega dados,
        CancellationToken cancelamento,
        Func<Guid, CancellationToken, Task>? aoCriar = null)
    {
        ArgumentNullException.ThrowIfNull(dados);

        var contexto = suporte.Contexto;
        var (id, codigo) = await contexto.ExecutarEmTransacaoAsync(
            async cancelamentoDaTentativa =>
            {
                await GarantirClienteAsync(dados.ClienteId, exigirAtivo: true, cancelamentoDaTentativa).ConfigureAwait(false);
                var destinatario = await CarregarDestinatarioAsync(dados.DestinatarioId, exigirAtivo: true, cancelamentoDaTentativa)
                    .ConfigureAwait(false);

                // Tudo validado antes de reservar o número: dado inválido não chega ao contador.
                var dadosDaEntrega = MontarDados(dados, destinatario);
                var agora = suporte.Agora;
                var ano = agora.UtcDateTime.Year;
                var numero = await contexto
                    .ReservarNumeroSequencialAsync(suporte.OrganizacaoId, CodigoDaEntrega.Serie, ano, cancelamentoDaTentativa)
                    .ConfigureAwait(false);

                var (entrega, evento) = Entrega.Criar(
                    suporte.NovoIdentificador(),
                    suporte.OrganizacaoId,
                    CodigoDaEntrega.Gerar(ano, numero),
                    dadosDaEntrega,
                    // Opcional de propósito: a entrega pode nascer de uma integração, e aí o autor é o
                    // sistema de origem, registrado na auditoria — não uma pessoa inventada.
                    suporte.AutorUsuarioId,
                    suporte.NovoIdentificador(),
                    agora);

                contexto.Entregas.Add(entrega);
                contexto.EventosDaEntrega.Add(evento);
                suporte.Auditar(Recurso, AcoesDeEntrega.Criada, entrega.Id, new { codigo = entrega.Codigo });

                if (aoCriar is not null)
                {
                    await aoCriar(entrega.Id, cancelamentoDaTentativa).ConfigureAwait(false);
                }

                await contexto.SaveChangesAsync(cancelamentoDaTentativa).ConfigureAwait(false);
                return (entrega.Id, entrega.Codigo);
            },
            cancelamento).ConfigureAwait(false);

        log.LogInformation(
            "Entrega {EntregaId} ({CodigoDaEntrega}) criada na organização {OrganizacaoId}.", id, codigo, suporte.OrganizacaoId);

        return await ObterAsync(id, cancelamento).ConfigureAwait(false);
    }

    /// <summary>Altera os dados de uma entrega, exigindo a versão lida.</summary>
    public async Task<EntregaResumo> AtualizarAsync(Guid id, uint versao, DadosDeEntrega dados, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(dados);
        ArgumentNullException.ThrowIfNull(dados.Endereco);

        var entrega = await CarregarAsync(id, cancelamento).ConfigureAwait(false);
        ExigirVersao(entrega, versao);

        // Referência nova precisa existir no tenant e estar ativa; a que já estava não é recobrada.
        if (entrega.ClienteId != dados.ClienteId)
        {
            await GarantirClienteAsync(dados.ClienteId, exigirAtivo: true, cancelamento).ConfigureAwait(false);
        }

        if (entrega.DestinatarioId != dados.DestinatarioId)
        {
            await CarregarDestinatarioAsync(dados.DestinatarioId, exigirAtivo: true, cancelamento).ConfigureAwait(false);
        }

        var novos = new DadosDaEntrega(
            dados.ClienteId,
            dados.DestinatarioId,
            dados.Endereco.ParaDominio(),
            CoordenadaGeografica.CriarOpcional(dados.Latitude, dados.Longitude),
            JanelaDeEntrega.Criar(dados.PrometidaDe, dados.PrometidaAte),
            dados.Observacoes);

        var alteracao = entrega.AtualizarDados(novos, suporte.UsuarioId, suporte.NovoIdentificador(), suporte.Agora);

        if (alteracao.Evento is { } evento)
        {
            suporte.Contexto.EventosDaEntrega.Add(evento);
            suporte.Auditar(Recurso, AcoesDeEntrega.Alterada, entrega.Id, new { campos = alteracao.Campos });
            await suporte.SalvarAsync(cancelamento, (NomesDeRestricoes.SequenciaDoEventoDaEntrega, ConflitoDeVersao)).ConfigureAwait(false);
        }

        return await ObterAsync(id, cancelamento).ConfigureAwait(false);
    }

    /// <summary>
    /// Cancela uma entrega. Repetir não gera novo evento. Se ela está numa rota que ainda não
    /// saiu, a parada é retirada na mesma gravação — entrega cancelada não fica ocupando rota.
    /// </summary>
    public async Task<EntregaResumo> CancelarAsync(
        Guid id,
        MotivoDeCancelamento motivo,
        string? descricao,
        CancellationToken cancelamento)
    {
        var contexto = suporte.Contexto;
        var entrega = await CarregarAsync(id, cancelamento).ConfigureAwait(false);
        var estavaEmRota = RegrasDaEntrega.EstaEmRotaNaoIniciada(entrega.Status);
        var agora = suporte.Agora;
        var evento = entrega.Cancelar(motivo, descricao, suporte.UsuarioId, identificadores.Novo(), agora);

        if (evento is not null)
        {
            if (estavaEmRota)
            {
                var rota = await contexto.Rotas
                    .Include(item => item.Paradas)
                    .SingleOrDefaultAsync(item => item.Paradas.Any(parada => parada.EntregaId == id && parada.Ativa), cancelamento)
                    .ConfigureAwait(false);

                if (rota is not null)
                {
                    contexto.EventosDaRota.AddRange(rota.RemoverParada(
                        id, MotivoDeRemocaoDeParada.EntregaCancelada, identificadores, suporte.UsuarioId, agora));
                }
            }

            contexto.EventosDaEntrega.Add(evento);
            suporte.Auditar(Recurso, AcoesDeEntrega.Cancelada, entrega.Id, new { motivo = motivo.ToString() });
            await suporte.SalvarAsync(
                cancelamento,
                (NomesDeRestricoes.SequenciaDoEventoDaEntrega, ConflitoDeVersao),
                (NomesDeRestricoes.SequenciaDoEventoDaRota, ConflitoDeVersao)).ConfigureAwait(false);

            log.LogInformation(
                "Entrega {EntregaId} cancelada na organização {OrganizacaoId}.", entrega.Id, entrega.OrganizacaoId);
        }

        return await ObterAsync(id, cancelamento).ConfigureAwait(false);
    }

    /// <summary>Marca nova janela para entrega com tentativa sem sucesso.</summary>
    public async Task<EntregaResumo> ReagendarAsync(Guid id, DateTimeOffset prometidaDe, DateTimeOffset prometidaAte, CancellationToken cancelamento)
    {
        var entrega = await CarregarAsync(id, cancelamento).ConfigureAwait(false);
        var janela = JanelaDeEntrega.Criar(prometidaDe, prometidaAte);

        if (entrega.Reagendar(janela, suporte.UsuarioId, identificadores.Novo(), suporte.Agora) is { } evento)
        {
            suporte.Contexto.EventosDaEntrega.Add(evento);
            suporte.Auditar(Recurso, AcoesDeEntrega.Reagendada, entrega.Id, new { tentativas = entrega.TentativasFrustradas });
            await suporte.SalvarAsync(cancelamento, (NomesDeRestricoes.SequenciaDoEventoDaEntrega, ConflitoDeVersao)).ConfigureAwait(false);

            log.LogInformation("Entrega {EntregaId} reagendada na organização {OrganizacaoId}.", entrega.Id, entrega.OrganizacaoId);
        }

        return await ObterAsync(id, cancelamento).ConfigureAwait(false);
    }

    private static DadosDaEntrega MontarDados(DadosDeEntrega dados, Destinatario destinatario)
    {
        Endereco endereco;
        CoordenadaGeografica? localizacao;

        if (dados.Endereco is null)
        {
            ExcecaoDeDominio.LancarSe(
                dados.Latitude is not null || dados.Longitude is not null,
                "coordenada_sem_endereco",
                "Coordenada só pode ser informada junto com o endereço.");

            // Cópia do endereço do destinatário neste instante — ver remarks de Entrega.
            endereco = destinatario.Endereco;
            localizacao = destinatario.Localizacao;
        }
        else
        {
            endereco = dados.Endereco.ParaDominio();
            localizacao = CoordenadaGeografica.CriarOpcional(dados.Latitude, dados.Longitude);
        }

        return new DadosDaEntrega(
            dados.ClienteId,
            dados.DestinatarioId,
            endereco,
            localizacao,
            JanelaDeEntrega.Criar(dados.PrometidaDe, dados.PrometidaAte),
            dados.Observacoes);
    }

    private async Task GarantirClienteAsync(Guid clienteId, bool exigirAtivo, CancellationToken cancelamento)
    {
        var cliente = await suporte.Contexto.Clientes
            .AsNoTracking()
            .Where(item => item.Id == clienteId)
            .Select(item => new { item.Ativo })
            .SingleOrDefaultAsync(cancelamento)
            .ConfigureAwait(false)
            ?? throw ExcecaoDeDominio.NaoEncontrado("cliente_nao_encontrado", "Cliente não encontrado.");

        ExcecaoDeDominio.LancarSe(
            exigirAtivo && !cliente.Ativo, "cliente_inativo", "Cliente inativo não recebe nova entrega.");
    }

    private async Task<Destinatario> CarregarDestinatarioAsync(Guid destinatarioId, bool exigirAtivo, CancellationToken cancelamento)
    {
        var destinatario = await suporte.Contexto.Destinatarios
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == destinatarioId, cancelamento)
            .ConfigureAwait(false)
            ?? throw ExcecaoDeDominio.NaoEncontrado("destinatario_nao_encontrado", "Destinatário não encontrado.");

        ExcecaoDeDominio.LancarSe(
            exigirAtivo && !destinatario.Ativo, "destinatario_inativo", "Destinatário inativo não recebe nova entrega.");

        return destinatario;
    }

    private async Task<Entrega> CarregarAsync(Guid id, CancellationToken cancelamento) =>
        await suporte.Contexto.Entregas
            .SingleOrDefaultAsync(entrega => entrega.Id == id, cancelamento)
            .ConfigureAwait(false)
        ?? throw EntregaNaoEncontrada();

    private void ExigirVersao(Entrega entrega, uint versaoInformada)
    {
        if (entrega.Versao != versaoInformada)
        {
            throw ConflitoDeVersao();
        }

        suporte.Contexto.DefinirVersaoEsperada(entrega, versaoInformada);
    }

    private IQueryable<LinhaDeEntrega> Projetar(IQueryable<Entrega> entregas) =>
        from entrega in entregas
        join cliente in suporte.Contexto.Clientes on entrega.ClienteId equals cliente.Id
        join destinatario in suporte.Contexto.Destinatarios on entrega.DestinatarioId equals destinatario.Id
        select new LinhaDeEntrega
        {
            Entrega = entrega,
            ClienteNome = cliente.Nome,
            DestinatarioNome = destinatario.Nome,
        };

    private static ExcecaoDeDominio EntregaNaoEncontrada() =>
        ExcecaoDeDominio.NaoEncontrado("entrega_nao_encontrada", "Entrega não encontrada.");

    private static ExcecaoDeDominio ConflitoDeVersao() =>
        ExcecaoDeDominio.Conflito(
            "conflito_de_versao",
            "A entrega foi alterada por outra pessoa. Recarregue e tente de novo.");

    private static EntregaResumo ParaResumo(LinhaDeEntrega linha)
    {
        var entrega = linha.Entrega;

        return new EntregaResumo(
            entrega.Id,
            entrega.Codigo,
            entrega.Status,
            entrega.ClienteId,
            linha.ClienteNome,
            entrega.DestinatarioId,
            linha.DestinatarioNome,
            EnderecoResumo.De(entrega.Endereco),
            CoordenadaResumo.De(entrega.Localizacao),
            new JanelaResumo(entrega.Janela.Inicio, entrega.Janela.Fim),
            entrega.Observacoes,
            entrega is { MotivoDoCancelamento: { } motivo, CanceladaEm: { } canceladaEm }
                ? new CancelamentoResumo(motivo, entrega.DescricaoDoCancelamento, canceladaEm)
                : null,
            new ExecucaoResumo(
                entrega.MotoristaId,
                entrega.SaiuParaRotaEm,
                entrega.ChegadaRegistradaEm,
                entrega.EntregueEm,
                entrega.TentativasFrustradas,
                entrega.MotivoDaUltimaTentativa,
                entrega.UltimaTentativaFrustradaEm),
            entrega.CriadaEm,
            entrega.AtualizadaEm,
            entrega.Versao);
    }

    private static EventoDaEntregaResumo ParaResumo(EventoDaEntrega evento)
    {
        using var documento = JsonDocument.Parse(evento.Dados);

        return new EventoDaEntregaResumo(
            evento.Sequencia,
            evento.Tipo,
            evento.StatusResultante,
            evento.OcorridoEm,
            evento.AutorUsuarioId,
            documento.RootElement.Clone());
    }

    private sealed class LinhaDeEntrega
    {
        public required Entrega Entrega { get; init; }

        public required string ClienteNome { get; init; }

        public required string DestinatarioNome { get; init; }
    }
}

/// <summary>Ações de entrega registradas na trilha de auditoria.</summary>
public static class AcoesDeEntrega
{
    /// <summary>Criação.</summary>
    public const string Criada = "criada";

    /// <summary>Alteração de dados.</summary>
    public const string Alterada = "alterada";

    /// <summary>Cancelamento.</summary>
    public const string Cancelada = "cancelada";

    /// <summary>Nova janela depois de tentativa sem sucesso.</summary>
    public const string Reagendada = "reagendada";
}
