using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Application.Comum;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Clientes;
using TorreLogistica.Domain.Comum;
using TorreLogistica.Domain.Operacao;

namespace TorreLogistica.Application.Cadastros;

/// <summary>Hub como devolvido pela API.</summary>
public sealed record HubResumo(
    Guid Id,
    string Nome,
    EnderecoResumo Endereco,
    CoordenadaResumo Localizacao,
    bool Ativo,
    DateTimeOffset CriadoEm,
    DateTimeOffset AtualizadoEm,
    uint Versao);

/// <summary>Dados de hub recebidos do cliente.</summary>
public sealed record DadosDeHub(string? Nome, DadosDeEndereco Endereco, double Latitude, double Longitude);

/// <summary>Cliente como devolvido pela API.</summary>
public sealed record ClienteResumo(
    Guid Id,
    string Nome,
    string? Cnpj,
    bool Ativo,
    DateTimeOffset CriadoEm,
    DateTimeOffset AtualizadoEm,
    uint Versao);

/// <summary>Dados de cliente recebidos do cliente da API.</summary>
public sealed record DadosDeCliente(string? Nome, string? Cnpj);

/// <summary>Destinatário como devolvido pela API.</summary>
public sealed record DestinatarioResumo(
    Guid Id,
    string Nome,
    string? Telefone,
    EnderecoResumo Endereco,
    CoordenadaResumo? Localizacao,
    string? InstrucoesDeEntrega,
    bool Ativo,
    DateTimeOffset CriadoEm,
    DateTimeOffset AtualizadoEm,
    uint Versao);

/// <summary>Dados de destinatário recebidos do cliente.</summary>
public sealed record DadosDeDestinatario(
    string? Nome,
    string? Telefone,
    DadosDeEndereco Endereco,
    double? Latitude,
    double? Longitude,
    string? InstrucoesDeEntrega);

/// <summary>Cadastro de hubs da organização autenticada.</summary>
public sealed class GestaoDeHubs(SuporteDeCadastro suporte, ILogger<GestaoDeHubs> log)
{
    private const string Recurso = "hub";
    private const string CodigoNaoEncontrado = "hub_nao_encontrado";
    private const string MensagemNaoEncontrado = "Hub não encontrado.";

    /// <summary>Lista hubs.</summary>
    public async Task<PaginaDeResultados<HubResumo>> ListarAsync(FiltroDeCadastro filtro, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(filtro);

        var consulta = suporte.Contexto.Hubs.AsNoTracking();
        if (filtro.Ativo is { } ativo)
        {
            consulta = consulta.Where(hub => hub.Ativo == ativo);
        }

        if (filtro.BuscaNormalizada is { } busca)
        {
            consulta = consulta.Where(hub => hub.NomeNormalizado.Contains(busca));
        }

        var (itens, total) = await SuporteDeCadastro.PaginarAsync(
            consulta.OrderBy(hub => hub.NomeNormalizado).ThenBy(hub => hub.Id), filtro, cancelamento)
            .ConfigureAwait(false);

        return new PaginaDeResultados<HubResumo>([.. itens.Select(ParaResumo)], filtro.Pagina, filtro.TamanhoDaPagina, total);
    }

    /// <summary>Obtém um hub.</summary>
    public async Task<HubResumo> ObterAsync(Guid id, CancellationToken cancelamento) =>
        ParaResumo(await Carregar(suporte.Contexto.Hubs.AsNoTracking(), id, cancelamento).ConfigureAwait(false));

    /// <summary>Cadastra um hub.</summary>
    public async Task<HubResumo> CriarAsync(DadosDeHub dados, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(dados);
        ArgumentNullException.ThrowIfNull(dados.Endereco);

        var hub = Hub.Criar(
            suporte.NovoIdentificador(),
            suporte.OrganizacaoId,
            dados.Nome,
            dados.Endereco.ParaDominio(),
            CoordenadaGeografica.Criar(dados.Latitude, dados.Longitude),
            suporte.Agora);

        await GarantirNomeLivreAsync(hub.NomeNormalizado, excetoId: null, cancelamento).ConfigureAwait(false);

        suporte.Contexto.Hubs.Add(hub);
        suporte.Auditar(Recurso, AcoesDeCadastro.Criado, hub.Id, new { });
        await suporte.SalvarAsync(cancelamento, (NomesDeRestricoes.NomeDoHubPorOrganizacao, HubJaCadastrado)).ConfigureAwait(false);

        log.LogInformation("Hub {HubId} cadastrado na organização {OrganizacaoId}.", hub.Id, hub.OrganizacaoId);
        return ParaResumo(hub);
    }

    /// <summary>Atualiza os dados de um hub, exigindo a versão lida.</summary>
    public async Task<HubResumo> AtualizarAsync(Guid id, uint versao, DadosDeHub dados, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(dados);
        ArgumentNullException.ThrowIfNull(dados.Endereco);

        var hub = await Carregar(suporte.Contexto.Hubs, id, cancelamento).ConfigureAwait(false);
        suporte.ExigirVersao(hub, versao);

        var campos = hub.AtualizarDados(
            dados.Nome, dados.Endereco.ParaDominio(), CoordenadaGeografica.Criar(dados.Latitude, dados.Longitude), suporte.Agora);

        if (campos.Count > 0)
        {
            await GarantirNomeLivreAsync(hub.NomeNormalizado, excetoId: id, cancelamento).ConfigureAwait(false);
            suporte.Auditar(Recurso, AcoesDeCadastro.Alterado, hub.Id, new { campos });
            await suporte.SalvarAsync(cancelamento, (NomesDeRestricoes.NomeDoHubPorOrganizacao, HubJaCadastrado)).ConfigureAwait(false);
        }

        return ParaResumo(hub);
    }

    /// <summary>Ativa um hub.</summary>
    public async Task<HubResumo> AtivarAsync(Guid id, CancellationToken cancelamento) =>
        ParaResumo(await suporte.AlterarSituacaoAsync(
            suporte.Contexto.Hubs, id, ativar: true, Recurso, CodigoNaoEncontrado, MensagemNaoEncontrado, cancelamento).ConfigureAwait(false));

    /// <summary>Inativa um hub.</summary>
    public async Task<HubResumo> InativarAsync(Guid id, CancellationToken cancelamento) =>
        ParaResumo(await suporte.AlterarSituacaoAsync(
            suporte.Contexto.Hubs, id, ativar: false, Recurso, CodigoNaoEncontrado, MensagemNaoEncontrado, cancelamento).ConfigureAwait(false));

    private async Task GarantirNomeLivreAsync(string nomeNormalizado, Guid? excetoId, CancellationToken cancelamento)
    {
        var emUso = await suporte.Contexto.Hubs
            .AnyAsync(hub => hub.NomeNormalizado == nomeNormalizado && hub.Id != excetoId, cancelamento)
            .ConfigureAwait(false);

        if (emUso)
        {
            throw HubJaCadastrado();
        }
    }

    private static Task<Hub> Carregar(IQueryable<Hub> conjunto, Guid id, CancellationToken cancelamento) =>
        SuporteDeCadastro.CarregarAsync(conjunto, id, CodigoNaoEncontrado, MensagemNaoEncontrado, cancelamento);

    private static ExcecaoDeDominio HubJaCadastrado() =>
        ExcecaoDeDominio.Conflito("hub_ja_cadastrado", "Já existe um hub com este nome na organização.");

    private static HubResumo ParaResumo(Hub hub) => new(
        hub.Id, hub.Nome, EnderecoResumo.De(hub.Endereco), CoordenadaResumo.De(hub.Localizacao)!, hub.Ativo,
        hub.CriadoEm, hub.AtualizadoEm, hub.Versao);
}

/// <summary>Cadastro de clientes da organização autenticada.</summary>
public sealed class GestaoDeClientes(SuporteDeCadastro suporte, ILogger<GestaoDeClientes> log)
{
    private const string Recurso = "cliente";
    private const string CodigoNaoEncontrado = "cliente_nao_encontrado";
    private const string MensagemNaoEncontrado = "Cliente não encontrado.";

    /// <summary>Lista clientes. A busca procura no nome e no CNPJ.</summary>
    public async Task<PaginaDeResultados<ClienteResumo>> ListarAsync(FiltroDeCadastro filtro, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(filtro);

        var consulta = suporte.Contexto.Clientes.AsNoTracking();
        if (filtro.Ativo is { } ativo)
        {
            consulta = consulta.Where(cliente => cliente.Ativo == ativo);
        }

        if (filtro.BuscaNormalizada is { } busca)
        {
            var buscaDeCnpj = new string(busca.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
            consulta = consulta.Where(cliente =>
                cliente.NomeNormalizado.Contains(busca)
                || (buscaDeCnpj.Length > 0 && cliente.Cnpj != null && cliente.Cnpj.Contains(buscaDeCnpj)));
        }

        var (itens, total) = await SuporteDeCadastro.PaginarAsync(
            consulta.OrderBy(cliente => cliente.NomeNormalizado).ThenBy(cliente => cliente.Id), filtro, cancelamento)
            .ConfigureAwait(false);

        return new PaginaDeResultados<ClienteResumo>([.. itens.Select(ParaResumo)], filtro.Pagina, filtro.TamanhoDaPagina, total);
    }

    /// <summary>Obtém um cliente.</summary>
    public async Task<ClienteResumo> ObterAsync(Guid id, CancellationToken cancelamento) =>
        ParaResumo(await Carregar(suporte.Contexto.Clientes.AsNoTracking(), id, cancelamento).ConfigureAwait(false));

    /// <summary>Cadastra um cliente.</summary>
    public async Task<ClienteResumo> CriarAsync(DadosDeCliente dados, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(dados);

        var cnpj = Cnpj.CriarOpcional(dados.Cnpj);
        await GarantirCnpjLivreAsync(cnpj, excetoId: null, cancelamento).ConfigureAwait(false);

        var cliente = Cliente.Criar(suporte.NovoIdentificador(), suporte.OrganizacaoId, dados.Nome, cnpj, suporte.Agora);

        suporte.Contexto.Clientes.Add(cliente);
        suporte.Auditar(Recurso, AcoesDeCadastro.Criado, cliente.Id, new { });
        await suporte.SalvarAsync(cancelamento, (NomesDeRestricoes.CnpjDoClientePorOrganizacao, CnpjJaCadastrado)).ConfigureAwait(false);

        log.LogInformation("Cliente {ClienteId} cadastrado na organização {OrganizacaoId}.", cliente.Id, cliente.OrganizacaoId);
        return ParaResumo(cliente);
    }

    /// <summary>Atualiza os dados de um cliente, exigindo a versão lida.</summary>
    public async Task<ClienteResumo> AtualizarAsync(Guid id, uint versao, DadosDeCliente dados, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(dados);

        var cliente = await Carregar(suporte.Contexto.Clientes, id, cancelamento).ConfigureAwait(false);
        suporte.ExigirVersao(cliente, versao);

        var cnpj = Cnpj.CriarOpcional(dados.Cnpj);
        await GarantirCnpjLivreAsync(cnpj, excetoId: id, cancelamento).ConfigureAwait(false);

        var campos = cliente.AtualizarDados(dados.Nome, cnpj, suporte.Agora);
        if (campos.Count > 0)
        {
            suporte.Auditar(Recurso, AcoesDeCadastro.Alterado, cliente.Id, new { campos });
            await suporte.SalvarAsync(cancelamento, (NomesDeRestricoes.CnpjDoClientePorOrganizacao, CnpjJaCadastrado)).ConfigureAwait(false);
        }

        return ParaResumo(cliente);
    }

    /// <summary>Ativa um cliente.</summary>
    public async Task<ClienteResumo> AtivarAsync(Guid id, CancellationToken cancelamento) =>
        ParaResumo(await suporte.AlterarSituacaoAsync(
            suporte.Contexto.Clientes, id, ativar: true, Recurso, CodigoNaoEncontrado, MensagemNaoEncontrado, cancelamento).ConfigureAwait(false));

    /// <summary>Inativa um cliente.</summary>
    public async Task<ClienteResumo> InativarAsync(Guid id, CancellationToken cancelamento) =>
        ParaResumo(await suporte.AlterarSituacaoAsync(
            suporte.Contexto.Clientes, id, ativar: false, Recurso, CodigoNaoEncontrado, MensagemNaoEncontrado, cancelamento).ConfigureAwait(false));

    private async Task GarantirCnpjLivreAsync(Cnpj? cnpj, Guid? excetoId, CancellationToken cancelamento)
    {
        if (cnpj is null)
        {
            return;
        }

        var emUso = await suporte.Contexto.Clientes
            .AnyAsync(cliente => cliente.Cnpj == cnpj.Valor && cliente.Id != excetoId, cancelamento)
            .ConfigureAwait(false);

        if (emUso)
        {
            throw CnpjJaCadastrado();
        }
    }

    private static Task<Cliente> Carregar(IQueryable<Cliente> conjunto, Guid id, CancellationToken cancelamento) =>
        SuporteDeCadastro.CarregarAsync(conjunto, id, CodigoNaoEncontrado, MensagemNaoEncontrado, cancelamento);

    private static ExcecaoDeDominio CnpjJaCadastrado() =>
        ExcecaoDeDominio.Conflito("cnpj_ja_cadastrado", "Já existe um cliente com este CNPJ na organização.");

    private static ClienteResumo ParaResumo(Cliente cliente) => new(
        cliente.Id,
        cliente.Nome,
        cliente.Cnpj is null ? null : Domain.Clientes.Cnpj.Criar(cliente.Cnpj).Formatado,
        cliente.Ativo,
        cliente.CriadoEm,
        cliente.AtualizadoEm,
        cliente.Versao);
}

/// <summary>Cadastro de destinatários da organização autenticada.</summary>
public sealed class GestaoDeDestinatarios(SuporteDeCadastro suporte, ILogger<GestaoDeDestinatarios> log)
{
    private const string Recurso = "destinatario";
    private const string CodigoNaoEncontrado = "destinatario_nao_encontrado";
    private const string MensagemNaoEncontrado = "Destinatário não encontrado.";

    /// <summary>Lista destinatários.</summary>
    public async Task<PaginaDeResultados<DestinatarioResumo>> ListarAsync(FiltroDeCadastro filtro, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(filtro);

        var consulta = suporte.Contexto.Destinatarios.AsNoTracking();
        if (filtro.Ativo is { } ativo)
        {
            consulta = consulta.Where(destinatario => destinatario.Ativo == ativo);
        }

        if (filtro.BuscaNormalizada is { } busca)
        {
            consulta = consulta.Where(destinatario => destinatario.NomeNormalizado.Contains(busca));
        }

        var (itens, total) = await SuporteDeCadastro.PaginarAsync(
            consulta.OrderBy(destinatario => destinatario.NomeNormalizado).ThenBy(destinatario => destinatario.Id), filtro, cancelamento)
            .ConfigureAwait(false);

        return new PaginaDeResultados<DestinatarioResumo>([.. itens.Select(ParaResumo)], filtro.Pagina, filtro.TamanhoDaPagina, total);
    }

    /// <summary>Obtém um destinatário.</summary>
    public async Task<DestinatarioResumo> ObterAsync(Guid id, CancellationToken cancelamento) =>
        ParaResumo(await Carregar(suporte.Contexto.Destinatarios.AsNoTracking(), id, cancelamento).ConfigureAwait(false));

    /// <summary>Cadastra um destinatário.</summary>
    public async Task<DestinatarioResumo> CriarAsync(DadosDeDestinatario dados, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(dados);
        ArgumentNullException.ThrowIfNull(dados.Endereco);

        var destinatario = Destinatario.Criar(
            suporte.NovoIdentificador(),
            suporte.OrganizacaoId,
            dados.Nome,
            Telefone.CriarOpcional(dados.Telefone),
            dados.Endereco.ParaDominio(),
            CoordenadaGeografica.CriarOpcional(dados.Latitude, dados.Longitude),
            dados.InstrucoesDeEntrega,
            suporte.Agora);

        suporte.Contexto.Destinatarios.Add(destinatario);
        suporte.Auditar(Recurso, AcoesDeCadastro.Criado, destinatario.Id, new { });
        await suporte.SalvarAsync(cancelamento).ConfigureAwait(false);

        // Nome, telefone e endereço de destinatário são dados pessoais: fora do log.
        log.LogInformation("Destinatário {DestinatarioId} cadastrado na organização {OrganizacaoId}.", destinatario.Id, destinatario.OrganizacaoId);
        return ParaResumo(destinatario);
    }

    /// <summary>Atualiza os dados de um destinatário, exigindo a versão lida.</summary>
    public async Task<DestinatarioResumo> AtualizarAsync(Guid id, uint versao, DadosDeDestinatario dados, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(dados);
        ArgumentNullException.ThrowIfNull(dados.Endereco);

        var destinatario = await Carregar(suporte.Contexto.Destinatarios, id, cancelamento).ConfigureAwait(false);
        suporte.ExigirVersao(destinatario, versao);

        var campos = destinatario.AtualizarDados(
            dados.Nome,
            Telefone.CriarOpcional(dados.Telefone),
            dados.Endereco.ParaDominio(),
            CoordenadaGeografica.CriarOpcional(dados.Latitude, dados.Longitude),
            dados.InstrucoesDeEntrega,
            suporte.Agora);

        if (campos.Count > 0)
        {
            suporte.Auditar(Recurso, AcoesDeCadastro.Alterado, destinatario.Id, new { campos });
            await suporte.SalvarAsync(cancelamento).ConfigureAwait(false);
        }

        return ParaResumo(destinatario);
    }

    /// <summary>Ativa um destinatário.</summary>
    public async Task<DestinatarioResumo> AtivarAsync(Guid id, CancellationToken cancelamento) =>
        ParaResumo(await suporte.AlterarSituacaoAsync(
            suporte.Contexto.Destinatarios, id, ativar: true, Recurso, CodigoNaoEncontrado, MensagemNaoEncontrado, cancelamento).ConfigureAwait(false));

    /// <summary>Inativa um destinatário.</summary>
    public async Task<DestinatarioResumo> InativarAsync(Guid id, CancellationToken cancelamento) =>
        ParaResumo(await suporte.AlterarSituacaoAsync(
            suporte.Contexto.Destinatarios, id, ativar: false, Recurso, CodigoNaoEncontrado, MensagemNaoEncontrado, cancelamento).ConfigureAwait(false));

    private static Task<Destinatario> Carregar(IQueryable<Destinatario> conjunto, Guid id, CancellationToken cancelamento) =>
        SuporteDeCadastro.CarregarAsync(conjunto, id, CodigoNaoEncontrado, MensagemNaoEncontrado, cancelamento);

    private static DestinatarioResumo ParaResumo(Destinatario destinatario) => new(
        destinatario.Id,
        destinatario.Nome,
        destinatario.Telefone,
        EnderecoResumo.De(destinatario.Endereco),
        CoordenadaResumo.De(destinatario.Localizacao),
        destinatario.InstrucoesDeEntrega,
        destinatario.Ativo,
        destinatario.CriadoEm,
        destinatario.AtualizadoEm,
        destinatario.Versao);
}
