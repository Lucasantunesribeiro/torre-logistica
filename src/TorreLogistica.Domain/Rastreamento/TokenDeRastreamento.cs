using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Entregas;

namespace TorreLogistica.Domain.Rastreamento;

/// <summary>
/// Link de acompanhamento entregue ao destinatário, guardado apenas como hash.
/// </summary>
/// <remarks>
/// <para>
/// O valor que vai no link nunca é persistido: a tabela guarda o SHA-256 dele, como nos tokens de
/// renovação de sessão. Vazamento do banco não devolve nenhum link funcionando, e é por isso que
/// um link perdido é <b>reemitido</b>, nunca recuperado.
/// </para>
/// <para>
/// Um link ativo por entrega. Reemitir revoga o anterior no mesmo commit, para que um link repassado
/// adiante deixe de funcionar assim que o operador emitir outro.
/// </para>
/// </remarks>
public sealed class TokenDeRastreamento
{
    private TokenDeRastreamento()
    {
        HashDoToken = [];
    }

    /// <summary>Identificador (UUIDv7).</summary>
    public Guid Id { get; private set; }

    /// <summary>Organização dona da entrega.</summary>
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Entrega acompanhada.</summary>
    public Guid EntregaId { get; private set; }

    /// <summary>SHA-256 do valor que foi para o link.</summary>
    public byte[] HashDoToken { get; private set; }

    /// <summary>Instante de emissão.</summary>
    public DateTimeOffset EmitidoEm { get; private set; }

    /// <summary>Fim da validade.</summary>
    public DateTimeOffset ExpiraEm { get; private set; }

    /// <summary>Quando foi revogado, se foi.</summary>
    public DateTimeOffset? RevogadoEm { get; private set; }

    /// <summary>Quem emitiu.</summary>
    public Guid? AutorUsuarioId { get; private set; }

    /// <summary>Emite um link para a entrega.</summary>
    /// <param name="id">Identificador do registro.</param>
    /// <param name="organizacaoId">Organização dona da entrega.</param>
    /// <param name="entregaId">Entrega acompanhada.</param>
    /// <param name="hashDoToken">SHA-256 do valor entregue ao destinatário.</param>
    /// <param name="autorUsuarioId">Quem emitiu.</param>
    /// <param name="validade">Quanto tempo o link vale.</param>
    /// <param name="agora">Instante da emissão.</param>
    public static TokenDeRastreamento Emitir(
        Guid id,
        Guid organizacaoId,
        Guid entregaId,
        byte[] hashDoToken,
        Guid? autorUsuarioId,
        TimeSpan validade,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(hashDoToken);

        ExcecaoDeDominio.LancarSe(
            hashDoToken.Length != PoliticaDeRastreamentoPublico.TamanhoDoHashEmBytes,
            "hash_do_token_invalido",
            "O hash do token de rastreamento precisa ter 32 bytes.");

        ExcecaoDeDominio.LancarSe(
            validade <= TimeSpan.Zero,
            "validade_invalida",
            "A validade do link de rastreamento precisa ser positiva.");

        return new TokenDeRastreamento
        {
            Id = id,
            OrganizacaoId = organizacaoId,
            EntregaId = entregaId,
            HashDoToken = hashDoToken,
            EmitidoEm = agora,
            ExpiraEm = agora + validade,
            AutorUsuarioId = autorUsuarioId,
        };
    }

    /// <summary>Revoga o link. Revogar duas vezes não muda a primeira data.</summary>
    public void Revogar(DateTimeOffset agora) => RevogadoEm ??= agora;

    /// <summary>Indica se o link ainda abre a página.</summary>
    public bool EstaValido(DateTimeOffset agora) => RevogadoEm is null && agora < ExpiraEm;
}

/// <summary>
/// O que a página pública pode mostrar, e com que grossura.
/// </summary>
/// <remarks>
/// <para>
/// A página é para o destinatário, não para a operação. Ela não mostra o motorista, o veículo, a
/// rota, outras entregas, nem qualquer identificador interno — e a localização aparece de forma
/// deliberadamente grossa, pelas três defesas abaixo, combinadas:
/// </para>
/// <list type="number">
/// <item><description>só enquanto a entrega está a caminho — antes e depois disso não há posição nenhuma;</description></item>
/// <item><description>arredondada numa grade de <see cref="GradeEmGraus"/>, que apaga a rua e preserva a região;</description></item>
/// <item><description>descartada quando a captura envelhece mais que <see cref="IdadeMaximaDaPosicao"/>, para
/// não deixar o destinatário olhando para um ponto que já não diz nada.</description></item>
/// </list>
/// <para>
/// Rastrear o motorista minuto a minuto expõe uma pessoa, não uma encomenda: a posição exata dele é
/// dado do console, com autenticação e perfil. Ver <c>docs/adr/0024-rastreamento-publico.md</c>.
/// </para>
/// </remarks>
public static class PoliticaDeRastreamentoPublico
{
    /// <summary>Tamanho do SHA-256 guardado.</summary>
    public const int TamanhoDoHashEmBytes = 32;

    /// <summary>Validade padrão do link.</summary>
    public static readonly TimeSpan ValidadePadrao = TimeSpan.FromDays(30);

    /// <summary>
    /// Lado da grade de arredondamento da posição pública, em graus. 0,01° é aproximadamente 1,1 km
    /// no eixo norte-sul — o suficiente para dizer "está chegando no seu bairro" e insuficiente para
    /// dizer em que rua o motorista está.
    /// </summary>
    public const double GradeEmGraus = 0.01;

    /// <summary>
    /// Lado aproximado da célula da grade, em metros, para a página declarar a grossura do que mostra.
    /// </summary>
    /// <remarks>
    /// Um grau de latitude vale cerca de 111 km em qualquer lugar do planeta; em longitude, encolhe com o
    /// cosseno da latitude. O valor aqui é o do eixo norte-sul — o maior dos dois — porque anunciar uma
    /// precisão melhor do que a real seria a única versão perigosa deste arredondamento.
    /// </remarks>
    public const int LadoDaCelulaEmMetros = 1_100;

    /// <summary>Posição capturada há mais que isto não é mostrada.</summary>
    public static readonly TimeSpan IdadeMaximaDaPosicao = TimeSpan.FromMinutes(15);

    /// <summary>Indica se o status permite mostrar alguma posição.</summary>
    public static bool MostraPosicao(StatusDaEntrega status) =>
        status is StatusDaEntrega.EmRota or StatusDaEntrega.ProximaDoDestino;

    /// <summary>Arredonda uma coordenada para o centro da célula da grade.</summary>
    public static double Arredondar(double grau) => Math.Round(grau / GradeEmGraus) * GradeEmGraus;

    /// <summary>
    /// Indica se o evento da timeline interna aparece para o destinatário.
    /// </summary>
    /// <remarks>
    /// Planejamento, atribuição, troca de motorista e alteração de cadastro são decisões internas da
    /// operação: contá-las ao destinatário revelaria como a empresa se organiza, sem lhe dizer nada
    /// sobre a encomenda.
    /// </remarks>
    public static bool EhMarcoPublico(TipoDeEventoDaEntrega tipo) => tipo is
        TipoDeEventoDaEntrega.Criada or
        TipoDeEventoDaEntrega.SaiuParaRota or
        TipoDeEventoDaEntrega.ProximidadeDetectada or
        TipoDeEventoDaEntrega.ChegadaRegistrada or
        TipoDeEventoDaEntrega.Entregue or
        TipoDeEventoDaEntrega.TentativaFrustrada or
        TipoDeEventoDaEntrega.Reagendada or
        TipoDeEventoDaEntrega.Cancelada;
}
