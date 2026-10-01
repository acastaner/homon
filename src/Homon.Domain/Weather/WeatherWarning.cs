namespace Homon.Domain.Weather;

/// <summary>
/// What a <see cref="WeatherWarning"/> is about.
/// </summary>
/// <remarks>
/// <b>Declaration order is load-bearing.</b> It is the final tie-break when more warnings
/// qualify than <see cref="WeatherWarningEvaluator"/> may return — see that class's ordering
/// rules — so reordering these members changes which three banners a reader sees. It matches
/// the threshold table in <see cref="WeatherWarningThresholds"/>, most immediately dangerous
/// first. Serialised camelCase by the API's global <c>JsonStringEnumConverter</c>.
/// </remarks>
public enum WeatherWarningKind
{
    Wind,
    Thunderstorm,
    Snow,
    Rain,
    Heat,
    Cold,
}

/// <summary>
/// How bad a <see cref="WeatherWarning"/> is. Two levels, deliberately: they map onto the two
/// status colours the design already owns (<c>unstable</c> and <c>down</c> in
/// <c>docs/design-brief.md</c>), so a banner introduces no new hue. Declaration order is
/// ascending severity, which the evaluator's sort relies on.
/// </summary>
public enum WeatherWarningSeverity
{
    /// <summary>Orange. The SPA's word is "Caution".</summary>
    Caution,

    /// <summary>Red. The SPA's word is "Severe".</summary>
    Severe,
}

/// <summary>
/// Weather worth changing plans over, <b>derived by Homon</b> from thresholds applied to the
/// forecast — see <see cref="WeatherWarningThresholds"/>. Open-Meteo publishes no warnings
/// endpoint, so this is never a relayed official advisory and no copy anywhere may suggest it
/// is (plan 020, D2).
/// </summary>
/// <remarks>
/// Carries no English. The SPA composes every sentence from these fields, the way
/// <c>WEATHER_CONDITION_LABEL</c> in <c>lib/weather.ts</c> already owns the condition words —
/// a server that renders copy is a server that has to be redeployed to fix a typo.
/// </remarks>
/// <param name="Kind">What the warning is about.</param>
/// <param name="Severity">How bad.</param>
/// <param name="Value">
/// The figure that tripped the threshold, in the settings' own units, or
/// <see langword="null"/> for a kind with no figure (<see cref="WeatherWarningKind.Thunderstorm"/>).
/// </param>
/// <param name="Date">The day the warning applies to, in the location's own timezone.</param>
/// <param name="FromTime">
/// Start on the location's clock, <c>"HH:mm"</c>, or <see langword="null"/> for a whole-day
/// warning derived from a daily aggregate.
/// </param>
/// <param name="ToTime">End, same shape and same null meaning as <paramref name="FromTime"/>.</param>
public sealed record WeatherWarning(
    WeatherWarningKind Kind,
    WeatherWarningSeverity Severity,
    double? Value,
    DateOnly Date,
    string? FromTime,
    string? ToTime);
