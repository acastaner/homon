using Homon.Api.Authentication;
using Homon.Domain.Messaging;
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
/// dashboard. See plan 002's Decision 9. Also <c>GET /status/probes/{id}</c>: one probe's page
/// — state, uptime, bucketed latency for a range and its recent polls, never its configuration
/// (plan 023).
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

        parent.MapGet("/status/probes/{id:guid}", GetProbeHistoryAsync)
            .RequireAuthorization(HomonPolicies.Reader)
            .WithName("GetProbeHistory")
            .WithSummary("One probe's page: state, uptime, bucketed latency for a range, recent polls. Never its configuration.");

        return parent;
    }

    /// <summary>
    /// The one server-side allow-list of kinds whose observations carry a latency worth drawing:
    /// ping plots round-trip time and HTTP plots time to first byte (ARCHITECTURE.md §3.28, plan
    /// 022 D6). Every other kind plots nothing until its own plan opts it in — an explicit list,
    /// not "every kind with a latency". Shared by the dashboard sparkline and the probe page
    /// (plan 023, D3); the SPA keeps the twin in <c>lib/status.ts</c>.
    /// </summary>
    private static bool PlotsLatency(ProbeKind kind) => kind is ProbeKind.Ping or ProbeKind.Http;

    /// <summary>The probe page's three ranges (plan 023, D5): window, bucket count. Width = window / count.</summary>
    private static readonly Dictionary<string, (TimeSpan Window, int Buckets)> ProbeHistoryRanges =
        new(StringComparer.Ordinal)
        {
            ["24h"] = (TimeSpan.FromHours(24), 96),
            ["7d"] = (TimeSpan.FromDays(7), 168),
            ["30d"] = (TimeSpan.FromDays(30), 120),
        };

    private const string DefaultProbeHistoryRange = "24h";

    /// <summary>How many polls the probe page's table lists, newest first (plan 023, D4).</summary>
    private const int RecentObservationLimit = 50;

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

        var messagesByProbe = await BuildMessageSummariesAsync(database, probes, now, cancellationToken);

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
                sparklinesByProbe.GetValueOrDefault(p.Id, []),
                messagesByProbe.GetValueOrDefault(p.Id)))
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
    /// One probe's page (plan 023). Deliberately has no configuration: the host, path and
    /// credentials stay behind <c>GET /probes/{id}</c>, which is administrator-only. Buckets are
    /// built in memory from one probe's rows, as <see cref="BuildSparklinesAsync"/> does for all
    /// of them — translating the arithmetic to SQL (<c>date_bin</c>) was rejected as unproven in
    /// this Npgsql setup and not yet a measured cost (D6).
    /// </summary>
    private static async Task<Results<Ok<ProbeHistoryResponse>, NotFound, ValidationProblem>> GetProbeHistoryAsync(
        Guid id,
        string? range,
        HomonDbContext database,
        IOptionsMonitor<MonitoringOptions> options,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        range ??= DefaultProbeHistoryRange;

        if (!ProbeHistoryRanges.TryGetValue(range, out var shape))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["range"] = ["Range must be '24h', '7d' or '30d'."],
            });
        }

        var probe = await database.Probes
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (probe is null)
        {
            return TypedResults.NotFound();
        }

        var monitoring = options.CurrentValue;
        var now = timeProvider.GetUtcNow();

        // The header's uptime is the dashboard row's: the same retention window, the same
        // calculator, so the two numbers agree (D7).
        var uptimeWindowStart = now - TimeSpan.FromDays(monitoring.RetentionWindowDays);
        var uptimeCounts = await database.ProbeObservations
            .Where(o => o.ProbeId == id && o.ObservedAt >= uptimeWindowStart)
            .GroupBy(o => o.ProbeId)
            .Select(g => new { Total = g.Count(), Success = g.Count(o => o.Succeeded) })
            .FirstOrDefaultAsync(cancellationToken);

        var uptimePercent = uptimeCounts is null
            ? null
            : ProbeUptimeCalculator.Calculate(uptimeCounts.Success, uptimeCounts.Total);

        var windowStart = now - shape.Window;
        var bucketWidth = shape.Window / shape.Buckets;

        var rows = await database.ProbeObservations
            .AsNoTracking()
            .Where(o => o.ProbeId == id && o.ObservedAt >= windowStart)
            .Select(o => new { o.ObservedAt, o.Succeeded, o.LatencyMs })
            .ToListAsync(cancellationToken);

        var plots = PlotsLatency(probe.Kind);
        var byBucket = rows
            .GroupBy(o => Math.Clamp((int)((o.ObservedAt - windowStart) / bucketWidth), 0, shape.Buckets - 1))
            .ToDictionary(g => g.Key, g => g.ToList());

        var latency = new LatencyBucketResponse[shape.Buckets];

        for (var i = 0; i < shape.Buckets; i++)
        {
            var start = windowStart + (bucketWidth * i);

            if (!byBucket.TryGetValue(i, out var inBucket))
            {
                latency[i] = new LatencyBucketResponse(start, null, 0, 0);
                continue;
            }

            double? average = null;

            if (plots)
            {
                var latencies = inBucket
                    .Where(o => o.Succeeded && o.LatencyMs is not null)
                    .Select(o => o.LatencyMs!.Value)
                    .ToList();

                average = latencies.Count == 0 ? null : latencies.Average();
            }

            latency[i] = new LatencyBucketResponse(start, average, inBucket.Count, inBucket.Count(o => !o.Succeeded));
        }

        var totalPolls = rows.Count;
        var totalFailures = rows.Count(o => !o.Succeeded);
        var rangeUptime = totalPolls == 0
            ? null
            : ProbeUptimeCalculator.Calculate(totalPolls - totalFailures, totalPolls);

        var recent = (await database.ProbeObservations
                .AsNoTracking()
                .Where(o => o.ProbeId == id)
                .OrderByDescending(o => o.ObservedAt)
                .ThenByDescending(o => o.Id)
                .Take(RecentObservationLimit)
                .Select(o => new { o.ObservedAt, o.Succeeded, o.LatencyMs, o.Detail })
                .ToListAsync(cancellationToken))
            .Select(o => new ProbeObservationResponse(
                o.ObservedAt, o.Succeeded, plots ? o.LatencyMs : null, o.Detail))
            .ToArray();

        var message = (await BuildMessageSummariesAsync(database, [probe], now, cancellationToken))
            .GetValueOrDefault(probe.Id);

        return TypedResults.Ok(new ProbeHistoryResponse(
            probe.Id,
            probe.Name,
            probe.Kind,
            probe.Status,
            probe.LastDetail,
            probe.LastObservedAt,
            uptimePercent,
            range,
            windowStart,
            (int)bucketWidth.TotalSeconds,
            rangeUptime,
            latency,
            recent,
            message));
    }

    /// <summary>
    /// The newest message behind every <see cref="ProbeKind.Message"/> probe, as much of it as a
    /// reader may see. Three queries at most and none of them per probe; nothing at all when the
    /// household has no message probes.
    /// </summary>
    /// <remarks>
    /// The body is here only when its reporter's <see cref="MessageBodyVisibility"/> says so, and
    /// then capped at <see cref="ReaderBodyMaxLength"/> characters: this payload is polled every
    /// 30 seconds, and a 64 KiB command output on that interval is not a thing to put on a
    /// kitchen-counter dashboard. The full body has exactly one route, and it is
    /// administrator-only (plan 021, Decision 11).
    /// </remarks>
    private static async Task<Dictionary<Guid, ProbeMessageResponse>> BuildMessageSummariesAsync(
        HomonDbContext database,
        List<Probe> probes,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var identifiers = probes
            .Where(p => p.Kind == ProbeKind.Message)
            .Select(p => p.Host)
            .ToHashSet(StringComparer.Ordinal);

        if (identifiers.Count == 0)
        {
            return [];
        }

        var reporters = await database.Reporters
            .Where(r => identifiers.Contains(r.Identifier))
            .Select(r => new { r.Id, r.Identifier, r.BodyVisibility })
            .ToListAsync(cancellationToken);

        var reporterIds = reporters.Select(r => r.Id).ToList();

        var latestIds = await database.Messages
            .Where(m => reporterIds.Contains(m.ReporterId))
            .GroupBy(m => m.ReporterId)
            .Select(g => g.Max(m => m.Id))
            .ToListAsync(cancellationToken);

        var latest = await database.Messages
            .AsNoTracking()
            .Where(m => latestIds.Contains(m.Id))
            .Select(m => new { m.ReporterId, m.Status, m.Body, m.NextExpectedAt })
            .ToListAsync(cancellationToken);

        var byReporter = latest.ToDictionary(m => m.ReporterId);

        var byIdentifier = new Dictionary<string, ProbeMessageResponse>(StringComparer.Ordinal);

        foreach (var reporter in reporters)
        {
            if (!byReporter.TryGetValue(reporter.Id, out var message))
            {
                continue;
            }

            var body = reporter.BodyVisibility is MessageBodyVisibility.Reader ? message.Body : null;

            if (body is { Length: > ReaderBodyMaxLength })
            {
                body = body[..ReaderBodyMaxLength];
            }

            byIdentifier[reporter.Identifier] = new ProbeMessageResponse(
                message.Status,
                message.NextExpectedAt is { } due && now > due,
                body);
        }

        return probes
            .Where(p => p.Kind == ProbeKind.Message && byIdentifier.ContainsKey(p.Host))
            .ToDictionary(p => p.Id, p => byIdentifier[p.Host]);
    }

    /// <summary>
    /// How much of a reader-visible body travels on a payload the dashboard polls every 30
    /// seconds. A reporter meant for readers sends a line, not a log.
    /// </summary>
    private const int ReaderBodyMaxLength = 2_000;

    /// <summary>
    /// The 30-day (<see cref="MonitoringOptions.RetentionWindowDays"/>) window split into
    /// <paramref name="bucketCount"/> equal-width buckets; each point is the mean
    /// <c>LatencyMs</c> of successful observations in that bucket. <see cref="ProbeKind.Ping"/>
    /// probes plot round-trip time and <see cref="ProbeKind.Http"/> probes plot time to first
    /// byte (ARCHITECTURE.md §3.28). Every other kind gets an empty array until its own plan opts
    /// it in (plan 022, D6) — an explicit allow-list, not "every kind with a latency", because a
    /// message probe has none and SMB/SNMP have not decided what theirs would mean; the dashboard
    /// keeps the matching list (shared with the probe page through <see cref="PlotsLatency"/>). A
    /// bucket with no successful observations is omitted, not zero or null.
    /// </summary>
    private static async Task<Dictionary<Guid, double[]>> BuildSparklinesAsync(
        HomonDbContext database,
        List<Probe> probes,
        DateTimeOffset windowStart,
        TimeSpan windowLength,
        int bucketCount,
        CancellationToken cancellationToken)
    {
        var sparklineProbeIds = probes
            .Where(p => PlotsLatency(p.Kind))
            .Select(p => p.Id)
            .ToHashSet();

        if (sparklineProbeIds.Count == 0)
        {
            return [];
        }

        var successfulObservations = await database.ProbeObservations
            .Where(o => o.ObservedAt >= windowStart && o.Succeeded && o.LatencyMs != null
                && sparklineProbeIds.Contains(o.ProbeId))
            .Select(o => new { o.ProbeId, o.ObservedAt, o.LatencyMs })
            .ToListAsync(cancellationToken);

        var bucketWidth = windowLength / bucketCount;

        return successfulObservations
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
        DateTimeOffset? LastCheckedAt, double? UptimePercent, double[] Sparkline,
        ProbeMessageResponse? Message);

    /// <summary>
    /// Present only on a <see cref="ProbeKind.Message"/> probe whose reporter has reported at
    /// least once. The status and the overdue flag are what the dashboard's chip word needs — a
    /// reporter that claimed nothing reads "Reported", not "Succeeded" — and <c>Body</c> is null
    /// unless its reporter is reader-visible.
    /// </summary>
    public sealed record ProbeMessageResponse(MessageStatus Status, bool Overdue, string? Body);

    /// <summary>
    /// One probe's page. It has <b>no</b> <c>Host</c>, path, URL or credential, on purpose: a
    /// probe's host is an internal hostname or LAN address not fit for an anonymous reader (plan
    /// 002, Decision 8), and this route is Reader-gated. An administrator's configuration block
    /// comes from <c>GET /probes/{id}</c> instead; a caller-dependent shape here was rejected
    /// because a nullable block that appears for admins invites the next change to leak it
    /// (plan 023, D1).
    /// </summary>
    public sealed record ProbeHistoryResponse(
        Guid Id, string Name, ProbeKind Kind, ProbeStatus State, string? Detail,
        DateTimeOffset? LastCheckedAt, double? UptimePercent,
        string Range, DateTimeOffset WindowStart, int BucketSeconds, double? RangeUptimePercent,
        LatencyBucketResponse[] Latency, ProbeObservationResponse[] RecentObservations,
        ProbeMessageResponse? Message);

    /// <summary>
    /// One bucket of the chosen range. Every bucket is present, empty ones with
    /// <c>Polls == 0</c>, so the client never infers gaps. <c>AverageLatencyMs</c> is the mean of
    /// successful polls' latency, null when there is none or the kind does not plot latency.
    /// </summary>
    public sealed record LatencyBucketResponse(DateTimeOffset Start, double? AverageLatencyMs, int Polls, int Failures);

    /// <summary>
    /// One raw poll for the table. <c>Detail</c> is the same kind of string <c>/status</c> already
    /// sends readers for the latest poll (plan 023, D2).
    /// </summary>
    public sealed record ProbeObservationResponse(DateTimeOffset ObservedAt, bool Succeeded, double? LatencyMs, string? Detail);

    public sealed record ProbeGroupSummary(Guid Id, string Name, Guid[] ProbeIds);
}
