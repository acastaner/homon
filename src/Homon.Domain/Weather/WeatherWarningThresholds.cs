namespace Homon.Domain.Weather;

/// <summary>
/// The figures at which forecast weather becomes a <see cref="WeatherWarning"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Hard-coded on purpose.</b> The weather module has exactly one setting — the location in
/// <see cref="WeatherSettings"/> — and the alternative costs a migration, six admin fields and
/// their validation to let a household retune numbers it will set once and forget. It is the
/// same trade <c>docs/ARCHITECTURE.md</c> §3.19 already made in rejecting an
/// admin-configurable cache TTL. If the figures ever become a real complaint, the clean
/// follow-up is a <c>Weather:Warnings:*</c> configuration section, not a database column.
/// </para>
/// <para>
/// <b>The imperial figures are rounded, not converted.</b> 90 °F is not 32 °C; it is the round
/// number a Fahrenheit reader recognises as "hot", which is what a threshold is for. Do not
/// "correct" them to 89.6 °F.
/// </para>
/// <para>
/// | Kind | Source | Caution (metric) | Severe (metric) | Caution (imperial) | Severe (imperial) |
/// | --- | --- | --- | --- | --- | --- |
/// | Wind | hourly gust | 60 km/h | 90 km/h | 38 mph | 56 mph |
/// | Thunderstorm | hourly WMO code | code 95 | code 96 or 99 | same | same |
/// | Snow | daily snowfall | 1 cm | 5 cm | 0.4 in | 2 in |
/// | Rain | daily precipitation | 20 mm | 40 mm | 0.8 in | 1.6 in |
/// | Heat | daily high | 32 °C | 38 °C | 90 °F | 100 °F |
/// | Cold | daily low | −10 °C | −18 °C | 14 °F | 0 °F |
/// </para>
/// <para>
/// Snowfall is in centimetres under <see cref="WeatherUnits.Metric"/> while rain is in
/// millimetres — that asymmetry is Open-Meteo's, confirmed against the live API on 2026-10-01,
/// and these thresholds are already written in the unit it answers in.
/// </para>
/// <para>
/// <see cref="WeatherWarningKind.Cold"/> is the one kind whose test is <c>&lt;=</c>, not
/// <c>&gt;=</c>: a lower number is worse. <see cref="Trips"/> is the only place that
/// asymmetry lives, so no caller has to remember it.
/// </para>
/// </remarks>
public static class WeatherWarningThresholds
{
    /// <summary>
    /// The pair of figures for one <paramref name="kind"/> under one
    /// <paramref name="units"/> system. Thunderstorm has no figure — it is graded off the WMO
    /// code — and returns <c>(0, 0)</c>, which <see cref="Trips"/> never consults.
    /// </summary>
    public static (double Caution, double Severe) For(WeatherWarningKind kind, WeatherUnits units)
    {
        var imperial = units == WeatherUnits.Imperial;

        return kind switch
        {
            WeatherWarningKind.Wind => imperial ? (38d, 56d) : (60d, 90d),
            WeatherWarningKind.Snow => imperial ? (0.4d, 2d) : (1d, 5d),
            WeatherWarningKind.Rain => imperial ? (0.8d, 1.6d) : (20d, 40d),
            WeatherWarningKind.Heat => imperial ? (90d, 100d) : (32d, 38d),
            WeatherWarningKind.Cold => imperial ? (14d, 0d) : (-10d, -18d),
            WeatherWarningKind.Thunderstorm => (0d, 0d),
            _ => (0d, 0d),
        };
    }

    /// <summary>
    /// The severity <paramref name="value"/> reaches for <paramref name="kind"/>, or
    /// <see langword="null"/> when it reaches neither threshold. A
    /// <see langword="null"/> <paramref name="value"/> — a variable the model did not report —
    /// never trips anything (plan 020, D8).
    /// </summary>
    public static WeatherWarningSeverity? Trips(WeatherWarningKind kind, double? value, WeatherUnits units)
    {
        if (value is null)
        {
            return null;
        }

        var (caution, severe) = For(kind, units);

        // Cold is the inverted one: -18 °C is worse than -10 °C, so the comparison flips.
        if (kind == WeatherWarningKind.Cold)
        {
            return value <= severe ? WeatherWarningSeverity.Severe
                : value <= caution ? WeatherWarningSeverity.Caution
                : null;
        }

        return value >= severe ? WeatherWarningSeverity.Severe
            : value >= caution ? WeatherWarningSeverity.Caution
            : null;
    }
}
