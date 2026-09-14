using TorreLogistica.Domain.Abstracoes.Erros;

namespace TorreLogistica.Domain.Comum;

/// <summary>
/// Ponto na superfície da Terra, em graus decimais WGS 84 (SRID 4326).
/// </summary>
/// <remarks>
/// <para>
/// O domínio usa este tipo próprio, e não o <c>Point</c> do NetTopologySuite: a
/// Infrastructure converte para <c>geography(Point,4326)</c> do PostGIS. Assim o domínio
/// não depende de biblioteca geoespacial, e o banco continua fazendo a geometria (ADR 0011).
/// </para>
/// <para>
/// <c>(0, 0)</c> é recusado. É um ponto no oceano Atlântico que nenhuma operação no Brasil
/// usa, e é exatamente o valor que aparece quando um formulário ou integração manda
/// latitude e longitude vazias convertidas em zero.
/// </para>
/// </remarks>
public sealed record CoordenadaGeografica
{
    private CoordenadaGeografica(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    /// <summary>Latitude, de -90 a 90.</summary>
    public double Latitude { get; }

    /// <summary>Longitude, de -180 a 180.</summary>
    public double Longitude { get; }

    /// <summary>Valida e cria a coordenada.</summary>
    public static CoordenadaGeografica Criar(double latitude, double longitude)
    {
        ExcecaoDeDominio.LancarSe(
            !double.IsFinite(latitude) || !double.IsFinite(longitude)
            || latitude is < -90 or > 90
            || longitude is < -180 or > 180,
            "coordenada_invalida",
            "Coordenada inválida: latitude entre -90 e 90, longitude entre -180 e 180.");

        ExcecaoDeDominio.LancarSe(
            latitude == 0 && longitude == 0,
            "coordenada_invalida",
            "Coordenada (0, 0) recusada: costuma ser latitude e longitude ausentes convertidas em zero.");

        return new CoordenadaGeografica(latitude, longitude);
    }

    /// <summary>Coordenada opcional: ambas ausentes viram <see langword="null"/>; só uma é erro.</summary>
    public static CoordenadaGeografica? CriarOpcional(double? latitude, double? longitude)
    {
        if (latitude is null && longitude is null)
        {
            return null;
        }

        ExcecaoDeDominio.LancarSe(
            latitude is null || longitude is null,
            "coordenada_invalida",
            "Informe latitude e longitude juntas.");

        return Criar(latitude!.Value, longitude!.Value);
    }
}
