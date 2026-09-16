namespace Homon.Domain.Weather;

/// <summary>
/// Maps a WMO weather-interpretation code (as Open-Meteo reports it, in both
/// <c>current.weather_code</c> and <c>daily.weather_code</c>) to a <see cref="WeatherCondition"/>.
/// Pure and dependency-free so it is unit-testable without any infrastructure.
/// </summary>
public static class WmoWeatherCodeMap
{
    /// <summary>
    /// | WMO code(s) | Condition | WMO code(s) | Condition |
    /// | --- | --- | --- | --- |
    /// | 0, 1 | Clear | 61,63,65,66,67,80,81,82 | Rain |
    /// | 2 | PartlyCloudy | 71,73,75,77,85,86 | Snow |
    /// | 3 | Cloudy | 95,96,99 | Thunderstorm |
    /// | 45, 48 | Fog | anything else | Unknown |
    /// | 51,53,55,56,57 | Drizzle | | |
    /// </summary>
    public static WeatherCondition Map(int wmoCode) => wmoCode switch
    {
        0 or 1 => WeatherCondition.Clear,
        2 => WeatherCondition.PartlyCloudy,
        3 => WeatherCondition.Cloudy,
        45 or 48 => WeatherCondition.Fog,
        51 or 53 or 55 or 56 or 57 => WeatherCondition.Drizzle,
        61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => WeatherCondition.Rain,
        71 or 73 or 75 or 77 or 85 or 86 => WeatherCondition.Snow,
        95 or 96 or 99 => WeatherCondition.Thunderstorm,
        _ => WeatherCondition.Unknown,
    };
}
