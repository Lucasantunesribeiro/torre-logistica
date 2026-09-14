using System.ComponentModel.DataAnnotations;
using TorreLogistica.Api.Autenticacao;
using TorreLogistica.Application.Comum;
using TorreLogistica.Application.Organizacoes;
using TorreLogistica.Application.Usuarios;
using TorreLogistica.Domain.Identidade;

namespace TorreLogistica.Api.Usuarios;

/// <summary>Corpo da criação de conta. Não há campo de organização, e campo extra é recusado.</summary>
public sealed record RequisicaoDeCriacaoDeUsuario(
    [property: Required(ErrorMessage = "Informe o nome.")]
    [property: StringLength(Usuario.TamanhoMaximoDoNome, MinimumLength = 1)]
    string? Nome,
    [property: Required(ErrorMessage = "Informe o e-mail.")]
    [property: StringLength(EnderecoDeEmail.TamanhoMaximo)]
    string? Email,
    [property: Required(ErrorMessage = "Informe a senha.")]
    [property: StringLength(PoliticaDeSenha.TamanhoMaximo, MinimumLength = PoliticaDeSenha.TamanhoMinimo,
        ErrorMessage = "A senha deve ter entre 12 e 128 caracteres.")]
    string? Senha,
    [property: Required(ErrorMessage = "Informe o perfil.")]
    Perfil? Perfil);

/// <summary>Corpo da alteração de perfil.</summary>
public sealed record RequisicaoDeAlteracaoDePerfil(
    [property: Required(ErrorMessage = "Informe o perfil.")]
    Perfil? Perfil);

/// <summary>Endpoints de gestão de contas e da organização atual.</summary>
/// <remarks>
/// Nenhuma rota recebe identificador de organização. O tenant é o da sessão, e uma conta
/// de outra organização responde exatamente como um identificador inexistente: 404.
/// </remarks>
public static class EndpointsDeUsuarios
{
    /// <summary>Registra os endpoints.</summary>
    public static IEndpointRouteBuilder MapearEndpointsDeUsuarios(this IEndpointRouteBuilder rotas)
    {
        ArgumentNullException.ThrowIfNull(rotas);

        rotas.MapGet("/api/organizacao", (ConsultaDeOrganizacao consulta, CancellationToken cancelamento) =>
                consulta.ObterAtualAsync(cancelamento))
            .WithTags("Organização")
            .RequireAuthorization(Politicas.Console);

        var grupo = rotas.MapGroup("/api/usuarios").WithTags("Usuários");

        grupo.MapGet("/", async (
                int? pagina,
                int? tamanhoDaPagina,
                GestaoDeUsuarios gestao,
                CancellationToken cancelamento) =>
            {
                var numero = pagina ?? 1;
                var tamanho = tamanhoDaPagina ?? LimitesDePaginacao.TamanhoPadrao;

                if (numero < 1 || tamanho is < 1 or > LimitesDePaginacao.TamanhoMaximo)
                {
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["paginacao"] = [$"A página começa em 1 e o tamanho vai de 1 a {LimitesDePaginacao.TamanhoMaximo}."],
                        },
                        title: "Requisição inválida");
                }

                return Results.Ok(await gestao.ListarAsync(numero, tamanho, cancelamento).ConfigureAwait(false));
            })
            .RequireAuthorization(Politicas.LeituraDeUsuarios);

        grupo.MapGet("/{usuarioId:guid}", (Guid usuarioId, GestaoDeUsuarios gestao, CancellationToken cancelamento) =>
                gestao.ObterAsync(usuarioId, cancelamento))
            .RequireAuthorization(Politicas.LeituraDeUsuarios);

        grupo.MapPost("/", async (
                RequisicaoDeCriacaoDeUsuario requisicao,
                GestaoDeUsuarios gestao,
                CancellationToken cancelamento) =>
            {
                var criado = await gestao
                    .CriarAsync(
                        new ComandoDeCriacaoDeUsuario(requisicao.Nome, requisicao.Email, requisicao.Senha, requisicao.Perfil!.Value),
                        cancelamento)
                    .ConfigureAwait(false);

                return Results.Created($"/api/usuarios/{criado.Id}", criado);
            })
            .RequireAuthorization(Politicas.GestaoDeUsuarios)
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDeCriacaoDeUsuario>>();

        grupo.MapPut("/{usuarioId:guid}/perfil", (
                Guid usuarioId,
                RequisicaoDeAlteracaoDePerfil requisicao,
                GestaoDeUsuarios gestao,
                CancellationToken cancelamento) =>
                gestao.AlterarPerfilAsync(usuarioId, requisicao.Perfil!.Value, cancelamento))
            .RequireAuthorization(Politicas.GestaoDeUsuarios)
            .AddEndpointFilter<FiltroDeValidacao<RequisicaoDeAlteracaoDePerfil>>();

        grupo.MapPost("/{usuarioId:guid}/desativacao", (Guid usuarioId, GestaoDeUsuarios gestao, CancellationToken cancelamento) =>
                gestao.DesativarAsync(usuarioId, cancelamento))
            .RequireAuthorization(Politicas.GestaoDeUsuarios);

        return rotas;
    }
}
