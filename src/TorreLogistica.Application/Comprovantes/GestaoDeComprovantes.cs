using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TorreLogistica.Application.Abstracoes.Armazenamento;
using TorreLogistica.Application.Cadastros;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Comprovantes;
using TorreLogistica.Domain.Comum;

namespace TorreLogistica.Application.Comprovantes;

/// <summary>Política de prova de entrega — seção <c>Torre:Comprovantes</c>.</summary>
public sealed class OpcoesDeComprovantes
{
    /// <summary>Seção de configuração.</summary>
    public const string Secao = "Torre:Comprovantes";

    /// <summary>
    /// Concluir entrega exige comprovante registrado. Desligado por padrão: a operação que quer prova
    /// obrigatória liga, e aí a conclusão sem evidência é recusada.
    /// </summary>
    public bool ExigirNaConclusao { get; set; }

    /// <summary>Com a exigência ligada, o comprovante precisa de ao menos um arquivo (foto ou assinatura).</summary>
    public bool ExigirArquivo { get; set; } = true;
}

/// <summary>Autorização para enviar um arquivo direto ao storage.</summary>
/// <param name="ArquivoId">Identificador do arquivo, usado depois no registro.</param>
/// <param name="Chave">Chave do objeto no storage.</param>
/// <param name="Url">URL assinada de curta duração.</param>
/// <param name="ExpiraEm">Fim da validade.</param>
/// <param name="CabecalhosObrigatorios">Cabeçalhos que o cliente precisa enviar.</param>
/// <param name="TamanhoMaximoEmBytes">Tamanho máximo autorizado.</param>
public sealed record AutorizacaoDeEnvio(
    Guid ArquivoId,
    string Chave,
    Uri Url,
    DateTimeOffset ExpiraEm,
    IReadOnlyDictionary<string, string> CabecalhosObrigatorios,
    long TamanhoMaximoEmBytes);

/// <summary>Arquivo que o cliente diz ter enviado.</summary>
/// <param name="Tipo">Foto ou assinatura.</param>
/// <param name="Chave">Chave devolvida na autorização.</param>
public sealed record ArquivoEnviado(TipoDeArquivoDoComprovante Tipo, string Chave);

/// <summary>Comprovante como o aplicativo o informa.</summary>
/// <param name="RecebidoPor">Nome de quem recebeu.</param>
/// <param name="Observacao">Observação de quem entregou.</param>
/// <param name="Latitude">Latitude no momento da entrega.</param>
/// <param name="Longitude">Longitude no momento da entrega.</param>
/// <param name="Arquivos">Arquivos já enviados ao storage.</param>
public sealed record DadosDoComprovante(
    string? RecebidoPor,
    string? Observacao,
    double? Latitude,
    double? Longitude,
    IReadOnlyList<ArquivoEnviado> Arquivos);

/// <summary>Arquivo do comprovante, como a API o devolve.</summary>
public sealed record ArquivoDoComprovanteResumo(
    Guid Id,
    TipoDeArquivoDoComprovante Tipo,
    string TipoDeConteudo,
    long TamanhoEmBytes,
    string HashSha256,
    DateTimeOffset EnviadoEm,
    Uri Url,
    DateTimeOffset UrlExpiraEm);

/// <summary>Comprovante como a API o devolve.</summary>
public sealed record ComprovanteResumo(
    Guid Id,
    Guid EntregaId,
    string CodigoDaEntrega,
    Guid? RotaId,
    Guid? MotoristaId,
    string RecebidoPor,
    string? Observacao,
    CoordenadaResumo? Localizacao,
    DateTimeOffset RegistradoEm,
    Guid AutorUsuarioId,
    IReadOnlyList<ArquivoDoComprovanteResumo> Arquivos);

/// <summary>
/// Prova de entrega: autoriza o envio dos arquivos, registra os metadados e devolve leitura assinada.
/// </summary>
/// <remarks>
/// <para>
/// O arquivo nunca passa por aqui (ADR 0007). Esta classe autoriza o envio direto ao storage e, depois,
/// confere no próprio storage o que realmente chegou — tamanho, tipo e hash — antes de gravar metadado.
/// Metadado que não corresponde a objeto existente é recusado.
/// </para>
/// <para>
/// Leitura nunca é pública: cada consulta devolve URLs assinadas de curta duração, emitidas só depois de a
/// consulta passar pelo filtro de organização.
/// </para>
/// </remarks>
public sealed class GestaoDeComprovantes(SuporteDeCadastro suporte, IObjectStorage storage, IOptions<OpcoesDeComprovantes> opcoes)
{
    /// <summary>Política configurada.</summary>
    public OpcoesDeComprovantes Politica => opcoes.Value;

    /// <summary>Autoriza o envio de um arquivo do comprovante de uma entrega.</summary>
    public async Task<AutorizacaoDeEnvio> AutorizarEnvioAsync(
        Guid entregaId,
        TipoDeArquivoDoComprovante tipo,
        string? tipoDeConteudo,
        CancellationToken cancelamento)
    {
        ExcecaoDeDominio.LancarSe(!Enum.IsDefined(tipo), "tipo_de_arquivo_invalido", "Tipo de arquivo de comprovante desconhecido.");
        ExcecaoDeDominio.LancarSe(
            !PoliticaDeComprovante.TipoAceito(tipoDeConteudo),
            "tipo_de_arquivo_nao_aceito",
            $"Tipo de arquivo não aceito. Aceitos: {string.Join(", ", PoliticaDeComprovante.TiposDeConteudoAceitos)}.");

        var arquivoId = suporte.NovoIdentificador();
        var chave = ChaveDoComprovante(suporte.OrganizacaoId, entregaId, arquivoId, PoliticaDeComprovante.ExtensaoDe(tipoDeConteudo!));

        var url = await storage
            .AutorizarEnvioAsync(chave, tipoDeConteudo!, PoliticaDeComprovante.TamanhoMaximoEmBytes, cancelamento)
            .ConfigureAwait(false);

        return new AutorizacaoDeEnvio(
            arquivoId, chave, url.Url, url.ExpiraEm, url.CabecalhosObrigatorios, PoliticaDeComprovante.TamanhoMaximoEmBytes);
    }

    /// <summary>
    /// Monta o comprovante conferindo cada arquivo no storage — sem gravar. Quem chama grava junto com a
    /// conclusão da entrega, no mesmo commit.
    /// </summary>
    public async Task<Comprovante> MontarAsync(
        Guid entregaId,
        Guid? rotaId,
        Guid? motoristaId,
        DadosDoComprovante dados,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(dados);

        ExcecaoDeDominio.LancarSe(
            Politica.ExigirNaConclusao && Politica.ExigirArquivo && dados.Arquivos.Count == 0,
            "comprovante_sem_arquivo",
            "A política desta operação exige ao menos uma foto ou assinatura no comprovante.");

        var confirmados = new List<ArquivoConfirmado>(dados.Arquivos.Count);

        foreach (var arquivo in dados.Arquivos)
        {
            ExcecaoDeDominio.LancarSe(
                !arquivo.Chave.StartsWith(PrefixoDaEntrega(suporte.OrganizacaoId, entregaId), StringComparison.Ordinal),
                "arquivo_de_outra_entrega",
                "O arquivo informado não pertence a esta entrega.");

            var objeto = await storage.ObterAsync(arquivo.Chave, cancelamento).ConfigureAwait(false)
                ?? throw ExcecaoDeDominio.RegraViolada(
                    "arquivo_nao_enviado",
                    "O arquivo não está no storage. Envie o arquivo pela URL autorizada antes de registrar o comprovante.");

            // O que vale é o que o storage gravou, não o que o cliente diz ter enviado.
            confirmados.Add(new ArquivoConfirmado(
                arquivo.Tipo, objeto.Chave, objeto.TipoDeConteudo, objeto.TamanhoEmBytes, objeto.HashSha256, objeto.GravadoEm));
        }

        return Comprovante.Registrar(
            suporte.NovoIdentificador(),
            suporte.OrganizacaoId,
            entregaId,
            rotaId,
            motoristaId,
            dados.RecebidoPor,
            dados.Observacao,
            CoordenadaGeografica.CriarOpcional(dados.Latitude, dados.Longitude),
            confirmados,
            suporte.UsuarioId,
            suporte.NovoIdentificador,
            suporte.Agora);
    }

    /// <summary>A entrega já tem comprovante registrado.</summary>
    public Task<bool> ExisteParaEntregaAsync(Guid entregaId, CancellationToken cancelamento) =>
        suporte.Contexto.Comprovantes.AnyAsync(comprovante => comprovante.EntregaId == entregaId, cancelamento);

    /// <summary>Comprovante de uma entrega, com URLs de leitura assinadas de curta duração.</summary>
    public async Task<ComprovanteResumo> ObterDaEntregaAsync(Guid entregaId, CancellationToken cancelamento)
    {
        var contexto = suporte.Contexto;

        var existeEntrega = await contexto.Entregas.AnyAsync(entrega => entrega.Id == entregaId, cancelamento).ConfigureAwait(false);
        if (!existeEntrega)
        {
            throw ExcecaoDeDominio.NaoEncontrado("entrega_nao_encontrada", "Entrega não encontrada.");
        }

        var comprovante = await contexto.Comprovantes
            .AsNoTracking()
            .Include(item => item.Arquivos)
            .SingleOrDefaultAsync(item => item.EntregaId == entregaId, cancelamento)
            .ConfigureAwait(false)
            ?? throw ExcecaoDeDominio.NaoEncontrado("comprovante_nao_encontrado", "Esta entrega ainda não tem comprovante.");

        var codigo = await contexto.Entregas
            .AsNoTracking()
            .Where(entrega => entrega.Id == entregaId)
            .Select(entrega => entrega.Codigo)
            .SingleAsync(cancelamento)
            .ConfigureAwait(false);

        var arquivos = new List<ArquivoDoComprovanteResumo>(comprovante.Arquivos.Count);
        foreach (var arquivo in comprovante.Arquivos.OrderBy(item => item.EnviadoEm))
        {
            var url = await storage.AutorizarLeituraAsync(arquivo.Chave, cancelamento).ConfigureAwait(false);
            arquivos.Add(new ArquivoDoComprovanteResumo(
                arquivo.Id,
                arquivo.Tipo,
                arquivo.TipoDeConteudo,
                arquivo.TamanhoEmBytes,
                arquivo.HashSha256,
                arquivo.EnviadoEm,
                url.Url,
                url.ExpiraEm));
        }

        return new ComprovanteResumo(
            comprovante.Id,
            comprovante.EntregaId,
            codigo,
            comprovante.RotaId,
            comprovante.MotoristaId,
            comprovante.RecebidoPor,
            comprovante.Observacao,
            CoordenadaResumo.De(comprovante.Localizacao),
            comprovante.RegistradoEm,
            comprovante.AutorUsuarioId,
            arquivos);
    }

    private static string PrefixoDaEntrega(Guid organizacaoId, Guid entregaId) =>
        $"organizacoes/{organizacaoId:N}/entregas/{entregaId:N}/";

    private static string ChaveDoComprovante(Guid organizacaoId, Guid entregaId, Guid arquivoId, string extensao) =>
        $"{PrefixoDaEntrega(organizacaoId, entregaId)}{arquivoId:N}.{extensao}";
}
