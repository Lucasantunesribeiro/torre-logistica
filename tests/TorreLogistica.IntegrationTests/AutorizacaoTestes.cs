using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.IntegrationTests.Infra;

namespace TorreLogistica.IntegrationTests;

/// <summary>
/// RBAC e separação de autoridade, verificados contra o roteamento real.
/// </summary>
/// <remarks>A tabela espelha <c>docs/seguranca/matriz-de-autorizacao.md</c>.</remarks>
[Collection(ColecaoDeIntegracao.Nome)]
public sealed class AutorizacaoTestes(ContainerPostgis banco) : TesteDeIntegracao(banco)
{
    private const string Anonimo = "Anonimo";

    private static readonly (string Metodo, string Rota, Dictionary<string, int> Esperado)[] Matriz =
    [
        ("GET", "/api/organizacao", Status(200, 200, 200, 401, 401)),
        ("GET", "/api/usuarios", Status(200, 200, 403, 401, 401)),
        ("GET", "/api/usuarios/{alvo}", Status(200, 200, 403, 401, 401)),
        ("POST", "/api/usuarios", Status(201, 403, 403, 401, 401)),
        ("PUT", "/api/usuarios/{alvo}/perfil", Status(200, 403, 403, 401, 401)),
        ("POST", "/api/usuarios/{alvo}/desativacao", Status(200, 403, 403, 401, 401)),
        ("GET", "/api/autenticacao/eu", Status(200, 200, 200, 401, 401)),
        ("GET", "/api/motorista/autenticacao/eu", Status(401, 401, 401, 200, 401)),
        ("GET", "/api/motoristas", Status(200, 200, 200, 401, 401)),
        ("POST", "/api/motoristas", Status(201, 201, 403, 401, 401)),
        ("GET", "/api/veiculos", Status(200, 200, 200, 401, 401)),
        ("POST", "/api/veiculos", Status(201, 201, 403, 401, 401)),
        ("GET", "/api/hubs", Status(200, 200, 200, 401, 401)),
        ("POST", "/api/hubs", Status(201, 201, 403, 401, 401)),
        ("GET", "/api/clientes", Status(200, 200, 200, 401, 401)),
        ("POST", "/api/clientes", Status(201, 201, 403, 401, 401)),
        ("GET", "/api/destinatarios", Status(200, 200, 200, 401, 401)),
        ("POST", "/api/destinatarios", Status(201, 201, 403, 401, 401)),
    ];

    public static TheoryData<string, string, string, int> Casos()
    {
        var dados = new TheoryData<string, string, string, int>();
        foreach (var (metodo, rota, esperado) in Matriz)
        {
            foreach (var (principal, status) in esperado)
            {
                dados.Add(principal, metodo, rota, status);
            }
        }

        return dados;
    }

    /// <summary>
    /// Motorista recebe 401 no console, e não 403: o token dele não é credencial para o
    /// console, então a requisição nem chega à autorização.
    /// </summary>
    [Theory]
    [MemberData(nameof(Casos))]
    public async Task CadaPerfilRecebeOStatusDaMatriz(string principal, string metodo, string rota, int esperado)
    {
        var organizacao = await Cenario.CriarOrganizacaoAsync(
            Perfil.Administrador, Perfil.Supervisor, Perfil.Operador, Perfil.Motorista, Perfil.Operador);
        var alvo = organizacao.TodasCom(Perfil.Operador)[1];
        using var cliente = Cliente();

        string? token = principal == Anonimo
            ? null
            : (await EntrarAsync(cliente, organizacao.Com(Enum.Parse<Perfil>(principal)))).TokenDeAcesso;

        object? corpo = (metodo, rota) switch
        {
            ("POST", "/api/usuarios") => new
            {
                nome = "Conta Nova",
                email = $"nova.{Guid.NewGuid():n}@teste.test",
                senha = "senha-da-conta-nova",
                perfil = "Operador",
            },
            ("PUT", _) => new { perfil = "Supervisor" },
            ("POST", "/api/motoristas") => RoteirosDeCadastro.Obter("motorista").Corpo(),
            ("POST", "/api/veiculos") => RoteirosDeCadastro.Obter("veiculo").Corpo(),
            ("POST", "/api/hubs") => RoteirosDeCadastro.Obter("hub").Corpo(),
            ("POST", "/api/clientes") => RoteirosDeCadastro.Obter("cliente").Corpo(),
            ("POST", "/api/destinatarios") => RoteirosDeCadastro.Obter("destinatario").Corpo(),
            _ => null,
        };

        using var resposta = await EnviarAsync(
            cliente, new HttpMethod(metodo), rota.Replace("{alvo}", alvo.Id.ToString(), StringComparison.Ordinal), token, corpo);

        Assert.True(
            (int)resposta.StatusCode == esperado,
            $"{principal} {metodo} {rota}: esperava {esperado}, veio {(int)resposta.StatusCode}.");
    }

    /// <summary>
    /// A matriz testada acima só vale se não houver endpoint fora dela. Este teste lê o
    /// roteamento real e falha quando surgir um endpoint sem entrada declarada aqui — ou
    /// sem política explícita, dependendo só do fallback.
    /// </summary>
    [Fact]
    public void TodoEndpointTemAutorizacaoDeclarada()
    {
        var esperado = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["* /health/live"] = "anonimo",
            ["* /health/ready"] = "anonimo",
            ["POST /api/autenticacao/login"] = "anonimo",
            ["POST /api/autenticacao/renovar"] = "anonimo",
            ["POST /api/autenticacao/sair"] = "anonimo",
            ["GET /api/autenticacao/eu"] = "console",
            ["POST /api/motorista/autenticacao/login"] = "anonimo",
            ["POST /api/motorista/autenticacao/renovar"] = "anonimo",
            ["POST /api/motorista/autenticacao/sair"] = "anonimo",
            ["GET /api/motorista/autenticacao/eu"] = "motorista",
            ["GET /api/organizacao"] = "console",
            ["GET /api/usuarios/"] = "usuarios:leitura",
            ["GET /api/usuarios/{usuarioId:guid}"] = "usuarios:leitura",
            ["POST /api/usuarios/"] = "usuarios:gestao",
            ["PUT /api/usuarios/{usuarioId:guid}/perfil"] = "usuarios:gestao",
            ["POST /api/usuarios/{usuarioId:guid}/desativacao"] = "usuarios:gestao",
            ["GET /api/motoristas/"] = "operacao:leitura",
            ["GET /api/motoristas/{id:guid}"] = "operacao:leitura",
            ["POST /api/motoristas/"] = "operacao:gestao",
            ["PUT /api/motoristas/{id:guid}"] = "operacao:gestao",
            ["POST /api/motoristas/{id:guid}/ativacao"] = "operacao:gestao",
            ["POST /api/motoristas/{id:guid}/inativacao"] = "operacao:gestao",
            ["GET /api/veiculos/"] = "operacao:leitura",
            ["GET /api/veiculos/{id:guid}"] = "operacao:leitura",
            ["POST /api/veiculos/"] = "operacao:gestao",
            ["PUT /api/veiculos/{id:guid}"] = "operacao:gestao",
            ["POST /api/veiculos/{id:guid}/ativacao"] = "operacao:gestao",
            ["POST /api/veiculos/{id:guid}/inativacao"] = "operacao:gestao",
            ["GET /api/hubs/"] = "operacao:leitura",
            ["GET /api/hubs/{id:guid}"] = "operacao:leitura",
            ["POST /api/hubs/"] = "operacao:gestao",
            ["PUT /api/hubs/{id:guid}"] = "operacao:gestao",
            ["POST /api/hubs/{id:guid}/ativacao"] = "operacao:gestao",
            ["POST /api/hubs/{id:guid}/inativacao"] = "operacao:gestao",
            ["GET /api/clientes/"] = "operacao:leitura",
            ["GET /api/clientes/{id:guid}"] = "operacao:leitura",
            ["POST /api/clientes/"] = "operacao:gestao",
            ["PUT /api/clientes/{id:guid}"] = "operacao:gestao",
            ["POST /api/clientes/{id:guid}/ativacao"] = "operacao:gestao",
            ["POST /api/clientes/{id:guid}/inativacao"] = "operacao:gestao",
            ["GET /api/destinatarios/"] = "operacao:leitura",
            ["GET /api/destinatarios/{id:guid}"] = "operacao:leitura",
            ["POST /api/destinatarios/"] = "operacao:gestao",
            ["PUT /api/destinatarios/{id:guid}"] = "operacao:gestao",
            ["POST /api/destinatarios/{id:guid}/ativacao"] = "operacao:gestao",
            ["POST /api/destinatarios/{id:guid}/inativacao"] = "operacao:gestao",
            ["PUT /api/motoristas/{id:guid}/conta"] = "operacao:gestao",
            ["DELETE /api/motoristas/{id:guid}/conta"] = "operacao:gestao",
        };

        var encontrado = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var endpoint in Fabrica.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            var anonimo = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            var politicas = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Select(dado => dado.Policy)
                .Where(politica => politica is not null)
                .Distinct(StringComparer.Ordinal);
            var classificacao = anonimo ? "anonimo" : string.Join(",", politicas);
            var metodos = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["*"];

            foreach (var metodo in metodos)
            {
                encontrado[$"{metodo} {endpoint.RoutePattern.RawText}"] =
                    string.IsNullOrEmpty(classificacao) ? "(só fallback)" : classificacao;
            }
        }

        Assert.True(
            esperado.SequenceEqual(encontrado),
            "Roteamento difere da matriz declarada. Encontrado:" + Environment.NewLine
            + string.Join(Environment.NewLine, encontrado.Select(item => $"{item.Key} => {item.Value}")));
    }

    private static Dictionary<string, int> Status(int administrador, int supervisor, int operador, int motorista, int anonimo) =>
        new(StringComparer.Ordinal)
        {
            [nameof(Perfil.Administrador)] = administrador,
            [nameof(Perfil.Supervisor)] = supervisor,
            [nameof(Perfil.Operador)] = operador,
            [nameof(Perfil.Motorista)] = motorista,
            [Anonimo] = anonimo,
        };
}
