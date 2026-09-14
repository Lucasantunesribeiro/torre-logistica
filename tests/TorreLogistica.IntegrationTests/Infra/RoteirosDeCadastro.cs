namespace TorreLogistica.IntegrationTests.Infra;

/// <summary>
/// Como exercitar um cadastro pela API: rota, corpo válido e corpo de alteração.
/// </summary>
/// <remarks>
/// Os cinco cadastros seguem o mesmo contrato. Descrevê-los como dados permite que o mesmo
/// teste prove o ciclo completo e o isolamento em todos — e um cadastro novo que fuja do
/// contrato falha no mesmo lugar que os outros.
/// </remarks>
public sealed record RoteiroDeCadastro(
    string Recurso,
    string Rota,
    Func<object> Corpo,
    Func<uint, object> CorpoDeAlteracao,
    string CampoAlterado,
    string ValorAlterado);

/// <summary>Roteiros e geradores de dado válido para os cadastros.</summary>
public static class RoteirosDeCadastro
{
    /// <summary>Nomes dos cadastros, para <c>MemberData</c>.</summary>
    public static TheoryData<string> Recursos() => new("motorista", "veiculo", "hub", "cliente", "destinatario");

    /// <summary>Roteiro do cadastro.</summary>
    public static RoteiroDeCadastro Obter(string recurso) => recurso switch
    {
        "motorista" => new(
            recurso,
            "/api/motoristas",
            () => new { nome = "João da Conceição", telefone = "(11) 98765-4321" },
            versao => new { versao, nome = "Motorista Alterado", telefone = "(11) 98765-4321" },
            "nome",
            "Motorista Alterado"),

        "veiculo" => new(
            recurso,
            "/api/veiculos",
            () => new { placa = PlacaAleatoria(), identificacao = "Fiorino 03", tipo = "Utilitario", capacidadeEmKg = 650 },
            versao => new { versao, placa = PlacaAleatoria(), identificacao = "Fiorino Alterado", tipo = "Utilitario", capacidadeEmKg = 650 },
            "identificacao",
            "Fiorino Alterado"),

        "hub" => new(
            recurso,
            "/api/hubs",
            () => new { nome = "Hub Centro", endereco = Endereco(), latitude = -22.9056, longitude = -47.0608 },
            versao => new { versao, nome = "Hub Alterado", endereco = Endereco(), latitude = -22.9056, longitude = -47.0608 },
            "nome",
            "Hub Alterado"),

        "cliente" => new(
            recurso,
            "/api/clientes",
            () => new { nome = "Mercado Horizonte", cnpj = CnpjAleatorio() },
            versao => new { versao, nome = "Cliente Alterado", cnpj = (string?)null },
            "nome",
            "Cliente Alterado"),

        "destinatario" => new(
            recurso,
            "/api/destinatarios",
            () => new
            {
                nome = "Carla Nunes",
                telefone = "11 3456-7890",
                endereco = Endereco(),
                latitude = -22.9100,
                longitude = -47.0650,
                instrucoesDeEntrega = "Portão lateral, interfone 12",
            },
            versao => new
            {
                versao,
                nome = "Destinatário Alterado",
                telefone = "11 3456-7890",
                endereco = Endereco(),
                latitude = (double?)null,
                longitude = (double?)null,
                instrucoesDeEntrega = (string?)null,
            },
            "nome",
            "Destinatário Alterado"),

        _ => throw new ArgumentOutOfRangeException(nameof(recurso), recurso, "Cadastro desconhecido."),
    };

    /// <summary>Endereço válido de teste.</summary>
    public static object Endereco(string cep = "13010-000", string uf = "SP") => new
    {
        logradouro = "Rua das Palmeiras",
        numero = "120",
        complemento = (string?)null,
        bairro = "Centro",
        cidade = "Campinas",
        uf,
        cep,
    };

    /// <summary>Placa Mercosul aleatória e válida.</summary>
    public static string PlacaAleatoria()
    {
        var bytes = Guid.NewGuid().ToByteArray();
        char Letra(int indice) => (char)('A' + (bytes[indice] % 26));
        char Digito(int indice) => (char)('0' + (bytes[indice] % 10));
        return $"{Letra(0)}{Letra(1)}{Letra(2)}{Digito(3)}{Letra(4)}{Digito(5)}{Digito(6)}";
    }

    /// <summary>CNPJ numérico aleatório com dígitos verificadores corretos.</summary>
    public static string CnpjAleatorio()
    {
        var bytes = Guid.NewGuid().ToByteArray();
        var baseDoCnpj = string.Concat(bytes.Take(12).Select(valor => (char)('0' + (valor % 10))));
        var primeiro = Digito(baseDoCnpj, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        var segundo = Digito(baseDoCnpj + primeiro, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return $"{baseDoCnpj}{primeiro}{segundo}";

        static int Digito(string texto, int[] pesos)
        {
            var resto = texto.Select((caractere, indice) => (caractere - '0') * pesos[indice]).Sum() % 11;
            return resto < 2 ? 0 : 11 - resto;
        }
    }
}
