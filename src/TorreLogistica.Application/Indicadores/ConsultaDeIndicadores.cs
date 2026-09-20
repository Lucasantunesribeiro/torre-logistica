using Microsoft.EntityFrameworkCore;
using TorreLogistica.Application.Cadastros;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Entregas;

namespace TorreLogistica.Application.Indicadores;

/// <summary>Período fechado sobre o qual os indicadores são calculados.</summary>
/// <param name="De">Início, inclusivo.</param>
/// <param name="Ate">Fim, exclusivo.</param>
public sealed record PeriodoDoIndicador(DateTimeOffset De, DateTimeOffset Ate)
{
    /// <summary>Maior período aceito. Acima disto a consulta varre a operação inteira.</summary>
    public static readonly TimeSpan DuracaoMaxima = TimeSpan.FromDays(186);

    /// <summary>Valida e normaliza para UTC.</summary>
    public static PeriodoDoIndicador Criar(DateTimeOffset? de, DateTimeOffset? ate, DateTimeOffset agora)
    {
        // Sem período informado, os últimos 30 dias: é a pergunta que o supervisor faz por padrão.
        var fim = (ate ?? agora).ToUniversalTime();
        var inicio = (de ?? fim.AddDays(-30)).ToUniversalTime();

        ExcecaoDeDominio.LancarSe(inicio >= fim, "periodo_invalido", "O início do período precisa ser antes do fim.");
        ExcecaoDeDominio.LancarSe(
            fim - inicio > DuracaoMaxima,
            "periodo_grande_demais",
            $"O período não pode passar de {DuracaoMaxima.TotalDays:0} dias.");

        return new PeriodoDoIndicador(inicio, fim);
    }
}

/// <summary>Um indicador com o número, a base de cálculo e a definição em texto.</summary>
/// <param name="Valor">
/// O número. <see langword="null"/> quando não há base para calcular — e isso é diferente de zero.
/// </param>
/// <param name="Base">Quantos registros entraram na conta.</param>
/// <param name="Definicao">Como o número é calculado, para a tela mostrar junto.</param>
public sealed record Indicador(double? Valor, int Base, string Definicao);

/// <summary>Linha de um recorte, como entregas por motorista.</summary>
public sealed record LinhaDoIndicador(string Rotulo, int Quantidade, double? Valor);

/// <summary>Painel completo de indicadores do período.</summary>
public sealed record IndicadoresDaOperacao(
    DateTimeOffset De,
    DateTimeOffset Ate,
    int EntregasConcluidas,
    int EntregasCanceladas,
    Indicador PontualidadeEmPercentual,
    Indicador SucessoNaPrimeiraTentativaEmPercentual,
    Indicador AtrasoMedioEmMinutos,
    Indicador TempoMedioPorParadaEmMinutos,
    Indicador TempoMedioEmRotaEmMinutos,
    IReadOnlyList<LinhaDoIndicador> EntregasPorMotorista,
    IReadOnlyList<LinhaDoIndicador> OcorrenciasPorMotivo,
    IReadOnlyList<LinhaDoIndicador> PontualidadePorCliente,
    IReadOnlyList<LinhaDoIndicador> EntregasPorRota);

/// <summary>
/// Indicadores operacionais do período, calculados direto no banco.
/// </summary>
/// <remarks>
/// <para>
/// Três escolhas atravessam todos os números aqui:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>O que entra na conta é a entrega concluída no período</b>, pelo instante em que foi entregue — não
/// pela data em que foi criada. Quem pergunta "como foi a semana" quer saber das entregas que aconteceram
/// naquela semana.
/// </description></item>
/// <item><description>
/// <b>Cancelada não entra no denominador.</b> Cancelamento não é falha de pontualidade: contá-lo como
/// atraso puniria a operação por uma decisão do cliente. Ela aparece à parte, como contagem própria.
/// </description></item>
/// <item><description>
/// <b>Sem base, o valor é vazio — nunca zero.</b> "0% de pontualidade" e "não houve entrega" são fatos
/// opostos, e mostrar zero nos dois casos faria o supervisor agir sobre um problema que não existe.
/// </description></item>
/// </list>
/// <para>
/// Tudo é comparado em UTC. O período chega com fuso e é normalizado, então a mesma janela pedida do
/// Brasil ou de Lisboa produz exatamente o mesmo resultado quando representa o mesmo instante.
/// </para>
/// </remarks>
public sealed class ConsultaDeIndicadores(SuporteDeCadastro suporte)
{
    /// <summary>Quantas linhas cada recorte devolve.</summary>
    public const int LimiteDoRecorte = 20;

    private const string DefinicaoDePontualidade =
        "Entregas concluídas dentro da janela prometida, dividido pelas entregas concluídas no período. "
        + "Canceladas não entram.";

    private const string DefinicaoDePrimeiraTentativa =
        "Entregas concluídas sem nenhuma tentativa frustrada, dividido pelas entregas concluídas no período.";

    private const string DefinicaoDeAtraso =
        "Média de minutos entre o fim da janela prometida e a conclusão, contando só as entregas que "
        + "passaram da janela.";

    private const string DefinicaoDeParada =
        "Média de minutos entre a chegada registrada ao destino e a conclusão da entrega.";

    private const string DefinicaoDeRota =
        "Média de minutos entre a saída para rota e a conclusão de cada entrega.";

    /// <summary>Calcula o painel do período.</summary>
    public async Task<IndicadoresDaOperacao> ObterAsync(PeriodoDoIndicador periodo, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(periodo);

        var concluidas = suporte.Contexto.Entregas
            .AsNoTracking()
            .Where(entrega => entrega.EntregueEm != null && entrega.EntregueEm >= periodo.De && entrega.EntregueEm < periodo.Ate);

        // Uma varredura só para o que é contagem simples: o banco devolve tudo numa linha.
        var resumo = await concluidas
            .GroupBy(_ => 1)
            .Select(grupo => new
            {
                Total = grupo.Count(),
                NoPrazo = grupo.Count(entrega => entrega.EntregueEm!.Value <= entrega.Janela.Fim),
                NaPrimeira = grupo.Count(entrega => entrega.TentativasFrustradas == 0),
                AtrasoEmMinutos = grupo
                    .Where(entrega => entrega.EntregueEm!.Value > entrega.Janela.Fim)
                    .Average(entrega => (double?)(entrega.EntregueEm!.Value - entrega.Janela.Fim).TotalMinutes),
                Atrasadas = grupo.Count(entrega => entrega.EntregueEm!.Value > entrega.Janela.Fim),
                ParadaEmMinutos = grupo
                    .Where(entrega => entrega.ChegadaRegistradaEm != null)
                    .Average(entrega => (double?)(entrega.EntregueEm!.Value - entrega.ChegadaRegistradaEm!.Value).TotalMinutes),
                ComChegada = grupo.Count(entrega => entrega.ChegadaRegistradaEm != null),
                RotaEmMinutos = grupo
                    .Where(entrega => entrega.SaiuParaRotaEm != null)
                    .Average(entrega => (double?)(entrega.EntregueEm!.Value - entrega.SaiuParaRotaEm!.Value).TotalMinutes),
                ComSaida = grupo.Count(entrega => entrega.SaiuParaRotaEm != null),
            })
            .SingleOrDefaultAsync(cancelamento)
            .ConfigureAwait(false);

        var canceladas = await suporte.Contexto.Entregas
            .AsNoTracking()
            .CountAsync(
                entrega => entrega.CanceladaEm != null && entrega.CanceladaEm >= periodo.De && entrega.CanceladaEm < periodo.Ate,
                cancelamento)
            .ConfigureAwait(false);

        var total = resumo?.Total ?? 0;

        return new IndicadoresDaOperacao(
            periodo.De,
            periodo.Ate,
            total,
            canceladas,
            Percentual(resumo?.NoPrazo, total, DefinicaoDePontualidade),
            Percentual(resumo?.NaPrimeira, total, DefinicaoDePrimeiraTentativa),
            new Indicador(Arredondar(resumo?.AtrasoEmMinutos), resumo?.Atrasadas ?? 0, DefinicaoDeAtraso),
            new Indicador(Arredondar(resumo?.ParadaEmMinutos), resumo?.ComChegada ?? 0, DefinicaoDeParada),
            new Indicador(Arredondar(resumo?.RotaEmMinutos), resumo?.ComSaida ?? 0, DefinicaoDeRota),
            await PorMotoristaAsync(concluidas, cancelamento).ConfigureAwait(false),
            await PorMotivoAsync(periodo, cancelamento).ConfigureAwait(false),
            await PorClienteAsync(concluidas, cancelamento).ConfigureAwait(false),
            await PorRotaAsync(concluidas, cancelamento).ConfigureAwait(false));
    }

    private static Indicador Percentual(int? parte, int total, string definicao) =>
        new(total == 0 ? null : Arredondar(parte.GetValueOrDefault() * 100d / total), total, definicao);

    private static double? Arredondar(double? valor) => valor is null ? null : Math.Round(valor.Value, 1);

    private async Task<IReadOnlyList<LinhaDoIndicador>> PorMotoristaAsync(
        IQueryable<Entrega> concluidas,
        CancellationToken cancelamento) =>
        await concluidas
            .Where(entrega => entrega.MotoristaId != null)
            .GroupBy(entrega => entrega.MotoristaId!.Value)
            .Select(grupo => new
            {
                MotoristaId = grupo.Key,
                Quantidade = grupo.Count(),
                NoPrazo = grupo.Count(entrega => entrega.EntregueEm!.Value <= entrega.Janela.Fim),
            })
            .OrderByDescending(linha => linha.Quantidade)
            .Take(LimiteDoRecorte)
            .Join(
                suporte.Contexto.Motoristas.AsNoTracking(),
                linha => linha.MotoristaId,
                motorista => motorista.Id,
                (linha, motorista) => new LinhaDoIndicador(
                    motorista.Nome,
                    linha.Quantidade,
                    Math.Round(linha.NoPrazo * 100d / linha.Quantidade, 1)))
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

    private async Task<IReadOnlyList<LinhaDoIndicador>> PorClienteAsync(
        IQueryable<Entrega> concluidas,
        CancellationToken cancelamento) =>
        await concluidas
            .GroupBy(entrega => entrega.ClienteId)
            .Select(grupo => new
            {
                ClienteId = grupo.Key,
                Quantidade = grupo.Count(),
                NoPrazo = grupo.Count(entrega => entrega.EntregueEm!.Value <= entrega.Janela.Fim),
            })
            .OrderByDescending(linha => linha.Quantidade)
            .Take(LimiteDoRecorte)
            .Join(
                suporte.Contexto.Clientes.AsNoTracking(),
                linha => linha.ClienteId,
                cliente => cliente.Id,
                (linha, cliente) => new LinhaDoIndicador(
                    cliente.Nome,
                    linha.Quantidade,
                    Math.Round(linha.NoPrazo * 100d / linha.Quantidade, 1)))
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

    private async Task<IReadOnlyList<LinhaDoIndicador>> PorMotivoAsync(
        PeriodoDoIndicador periodo,
        CancellationToken cancelamento)
    {
        // O motivo vira texto só depois de vir do banco: nome de enum não é expressão que o SQL saiba
        // montar, e forçar a tradução custaria um CASE escrito à mão que sairia do lugar a cada motivo novo.
        var motivos = await suporte.Contexto.Ocorrencias
            .AsNoTracking()
            .Where(ocorrencia => ocorrencia.OcorridaEm >= periodo.De && ocorrencia.OcorridaEm < periodo.Ate)
            .GroupBy(ocorrencia => ocorrencia.Tipo)
            .Select(grupo => new { Motivo = grupo.Key, Quantidade = grupo.Count() })
            .OrderByDescending(linha => linha.Quantidade)
            .Take(LimiteDoRecorte)
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

        return [.. motivos.Select(linha => new LinhaDoIndicador(linha.Motivo.ToString(), linha.Quantidade, null))];
    }

    private async Task<IReadOnlyList<LinhaDoIndicador>> PorRotaAsync(
        IQueryable<Entrega> concluidas,
        CancellationToken cancelamento)
    {
        // A entrega não guarda a rota: quem guarda é a parada, e a entrega pode ter passado por mais de
        // uma — uma tentativa frustrada reagendada sai de novo noutra rota, noutro dia. A rota que conta é
        // a que estava carregando a entrega no instante em que ela foi concluída: adicionada antes disso e
        // ainda não removida. Não serve olhar só a parada ativa, porque concluir a rota desativa todas as
        // paradas dela — inclusive as das entregas que deram certo.
        var porRota = await concluidas
            .Select(entrega => new
            {
                RotaId = suporte.Contexto.Paradas
                    .Where(parada =>
                        parada.EntregaId == entrega.Id
                        && parada.AdicionadaEm <= entrega.EntregueEm!.Value
                        && (parada.RemovidaEm == null || parada.RemovidaEm >= entrega.EntregueEm!.Value))
                    .OrderByDescending(parada => parada.AdicionadaEm)
                    .Select(parada => (Guid?)parada.RotaId)
                    .FirstOrDefault(),
                NoPrazo = entrega.EntregueEm!.Value <= entrega.Janela.Fim,
            })
            .Where(linha => linha.RotaId != null)
            .GroupBy(linha => linha.RotaId!.Value)
            .Select(grupo => new
            {
                RotaId = grupo.Key,
                Quantidade = grupo.Count(),
                NoPrazo = grupo.Count(linha => linha.NoPrazo),
            })
            .OrderByDescending(linha => linha.Quantidade)
            .Take(LimiteDoRecorte)
            .Join(
                suporte.Contexto.Rotas.AsNoTracking(),
                linha => linha.RotaId,
                rota => rota.Id,
                (linha, rota) => new LinhaDoIndicador(
                    rota.Codigo,
                    linha.Quantidade,
                    Math.Round(linha.NoPrazo * 100d / linha.Quantidade, 1)))
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

        return porRota;
    }
}
