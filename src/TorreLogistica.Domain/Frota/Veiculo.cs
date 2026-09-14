using TorreLogistica.Domain.Abstracoes.Cadastro;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Comum;

namespace TorreLogistica.Domain.Frota;

/// <summary>Categoria operacional do veículo.</summary>
public enum TipoDeVeiculo
{
    /// <summary>Motocicleta.</summary>
    Motocicleta = 1,

    /// <summary>Carro de passeio adaptado para entrega.</summary>
    Carro = 2,

    /// <summary>Utilitário ou furgão.</summary>
    Utilitario = 3,

    /// <summary>VUC — veículo urbano de carga.</summary>
    VeiculoUrbanoDeCarga = 4,

    /// <summary>Caminhão.</summary>
    Caminhao = 5,
}

/// <summary>Veículo da frota.</summary>
public sealed class Veiculo : IRecursoAtivavel
{
    /// <summary>Tamanho máximo da identificação interna.</summary>
    public const int TamanhoMaximoDaIdentificacao = 60;

    /// <summary>Maior capacidade aceita, em quilogramas.</summary>
    public const int CapacidadeMaximaEmKg = 60_000;

    private Veiculo()
    {
        Placa = string.Empty;
        Identificacao = string.Empty;
        IdentificacaoNormalizada = string.Empty;
    }

    /// <inheritdoc />
    public Guid Id { get; private set; }

    /// <inheritdoc />
    public Guid OrganizacaoId { get; private set; }

    /// <summary>Placa normalizada. Única por organização.</summary>
    public string Placa { get; private set; }

    /// <summary>Como a equipe chama o veículo: "Fiorino 03".</summary>
    public string Identificacao { get; private set; }

    /// <summary>Identificação sem acento e em minúsculas, para busca.</summary>
    public string IdentificacaoNormalizada { get; private set; }

    /// <summary>Categoria.</summary>
    public TipoDeVeiculo Tipo { get; private set; }

    /// <summary>Capacidade de carga, quando útil para planejamento.</summary>
    public int? CapacidadeEmKg { get; private set; }

    /// <inheritdoc />
    public bool Ativo { get; private set; }

    /// <summary>Instante de criação.</summary>
    public DateTimeOffset CriadoEm { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset AtualizadoEm { get; private set; }

    /// <inheritdoc />
    public uint Versao { get; private set; }

    /// <summary>Cadastra um veículo ativo.</summary>
    public static Veiculo Criar(
        Guid id,
        Guid organizacaoId,
        PlacaDeVeiculo placa,
        string? identificacao,
        TipoDeVeiculo tipo,
        int? capacidadeEmKg,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(placa);
        ExcecaoDeDominio.LancarSe(id == Guid.Empty, "identificador_invalido", "Identificador inválido.");
        ExcecaoDeDominio.LancarSe(organizacaoId == Guid.Empty, "organizacao_invalida", "Organização inválida.");

        var instante = agora.ToUniversalTime();
        var identificacaoValida = ValidarIdentificacao(identificacao);
        ValidarTipoECapacidade(tipo, capacidadeEmKg);

        return new Veiculo
        {
            Id = id,
            OrganizacaoId = organizacaoId,
            Placa = placa.Valor,
            Identificacao = identificacaoValida,
            IdentificacaoNormalizada = TextoNormalizado.ParaBusca(identificacaoValida),
            Tipo = tipo,
            CapacidadeEmKg = capacidadeEmKg,
            Ativo = true,
            CriadoEm = instante,
            AtualizadoEm = instante,
        };
    }

    /// <summary>
    /// Atualiza os dados. A placa pode mudar — a conversão para o padrão Mercosul troca a
    /// placa do mesmo veículo.
    /// </summary>
    /// <returns>Nomes dos campos que mudaram.</returns>
    public IReadOnlyList<string> AtualizarDados(
        PlacaDeVeiculo placa,
        string? identificacao,
        TipoDeVeiculo tipo,
        int? capacidadeEmKg,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(placa);

        var identificacaoValida = ValidarIdentificacao(identificacao);
        ValidarTipoECapacidade(tipo, capacidadeEmKg);

        var alteracoes = new RegistroDeAlteracoes();
        alteracoes.Aplicar("placa", Placa, placa.Valor, valor => Placa = valor);
        alteracoes.Aplicar("identificacao", Identificacao, identificacaoValida, valor =>
        {
            Identificacao = valor;
            IdentificacaoNormalizada = TextoNormalizado.ParaBusca(valor);
        });
        alteracoes.Aplicar("tipo", Tipo, tipo, valor => Tipo = valor);
        alteracoes.Aplicar("capacidadeEmKg", CapacidadeEmKg, capacidadeEmKg, valor => CapacidadeEmKg = valor);

        if (alteracoes.HouveAlteracao)
        {
            AtualizadoEm = agora.ToUniversalTime();
        }

        return alteracoes.Campos;
    }

    /// <inheritdoc />
    public bool Ativar(DateTimeOffset agora) => MudarSituacao(true, agora);

    /// <inheritdoc />
    public bool Inativar(DateTimeOffset agora) => MudarSituacao(false, agora);

    /// <summary>
    /// Regra da Fase 2, usada a partir da Fase 4: veículo inativo não inicia nova rota.
    /// </summary>
    public void GarantirAptoParaIniciarRota() =>
        ExcecaoDeDominio.LancarSe(!Ativo, "veiculo_inativo", "Veículo inativo não inicia nova rota.");

    private static string ValidarIdentificacao(string? identificacao) =>
        TextoNormalizado.Obrigatorio(identificacao, TamanhoMaximoDaIdentificacao, "identificacao_invalida", "A identificação");

    private static void ValidarTipoECapacidade(TipoDeVeiculo tipo, int? capacidadeEmKg)
    {
        ExcecaoDeDominio.LancarSe(!Enum.IsDefined(tipo), "tipo_de_veiculo_invalido", "Tipo de veículo inválido.");
        ExcecaoDeDominio.LancarSe(
            capacidadeEmKg is < 1 or > CapacidadeMaximaEmKg,
            "capacidade_invalida",
            $"A capacidade deve estar entre 1 e {CapacidadeMaximaEmKg} kg.");
    }

    private bool MudarSituacao(bool ativo, DateTimeOffset agora)
    {
        if (Ativo == ativo)
        {
            return false;
        }

        Ativo = ativo;
        AtualizadoEm = agora.ToUniversalTime();
        return true;
    }
}
