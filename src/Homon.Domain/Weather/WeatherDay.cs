namespace Homon.Domain.Weather;

/// <summary>
/// One day of a forecast, in the <see cref="WeatherSettings"/>' own units. Named
/// <c>WeatherDay</c> rather than <c>WeatherForecastDay</c> since plan 020, because the first
/// entry of a <c>WeatherForecast</c>'s list is now *today* — see that record's remarks.
/// </summary>
/// <param name="Date">The day's calendar date, in the location's own timezone.</param>
/// <param name="Condition">The simplified condition word for the day.</param>
/// <param name="High">The day's high, in the settings' units.</param>
/// <param name="Low">The day's low, in the settings' units.</param>
/// <param name="PrecipitationSum">
/// Total precipitation — millimetres under <see cref="WeatherUnits.Metric"/>, inches under
/// <see cref="WeatherUnits.Imperial"/> — or <see langword="null"/> when unreported.
/// </param>
/// <param name="SnowfallSum">
/// Total snowfall. <b>Centimetres</b> under <see cref="WeatherUnits.Metric"/> — Open-Meteo
/// reports <c>snowfall_sum</c> in cm while <c>precipitation_sum</c> is in mm, confirmed
/// against the live API on 2026-10-01 — and inches under <see cref="WeatherUnits.Imperial"/>,
/// which <c>precipitation_unit=inch</c> converts along with the rain total.
/// </param>
/// <param name="WindSpeedMax">The day's peak mean wind speed, or <see langword="null"/>.</param>
/// <param name="WindGustsMax">The day's peak gust, or <see langword="null"/>.</param>
/// <param name="Sunrise">
/// Sunrise on the location's own clock, <c>"HH:mm"</c>, for the same reason
/// <see cref="WeatherHour.Time"/> is a string. <see langword="null"/> where the sun does not
/// rise — Homon is a generic product and a polar installation is a legitimate one.
/// </param>
/// <param name="Sunset">Sunset, same shape and same caveat as <paramref name="Sunrise"/>.</param>
public sealed record WeatherDay(
    DateOnly Date,
    WeatherCondition Condition,
    double High,
    double Low,
    double? PrecipitationSum,
    double? SnowfallSum,
    double? WindSpeedMax,
    double? WindGustsMax,
    string? Sunrise,
    string? Sunset);
