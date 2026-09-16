using Homon.Domain.Weather;

namespace Homon.Infrastructure.Weather;

/// <summary>
/// A forecast in a <see cref="WeatherSettings"/>' own units — neither
/// <see cref="IWeatherProvider"/> nor the API converts anything; Open-Meteo is asked directly
/// for the settings' unit system.
/// </summary>
/// <param name="Current">Conditions right now.</param>
/// <param name="Days">Up to three upcoming days, per the artboards' three forecast rows.</param>
public sealed record WeatherForecast(WeatherCurrent Current, IReadOnlyList<WeatherForecastDay> Days);

/// <param name="Temperature">Current temperature, in the settings' units.</param>
/// <param name="ApparentTemperature">"Feels like" temperature, in the settings' units.</param>
/// <param name="WindSpeed">Current wind speed, in the settings' units.</param>
/// <param name="Condition">The simplified condition word.</param>
/// <param name="IsDay">Whether it is currently daytime at the location.</param>
public sealed record WeatherCurrent(
    double Temperature, double ApparentTemperature, double WindSpeed, WeatherCondition Condition, bool IsDay);

/// <param name="Date">The forecast day's calendar date, in the location's own timezone.</param>
/// <param name="Condition">The simplified condition word.</param>
/// <param name="High">The day's forecast high, in the settings' units.</param>
/// <param name="Low">The day's forecast low, in the settings' units.</param>
public sealed record WeatherForecastDay(DateOnly Date, WeatherCondition Condition, double High, double Low);
