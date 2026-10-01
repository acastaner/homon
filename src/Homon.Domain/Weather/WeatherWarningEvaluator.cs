namespace Homon.Domain.Weather;

/// <summary>
/// Turns a forecast window into the handful of advisories worth putting a banner on the
/// weather page for.
/// </summary>
/// <remarks>
/// <para>
/// Pure: it reads no clock and does no slicing. The caller hands it the window it already
/// decided on — <c>WeatherEndpoints.GetWeatherAsync</c> passes the next 48 hours and the days
/// those hours fall in — which is what keeps this unit-testable without any infrastructure, in
/// the same spirit as <see cref="WmoWeatherCodeMap"/> and <c>ProbeUptimeCalculator</c>.
/// </para>
/// <para>
/// Open-Meteo publishes no warnings endpoint, so every advisory here is Homon's own reading of
/// the forecast against <see cref="WeatherWarningThresholds"/>. See plan 020, D2.
/// </para>
/// </remarks>
public static class WeatherWarningEvaluator
{
    /// <summary>
    /// How many advisories a reader is shown. Three: the page's whole point is the tables
    /// below the banners, and a stormy week can trip every kind at once. Surplus advisories
    /// are dropped silently — there is deliberately no "and 2 more" affordance.
    /// </summary>
    public const int MaxWarnings = 3;

    /// <summary>
    /// The advisories for <paramref name="hours"/> and <paramref name="days"/>, severest
    /// first, at most <see cref="MaxWarnings"/>. Empty — never <see langword="null"/> — when
    /// nothing trips.
    /// </summary>
    /// <remarks>
    /// At most one advisory per <see cref="WeatherWarningKind"/>: a six-hour gale is one
    /// banner, not six. The order is total, because the tests pin it — severity descending,
    /// then earliest start, then <see cref="WeatherWarningKind"/>'s own declaration order.
    /// </remarks>
    /// <param name="days">The days the window covers. Supplies the accumulation and extreme-temperature kinds.</param>
    /// <param name="hours">The hourly window, contiguous and in ascending time. Supplies the gust and thunderstorm kinds.</param>
    /// <param name="units">The unit system the figures are in, which selects the thresholds.</param>
    public static IReadOnlyList<WeatherWarning> Evaluate(
        IReadOnlyList<WeatherDay> days, IReadOnlyList<WeatherHour> hours, WeatherUnits units)
    {
        ArgumentNullException.ThrowIfNull(days);
        ArgumentNullException.ThrowIfNull(hours);

        var warnings = new List<WeatherWarning>();

        AddHourly(
            warnings, hours, WeatherWarningKind.Wind,
            hour => WeatherWarningThresholds.Trips(WeatherWarningKind.Wind, hour.WindGusts, units),
            hour => hour.WindGusts);

        AddHourly(warnings, hours, WeatherWarningKind.Thunderstorm, ThunderstormSeverity, _ => null);

        AddDaily(warnings, days, units, WeatherWarningKind.Snow, day => day.SnowfallSum);
        AddDaily(warnings, days, units, WeatherWarningKind.Rain, day => day.PrecipitationSum);
        AddDaily(warnings, days, units, WeatherWarningKind.Heat, day => day.High);
        AddDaily(warnings, days, units, WeatherWarningKind.Cold, day => day.Low);

        return warnings
            .OrderByDescending(warning => warning.Severity)
            .ThenBy(warning => warning.Date)
            .ThenBy(warning => warning.FromTime ?? "00:00", StringComparer.Ordinal)
            .ThenBy(warning => warning.Kind)
            .Take(MaxWarnings)
            .ToArray();
    }

    /// <summary>
    /// Collapses runs of consecutive qualifying hours and keeps the worst run. A tie on
    /// severity goes to the earliest run, since that is the one a reader needs first.
    /// </summary>
    /// <remarks>
    /// A run never spans midnight: <see cref="WeatherWarning"/> carries one
    /// <see cref="WeatherWarning.Date"/>, and a run from 22:00 to 02:00 described as
    /// "22:00 to 02:00" on the first day would read as a four-hour window in the wrong
    /// direction. Breaking at the date boundary splits it in two, and only one survives
    /// anyway.
    /// </remarks>
    private static void AddHourly(
        List<WeatherWarning> warnings,
        IReadOnlyList<WeatherHour> hours,
        WeatherWarningKind kind,
        Func<WeatherHour, WeatherWarningSeverity?> severityOf,
        Func<WeatherHour, double?> figureOf)
    {
        WeatherWarning? worst = null;
        var index = 0;

        while (index < hours.Count)
        {
            if (severityOf(hours[index]) is null)
            {
                index++;
                continue;
            }

            var start = index;
            var severity = WeatherWarningSeverity.Caution;
            double? figure = null;

            while (index < hours.Count
                && hours[index].Date == hours[start].Date
                && severityOf(hours[index]) is { } hourSeverity)
            {
                if (hourSeverity > severity)
                {
                    severity = hourSeverity;
                }

                if (figureOf(hours[index]) is { } hourFigure && (figure is null || hourFigure > figure))
                {
                    figure = hourFigure;
                }

                index++;
            }

            var run = new WeatherWarning(
                kind, severity, figure, hours[start].Date, hours[start].Time, hours[index - 1].Time);

            if (worst is null || run.Severity > worst.Severity)
            {
                worst = run;
            }
        }

        if (worst is not null)
        {
            warnings.Add(worst);
        }
    }

    /// <summary>
    /// Keeps the worst day for a kind read off a daily aggregate. A tie goes to the earliest
    /// day, which is the one the loop is already holding.
    /// </summary>
    private static void AddDaily(
        List<WeatherWarning> warnings,
        IReadOnlyList<WeatherDay> days,
        WeatherUnits units,
        WeatherWarningKind kind,
        Func<WeatherDay, double?> figureOf)
    {
        WeatherWarning? worst = null;

        foreach (var day in days)
        {
            var figure = figureOf(day);

            if (WeatherWarningThresholds.Trips(kind, figure, units) is not { } severity)
            {
                continue;
            }

            var candidate = new WeatherWarning(kind, severity, figure, day.Date, null, null);

            if (worst is null || candidate.Severity > worst.Severity)
            {
                worst = candidate;
            }
        }

        if (worst is not null)
        {
            warnings.Add(worst);
        }
    }

    /// <summary>
    /// Thunderstorm severity off the raw WMO code, which
    /// <see cref="WeatherCondition.Thunderstorm"/> flattens: 95 is a thunderstorm, 96 and 99
    /// add hail.
    /// </summary>
    private static WeatherWarningSeverity? ThunderstormSeverity(WeatherHour hour) => hour.WmoCode switch
    {
        96 or 99 => WeatherWarningSeverity.Severe,
        95 => WeatherWarningSeverity.Caution,
        _ => null,
    };
}
