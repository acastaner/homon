using System.Collections.Concurrent;
using Homon.Domain.Alerts;
using Homon.Domain.Monitoring;
using Homon.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Homon.Infrastructure.Monitoring;

/// <summary>What became of one <see cref="ProbePoller.PollAsync"/> call.</summary>
public enum ProbePollOutcome
{
    /// <summary>The runner ran and its result was saved — whatever that result was.</summary>
    Polled,

    /// <summary>No probe has that id (or it was deleted before the poll began).</summary>
    NotFound,

    /// <summary>The probe is paused; a paused probe is never polled, scheduled or not.</summary>
    Paused,

    /// <summary>A poll of this probe is already running; this call did nothing.</summary>
    AlreadyPolling,

    /// <summary>No <see cref="IProbeRunner"/> is registered for the probe's kind.</summary>
    NoRunner,
}

/// <summary>
/// Polls one probe and saves what it saw: the runner, <see cref="Probe.RecordObservation"/>, the
/// observation row and any outage notification, in one save. <see cref="ProbeScheduler"/> calls
/// it for every due probe, and <c>POST /probes/{id}/poll</c> calls it when an administrator
/// presses "Poll now" (plan 027) — the same poll either way, so a manual one counts toward the
/// failure threshold and can send an alert exactly as a scheduled one would.
/// </summary>
/// <remarks>
/// A singleton because <see cref="_inFlight"/> must be one set across both callers: it is what
/// stops a manual poll and a scheduled poll of the same probe from overlapping, since the
/// database cannot tell (<c>LastObservedAt</c> is only written when a poll finishes).
/// <para>
/// Rejected: leaving the poll inside <see cref="ProbeScheduler"/>, registering the scheduler as a
/// singleton as well as a hosted service, and having the API call into it. That exposes the
/// scheduler's internals across assemblies and mixes deciding <em>when</em> to poll with
/// <em>how</em> to poll; the API only ever wants the second.
/// </para>
/// </remarks>
public sealed partial class ProbePoller(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<MonitoringOptions> options,
    TimeProvider timeProvider,
    ILogger<ProbePoller> logger)
{
    private readonly ConcurrentDictionary<Guid, byte> _inFlight = new();

    /// <summary>
    /// Polls <paramref name="probeId"/> now, unless a poll of it is already running — in which
    /// case this returns <see cref="ProbePollOutcome.AlreadyPolling"/> at once and never waits.
    /// </summary>
    public async Task<ProbePollOutcome> PollAsync(Guid probeId, CancellationToken cancellationToken)
    {
        if (!_inFlight.TryAdd(probeId, 0))
        {
            return ProbePollOutcome.AlreadyPolling;
        }

        try
        {
            return await PollAndPersistAsync(probeId, cancellationToken);
        }
        finally
        {
            _inFlight.TryRemove(probeId, out _);
        }
    }

    private async Task<ProbePollOutcome> PollAndPersistAsync(Guid probeId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        var runners = scope.ServiceProvider.GetServices<IProbeRunner>();

        var probe = await database.Probes.FindAsync([probeId], cancellationToken);
        if (probe is null)
        {
            return ProbePollOutcome.NotFound; // deleted since the scheduler's scan, or never existed
        }

        if (probe.IsPaused)
        {
            return ProbePollOutcome.Paused;
        }

        var runner = runners.FirstOrDefault(r => r.Kind == probe.Kind);
        if (runner is null)
        {
            LogNoRunnerForKind(logger, probe.Kind);
            return ProbePollOutcome.NoRunner;
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(options.CurrentValue.PerPollTimeout);

        ProbeResult result;
        try
        {
            result = await runner.RunAsync(probe, timeoutSource.Token);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            result = new ProbeResult(false, null, "probe timed out");
        }
        catch (Exception ex)
        {
            // A broken runner must never take the whole tick down with it — see Decision 3's
            // "survives runner exceptions" requirement.
            LogRunnerThrew(logger, probe.Id, ex);
            result = new ProbeResult(false, null, $"probe runner threw: {ex.GetType().Name}");
        }

        var observedAt = timeProvider.GetUtcNow();

        // The outage rule lives on the probe (Probe.DownSince, plan 026's D1). An outage change is
        // written as an AlertNotification in this same save, so the status change and the mail that
        // announces it commit together — the outbox AlertDispatcher drains (D2).
        var downSinceBefore = probe.DownSince;
        var outage = probe.RecordObservation(result.Succeeded, result.LatencyMs, result.Detail, observedAt, result.DerivedStatus);
        if (outage is not ProbeOutageChange.None)
        {
            database.AlertNotifications.Add(AlertNotification.ForOutage(probe, outage, downSinceBefore, observedAt));
        }

        // A derived Unknown is a runner saying "no verdict" — a message probe whose reporter has
        // never reported, or reported that it cannot tell (plan 021, A1). Recording an
        // observation for it would give uptime a denominator it has not earned and drag the
        // figure to 0.00% for a reporter that has simply not spoken yet, when §3.16 says a probe
        // with no observations reads "—". RecordObservation above still ran, so LastObservedAt
        // advanced and the due query will not re-dispatch this probe on every tick.
        if (result.DerivedStatus is not ProbeStatus.Unknown)
        {
            database.ProbeObservations.Add(new ProbeObservation
            {
                ProbeId = probe.Id,
                ObservedAt = observedAt,
                Succeeded = result.Succeeded,
                LatencyMs = result.LatencyMs,
                Detail = result.Detail,
            });
        }

        await database.SaveChangesAsync(cancellationToken);

        return ProbePollOutcome.Polled;
    }

    [LoggerMessage(EventId = 2000, Level = LogLevel.Warning, Message = "No IProbeRunner registered for probe kind {Kind}.")]
    private static partial void LogNoRunnerForKind(ILogger logger, ProbeKind kind);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Warning, Message = "Probe runner threw while polling probe {ProbeId}.")]
    private static partial void LogRunnerThrew(ILogger logger, Guid probeId, Exception exception);
}
