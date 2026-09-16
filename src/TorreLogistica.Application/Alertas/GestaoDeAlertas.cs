using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Application.Cadastros;
using TorreLogistica.Application.Comum;
using TorreLogistica.Application.Previsao;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Alertas;

namespace TorreLogistica.Application.Alertas;

/// <summary>Filtro da lista de alertas.</summary>
public sealed record FiltroDeAlertas(
    int Pagina,
    int TamanhoDaPagina,
    EstadoDoAlerta? Estado,
    TipoDeAlerta? Tipo,
    SeveridadeDoAlerta? Severidade,
    Guid? EntregaId,
    Guid? MotoristaId);

/// <summary>Alerta como devolvido pela API.</summary>
public sealed record AlertaResumo(
    Guid Id,
    TipoDeAlerta Tipo,
    SeveridadeDoAlerta Severidade,
    EstadoDoAlerta Estado,
    string Descricao,
    Guid? EntregaId,
    string? CodigoDaEntrega,
    Guid? MotoristaId,
    string? NomeDoMotorista,
    Guid? RotaId,
    string? CodigoDaRota,
    JsonElement Evidencia,
    JsonElement EvidenciaDeAbertura,
    DateTimeOffset AbertoEm,
    DateTimeOffset UltimaConstatacaoEm,
    bool CondicaoAtiva,
    DateTimeOffset? ResolvidoEm,
    FormaDeResolucao? FormaDeResolucao,
    Guid? ResolvidoPorUsuarioId,
    string? ObservacaoDaResolucao,
    int Reaberturas,
    uint Versao);

/// <summary>Evento do ciclo de vida, como devolvido pela API.</summary>
public sealed record EventoDoAlertaResumo(
    int Sequencia,
    TipoDeEventoDoAlerta Tipo,
    JsonElement Evidencia,
    Guid? UsuarioId,
    string? Observacao,
    DateTimeOffset OcorridoEm);

/// <summary>Alerta com o ciclo de vida.</summary>
public sealed record AlertaDetalhe(AlertaResumo Alerta, IReadOnlyList<EventoDoAlertaResumo> Eventos);

/// <summary>
/// Consulta e tratamento de alertas pelo console.
/// </summary>
/// <remarks>
/// A lista vem do mais severo para o menos severo e, na mesma severidade, do mais antigo para o mais
/// recente: o que espera há mais tempo aparece primeiro. Cada alerta traz a descrição e a evidência — o
/// operador identifica a entrega problemática sem abrir o histórico de GPS.
/// </remarks>
public sealed class GestaoDeAlertas(SuporteDeCadastro suporte)
{
    /// <summary>Lista alertas.</summary>
    public async Task<PaginaDeResultados<AlertaResumo>> ListarAsync(FiltroDeAlertas filtro, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(filtro);

        var consulta = suporte.Contexto.AlertasOperacionais.AsNoTracking();

        if (filtro.Estado is { } estado)
        {
            consulta = consulta.Where(alerta => alerta.Estado == estado);
        }

        if (filtro.Tipo is { } tipo)
        {
            consulta = consulta.Where(alerta => alerta.Tipo == tipo);
        }

        if (filtro.Severidade is { } severidade)
        {
            consulta = consulta.Where(alerta => alerta.Severidade == severidade);
        }

        if (filtro.EntregaId is { } entregaId)
        {
            consulta = consulta.Where(alerta => alerta.EntregaId == entregaId);
        }

        if (filtro.MotoristaId is { } motoristaId)
        {
            consulta = consulta.Where(alerta => alerta.MotoristaId == motoristaId);
        }

        var total = await consulta.CountAsync(cancelamento).ConfigureAwait(false);

        // Severidade é gravada por nome: a ordem vem explícita, não da ordem alfabética.
        var linhas = await Projetar(consulta
                .OrderByDescending(alerta =>
                    alerta.Severidade == SeveridadeDoAlerta.Critica ? 4
                    : alerta.Severidade == SeveridadeDoAlerta.Alta ? 3
                    : alerta.Severidade == SeveridadeDoAlerta.Media ? 2
                    : 1)
                .ThenBy(alerta => alerta.AbertoEm)
                .ThenBy(alerta => alerta.Id)
                .Skip((filtro.Pagina - 1) * filtro.TamanhoDaPagina)
                .Take(filtro.TamanhoDaPagina))
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

        return new PaginaDeResultados<AlertaResumo>([.. linhas.Select(Resumir)], filtro.Pagina, filtro.TamanhoDaPagina, total);
    }

    /// <summary>Alerta com o ciclo de vida.</summary>
    public async Task<AlertaDetalhe> ObterAsync(Guid id, CancellationToken cancelamento)
    {
        var linha = await Projetar(suporte.Contexto.AlertasOperacionais.AsNoTracking().Where(alerta => alerta.Id == id))
            .SingleOrDefaultAsync(cancelamento)
            .ConfigureAwait(false)
            ?? throw AlertaNaoEncontrado();

        var eventos = await suporte.Contexto.EventosDeAlerta
            .AsNoTracking()
            .Where(evento => evento.AlertaId == id)
            .OrderBy(evento => evento.Sequencia)
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

        return new AlertaDetalhe(
            Resumir(linha),
            [.. eventos.Select(evento => new EventoDoAlertaResumo(
                evento.Sequencia, evento.Tipo, Json(evento.Evidencia), evento.UsuarioId, evento.Observacao, evento.OcorridoEm))]);
    }

    /// <summary>Resolução pelo operador. Repetir responde com o alerta já resolvido.</summary>
    public async Task<AlertaDetalhe> ResolverAsync(Guid id, string? observacao, CancellationToken cancelamento)
    {
        var contexto = suporte.Contexto;
        var alerta = await contexto.AlertasOperacionais
            .SingleOrDefaultAsync(item => item.Id == id, cancelamento)
            .ConfigureAwait(false)
            ?? throw AlertaNaoEncontrado();

        if (alerta.ResolverPeloOperador(suporte.UsuarioId, observacao, suporte.NovoIdentificador(), suporte.Agora) is { } evento)
        {
            contexto.EventosDeAlerta.Add(evento);
            await suporte.SalvarAsync(
                    cancelamento,
                    (NomesDeRestricoes.SequenciaDoEventoDoAlerta, () => ExcecaoDeDominio.Conflito(
                        "conflito_de_versao", "O alerta foi alterado ao mesmo tempo. Recarregue e tente de novo.")))
                .ConfigureAwait(false);
        }

        return await ObterAsync(id, cancelamento).ConfigureAwait(false);
    }

    private static ExcecaoDeDominio AlertaNaoEncontrado() =>
        ExcecaoDeDominio.NaoEncontrado("alerta_nao_encontrado", "Alerta não encontrado.");

    private IQueryable<LinhaDeAlerta> Projetar(IQueryable<AlertaOperacional> alertas)
    {
        var contexto = suporte.Contexto;

        return alertas.Select(alerta => new LinhaDeAlerta
        {
            Alerta = alerta,
            CodigoDaEntrega = contexto.Entregas.Where(entrega => entrega.Id == alerta.EntregaId).Select(entrega => entrega.Codigo).FirstOrDefault(),
            NomeDoMotorista = contexto.Motoristas.Where(motorista => motorista.Id == alerta.MotoristaId).Select(motorista => motorista.Nome).FirstOrDefault(),
            CodigoDaRota = contexto.Rotas.Where(rota => rota.Id == alerta.RotaId).Select(rota => rota.Codigo).FirstOrDefault(),
        });
    }

    private static AlertaResumo Resumir(LinhaDeAlerta linha)
    {
        var alerta = linha.Alerta;

        return new AlertaResumo(
            alerta.Id,
            alerta.Tipo,
            alerta.Severidade,
            alerta.Estado,
            DescricaoDoAlerta.Montar(alerta.Tipo, alerta.UltimaEvidencia),
            alerta.EntregaId,
            linha.CodigoDaEntrega,
            alerta.MotoristaId,
            linha.NomeDoMotorista,
            alerta.RotaId,
            linha.CodigoDaRota,
            Json(alerta.UltimaEvidencia),
            Json(alerta.EvidenciaDeAbertura),
            alerta.AbertoEm,
            alerta.UltimaConstatacaoEm,
            alerta.CondicaoAtiva,
            alerta.ResolvidoEm,
            alerta.FormaDeResolucao,
            alerta.ResolvidoPorUsuarioId,
            alerta.ObservacaoDaResolucao,
            alerta.Reaberturas,
            alerta.Versao);
    }

    private static JsonElement Json(string texto)
    {
        using var documento = JsonDocument.Parse(texto);
        return documento.RootElement.Clone();
    }

    private sealed class LinhaDeAlerta
    {
        public required AlertaOperacional Alerta { get; init; }

        public string? CodigoDaEntrega { get; init; }

        public string? NomeDoMotorista { get; init; }

        public string? CodigoDaRota { get; init; }
    }
}

/// <summary>Descrição legível de um alerta, a partir da evidência.</summary>
/// <remarks>Só durações, contagens e limites: fuso é assunto de apresentação, e coordenada não aparece.</remarks>
public static class DescricaoDoAlerta
{
    /// <summary>Monta a descrição.</summary>
    public static string Montar(TipoDeAlerta tipo, string evidencia)
    {
        using var documento = JsonDocument.Parse(evidencia);
        var raiz = documento.RootElement;

        if (raiz.TryGetProperty("foraDaAvaliacaoDaRota", out _))
        {
            return "O alvo deste alerta saiu da avaliação da rota (motorista trocado ou entrega retirada).";
        }

        return tipo switch
        {
            TipoDeAlerta.RiscoDeAtraso => Texto(raiz, "explicacaoDaPrevisao") is { } explicacao
                ? $"Entrega em risco de atraso. {explicacao}"
                : "Entrega em risco de atraso: a previsão deixou de estar ativa.",
            TipoDeAlerta.EntregaAtrasada => Inteiro(raiz, "folgaEmSegundos") is { } folga
                ? $"Entrega atrasada: a janela prometida terminou há {ExplicacaoDaPrevisao.Duracao(TimeSpan.FromSeconds(-folga))} sem a entrega concluída."
                : "Entrega atrasada: a previsão deixou de estar ativa.",
            TipoDeAlerta.MotoristaOffline => raiz.GetProperty("ultimaPosicaoCapturadaEm").ValueKind == JsonValueKind.Null
                ? $"Motorista sem enviar nenhuma posição há {raiz.GetProperty("minutosSemPosicao").GetInt32()} min desde a saída da rota (limite {raiz.GetProperty("limiteEmMinutos").GetInt32()} min), com {raiz.GetProperty("entregasPendentes").GetInt32()} entrega(s) pendente(s)."
                : $"Motorista sem enviar posição há {raiz.GetProperty("minutosSemPosicao").GetInt32()} min (limite {raiz.GetProperty("limiteEmMinutos").GetInt32()} min), com {raiz.GetProperty("entregasPendentes").GetInt32()} entrega(s) pendente(s).",
            TipoDeAlerta.ParadoTempoExcessivo =>
                $"Motorista parado há {raiz.GetProperty("minutosParado").GetInt32()} min num raio de {raiz.GetProperty("raioEmMetros").GetDouble():0} m "
                + $"(limite {raiz.GetProperty("limiteEmMinutos").GetInt32()} min{(raiz.GetProperty("atendendoParada").GetBoolean() ? ", atendendo uma parada" : string.Empty)}).",
            TipoDeAlerta.TentativasExcedidas =>
                $"{raiz.GetProperty("tentativas").GetInt32()} tentativa(s) sem sucesso (limite {raiz.GetProperty("limite").GetInt32()})"
                + (Texto(raiz, "ultimoMotivo") is { } motivo ? $"; último motivo: {motivo}." : "."),
            TipoDeAlerta.OcorrenciaCritica => Texto(raiz, "tipo") is { } tipoDaOcorrencia
                ? $"Ocorrência crítica registrada: {RotuloDaOcorrencia(tipoDaOcorrencia)}"
                    + (Texto(raiz, "motivo") is { } motivoDaTentativa ? $" ({motivoDaTentativa})." : ".")
                    + (raiz.TryGetProperty("comObservacao", out var comObservacao) && comObservacao.ValueKind == JsonValueKind.True
                        ? " Há descrição registrada na ocorrência."
                        : string.Empty)
                : "Ocorrência crítica registrada.",
            _ => tipo.ToString(),
        };
    }

    private static string RotuloDaOcorrencia(string tipo) => tipo switch
    {
        "TentativaDeEntrega" => "tentativa de entrega",
        "ProblemaComVeiculo" => "problema com o veículo",
        "ProblemaComMercadoria" => "problema com a mercadoria",
        "AcidenteOuIncidente" => "acidente ou incidente",
        "DificuldadeDeAcesso" => "dificuldade de acesso",
        _ => "outro",
    };

    private static string? Texto(JsonElement raiz, string nome) =>
        raiz.TryGetProperty(nome, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() : null;

    private static int? Inteiro(JsonElement raiz, string nome) =>
        raiz.TryGetProperty(nome, out var valor) && valor.ValueKind == JsonValueKind.Number ? valor.GetInt32() : null;
}
