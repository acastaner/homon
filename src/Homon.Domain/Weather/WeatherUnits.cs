namespace Homon.Domain.Weather;

/// <summary>
/// The unit system a <see cref="WeatherSettings"/> row reports in. Open-Meteo accepts both
/// directly (<c>temperature_unit</c>/<c>wind_speed_unit</c>), so nothing downstream of the
/// provider ever converts a value — see <c>OpenMeteoWeatherProvider</c>.
/// </summary>
public enum WeatherUnits
{
    /// <summary>Celsius, km/h.</summary>
    Metric,

    /// <summary>Fahrenheit, mph.</summary>
    Imperial,
}
