using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Comum;
using TorreLogistica.Domain.Entregas;

namespace TorreLogistica.Domain.Ocorrencias;

/// <summary>O que aconteceu na última milha (CLAUDE.md, seção 8).</summary>
public enum TipoDeOcorrencia
{
    /// <summary>Tentativa de entrega sem sucesso, com o motivo tipado da tentativa.</summary>
    TentativaDeEntrega = 1,

    /// <summary>Veículo quebrado, sem combustível, pneu, guincho.</summary>
    ProblemaComVeiculo = 2,

    /// <summary>Mercadoria avariada, faltando, trocada ou violada.</summary>
    ProblemaComMercadoria = 3,

    /// <summary>Acidente, roubo, furto ou qualquer incidente de segurança.</summary>
    AcidenteOuIncidente = 4,

    /// <summary>Acesso impedido, rua interditada, área de risco.</summary>
    DificuldadeDeAcesso = 5,

    /// <summary>Outro — exige descrição.</summary>
    Outro = 6,
}

/// <summary>Severidade da ocorrência, da menor para a maior.</summary>
public enum SeveridadeDaOcorrencia
{
    /// <summary>Informativa.</summary>
    Baixa = 1,

    /// <summary>Pede acompanhamento.</summary>
    Media = 2,

    /// <summary>Pede ação.</summary>
    Alta = 3,

    /// <summary>Pede ação imediata: vira alerta crítico na torre.</summary>
    Critica = 4,
}

/// <summary>Quem registrou.</summary>
public enum OrigemDaOcorrencia
{
    /// <summary>O motorista, pelo aplicativo.</summary>
    Motorista = 1,

    /// <summary>A operação, pelo console.</summary>
    Operacao = 2,
}

/// <summary>Catálogo dos tipos: severidade sugerida e o que cada um exige.</summary>
public static class CatalogoDeOcorrencias
{
    /// <summary>
    /// Severidade que o tipo tem quando ninguém escolhe outra. Mercadoria e incidente nascem críticos: são os
    /// dois casos em que a torre precisa agir agora, não depois da rota.
    /// </summary>
    public static SeveridadeDaOcorrencia SeveridadePadrao(TipoDeOcorrencia tipo, MotivoDeTentativaFrustrada? motivo) => tipo switch
    {
        TipoDeOcorrencia.TentativaDeEntrega => motivo switch
        {
            MotivoDeTentativaFrustrada.ProblemaComMercadoria => SeveridadeDaOcorrencia.Critica,
            MotivoDeTentativaFrustrada.ProblemaComVeiculo => SeveridadeDaOcorrencia.Alta,
            _ => SeveridadeDaOcorrencia.Media,
        },
        TipoDeOcorrencia.ProblemaComMercadoria or TipoDeOcorrencia.AcidenteOuIncidente => SeveridadeDaOcorrencia.Critica,
        TipoDeOcorrencia.ProblemaComVeiculo => SeveridadeDaOcorrencia.Alta,
        TipoDeOcorrencia.DificuldadeDeAcesso => SeveridadeDaOcorrencia.Media,
        TipoDeOcorrencia.Outro => SeveridadeDaOcorrencia.Baixa,
        _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "Tipo de ocorrência desconhecido."),
    };

    /// <summary>O tipo é a tentativa de entrega, e por isso carrega o motivo tipado dela.</summary>
    public static bool ExigeMotivoDaTentativa(TipoDeOcorrencia tipo) => tipo == TipoDeOcorrencia.TentativaDeEntrega;

    /// <summary>
    /// A descrição é obrigatória: só quando o vocabulário fechado não diz o que houve — "Outro" no tipo ou no
    /// motivo. Fora daí ela é complemento, nunca a única estrutura (roadmap, Fase 13).
    /// </summary>
    public static bool ExigeObservacao(TipoDeOcorrencia tipo, MotivoDeTentativaFrustrada? motivo) =>
        tipo == TipoDeOcorrencia.Outro || motivo == MotivoDeTentativaFrustrada.Outro;
}

/// <summary>
/// Uma ocorrência da operação: o que aconteceu, quando, onde, com que gravidade e por quem foi registrado.
/// </summary>
/// <remarks>
/// <para>
/// Somente-inserção, como a timeline da entrega: ocorrência é fato, e fato não se edita. Corrigir é registrar
/// outra — e as duas ficam.
/// </para>
/// <para>
/// O tipo e o motivo são vocabulário fechado; a observação existe para o que o vocabulário não cobre, e só é
/// exigida quando o próprio vocabulário diz "Outro". A localização é a do aparelho no momento, quando o
/// motorista permitiu; a operação registra sem ela.
/// </para>
/// </remarks>
public sealed class Ocorrencia
{
    /// <summary>Tamanho máximo da observação.</summary>
    public const int TamanhoMaximoDaObservacao = 500;

    /// <summary>Quanto o relógio de quem registra pode estar adiantado.</summary>
    public static readonly TimeSpan ToleranciaDeRelogio = TimeSpan.FromMinutes(2);

    /// <summary>Idade máxima de uma ocorrência registrada depois do fato (operação offline, Fase 12).</summary>
    public static readonly TimeSpan IdadeMaxima = TimeSpan.FromDays(7);

    private Ocorrencia()
    {
    }

    /// <summary>Identificador (UUIDv7).</summary>
    public Guid Id { get; private set; }

    /// <summary>Organização.</summary>
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Entrega a que a ocorrência se refere.</summary>
    public Guid EntregaId { get; private set; }

    /// <summary>Rota em que aconteceu, quando havia uma.</summary>
    public Guid? RotaId { get; private set; }

    /// <summary>Motorista envolvido, quando havia um.</summary>
    public Guid? MotoristaId { get; private set; }

    /// <summary>Tipo.</summary>
    public TipoDeOcorrencia Tipo { get; private set; }

    /// <summary>Severidade.</summary>
    public SeveridadeDaOcorrencia Severidade { get; private set; }

    /// <summary>Motivo tipado, nas ocorrências de tentativa de entrega.</summary>
    public MotivoDeTentativaFrustrada? MotivoDaTentativa { get; private set; }

    /// <summary>Descrição livre, complementar ao vocabulário fechado.</summary>
    public string? Observacao { get; private set; }

    /// <summary>Onde aconteceu, quando o aparelho informou.</summary>
    public CoordenadaGeografica? Localizacao { get; private set; }

    /// <summary>Quando aconteceu, segundo quem registrou.</summary>
    public DateTimeOffset OcorridaEm { get; private set; }

    /// <summary>Quando o servidor registrou.</summary>
    public DateTimeOffset RegistradaEm { get; private set; }

    /// <summary>Motorista ou operação.</summary>
    public OrigemDaOcorrencia Origem { get; private set; }

    /// <summary>Conta que registrou.</summary>
    public Guid AutorUsuarioId { get; private set; }

    /// <summary>Registra a ocorrência.</summary>
    /// <param name="id">Identificador (UUIDv7).</param>
    /// <param name="organizacaoId">Organização.</param>
    /// <param name="entregaId">Entrega.</param>
    /// <param name="rotaId">Rota, quando houver.</param>
    /// <param name="motoristaId">Motorista, quando houver.</param>
    /// <param name="tipo">Tipo.</param>
    /// <param name="severidade">Severidade; sem ela, vale a padrão do tipo.</param>
    /// <param name="motivo">Motivo tipado, na tentativa de entrega.</param>
    /// <param name="observacao">Descrição complementar.</param>
    /// <param name="localizacao">Onde aconteceu.</param>
    /// <param name="ocorridaEm">Quando aconteceu; sem ela, vale o instante do registro.</param>
    /// <param name="origem">Motorista ou operação.</param>
    /// <param name="autorUsuarioId">Conta que registrou.</param>
    /// <param name="agora">Instante do registro.</param>
    public static Ocorrencia Registrar(
        Guid id,
        Guid organizacaoId,
        Guid entregaId,
        Guid? rotaId,
        Guid? motoristaId,
        TipoDeOcorrencia tipo,
        SeveridadeDaOcorrencia? severidade,
        MotivoDeTentativaFrustrada? motivo,
        string? observacao,
        CoordenadaGeografica? localizacao,
        DateTimeOffset? ocorridaEm,
        OrigemDaOcorrencia origem,
        Guid autorUsuarioId,
        DateTimeOffset agora)
    {
        ExcecaoDeDominio.LancarSe(
            id == Guid.Empty || organizacaoId == Guid.Empty || entregaId == Guid.Empty || autorUsuarioId == Guid.Empty,
            "identificador_invalido",
            "Identificador inválido.");
        ExcecaoDeDominio.LancarSe(!Enum.IsDefined(tipo), "tipo_de_ocorrencia_invalido", "Tipo de ocorrência desconhecido.");
        ExcecaoDeDominio.LancarSe(!Enum.IsDefined(origem), "origem_invalida", "Origem de ocorrência desconhecida.");
        ExcecaoDeDominio.LancarSe(
            severidade is { } informada && !Enum.IsDefined(informada),
            "severidade_invalida",
            "Severidade de ocorrência desconhecida.");

        if (CatalogoDeOcorrencias.ExigeMotivoDaTentativa(tipo))
        {
            ExcecaoDeDominio.LancarSe(
                motivo is null || !Enum.IsDefined(motivo.Value),
                "motivo_obrigatorio",
                "A ocorrência de tentativa de entrega precisa do motivo tipado.");
        }
        else
        {
            ExcecaoDeDominio.LancarSe(motivo is not null, "motivo_nao_se_aplica", "Só a tentativa de entrega tem motivo tipado.");
        }

        var texto = TextoNormalizado.Opcional(observacao, TamanhoMaximoDaObservacao, "observacao_invalida", "A observação");
        ExcecaoDeDominio.LancarSe(
            CatalogoDeOcorrencias.ExigeObservacao(tipo, motivo) && texto is null,
            "observacao_obrigatoria",
            "Descreva o que aconteceu quando o tipo ou o motivo for \"Outro\".");

        var registrada = agora.ToUniversalTime();
        var instante = (ocorridaEm ?? registrada).ToUniversalTime();
        ExcecaoDeDominio.LancarSe(
            instante > registrada + ToleranciaDeRelogio,
            "ocorrencia_no_futuro",
            "A ocorrência diz ter acontecido no futuro. Confira o relógio do aparelho.");
        ExcecaoDeDominio.LancarSe(
            instante < registrada - IdadeMaxima,
            "ocorrencia_antiga",
            "A ocorrência aconteceu há mais de 7 dias e não pode mais ser registrada.");

        return new Ocorrencia
        {
            Id = id,
            OrganizacaoId = organizacaoId,
            EntregaId = entregaId,
            RotaId = rotaId,
            MotoristaId = motoristaId,
            Tipo = tipo,
            Severidade = severidade ?? CatalogoDeOcorrencias.SeveridadePadrao(tipo, motivo),
            MotivoDaTentativa = motivo,
            Observacao = texto,
            Localizacao = localizacao,
            OcorridaEm = instante,
            RegistradaEm = registrada,
            Origem = origem,
            AutorUsuarioId = autorUsuarioId,
        };
    }

    /// <summary>A ocorrência é crítica e, por isso, abre alerta na torre.</summary>
    public bool EhCritica => Severidade == SeveridadeDaOcorrencia.Critica;
}
