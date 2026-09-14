namespace TorreLogistica.Domain.Identidade;

/// <summary>O que fazer com um token de renovação apresentado.</summary>
public enum DecisaoDeRenovacao
{
    /// <summary>Token disponível: trocar por um sucessor.</summary>
    Rotacionar = 1,

    /// <summary>
    /// Token já trocado há poucos segundos e o sucessor ainda intacto: é quase certamente
    /// o mesmo cliente repetindo uma requisição cuja resposta se perdeu. Descarta-se o
    /// sucessor não usado e emite-se outro.
    /// </summary>
    RotacionarNovamenteNaJanelaDeTolerancia = 2,

    /// <summary>Token consumido reapresentado fora das condições de tolerância: revogar a sessão.</summary>
    ReusoDetectado = 3,

    /// <summary>Sessão encerrada, expirada, de outro canal ou token vencido: recusar sem revogar nada.</summary>
    Recusar = 4,
}

/// <summary>
/// Decide o destino de um token de renovação apresentado pelo cliente.
/// </summary>
/// <remarks>
/// <para>
/// Função pura, separada de banco e de HTTP, porque é o ponto do sistema em que um erro
/// ou deixa um invasor manter a sessão, ou derruba um usuário legítimo a cada rede ruim.
/// </para>
/// <para>
/// A janela de tolerância existe porque retry é comportamento esperado — a resposta da
/// renovação pode se perder no caminho, principalmente no celular do motorista. Ela não
/// elimina a detecção de reuso, apenas a adia em um passo: se um invasor aproveitar a
/// janela, o sucessor do usuário legítimo é descartado e a próxima renovação dele é que
/// dispara a revogação da família.
/// </para>
/// </remarks>
public static class PoliticaDeRenovacao
{
    /// <summary>Avalia um token apresentado.</summary>
    /// <param name="apresentado">Token cujo hash o cliente enviou.</param>
    /// <param name="sucessor">Token que substituiu o apresentado, se existir.</param>
    /// <param name="sessao">Sessão do token.</param>
    /// <param name="canalDaRequisicao">Canal pelo qual a renovação chegou.</param>
    /// <param name="agora">Instante atual.</param>
    /// <param name="janelaDeTolerancia">Janela para repetição legítima.</param>
    public static DecisaoDeRenovacao Avaliar(
        TokenDeRenovacao apresentado,
        TokenDeRenovacao? sucessor,
        Sessao sessao,
        CanalDeAcesso canalDaRequisicao,
        DateTimeOffset agora,
        TimeSpan janelaDeTolerancia)
    {
        ArgumentNullException.ThrowIfNull(apresentado);
        ArgumentNullException.ThrowIfNull(sessao);

        if (apresentado.SessaoId != sessao.Id
            || sessao.Canal != canalDaRequisicao
            || !sessao.EstaAtiva(agora))
        {
            return DecisaoDeRenovacao.Recusar;
        }

        // Token descartado sem uso só volta a aparecer nas mãos de quem ficou para trás
        // numa corrida — ou de quem o roubou. As duas situações exigem encerrar a família.
        if (apresentado.InvalidadoEm is not null)
        {
            return DecisaoDeRenovacao.ReusoDetectado;
        }

        if (apresentado.UsadoEm is null)
        {
            return agora < apresentado.ExpiraEm
                ? DecisaoDeRenovacao.Rotacionar
                : DecisaoDeRenovacao.Recusar;
        }

        var dentroDaJanela = agora - apresentado.UsadoEm.Value <= janelaDeTolerancia;
        var sucessorIntacto = sucessor is not null
            && sucessor.Id == apresentado.SubstitutoId
            && sucessor.EstaDisponivel(agora);

        return dentroDaJanela && sucessorIntacto
            ? DecisaoDeRenovacao.RotacionarNovamenteNaJanelaDeTolerancia
            : DecisaoDeRenovacao.ReusoDetectado;
    }
}
