using Homon.Api.Authentication;
using Homon.Domain.Monitoring;
using Homon.Infrastructure.Monitoring;
using Homon.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Homon.Api.Endpoints;

/// <summary>
/// <c>GET /status</c>: the dashboard's read model — totals, every probe, non-empty groups,
/// the ungrouped ids. <see cref="HomonPolicies.Reader"/>-gated, like the rest of the
/// dashboard. See plan 002's Decision 9.
/// </summary>
internal static class StatusEndpoints
{
    internal static RouteGroupBuilder MapStatusEndpoints(this RouteGroupBuilder parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        parent.MapGet("/status", GetStatusAsync)
            .RequireAuthorization(HomonPolicies.Reader)
            .WithName("GetStatus")
            .WithSummary("The dashboard's read model: totals, every probe, groups.");

        return parent;
    }

    private static async Task<Ok<StatusResponse>> GetStatusAsync(
        HomonDbContext database,
        IOptionsMonitor<MonitoringOptions> options,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var monitoring = options.CurrentValue;
        var now = timeProvider.GetUtcNow();
        var windowStart = now - TimeSpan.FromDays(monitoring.RetentionWindowDays);

        var probes = await database.Probes
            .OrderBy(p => p.Position)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

        // One grouped query for every probe's observation counts in the retained window —
        // not one query per probe, which would turn this endpoint into an N+1 the moment the
        // household has more than a handful of probes.
        var uptimeCounts = await database.ProbeObservations
            .Where(o => o.ObservedAt >= windowStart)
            .GroupBy(o => o.ProbeId)
            .Select(g => new { ProbeId = g.Key, Total = g.Count(), Success = g.Count(o => o.Succeeded) })
            .ToDictionaryAsync(row => row.ProbeId, row => (row.Total, row.Success), cancellationToken);

        var perProbeUptime = probes.ToDictionary(
            p => p.Id,
            p => uptimeCounts.TryGetValue(p.Id, out var counts)
                ? ProbeUptimeCalculator.Calculate(counts.Success, counts.Total)
                : null);

        var sparklinesByProbe = await BuildSparklinesAsync(
            database, probes, windowStart, now - windowStart, monitoring.SparklineBucketCount, cancellationToken);

        var totals = new StatusTotals(
            Up: probes.Count(p => p.Status == ProbeStatus.Up),
            Unstable: probes.Count(p => p.Status == ProbeStatus.Unstable),
            Down: probes.Count(p => p.Status == ProbeStatus.Down),
            Unknown: probes.Count(p => p.Status == ProbeStatus.Unknown),
            Paused: probes.Count(p => p.Status == ProbeStatus.Paused),
            UptimePercent: AverageUptime(perProbeUptime.Values));

        var probeResponses = probes
            .Select(p => new ProbeStatusResponse(
                p.Id,
                p.Name,
                p.Kind,
                p.Status,
                p.LastDetail,
                p.LastObservedAt,
                perProbeUptime.GetValueOrDefault(p.Id),
                sparklinesByProbe.GetValueOrDefault(p.Id, [])))
            .ToArray();

        var groups = await database.ProbeGroups
            .Include(g => g.Members.OrderBy(m => m.Position))
            .OrderBy(g => g.Position)
            .ThenBy(g => g.Id)
            .ToListAsync(cancellationToken);

        var groupedProbeIds = groups
            .SelectMany(g => g.Members.Select(m => m.ProbeId))
            .ToHashSet();

        var groupSummaries = groups
            .Where(g => g.Members.Count > 0)
            .Select(g => new ProbeGroupSummary(g.Id, g.Name, g.Members.Select(m => m.ProbeId).ToArray()))
            .ToArray();

        var ungroupedProbeIds = probes
            .Where(p => !groupedProbeIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToArray();

        return TypedResults.Ok(new StatusResponse(totals, probeResponses, groupSummaries, ungroupedProbeIds, now));
    }

    /// <summary>
    /// The 30-day (<see cref="MonitoringOptions.RetentionWindowDays"/>) window split into
    /// <paramref name="bucketCount"/> equal-width buckets; each point is the mean
    /// <c>LatencyMs</c> of successful observations in that bucket, for <see cref="ProbeKind.Ping"/>
    /// probes only. A bucket with no successful observations is omitted, not zero or null.
    /// </summary>
    private static async Task<Dictionary<Guid, double[]>> BuildSparklinesAsync(
        HomonDbContext database,
        List<Probe> probes,
        DateTimeOffset windowStart,
        TimeSpan windowLength,
        int bucketCount,
        CancellationToken cancellationToken)
    {
        var pingProbeIds = probes
            .Where(p => p.Kind == ProbeKind.Ping)
            .Select(p => p.Id)
            .ToHashSet();

        if (pingProbeIds.Count == 0)
        {
            return [];
        }

        var successfulPingObservations = await database.ProbeObservations
            .Where(o => o.ObservedAt >= windowStart && o.Succeeded && o.LatencyMs != null
                && pingProbeIds.Contains(o.ProbeId))
            .Select(o => new { o.ProbeId, o.ObservedAt, o.LatencyMs })
            .ToListAsync(cancellationToken);

        var bucketWidth = windowLength / bucketCount;

        return successfulPingObservations
            .GroupBy(o => o.ProbeId)
            .ToDictionary(
                probeGroup => probeGroup.Key,
                probeGroup => probeGroup
                    .GroupBy(o => Math.Clamp((int)((o.ObservedAt - windowStart) / bucketWidth), 0, bucketCount - 1))
                    .OrderBy(bucket => bucket.Key)
                    .Select(bucket => bucket.Average(o => o.LatencyMs!.Value))
                    .ToArray());
    }

    private static double? AverageUptime(IEnumerable<double?> values)
    {
        var eligible = values.Where(v => v is not null).Select(v => v!.Value).ToArray();

        return eligible.Length == 0 ? null : Math.Round(eligible.Average(), 2);
    }

    public sealed record StatusResponse(
        StatusTotals Totals,
        ProbeStatusResponse[] Probes,
        ProbeGroupSummary[] Groups,
        Guid[] UngroupedProbeIds,
        DateTimeOffset GeneratedAt);

    public sealed record StatusTotals(
        int Up, int Unstable, int Down, int Unknown, int Paused, double? UptimePercent);

    public sealed record ProbeStatusResponse(
        Guid Id, string Name, ProbeKind Kind, ProbeStatus State, string? Detail,
        DateTimeOffset? LastCheckedAt, double? UptimePercent, double[] Sparkline);

    public sealed record ProbeGroupSummary(Guid Id, string Name, Guid[] ProbeIds);
}
