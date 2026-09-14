using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Application.Comum;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Comum;
using TorreLogistica.Domain.Frota;

namespace TorreLogistica.Application.Cadastros;

/// <summary>Motorista como devolvido pela API. Sem dado além do cadastrado.</summary>
public sealed record MotoristaResumo(
    Guid Id,
    string Nome,
    string? Telefone,
    Guid? UsuarioId,
    bool Ativo,
    DateTimeOffset CriadoEm,
    DateTimeOffset AtualizadoEm,
    uint Versao);

/// <summary>Dados de motorista recebidos do cliente.</summary>
public sealed record DadosDeMotorista(string? Nome, string? Telefone);

/// <summary>Veículo como devolvido pela API.</summary>
public sealed record VeiculoResumo(
    Guid Id,
    string Placa,
    string Identificacao,
    TipoDeVeiculo Tipo,
    int? CapacidadeEmKg,
    bool Ativo,
    DateTimeOffset CriadoEm,
    DateTimeOffset AtualizadoEm,
    uint Versao);

/// <summary>Dados de veículo recebidos do cliente.</summary>
public sealed record DadosDeVeiculo(string? Placa, string? Identificacao, TipoDeVeiculo Tipo, int? CapacidadeEmKg);

/// <summary>Cadastro de motoristas da organização autenticada.</summary>
public sealed class GestaoDeMotoristas(SuporteDeCadastro suporte, ILogger<GestaoDeMotoristas> log)
{
    private const string Recurso = "motorista";

    /// <summary>Lista motoristas.</summary>
    public async Task<PaginaDeResultados<MotoristaResumo>> ListarAsync(FiltroDeCadastro filtro, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(filtro);

        var consulta = suporte.Contexto.Motoristas.AsNoTracking();
        if (filtro.Ativo is { } ativo)
        {
            consulta = consulta.Where(motorista => motorista.Ativo == ativo);
        }

        if (filtro.BuscaNormalizada is { } busca)
        {
            consulta = consulta.Where(motorista => motorista.NomeNormalizado.Contains(busca));
        }

        var (itens, total) = await SuporteDeCadastro.PaginarAsync(
            consulta.OrderBy(motorista => motorista.NomeNormalizado).ThenBy(motorista => motorista.Id), filtro, cancelamento)
            .ConfigureAwait(false);

        return new PaginaDeResultados<MotoristaResumo>([.. itens.Select(ParaResumo)], filtro.Pagina, filtro.TamanhoDaPagina, total);
    }

    /// <summary>Obtém um motorista.</summary>
    public async Task<MotoristaResumo> ObterAsync(Guid id, CancellationToken cancelamento) =>
        ParaResumo(await Carregar(suporte.Contexto.Motoristas.AsNoTracking(), id, cancelamento).ConfigureAwait(false));

    /// <summary>Cadastra um motorista.</summary>
    public async Task<MotoristaResumo> CriarAsync(DadosDeMotorista dados, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(dados);

        var motorista = Motorista.Criar(
            suporte.NovoIdentificador(), suporte.OrganizacaoId, dados.Nome, Telefone.CriarOpcional(dados.Telefone), suporte.Agora);

        suporte.Contexto.Motoristas.Add(motorista);
        suporte.Auditar(Recurso, AcoesDeCadastro.Criado, motorista.Id, new { });
        await suporte.SalvarAsync(cancelamento).ConfigureAwait(false);

        log.LogInformation("Motorista {MotoristaId} cadastrado na organização {OrganizacaoId}.", motorista.Id, motorista.OrganizacaoId);
        return ParaResumo(motorista);
    }

    /// <summary>Atualiza os dados de um motorista, exigindo a versão lida.</summary>
    public async Task<MotoristaResumo> AtualizarAsync(Guid id, uint versao, DadosDeMotorista dados, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(dados);

        var motorista = await Carregar(suporte.Contexto.Motoristas, id, cancelamento).ConfigureAwait(false);
        suporte.ExigirVersao(motorista, versao);

        var campos = motorista.AtualizarDados(dados.Nome, Telefone.CriarOpcional(dados.Telefone), suporte.Agora);
        if (campos.Count > 0)
        {
            suporte.Auditar(Recurso, AcoesDeCadastro.Alterado, motorista.Id, new { campos });
            await suporte.SalvarAsync(cancelamento).ConfigureAwait(false);
        }

        return ParaResumo(motorista);
    }

    /// <summary>Ativa um motorista.</summary>
    public async Task<MotoristaResumo> AtivarAsync(Guid id, CancellationToken cancelamento) =>
        ParaResumo(await suporte.AlterarSituacaoAsync(
            suporte.Contexto.Motoristas, id, ativar: true, Recurso, CodigoNaoEncontrado, MensagemNaoEncontrado, cancelamento).ConfigureAwait(false));

    /// <summary>Inativa um motorista.</summary>
    public async Task<MotoristaResumo> InativarAsync(Guid id, CancellationToken cancelamento) =>
        ParaResumo(await suporte.AlterarSituacaoAsync(
            suporte.Contexto.Motoristas, id, ativar: false, Recurso, CodigoNaoEncontrado, MensagemNaoEncontrado, cancelamento).ConfigureAwait(false));

    /// <summary>
    /// Associa a conta de acesso (perfil Motorista) ao motorista.
    /// </summary>
    /// <remarks>
    /// O identificador da conta vem do corpo, mas é procurado na consulta filtrada pelo
    /// tenant: conta de outra organização responde exatamente como conta inexistente.
    /// </remarks>
    public async Task<MotoristaResumo> AssociarContaAsync(Guid id, Guid usuarioId, CancellationToken cancelamento)
    {
        var motorista = await Carregar(suporte.Contexto.Motoristas, id, cancelamento).ConfigureAwait(false);

        var conta = await suporte.Contexto.Usuarios
            .AsNoTracking()
            .SingleOrDefaultAsync(usuario => usuario.Id == usuarioId, cancelamento)
            .ConfigureAwait(false)
            ?? throw ExcecaoDeDominio.NaoEncontrado("usuario_nao_encontrado", "Usuário não encontrado.");

        var jaUsada = await suporte.Contexto.Motoristas
            .AnyAsync(outro => outro.UsuarioId == usuarioId && outro.Id != id, cancelamento)
            .ConfigureAwait(false);

        if (jaUsada)
        {
            throw ContaJaAssociada();
        }

        if (motorista.AssociarConta(conta, suporte.Agora))
        {
            suporte.Auditar(Recurso, AcoesDeCadastro.ContaAssociada, motorista.Id, new { usuarioId });
            await suporte.SalvarAsync(cancelamento, (NomesDeRestricoes.ContaDoMotorista, ContaJaAssociada)).ConfigureAwait(false);

            log.LogInformation("Conta {UsuarioId} associada ao motorista {MotoristaId}.", usuarioId, motorista.Id);
        }

        return ParaResumo(motorista);
    }

    /// <summary>Desassocia a conta de acesso do motorista.</summary>
    public async Task<MotoristaResumo> DesassociarContaAsync(Guid id, CancellationToken cancelamento)
    {
        var motorista = await Carregar(suporte.Contexto.Motoristas, id, cancelamento).ConfigureAwait(false);
        var contaAnterior = motorista.UsuarioId;

        if (motorista.DesassociarConta(suporte.Agora))
        {
            suporte.Auditar(Recurso, AcoesDeCadastro.ContaDesassociada, motorista.Id, new { usuarioId = contaAnterior });
            await suporte.SalvarAsync(cancelamento).ConfigureAwait(false);
        }

        return ParaResumo(motorista);
    }

    private const string CodigoNaoEncontrado = "motorista_nao_encontrado";
    private const string MensagemNaoEncontrado = "Motorista não encontrado.";

    private static Task<Motorista> Carregar(IQueryable<Motorista> conjunto, Guid id, CancellationToken cancelamento) =>
        SuporteDeCadastro.CarregarAsync(conjunto, id, CodigoNaoEncontrado, MensagemNaoEncontrado, cancelamento);

    private static ExcecaoDeDominio ContaJaAssociada() =>
        ExcecaoDeDominio.Conflito("conta_ja_associada", "Esta conta já está associada a outro motorista.");

    private static MotoristaResumo ParaResumo(Motorista motorista) => new(
        motorista.Id, motorista.Nome, motorista.Telefone, motorista.UsuarioId, motorista.Ativo,
        motorista.CriadoEm, motorista.AtualizadoEm, motorista.Versao);
}

/// <summary>Cadastro de veículos da organização autenticada.</summary>
public sealed class GestaoDeVeiculos(SuporteDeCadastro suporte, ILogger<GestaoDeVeiculos> log)
{
    private const string Recurso = "veiculo";
    private const string CodigoNaoEncontrado = "veiculo_nao_encontrado";
    private const string MensagemNaoEncontrado = "Veículo não encontrado.";

    /// <summary>Lista veículos. A busca procura na identificação e na placa.</summary>
    public async Task<PaginaDeResultados<VeiculoResumo>> ListarAsync(FiltroDeCadastro filtro, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(filtro);

        var consulta = suporte.Contexto.Veiculos.AsNoTracking();
        if (filtro.Ativo is { } ativo)
        {
            consulta = consulta.Where(veiculo => veiculo.Ativo == ativo);
        }

        if (filtro.BuscaNormalizada is { } busca)
        {
            var buscaDePlaca = busca.Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
            consulta = consulta.Where(veiculo =>
                veiculo.IdentificacaoNormalizada.Contains(busca) || veiculo.Placa.Contains(buscaDePlaca));
        }

        var (itens, total) = await SuporteDeCadastro.PaginarAsync(
            consulta.OrderBy(veiculo => veiculo.IdentificacaoNormalizada).ThenBy(veiculo => veiculo.Id), filtro, cancelamento)
            .ConfigureAwait(false);

        return new PaginaDeResultados<VeiculoResumo>([.. itens.Select(ParaResumo)], filtro.Pagina, filtro.TamanhoDaPagina, total);
    }

    /// <summary>Obtém um veículo.</summary>
    public async Task<VeiculoResumo> ObterAsync(Guid id, CancellationToken cancelamento) =>
        ParaResumo(await Carregar(suporte.Contexto.Veiculos.AsNoTracking(), id, cancelamento).ConfigureAwait(false));

    /// <summary>Cadastra um veículo.</summary>
    public async Task<VeiculoResumo> CriarAsync(DadosDeVeiculo dados, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(dados);

        var placa = PlacaDeVeiculo.Criar(dados.Placa);
        await GarantirPlacaLivreAsync(placa, excetoId: null, cancelamento).ConfigureAwait(false);

        var veiculo = Veiculo.Criar(
            suporte.NovoIdentificador(), suporte.OrganizacaoId, placa, dados.Identificacao, dados.Tipo, dados.CapacidadeEmKg, suporte.Agora);

        suporte.Contexto.Veiculos.Add(veiculo);
        suporte.Auditar(Recurso, AcoesDeCadastro.Criado, veiculo.Id, new { tipo = veiculo.Tipo.ToString() });
        await suporte.SalvarAsync(cancelamento, (NomesDeRestricoes.PlacaDoVeiculoPorOrganizacao, PlacaJaCadastrada)).ConfigureAwait(false);

        log.LogInformation("Veículo {VeiculoId} cadastrado na organização {OrganizacaoId}.", veiculo.Id, veiculo.OrganizacaoId);
        return ParaResumo(veiculo);
    }

    /// <summary>Atualiza os dados de um veículo, exigindo a versão lida.</summary>
    public async Task<VeiculoResumo> AtualizarAsync(Guid id, uint versao, DadosDeVeiculo dados, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(dados);

        var veiculo = await Carregar(suporte.Contexto.Veiculos, id, cancelamento).ConfigureAwait(false);
        suporte.ExigirVersao(veiculo, versao);

        var placa = PlacaDeVeiculo.Criar(dados.Placa);
        await GarantirPlacaLivreAsync(placa, excetoId: id, cancelamento).ConfigureAwait(false);

        var campos = veiculo.AtualizarDados(placa, dados.Identificacao, dados.Tipo, dados.CapacidadeEmKg, suporte.Agora);
        if (campos.Count > 0)
        {
            suporte.Auditar(Recurso, AcoesDeCadastro.Alterado, veiculo.Id, new { campos });
            await suporte.SalvarAsync(cancelamento, (NomesDeRestricoes.PlacaDoVeiculoPorOrganizacao, PlacaJaCadastrada)).ConfigureAwait(false);
        }

        return ParaResumo(veiculo);
    }

    /// <summary>Ativa um veículo.</summary>
    public async Task<VeiculoResumo> AtivarAsync(Guid id, CancellationToken cancelamento) =>
        ParaResumo(await suporte.AlterarSituacaoAsync(
            suporte.Contexto.Veiculos, id, ativar: true, Recurso, CodigoNaoEncontrado, MensagemNaoEncontrado, cancelamento).ConfigureAwait(false));

    /// <summary>Inativa um veículo.</summary>
    public async Task<VeiculoResumo> InativarAsync(Guid id, CancellationToken cancelamento) =>
        ParaResumo(await suporte.AlterarSituacaoAsync(
            suporte.Contexto.Veiculos, id, ativar: false, Recurso, CodigoNaoEncontrado, MensagemNaoEncontrado, cancelamento).ConfigureAwait(false));

    private async Task GarantirPlacaLivreAsync(PlacaDeVeiculo placa, Guid? excetoId, CancellationToken cancelamento)
    {
        var emUso = await suporte.Contexto.Veiculos
            .AnyAsync(veiculo => veiculo.Placa == placa.Valor && veiculo.Id != excetoId, cancelamento)
            .ConfigureAwait(false);

        if (emUso)
        {
            throw PlacaJaCadastrada();
        }
    }

    private static Task<Veiculo> Carregar(IQueryable<Veiculo> conjunto, Guid id, CancellationToken cancelamento) =>
        SuporteDeCadastro.CarregarAsync(conjunto, id, CodigoNaoEncontrado, MensagemNaoEncontrado, cancelamento);

    private static ExcecaoDeDominio PlacaJaCadastrada() =>
        ExcecaoDeDominio.Conflito("placa_ja_cadastrada", "Já existe um veículo com esta placa na organização.");

    private static VeiculoResumo ParaResumo(Veiculo veiculo) => new(
        veiculo.Id, veiculo.Placa, veiculo.Identificacao, veiculo.Tipo, veiculo.CapacidadeEmKg, veiculo.Ativo,
        veiculo.CriadoEm, veiculo.AtualizadoEm, veiculo.Versao);
}
