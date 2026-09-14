using TorreLogistica.Domain.Identidade;

namespace TorreLogistica.Application.Abstracoes.Seguranca;

/// <summary>Token de acesso emitido e sua validade.</summary>
/// <param name="Token">Valor a enviar no cabeçalho <c>Authorization</c>.</param>
/// <param name="ExpiraEm">Fim da validade.</param>
public sealed record TokenDeAcessoEmitido(string Token, DateTimeOffset ExpiraEm);

/// <summary>Emite o token de acesso de curta duração de uma sessão.</summary>
/// <remarks>
/// O formato do token é decisão da borda HTTP; a aplicação só precisa pedir um. Por isso
/// a implementação vive na API, junto do esquema de autenticação que o valida.
/// </remarks>
public interface IEmissorDeTokenDeAcesso
{
    /// <summary>Emite um token para a conta e a sessão informadas.</summary>
    TokenDeAcessoEmitido Emitir(Usuario usuario, Sessao sessao);
}
