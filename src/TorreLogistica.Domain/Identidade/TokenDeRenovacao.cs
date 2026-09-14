using TorreLogistica.Domain.Abstracoes.Erros;

namespace TorreLogistica.Domain.Identidade;

/// <summary>
/// Um elo da cadeia de renovação de uma sessão.
/// </summary>
/// <remarks>
/// Só o hash SHA-256 do token é guardado. Quem ler o banco não consegue se passar pelo
/// usuário, porque o valor que vai no cookie não está em lugar nenhum do servidor.
/// </remarks>
public sealed class TokenDeRenovacao
{
    private TokenDeRenovacao()
    {
        HashDoToken = [];
    }

    /// <summary>Identificador (UUIDv7).</summary>
    public Guid Id { get; private set; }

    /// <summary>Sessão a que o token pertence.</summary>
    public Guid SessaoId { get; private set; }

    /// <summary>Organização da sessão.</summary>
    public Guid OrganizacaoId { get; private set; }

    /// <summary>SHA-256 do valor entregue ao cliente.</summary>
    public byte[] HashDoToken { get; private set; }

    /// <summary>Instante de emissão.</summary>
    public DateTimeOffset EmitidoEm { get; private set; }

    /// <summary>Validade do token.</summary>
    public DateTimeOffset ExpiraEm { get; private set; }

    /// <summary>Quando foi trocado por um sucessor, se foi.</summary>
    public DateTimeOffset? UsadoEm { get; private set; }

    /// <summary>
    /// Quando foi descartado sem nunca ter sido usado — acontece quando o antecessor é
    /// reapresentado dentro da janela de tolerância e um novo sucessor toma o lugar deste.
    /// </summary>
    public DateTimeOffset? InvalidadoEm { get; private set; }

    /// <summary>Token que substituiu este.</summary>
    public Guid? SubstitutoId { get; private set; }

    /// <summary>Emite um token para a sessão.</summary>
    public static TokenDeRenovacao Emitir(
        Guid id,
        Sessao sessao,
        byte[] hashDoToken,
        DateTimeOffset agora,
        TimeSpan validade)
    {
        ArgumentNullException.ThrowIfNull(sessao);
        ArgumentNullException.ThrowIfNull(hashDoToken);
        ExcecaoDeDominio.LancarSe(id == Guid.Empty, "identificador_invalido", "Identificador inválido.");
        ExcecaoDeDominio.LancarSe(hashDoToken.Length != 32, "hash_invalido", "Hash de token inválido.");

        var instante = agora.ToUniversalTime();
        var expiracao = instante + validade;

        return new TokenDeRenovacao
        {
            Id = id,
            SessaoId = sessao.Id,
            OrganizacaoId = sessao.OrganizacaoId,
            HashDoToken = hashDoToken,
            EmitidoEm = instante,
            // O token nunca sobrevive à sessão que o originou.
            ExpiraEm = expiracao < sessao.ExpiraEm ? expiracao : sessao.ExpiraEm,
        };
    }

    /// <summary>Token ainda não usado, não descartado e dentro da validade.</summary>
    public bool EstaDisponivel(DateTimeOffset agora) =>
        UsadoEm is null && InvalidadoEm is null && agora < ExpiraEm;

    /// <summary>Marca o token como trocado por um sucessor.</summary>
    public void MarcarComoUsado(Guid substitutoId, DateTimeOffset agora)
    {
        UsadoEm ??= agora.ToUniversalTime();
        SubstitutoId = substitutoId;
    }

    /// <summary>Descarta o token sem uso.</summary>
    public void Invalidar(DateTimeOffset agora) => InvalidadoEm ??= agora.ToUniversalTime();
}
