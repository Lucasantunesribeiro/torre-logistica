using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TorreLogistica.Application.Abstracoes.Armazenamento;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Application.Cadastros;
using TorreLogistica.Application.Identidade;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Comprovantes;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Previsao;
using TorreLogistica.Domain.Rastreamento;

namespace TorreLogistica.Application.Rastreamento;

/// <summary>Configuração do rastreamento público.</summary>
public sealed class OpcoesDeRastreamentoPublico
{
    /// <summary>Seção correspondente na configuração.</summary>
    public const string Secao = "Torre:RastreamentoPublico";

    /// <summary>Dias de validade do link emitido.</summary>
    [Range(1, 365)]
    public int ValidadeEmDias { get; set; } = 30;

    /// <summary>
    /// Mostrar a prova de entrega ao destinatário depois da conclusão.
    /// </summary>
    /// <remarks>
    /// Ligado por padrão: o comprovante é a prova da entrega <b>dele</b>, e sai sempre por URL assinada de
    /// curta duração. Operações que fotografam o interior da casa ou coletam assinatura manuscrita podem
    /// preferir desligar.
    /// </remarks>
    public bool ExporComprovante { get; set; } = true;
}

/// <summary>Link emitido, devolvido uma única vez a quem o emitiu.</summary>
/// <param name="Token">Valor que vai no link. Não é recuperável depois.</param>
/// <param name="ExpiraEm">Fim da validade.</param>
public sealed record LinkDeRastreamento(string Token, DateTimeOffset ExpiraEm);

/// <summary>Destino como o destinatário o vê: região, sem a rua.</summary>
public sealed record DestinoPublico(string Bairro, string Cidade, string Uf);

/// <summary>Marco da entrega visível ao destinatário.</summary>
public sealed record MarcoPublico(TipoDeEventoDaEntrega Tipo, DateTimeOffset OcorridoEm);

/// <summary>Posição aproximada do veículo, quando a política permite mostrá-la.</summary>
/// <param name="Latitude">Latitude arredondada na grade da política.</param>
/// <param name="Longitude">Longitude arredondada na grade da política.</param>
/// <param name="PrecisaoAproximadaEmMetros">Lado da célula da grade, para a página não fingir exatidão.</param>
/// <param name="AtualizadaEm">Captura da posição que originou a célula.</param>
public sealed record PosicaoAproximada(
    double Latitude,
    double Longitude,
    int PrecisaoAproximadaEmMetros,
    DateTimeOffset AtualizadaEm);

/// <summary>Arquivo da prova, por URL assinada de curta duração.</summary>
public sealed record ArquivoPublico(TipoDeArquivoDoComprovante Tipo, Uri Url, DateTimeOffset UrlExpiraEm);

/// <summary>Prova da entrega, depois da conclusão.</summary>
public sealed record ComprovantePublico(
    string RecebidoPor,
    DateTimeOffset RegistradoEm,
    IReadOnlyList<ArquivoPublico> Arquivos);

/// <summary>O que a página pública mostra.</summary>
public sealed record AcompanhamentoPublico(
    string Codigo,
    StatusDaEntrega Status,
    DateTimeOffset JanelaDe,
    DateTimeOffset JanelaAte,
    DestinoPublico Destino,
    DateTimeOffset? ChegadaPrevistaEm,
    SituacaoDoSla? Situacao,
    PosicaoAproximada? Posicao,
    ComprovantePublico? Comprovante,
    IReadOnlyList<MarcoPublico> Marcos,
    DateTimeOffset ConsultadoEm);

/// <summary>
/// Acompanhamento da entrega pelo destinatário, por link com token forte.
/// </summary>
/// <remarks>
/// <para>
/// O token só existe no link. O banco guarda o SHA-256 dele, como nos tokens de renovação: é por esse hash
/// que um token apresentado vira uma entrega. Nada é consultado antes desse acerto — token malformado,
/// desconhecido, expirado e revogado terminam todos no mesmo erro, com a mesma mensagem, para que a resposta
/// não conte se aquele link já existiu.
/// </para>
/// <para>
/// A consulta pública não tem sessão, então o filtro de tenant não tem o que filtrar (ADR 0010, falha
/// fechado). Aqui ele é ignorado de propósito e substituído por comparação explícita com a organização
/// <b>do token</b> em cada consulta — a autoridade é o token apresentado, nunca algo vindo da requisição.
/// </para>
/// </remarks>
public sealed class GestaoDoRastreamentoPublico(
    SuporteDeCadastro suporte,
    IObjectStorage storage,
    IOptions<OpcoesDeRastreamentoPublico> opcoes)
{
    private const string Recurso = "rastreamento";

    /// <summary>Política configurada.</summary>
    public OpcoesDeRastreamentoPublico Politica => opcoes.Value;

    /// <summary>
    /// Emite o link de uma entrega, revogando o anterior. O valor devolvido não é recuperável depois.
    /// </summary>
    public async Task<LinkDeRastreamento> EmitirAsync(Guid entregaId, CancellationToken cancelamento)
    {
        return await suporte.Contexto.ExecutarEmTransacaoAsync(async token =>
        {
            var entrega = await suporte.Contexto.Entregas
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == entregaId, token)
                .ConfigureAwait(false)
                ?? throw ExcecaoDeDominio.NaoEncontrado("entrega_nao_encontrada", "Entrega não encontrada.");

            var agora = suporte.Agora;

            var ativos = await suporte.Contexto.TokensDeRastreamento
                .Where(item => item.EntregaId == entregaId && item.RevogadoEm == null)
                .ToListAsync(token)
                .ConfigureAwait(false);

            foreach (var ativo in ativos)
            {
                ativo.Revogar(agora);
            }

            // Duas gravações, uma transação: o índice único parcial só aceita um link ativo por entrega, e
            // o EF não garante que o UPDATE da revogação saia antes do INSERT do novo.
            if (ativos.Count > 0)
            {
                await suporte.Contexto.SaveChangesAsync(token).ConfigureAwait(false);
            }

            var (cru, hash) = SegredosDeRenovacao.Gerar();

            var novo = TokenDeRastreamento.Emitir(
                suporte.NovoIdentificador(),
                suporte.OrganizacaoId,
                entrega.Id,
                hash,
                suporte.UsuarioId,
                TimeSpan.FromDays(Politica.ValidadeEmDias),
                agora);

            suporte.Contexto.TokensDeRastreamento.Add(novo);

            // O que é auditado é o ato de emitir, nunca o valor emitido: auditoria que guardasse o token
            // desfaria o motivo de só persistirmos o hash.
            suporte.Auditar(Recurso, "emitido", entrega.Id, new
            {
                entrega.Codigo,
                TokenId = novo.Id,
                novo.ExpiraEm,
                RevogouAnteriores = ativos.Count,
            });

            await suporte.SalvarAsync(
                token,
                (NomesDeRestricoes.TokenDeRastreamentoAtivoPorEntrega,
                    () => ExcecaoDeDominio.Conflito("link_em_emissao", "Outro link para esta entrega está sendo emitido agora. Tente de novo.")),
                (NomesDeRestricoes.HashDoTokenDeRastreamento,
                    () => ExcecaoDeDominio.Conflito("link_em_emissao", "Não foi possível emitir o link. Tente de novo.")))
                .ConfigureAwait(false);

            return new LinkDeRastreamento(cru, novo.ExpiraEm);
        }, cancelamento).ConfigureAwait(false);
    }

    /// <summary>Acompanhamento correspondente ao token apresentado.</summary>
    /// <exception cref="ExcecaoDeDominio">
    /// Sempre o mesmo <c>rastreamento_nao_encontrado</c> quando o token não abre nada, seja qual for o motivo.
    /// </exception>
    public async Task<AcompanhamentoPublico> ObterAsync(string? tokenApresentado, CancellationToken cancelamento)
    {
        var agora = suporte.Agora;

        // Descarta antes de tocar no banco o que nem tem forma de token: limitador de requisições e banco
        // não precisam gastar nada com varredura.
        if (!SegredosDeRenovacao.FormatoEhPlausivel(tokenApresentado))
        {
            throw LinkNaoAbre();
        }

        var hash = SegredosDeRenovacao.CalcularHash(tokenApresentado!);

        var token = await suporte.Contexto.TokensDeRastreamento
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.HashDoToken == hash, cancelamento)
            .ConfigureAwait(false);

        if (token is null || !token.EstaValido(agora))
        {
            throw LinkNaoAbre();
        }

        var entrega = await suporte.Contexto.Entregas
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == token.EntregaId && item.OrganizacaoId == token.OrganizacaoId,
                cancelamento)
            .ConfigureAwait(false)
            ?? throw LinkNaoAbre();

        var marcos = await suporte.Contexto.EventosDaEntrega
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(evento => evento.EntregaId == entrega.Id && evento.OrganizacaoId == token.OrganizacaoId)
            .OrderBy(evento => evento.Sequencia)
            .Select(evento => new { evento.Tipo, evento.OcorridoEm })
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

        var previsao = await suporte.Contexto.PrevisoesDaEntrega
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.EntregaId == entrega.Id && item.OrganizacaoId == token.OrganizacaoId)
            .Select(item => new { item.ChegadaPrevistaEm, item.Situacao })
            .SingleOrDefaultAsync(cancelamento)
            .ConfigureAwait(false);

        return new AcompanhamentoPublico(
            entrega.Codigo,
            entrega.Status,
            entrega.Janela.Inicio,
            entrega.Janela.Fim,
            new DestinoPublico(entrega.Endereco.Bairro, entrega.Endereco.Cidade, entrega.Endereco.Uf),
            previsao?.ChegadaPrevistaEm,
            previsao?.Situacao,
            await PosicaoAsync(entrega, token.OrganizacaoId, agora, cancelamento).ConfigureAwait(false),
            await ComprovanteAsync(entrega, token.OrganizacaoId, cancelamento).ConfigureAwait(false),
            [.. marcos
                .Where(marco => PoliticaDeRastreamentoPublico.EhMarcoPublico(marco.Tipo))
                .Select(marco => new MarcoPublico(marco.Tipo, marco.OcorridoEm))],
            agora);
    }

    private static ExcecaoDeDominio LinkNaoAbre() =>
        ExcecaoDeDominio.NaoEncontrado(
            "rastreamento_nao_encontrado",
            "Este link de acompanhamento não é válido ou expirou.");

    private async Task<PosicaoAproximada?> PosicaoAsync(
        Entrega entrega,
        Guid organizacaoId,
        DateTimeOffset agora,
        CancellationToken cancelamento)
    {
        if (!PoliticaDeRastreamentoPublico.MostraPosicao(entrega.Status))
        {
            return null;
        }

        if (entrega.MotoristaId is not { } motoristaId)
        {
            return null;
        }

        var atual = await suporte.Contexto.PosicoesAtuais
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(posicao => posicao.MotoristaId == motoristaId && posicao.OrganizacaoId == organizacaoId)
            .Select(posicao => new { posicao.Localizacao.Latitude, posicao.Localizacao.Longitude, posicao.CapturadaEm })
            .SingleOrDefaultAsync(cancelamento)
            .ConfigureAwait(false);

        if (atual is null || agora - atual.CapturadaEm > PoliticaDeRastreamentoPublico.IdadeMaximaDaPosicao)
        {
            return null;
        }

        return new PosicaoAproximada(
            PoliticaDeRastreamentoPublico.Arredondar(atual.Latitude),
            PoliticaDeRastreamentoPublico.Arredondar(atual.Longitude),
            PoliticaDeRastreamentoPublico.LadoDaCelulaEmMetros,
            atual.CapturadaEm);
    }

    private async Task<ComprovantePublico?> ComprovanteAsync(
        Entrega entrega,
        Guid organizacaoId,
        CancellationToken cancelamento)
    {
        if (!Politica.ExporComprovante || entrega.Status != StatusDaEntrega.Entregue)
        {
            return null;
        }

        var comprovante = await suporte.Contexto.Comprovantes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(item => item.Arquivos)
            .SingleOrDefaultAsync(
                item => item.EntregaId == entrega.Id && item.OrganizacaoId == organizacaoId,
                cancelamento)
            .ConfigureAwait(false);

        if (comprovante is null)
        {
            return null;
        }

        var arquivos = new List<ArquivoPublico>(comprovante.Arquivos.Count);

        foreach (var arquivo in comprovante.Arquivos)
        {
            var url = await storage.AutorizarLeituraAsync(arquivo.Chave, cancelamento).ConfigureAwait(false);
            arquivos.Add(new ArquivoPublico(arquivo.Tipo, url.Url, url.ExpiraEm));
        }

        // Observação e localização do registro ficam de fora: são anotações da operação, não da encomenda.
        return new ComprovantePublico(comprovante.RecebidoPor, comprovante.RegistradoEm, arquivos);
    }
}
