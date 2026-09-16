namespace Homon.Domain.Weather;

/// <summary>
/// The household's one location. The table holds at most one row, always at
/// <see cref="SingletonId"/> — every read or write targets that constant id, so there is no
/// ordering problem and no risk of a stray second row.
/// </summary>
public sealed class WeatherSettings
{
    /// <summary>Longest <see cref="Place"/> the admin may give.</summary>
    public const int PlaceMaxLength = 100;

    /// <summary>The only id a <see cref="WeatherSettings"/> row is ever stored at.</summary>
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public Guid Id { get; set; } = SingletonId;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    /// <summary>Optional, generic label shown beside "Weather" on the dashboard — nothing here assumes a household.</summary>
    public string? Place { get; set; }

    public WeatherUnits Units { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
