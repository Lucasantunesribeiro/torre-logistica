using System.ComponentModel.DataAnnotations;
using TorreLogistica.Api.Autenticacao;
using TorreLogistica.Application.Cadastros;
using TorreLogistica.Application.Comum;
using TorreLogistica.Domain.Clientes;
using TorreLogistica.Domain.Frota;
using TorreLogistica.Domain.Operacao;

namespace TorreLogistica.Api.Cadastros;

/// <summary>Endereço no corpo. O formato fino (CEP, UF) é regra de domínio e responde 422.</summary>
public sealed record RequisicaoDeEndereco(
    [property: Required(ErrorMessage = "Informe o logradouro.")] string? Logradouro,
    [property: Required(ErrorMessage = "Informe o número.")] string? Numero,
    string? Complemento,
    [property: Required(ErrorMessage = "Informe o bairro.")] string? Bairro,
    [property: Required(ErrorMessage = "Informe a cidade.")] string? Cidade,
    [property: Required(ErrorMessage = "Informe a UF.")] string? Uf,
    [property: Required(ErrorMessage = "Informe o CEP.")] string? Cep)
{
    /// <summary>Converte para o modelo da aplicação.</summary>
    public DadosDeEndereco ParaDados() => new(Logradouro, Numero, Complemento, Bairro, Cidade, Uf, Cep);
}

/// <summary>Criação de motorista.</summary>
public sealed record RequisicaoDeMotorista(
    [property: Required(ErrorMessage = "Informe o nome.")][property: StringLength(Motorista.TamanhoMaximoDoNome)] string? Nome,
    [property: StringLength(32)] string? Telefone);

/// <summary>Alteração de motorista, com a versão lida.</summary>
public sealed record RequisicaoDeAtualizacaoDeMotorista(
    [property: Required(ErrorMessage = "Informe a versão lida.")] uint? Versao,
    [property: Required(ErrorMessage = "Informe o nome.")][property: StringLength(Motorista.TamanhoMaximoDoNome)] string? Nome,
    [property: StringLength(32)] string? Telefone);

/// <summary>Associação de conta de acesso ao motorista.</summary>
public sealed record RequisicaoDeAssociacaoDeConta(
    [property: Required(ErrorMessage = "Informe a conta.")] Guid? UsuarioId);

/// <summary>Criação de veículo.</summary>
public sealed record RequisicaoDeVeiculo(
    [property: Required(ErrorMessage = "Informe a placa.")][property: StringLength(16)] string? Placa,
    [property: Required(ErrorMessage = "Informe a identificação.")][property: StringLength(Veiculo.TamanhoMaximoDaIdentificacao)] string? Identificacao,
    [property: Required(ErrorMessage = "Informe o tipo.")] TipoDeVeiculo? Tipo,
    int? CapacidadeEmKg);

/// <summary>Alteração de veículo, com a versão lida.</summary>
public sealed record RequisicaoDeAtualizacaoDeVeiculo(
    [property: Required(ErrorMessage = "Informe a versão lida.")] uint? Versao,
    [property: Required(ErrorMessage = "Informe a placa.")][property: StringLength(16)] string? Placa,
    [property: Required(ErrorMessage = "Informe a identificação.")][property: StringLength(Veiculo.TamanhoMaximoDaIdentificacao)] string? Identificacao,
    [property: Required(ErrorMessage = "Informe o tipo.")] TipoDeVeiculo? Tipo,
    int? CapacidadeEmKg);

/// <summary>Criação de hub.</summary>
public sealed record RequisicaoDeHub(
    [property: Required(ErrorMessage = "Informe o nome.")][property: StringLength(Hub.TamanhoMaximoDoNome)] string? Nome,
    [property: Required(ErrorMessage = "Informe o endereço.")] RequisicaoDeEndereco? Endereco,
    [property: Required(ErrorMessage = "Informe a latitude.")] double? Latitude,
    [property: Required(ErrorMessage = "Informe a longitude.")] double? Longitude);

/// <summary>Alteração de hub, com a versão lida.</summary>
public sealed record RequisicaoDeAtualizacaoDeHub(
    [property: Required(ErrorMessage = "Informe a versão lida.")] uint? Versao,
    [property: Required(ErrorMessage = "Informe o nome.")][property: StringLength(Hub.TamanhoMaximoDoNome)] string? Nome,
    [property: Required(ErrorMessage = "Informe o endereço.")] RequisicaoDeEndereco? Endereco,
    [property: Required(ErrorMessage = "Informe a latitude.")] double? Latitude,
    [property: Required(ErrorMessage = "Informe a longitude.")] double? Longitude);

/// <summary>Criação de cliente.</summary>
public sealed record RequisicaoDeCliente(
    [property: Required(ErrorMessage = "Informe o nome.")][property: StringLength(Cliente.TamanhoMaximoDoNome)] string? Nome,
    [property: StringLength(32)] string? Cnpj);

/// <summary>Alteração de cliente, com a versão lida.</summary>
public sealed record RequisicaoDeAtualizacaoDeCliente(
    [property: Required(ErrorMessage = "Informe a versão lida.")] uint? Versao,
    [property: Required(ErrorMessage = "Informe o nome.")][property: StringLength(Cliente.TamanhoMaximoDoNome)] string? Nome,
    [property: StringLength(32)] string? Cnpj);

/// <summary>Criação de destinatário.</summary>
public sealed record RequisicaoDeDestinatario(
    [property: Required(ErrorMessage = "Informe o nome.")][property: StringLength(Destinatario.TamanhoMaximoDoNome)] string? Nome,
    [property: StringLength(32)] string? Telefone,
    [property: Required(ErrorMessage = "Informe o endereço.")] RequisicaoDeEndereco? Endereco,
    double? Latitude,
    double? Longitude,
    [property: StringLength(Destinatario.TamanhoMaximoDasInstrucoes)] string? InstrucoesDeEntrega);

/// <summary>Alteração de destinatário, com a versão lida.</summary>
public sealed record RequisicaoDeAtualizacaoDeDestinatario(
    [property: Required(ErrorMessage = "Informe a versão lida.")] uint? Versao,
    [property: Required(ErrorMessage = "Informe o nome.")][property: StringLength(Destinatario.TamanhoMaximoDoNome)] string? Nome,
    [property: StringLength(32)] string? Telefone,
    [property: Required(ErrorMessage = "Informe o endereço.")] RequisicaoDeEndereco? Endereco,
    double? Latitude,
    double? Longitude,
    [property: StringLength(Destinatario.TamanhoMaximoDasInstrucoes)] string? InstrucoesDeEntrega);

/// <summary>
/// Endpoints da estrutura operacional: motoristas, veículos, hubs, clientes e destinatários.
/// </summary>
/// <remarks>
/// <para>
/// O mesmo desenho nos cinco: listagem paginada com filtro de situação e busca, leitura,
/// criação, alteração com versão, ativação e inativação. Não há <c>DELETE</c>: inativar é a
/// forma de retirar um cadastro de uso sem reescrever o histórico das entregas.
/// </para>
/// <para>
/// Alteração exige no corpo a <c>versao</c> recebida na leitura. Se outra pessoa gravou
/// antes, a resposta é <c>409 conflito_de_versao</c> — nunca a sobrescrita silenciosa.
/// </para>
/// </remarks>
public static class EndpointsDeCadastros
{
    private const int TamanhoMaximoDaBusca = 100;

    /// <summary>Registra os endpoints.</summary>
    public static IEndpointRouteBuilder MapearEndpointsDeCadastros(this IEndpointRouteBuilder rotas)
    {
        ArgumentNullException.ThrowIfNull(rotas);

        MapearMotoristas(rotas.MapGroup("/api/motoristas").WithTags("Motoristas"));
        MapearVeiculos(rotas.MapGroup("/api/veiculos").WithTags("Veículos"));
        MapearHubs(rotas.MapGroup("/api/hubs").WithTags("Hubs"));
        MapearClientes(rotas.MapGroup("/api/clientes").WithTags("Clientes"));
        MapearDestinatarios(rotas.MapGroup("/api/destinatarios").WithTags("Destinatários"));

        return rotas;
    }

    private static void MapearMotoristas(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/", async (int? pagina, int? tamanhoDaPagina, bool? ativo, string? busca, GestaoDeMotoristas gestao, CancellationToken cancelamento) =>
                MontarFiltro(pagina, tamanhoDaPagina, ativo, busca) is { } filtro
                    ? Results.Ok(await gestao.ListarAsync(filtro, cancelamento).ConfigureAwait(false))
                    : FiltroInvalido())
            .RequireAuthorization(Politicas.LeituraDaOperacao);

        grupo.MapGet("/{id:guid}", (Guid id, GestaoDeMotoristas gestao, CancellationToken cancelamento) =>
                gestao.ObterAsync(id, cancelamento))
            .RequireAuthorization(Politicas.LeituraDaOperacao);

        grupo.MapPost("/", async (RequisicaoDeMotorista requisicao, GestaoDeMotoristas gestao, CancellationToken cancelamento) =>
            {
                var criado = await gestao.CriarAsync(new DadosDeMotorista(requisicao.Nome, requisicao.Telefone), cancelamento).ConfigureAwait(false);
                return Results.Created($"/api/motoristas/{criado.Id}", criado);
            })
            .RequireAuthorization(Politicas.GestaoDaOperacao)
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDeMotorista>>();

        grupo.MapPut("/{id:guid}", (Guid id, RequisicaoDeAtualizacaoDeMotorista requisicao, GestaoDeMotoristas gestao, CancellationToken cancelamento) =>
                gestao.AtualizarAsync(id, requisicao.Versao!.Value, new DadosDeMotorista(requisicao.Nome, requisicao.Telefone), cancelamento))
            .RequireAuthorization(Politicas.GestaoDaOperacao)
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDeAtualizacaoDeMotorista>>();

        grupo.MapPost("/{id:guid}/ativacao", (Guid id, GestaoDeMotoristas gestao, CancellationToken cancelamento) =>
                gestao.AtivarAsync(id, cancelamento))
            .RequireAuthorization(Politicas.GestaoDaOperacao);

        grupo.MapPost("/{id:guid}/inativacao", (Guid id, GestaoDeMotoristas gestao, CancellationToken cancelamento) =>
                gestao.InativarAsync(id, cancelamento))
            .RequireAuthorization(Politicas.GestaoDaOperacao);

        grupo.MapPut("/{id:guid}/conta", (Guid id, RequisicaoDeAssociacaoDeConta requisicao, GestaoDeMotoristas gestao, CancellationToken cancelamento) =>
                gestao.AssociarContaAsync(id, requisicao.UsuarioId!.Value, cancelamento))
            .RequireAuthorization(Politicas.GestaoDaOperacao)
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDeAssociacaoDeConta>>();

        grupo.MapDelete("/{id:guid}/conta", (Guid id, GestaoDeMotoristas gestao, CancellationToken cancelamento) =>
                gestao.DesassociarContaAsync(id, cancelamento))
            .RequireAuthorization(Politicas.GestaoDaOperacao);
    }

    private static void MapearVeiculos(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/", async (int? pagina, int? tamanhoDaPagina, bool? ativo, string? busca, GestaoDeVeiculos gestao, CancellationToken cancelamento) =>
                MontarFiltro(pagina, tamanhoDaPagina, ativo, busca) is { } filtro
                    ? Results.Ok(await gestao.ListarAsync(filtro, cancelamento).ConfigureAwait(false))
                    : FiltroInvalido())
            .RequireAuthorization(Politicas.LeituraDaOperacao);

        grupo.MapGet("/{id:guid}", (Guid id, GestaoDeVeiculos gestao, CancellationToken cancelamento) =>
                gestao.ObterAsync(id, cancelamento))
            .RequireAuthorization(Politicas.LeituraDaOperacao);

        grupo.MapPost("/", async (RequisicaoDeVeiculo requisicao, GestaoDeVeiculos gestao, CancellationToken cancelamento) =>
            {
                var criado = await gestao
                    .CriarAsync(new DadosDeVeiculo(requisicao.Placa, requisicao.Identificacao, requisicao.Tipo!.Value, requisicao.CapacidadeEmKg), cancelamento)
                    .ConfigureAwait(false);
                return Results.Created($"/api/veiculos/{criado.Id}", criado);
            })
            .RequireAuthorization(Politicas.GestaoDaOperacao)
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDeVeiculo>>();

        grupo.MapPut("/{id:guid}", (Guid id, RequisicaoDeAtualizacaoDeVeiculo requisicao, GestaoDeVeiculos gestao, CancellationToken cancelamento) =>
                gestao.AtualizarAsync(
                    id,
                    requisicao.Versao!.Value,
                    new DadosDeVeiculo(requisicao.Placa, requisicao.Identificacao, requisicao.Tipo!.Value, requisicao.CapacidadeEmKg),
                    cancelamento))
            .RequireAuthorization(Politicas.GestaoDaOperacao)
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDeAtualizacaoDeVeiculo>>();

        grupo.MapPost("/{id:guid}/ativacao", (Guid id, GestaoDeVeiculos gestao, CancellationToken cancelamento) =>
                gestao.AtivarAsync(id, cancelamento))
            .RequireAuthorization(Politicas.GestaoDaOperacao);

        grupo.MapPost("/{id:guid}/inativacao", (Guid id, GestaoDeVeiculos gestao, CancellationToken cancelamento) =>
                gestao.InativarAsync(id, cancelamento))
            .RequireAuthorization(Politicas.GestaoDaOperacao);
    }

    private static void MapearHubs(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/", async (int? pagina, int? tamanhoDaPagina, bool? ativo, string? busca, GestaoDeHubs gestao, CancellationToken cancelamento) =>
                MontarFiltro(pagina, tamanhoDaPagina, ativo, busca) is { } filtro
                    ? Results.Ok(await gestao.ListarAsync(filtro, cancelamento).ConfigureAwait(false))
                    : FiltroInvalido())
            .RequireAuthorization(Politicas.LeituraDaOperacao);

        grupo.MapGet("/{id:guid}", (Guid id, GestaoDeHubs gestao, CancellationToken cancelamento) =>
                gestao.ObterAsync(id, cancelamento))
            .RequireAuthorization(Politicas.LeituraDaOperacao);

        grupo.MapPost("/", async (RequisicaoDeHub requisicao, GestaoDeHubs gestao, CancellationToken cancelamento) =>
            {
                var criado = await gestao
                    .CriarAsync(
                        new DadosDeHub(requisicao.Nome, requisicao.Endereco!.ParaDados(), requisicao.Latitude!.Value, requisicao.Longitude!.Value),
                        cancelamento)
                    .ConfigureAwait(false);
                return Results.Created($"/api/hubs/{criado.Id}", criado);
            })
            .RequireAuthorization(Politicas.GestaoDaOperacao)
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDeHub>>();

        grupo.MapPut("/{id:guid}", (Guid id, RequisicaoDeAtualizacaoDeHub requisicao, GestaoDeHubs gestao, CancellationToken cancelamento) =>
                gestao.AtualizarAsync(
                    id,
                    requisicao.Versao!.Value,
                    new DadosDeHub(requisicao.Nome, requisicao.Endereco!.ParaDados(), requisicao.Latitude!.Value, requisicao.Longitude!.Value),
                    cancelamento))
            .RequireAuthorization(Politicas.GestaoDaOperacao)
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDeAtualizacaoDeHub>>();

        grupo.MapPost("/{id:guid}/ativacao", (Guid id, GestaoDeHubs gestao, CancellationToken cancelamento) =>
                gestao.AtivarAsync(id, cancelamento))
            .RequireAuthorization(Politicas.GestaoDaOperacao);

        grupo.MapPost("/{id:guid}/inativacao", (Guid id, GestaoDeHubs gestao, CancellationToken cancelamento) =>
                gestao.InativarAsync(id, cancelamento))
            .RequireAuthorization(Politicas.GestaoDaOperacao);
    }

    private static void MapearClientes(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/", async (int? pagina, int? tamanhoDaPagina, bool? ativo, string? busca, GestaoDeClientes gestao, CancellationToken cancelamento) =>
                MontarFiltro(pagina, tamanhoDaPagina, ativo, busca) is { } filtro
                    ? Results.Ok(await gestao.ListarAsync(filtro, cancelamento).ConfigureAwait(false))
                    : FiltroInvalido())
            .RequireAuthorization(Politicas.LeituraDaOperacao);

        grupo.MapGet("/{id:guid}", (Guid id, GestaoDeClientes gestao, CancellationToken cancelamento) =>
                gestao.ObterAsync(id, cancelamento))
            .RequireAuthorization(Politicas.LeituraDaOperacao);

        grupo.MapPost("/", async (RequisicaoDeCliente requisicao, GestaoDeClientes gestao, CancellationToken cancelamento) =>
            {
                var criado = await gestao.CriarAsync(new DadosDeCliente(requisicao.Nome, requisicao.Cnpj), cancelamento).ConfigureAwait(false);
                return Results.Created($"/api/clientes/{criado.Id}", criado);
            })
            .RequireAuthorization(Politicas.GestaoDaOperacao)
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDeCliente>>();

        grupo.MapPut("/{id:guid}", (Guid id, RequisicaoDeAtualizacaoDeCliente requisicao, GestaoDeClientes gestao, CancellationToken cancelamento) =>
                gestao.AtualizarAsync(id, requisicao.Versao!.Value, new DadosDeCliente(requisicao.Nome, requisicao.Cnpj), cancelamento))
            .RequireAuthorization(Politicas.GestaoDaOperacao)
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDeAtualizacaoDeCliente>>();

        grupo.MapPost("/{id:guid}/ativacao", (Guid id, GestaoDeClientes gestao, CancellationToken cancelamento) =>
                gestao.AtivarAsync(id, cancelamento))
            .RequireAuthorization(Politicas.GestaoDaOperacao);

        grupo.MapPost("/{id:guid}/inativacao", (Guid id, GestaoDeClientes gestao, CancellationToken cancelamento) =>
                gestao.InativarAsync(id, cancelamento))
            .RequireAuthorization(Politicas.GestaoDaOperacao);
    }

    private static void MapearDestinatarios(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/", async (int? pagina, int? tamanhoDaPagina, bool? ativo, string? busca, GestaoDeDestinatarios gestao, CancellationToken cancelamento) =>
                MontarFiltro(pagina, tamanhoDaPagina, ativo, busca) is { } filtro
                    ? Results.Ok(await gestao.ListarAsync(filtro, cancelamento).ConfigureAwait(false))
                    : FiltroInvalido())
            .RequireAuthorization(Politicas.LeituraDaOperacao);

        grupo.MapGet("/{id:guid}", (Guid id, GestaoDeDestinatarios gestao, CancellationToken cancelamento) =>
                gestao.ObterAsync(id, cancelamento))
            .RequireAuthorization(Politicas.LeituraDaOperacao);

        grupo.MapPost("/", async (RequisicaoDeDestinatario requisicao, GestaoDeDestinatarios gestao, CancellationToken cancelamento) =>
            {
                var criado = await gestao
                    .CriarAsync(
                        new DadosDeDestinatario(
                            requisicao.Nome, requisicao.Telefone, requisicao.Endereco!.ParaDados(),
                            requisicao.Latitude, requisicao.Longitude, requisicao.InstrucoesDeEntrega),
                        cancelamento)
                    .ConfigureAwait(false);
                return Results.Created($"/api/destinatarios/{criado.Id}", criado);
            })
            .RequireAuthorization(Politicas.GestaoDaOperacao)
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDeDestinatario>>();

        grupo.MapPut("/{id:guid}", (Guid id, RequisicaoDeAtualizacaoDeDestinatario requisicao, GestaoDeDestinatarios gestao, CancellationToken cancelamento) =>
                gestao.AtualizarAsync(
                    id,
                    requisicao.Versao!.Value,
                    new DadosDeDestinatario(
                        requisicao.Nome, requisicao.Telefone, requisicao.Endereco!.ParaDados(),
                        requisicao.Latitude, requisicao.Longitude, requisicao.InstrucoesDeEntrega),
                    cancelamento))
            .RequireAuthorization(Politicas.GestaoDaOperacao)
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDeAtualizacaoDeDestinatario>>();

        grupo.MapPost("/{id:guid}/ativacao", (Guid id, GestaoDeDestinatarios gestao, CancellationToken cancelamento) =>
                gestao.AtivarAsync(id, cancelamento))
            .RequireAuthorization(Politicas.GestaoDaOperacao);

        grupo.MapPost("/{id:guid}/inativacao", (Guid id, GestaoDeDestinatarios gestao, CancellationToken cancelamento) =>
                gestao.InativarAsync(id, cancelamento))
            .RequireAuthorization(Politicas.GestaoDaOperacao);
    }

    private static FiltroDeCadastro? MontarFiltro(int? pagina, int? tamanhoDaPagina, bool? ativo, string? busca)
    {
        var numero = pagina ?? 1;
        var tamanho = tamanhoDaPagina ?? LimitesDePaginacao.TamanhoPadrao;

        return numero < 1 || tamanho is < 1 or > LimitesDePaginacao.TamanhoMaximo || busca is { Length: > TamanhoMaximoDaBusca }
            ? null
            : new FiltroDeCadastro(numero, tamanho, ativo, busca);
    }

    private static IResult FiltroInvalido() =>
        Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["filtro"] =
                [
                    $"A página começa em 1, o tamanho vai de 1 a {LimitesDePaginacao.TamanhoMaximo} "
                    + $"e a busca tem no máximo {TamanhoMaximoDaBusca} caracteres.",
                ],
            },
            title: "Requisição inválida");
}
