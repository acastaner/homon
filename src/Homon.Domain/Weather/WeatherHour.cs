namespace Homon.Domain.Weather;

/// <summary>
/// One hourly row of a forecast, in the <see cref="WeatherSettings"/>' own units. Lives in the
/// domain rather than beside <c>WeatherForecast</c> in the infrastructure project because
/// <see cref="WeatherWarningEvaluator"/> consumes it, and the domain may not depend upwards.
/// </summary>
/// <param name="Date">The hour's calendar date in the location's own timezone.</param>
/// <param name="Time">
/// The hour on the location's own clock, <c>"HH:mm"</c>. A string, not a
/// <see cref="DateTimeOffset"/>: Open-Meteo is asked with <c>timezone=auto</c> and so already
/// answers in the location's zone, and re-parsing that instant in the browser would
/// re-interpret it in the *reader's* zone — the same trap <c>formatForecastDay</c> in
/// <c>dashboard-page.tsx</c> documents for calendar dates. See plan 020, D7.
/// </param>
/// <param name="Condition">The simplified condition word.</param>
/// <param name="WmoCode">
/// The raw WMO code behind <paramref name="Condition"/>. Kept because
/// <see cref="WeatherCondition.Thunderstorm"/> flattens 95 (thunderstorm) together with 96
/// and 99 (thunderstorm with hail), and the warning evaluator grades those differently. It
/// does not cross the wire.
/// </param>
/// <param name="Temperature">Temperature at the hour, in the settings' units.</param>
/// <param name="ApparentTemperature">"Feels like" temperature, in the settings' units.</param>
/// <param name="WindSpeed">Mean wind speed, in the settings' units.</param>
/// <param name="WindGusts">
/// Peak gust, in the settings' units, or <see langword="null"/> when the model does not report
/// one. A null never trips a warning (plan 020, D8).
/// </param>
/// <param name="PrecipitationProbability">
/// Chance of precipitation, 0–100, or <see langword="null"/> when the model does not report one.
/// </param>
public sealed record WeatherHour(
    DateOnly Date,
    string Time,
    WeatherCondition Condition,
    int WmoCode,
    double Temperature,
    double ApparentTemperature,
    double WindSpeed,
    double? WindGusts,
    int? PrecipitationProbability);
