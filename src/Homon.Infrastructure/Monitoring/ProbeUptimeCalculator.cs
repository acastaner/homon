namespace Homon.Infrastructure.Monitoring;

/// <summary>
/// Uptime, per probe: <c>successCount / totalCount</c> over <c>ProbeObservation</c> rows
/// within the retained window, as a percentage, rounded to two decimals; <c>null</c> when the
/// count is zero. See plan 002's Decision 5 for the rejected time-weighted alternative.
/// </summary>
public static class ProbeUptimeCalculator
{
    public static double? Calculate(int successCount, int totalCount) =>
        totalCount == 0 ? null : Math.Round(100.0 * successCount / totalCount, 2);
}
