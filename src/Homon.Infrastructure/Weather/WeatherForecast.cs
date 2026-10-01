using Homon.Domain.Weather;

namespace Homon.Infrastructure.Weather;

/// <summary>
/// A forecast in a <see cref="WeatherSettings"/>' own units — neither
/// <see cref="IWeatherProvider"/> nor the API converts anything; Open-Meteo is asked directly
/// for the settings' unit system.
/// </summary>
/// <remarks>
/// This is the shape <c>WeatherCache</c> holds, so it carries everything the provider parsed
/// rather than a window: the cache keeps a snapshot for up to <c>WeatherCache.FreshFor</c>
/// (15 minutes), and a window chosen at fetch time would open on an hour already past by the
/// time a reader loads the page. <c>WeatherEndpoints</c> therefore slices per request from
/// <see cref="Hours"/> using the injected <see cref="TimeProvider"/>. See plan 020, D6.
/// </remarks>
/// <param name="Current">Conditions right now.</param>
/// <param name="Days">
/// Eight days, <b>today first</b>. Today was deliberately dropped before plan 020, because
/// <paramref name="Current"/> covered it — but the dashboard widget now shows today's high and
/// low, and the weather page summarises the day, so it is kept. The endpoint splits
/// <c>Days[0]</c> off as <c>Today</c> and sends the remaining seven as the forecast.
/// </param>
/// <param name="Hours">
/// Every hourly row Open-Meteo returned, ascending, starting at <b>today 00:00 in the
/// location's own timezone</b> — confirmed against the live API on 2026-10-01. Eight days of
/// them, so 192 rows.
/// </param>
/// <param name="UtcOffsetSeconds">
/// The location's offset from UTC, as Open-Meteo reports it under <c>timezone=auto</c>. The
/// endpoint needs it to work out which of <paramref name="Hours"/> is the current hour there;
/// without it the server would need a tz database and an IANA setting, which is plan 011's
/// <c>Calendar:TimeZone</c> territory rather than this module's.
/// </param>
public sealed record WeatherForecast(
    WeatherCurrent Current,
    IReadOnlyList<WeatherDay> Days,
    IReadOnlyList<WeatherHour> Hours,
    int UtcOffsetSeconds);

/// <param name="Temperature">Current temperature, in the settings' units.</param>
/// <param name="ApparentTemperature">"Feels like" temperature, in the settings' units.</param>
/// <param name="WindSpeed">Current wind speed, in the settings' units.</param>
/// <param name="Condition">The simplified condition word.</param>
/// <param name="IsDay">Whether it is currently daytime at the location.</param>
public sealed record WeatherCurrent(
    double Temperature, double ApparentTemperature, double WindSpeed, WeatherCondition Condition, bool IsDay);
