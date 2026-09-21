using System.Globalization;

namespace TorreLogistica.Simulator.Cenario;

/// <summary>As histórias que a demonstração conta.</summary>
public enum Historia
{
    /// <summary>A — operação normal: entrega dentro do prazo.</summary>
    OperacaoNormal = 0,

    /// <summary>B — risco de atraso: a previsão piora e a torre avisa.</summary>
    RiscoDeAtraso = 1,

    /// <summary>C — motorista offline: a última posição envelhece.</summary>
    MotoristaOffline = 2,

    /// <summary>D — tentativa frustrada: destinatário ausente.</summary>
    TentativaFrustrada = 3,

    /// <summary>E — geofence: entrar no raio muda o estado sozinho.</summary>
    EntradaNoGeofence = 4,

    /// <summary>F — prova de entrega: conclusão com evidência.</summary>
    ProvaDeEntrega = 5,
}

/// <summary>Um destinatário do roteiro, com endereço e ponto fixos.</summary>
/// <param name="Nome">Nome fictício.</param>
/// <param name="Logradouro">Rua.</param>
/// <param name="Numero">Número.</param>
/// <param name="Bairro">Bairro.</param>
/// <param name="Latitude">Latitude do destino.</param>
/// <param name="Longitude">Longitude do destino.</param>
public sealed record DestinoDoRoteiro(
    string Nome,
    string Logradouro,
    string Numero,
    string Bairro,
    double Latitude,
    double Longitude);

/// <summary>Uma história do roteiro, já resolvida em dados concretos.</summary>
/// <param name="Historia">Qual história.</param>
/// <param name="Titulo">Título legível, para o log da demonstração.</param>
/// <param name="Destino">Para onde a entrega vai.</param>
/// <param name="MinutosDeJanela">Duração da janela prometida, em minutos do roteiro.</param>
/// <param name="DistanciaInicialEmMetros">A que distância do destino o motorista começa.</param>
public sealed record CapituloDoRoteiro(
    Historia Historia,
    string Titulo,
    DestinoDoRoteiro Destino,
    int MinutosDeJanela,
    int DistanciaInicialEmMetros);

/// <summary>
/// O roteiro da demonstração: sempre o mesmo, a partir da mesma semente.
/// </summary>
/// <remarks>
/// <para>
/// Nada aqui é sorteado no momento da execução. Nomes, endereços, coordenadas e distâncias saem de uma
/// semente, e a mesma semente produz a mesma operação — que é o que significa "reproduzível". Um roteiro
/// com <c>Random</c> sem semente contaria uma história diferente a cada demonstração, e a primeira pergunta
/// de quem assiste seria por que o número mudou.
/// </para>
/// <para>
/// A ordem dos capítulos também é fixa. Ela não é arbitrária: a operação normal vem primeiro para
/// estabelecer o que é o certo, e só então aparecem o risco, o silêncio do motorista e a porta fechada.
/// </para>
/// </remarks>
public sealed class RoteiroDaDemonstracao
{
    /// <summary>Ponto de partida da operação — um hub urbano fictício em Campinas.</summary>
    public const double LatitudeDoHub = -22.9050;

    /// <summary>Longitude do hub.</summary>
    public const double LongitudeDoHub = -47.0600;

    private static readonly string[] Ruas =
    [
        "Rua das Palmeiras", "Avenida dos Ipês", "Rua Guaraciaba", "Travessa do Moinho",
        "Alameda Sabiá", "Rua Caiapós", "Avenida Ribeirão", "Rua Vergueiro",
    ];

    private static readonly string[] Bairros =
    [
        "Centro", "Vila Industrial", "Jardim Aurora", "Barão Geraldo",
        "Cambuí", "Taquaral", "Sousas", "Nova Campinas",
    ];

    private static readonly string[] Nomes =
    [
        "Padaria Sol Nascente", "Clínica Verde", "Mercado do Largo", "Ateliê Bordô",
        "Escola Girassol", "Oficina Ponto Certo", "Farmácia Pedra Azul", "Livraria Rumo",
    ];

    private readonly int _semente;
    private readonly string _execucao;

    /// <summary>
    /// Monta o roteiro a partir da semente.
    /// </summary>
    /// <param name="semente">Decide a narrativa: destinos, ordem, janelas, distancias.</param>
    /// <param name="execucao">
    /// Distingue uma encenacao da outra nos campos que o sistema exige unicos. Omitido, vem do relogio.
    /// </param>
    /// <remarks>
    /// A separacao e o ponto: o que se repete e a <b>historia</b>, nao a identidade dos cadastros. Placa e
    /// CNPJ iguais fariam a segunda execucao esbarrar na unicidade que o proprio sistema garante — e a
    /// demonstracao provaria, sem querer, que nao da para demonstrar duas vezes.
    /// </remarks>
    public RoteiroDaDemonstracao(int semente, string? execucao = null)
    {
        _semente = semente;
        _execucao = execucao ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)[^6..];
        Capitulos = [.. MontarCapitulos(semente)];
    }

    /// <summary>Os capítulos, na ordem em que são encenados.</summary>
    public IReadOnlyList<CapituloDoRoteiro> Capitulos { get; }

    /// <summary>Etiqueta desta encenação, com a semente da narrativa e o carimbo da execução.</summary>
    public string Etiqueta => string.Create(CultureInfo.InvariantCulture, $"demo-{_semente:D4}-{_execucao}");

    /// <summary>Nome do motorista da demonstração — parte da narrativa, então vem só da semente.</summary>
    public string NomeDoMotorista => $"Rafael Demonstração {_semente % 100:D2}";

    /// <summary>E-mail da conta do motorista. Único por execução.</summary>
    public string EmailDoMotorista =>
        string.Create(CultureInfo.InvariantCulture, $"motorista.{_semente:D4}.{_execucao}@demo.test");

    /// <summary>Placa Mercosul do veículo. Única por execução.</summary>
    public string PlacaDoVeiculo
    {
        get
        {
            var bruto = Math.Abs(HashCode.Combine(_semente, _execucao));
            char Letra(int posicao) => (char)('A' + (bruto / (int)Math.Pow(26, posicao) % 26));
            char Digito(int posicao) => (char)('0' + (bruto / (int)Math.Pow(10, posicao) % 10));
            return $"{Letra(0)}{Letra(1)}{Letra(2)}{Digito(0)}{Letra(3)}{Digito(1)}{Digito(2)}";
        }
    }

    /// <summary>CNPJ com dígitos verificadores corretos, único por execução.</summary>
    public string CnpjDoCliente
    {
        get
        {
            var bruto = Math.Abs(HashCode.Combine(_semente, _execucao, 17));
            var baseDoCnpj = string.Create(CultureInfo.InvariantCulture, $"{bruto % 1_000_000:D6}").PadLeft(12, '0');
            var primeiro = DigitoDoCnpj(baseDoCnpj, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
            var segundo = DigitoDoCnpj(baseDoCnpj + primeiro, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
            return $"{baseDoCnpj}{primeiro}{segundo}";
        }
    }

    private static int DigitoDoCnpj(string texto, int[] pesos)
    {
        var resto = texto.Select((caractere, indice) => (caractere - '0') * pesos[indice]).Sum() % 11;
        return resto < 2 ? 0 : 11 - resto;
    }

    /// <summary>
    /// Ponto a uma distância aproximada ao sul do destino, para o motorista começar longe e se aproximar.
    /// </summary>
    /// <remarks>
    /// A conversão usa a aproximação de 111.320 metros por grau de latitude. Para a demonstração isso
    /// basta: o que precisa ser exato é a distância que o PostGIS calcula depois, não a que o roteiro
    /// pediu — e é ela que decide o geofence.
    /// </remarks>
    public static (double Latitude, double Longitude) AoSulDe(DestinoDoRoteiro destino, double metros) =>
        (destino.Latitude - (metros / 111_320d), destino.Longitude);

    private static IEnumerable<CapituloDoRoteiro> MontarCapitulos(int semente)
    {
        var sorteio = new Random(semente);

        (string Titulo, Historia Historia, int Janela, int Distancia)[] enredo =
        [
            ("Entrega no prazo, do jeito que deveria ser", Historia.OperacaoNormal, 180, 4_000),
            ("A previsão piora e a torre avisa antes de estourar", Historia.RiscoDeAtraso, 35, 9_000),
            ("O motorista some do mapa", Historia.MotoristaOffline, 240, 6_000),
            ("Ninguém para receber", Historia.TentativaFrustrada, 180, 3_000),
            ("Chegou perto: o estado muda sozinho", Historia.EntradaNoGeofence, 180, 2_000),
            ("Entrega com foto, assinatura de quem recebeu e hora", Historia.ProvaDeEntrega, 180, 2_500),
        ];

        for (var indice = 0; indice < enredo.Length; indice++)
        {
            var (titulo, historia, janela, distancia) = enredo[indice];

            // O sorteio roda na mesma ordem sempre, então cada capítulo recebe sempre o mesmo destino.
            var destino = new DestinoDoRoteiro(
                Nomes[sorteio.Next(Nomes.Length)],
                Ruas[sorteio.Next(Ruas.Length)],
                sorteio.Next(10, 1_999).ToString(CultureInfo.InvariantCulture),
                Bairros[sorteio.Next(Bairros.Length)],

                // Destinos espalhados ao norte e a leste do hub, a alguns quilômetros.
                LatitudeDoHub + (0.008 * (indice + 1)),
                LongitudeDoHub + (0.006 * ((indice % 3) + 1)));

            yield return new CapituloDoRoteiro(historia, titulo, destino, janela, distancia);
        }
    }
}
