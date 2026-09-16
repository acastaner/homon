namespace Homon.Domain.Weather;

/// <summary>
/// A simplified condition word, mirroring the WMO weather-interpretation codes Open-Meteo
/// reports — see <see cref="WmoWeatherCodeMap"/> for the mapping. Serialised camelCase by
/// the API's global <c>JsonStringEnumConverter</c> (<c>Program.cs</c>); the SPA maps each
/// value to a Lucide icon and shows the same word as text beside it.
/// </summary>
public enum WeatherCondition
{
    Clear,
    PartlyCloudy,
    Cloudy,
    Fog,
    Drizzle,
    Rain,
    Snow,
    Thunderstorm,

    /// <summary>A WMO code this map does not recognise.</summary>
    Unknown,
}
