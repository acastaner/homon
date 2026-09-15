using System.Collections.Concurrent;
using Homon.Domain.Monitoring;
using Homon.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Homon.Infrastructure.Monitoring;

/// <summary>
/// One tick-based <see cref="BackgroundService"/> that wakes on a fixed tick
/// (<see cref="MonitoringOptions.TickInterval"/>), queries the database each tick for probes
/// that are due, and dispatches each due probe's poll under a bounded-concurrency gate — not
/// a per-probe timer. See plan 002's Decision 3 for the rejected alternative and why.
/// </summary>
public sealed partial class ProbeScheduler(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<MonitoringOptions> options,
    TimeProvider timeProvider,
    ILogger<ProbeScheduler> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, byte> _inFlight = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (options.CurrentValue.SchedulerEnabled)
            {
                try
                {
                    await TickAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Shutting down — the loop's own check ends it, not this catch.
                }
                catch (Exception ex)
                {
                    // TickAsync's own probe-selection query (inside the try below) is NOT
                    // wrapped the way PollAndPersistAsync's runner call is — a transient
                    // Postgres blip here would otherwise be an unhandled exception out of
                    // ExecuteAsync, and BackgroundServiceExceptionBehavior defaults to
                    // StopHost: one bad tick would stop the whole API, not just monitoring.
                    // Log and try again next tick instead.
                    LogTickFailed(logger, ex);
                }
            }

            await Task.Delay(options.CurrentValue.TickInterval, timeProvider, stoppingToken);
        }
    }

    internal async Task TickAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        List<Guid> due;

        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
            due = await database.Probes
                .Where(p => !p.IsPaused)
                .Where(p => p.LastObservedAt == null || now - p.LastObservedAt >= p.PollInterval)
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);
        }

        using var gate = new SemaphoreSlim(options.CurrentValue.MaxConcurrentPolls);

        var tasks = due
            .Where(id => _inFlight.TryAdd(id, 0)) // never overlap a poll already running
            .Select(async id =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    await PollAndPersistAsync(id, cancellationToken);
                }
                finally
                {
                    gate.Release();
                    _inFlight.TryRemove(id, out _);
                }
            });

        await Task.WhenAll(tasks);
    }

    private async Task PollAndPersistAsync(Guid probeId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        var runners = scope.ServiceProvider.GetServices<IProbeRunner>();

        var probe = await database.Probes.FindAsync([probeId], cancellationToken);
        if (probe is null || probe.IsPaused)
        {
            return; // deleted or paused since the scan above
        }

        var runner = runners.FirstOrDefault(r => r.Kind == probe.Kind);
        if (runner is null)
        {
            LogNoRunnerForKind(logger, probe.Kind);
            return;
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

        // oldStatus and probe.Status (after RecordObservation) are both in scope here, right
        // before the save — this is the hook plan 009's IProbeTransitionPublisher attaches to.
        // Not read by anything yet, hence the discard: a reviewer of 009 should confirm it
        // did not move this read earlier or later in the method (see plan 002's Maintenance
        // notes) rather than removing the line outright.
        _ = probe.Status;
        probe.RecordObservation(result.Succeeded, result.LatencyMs, result.Detail, observedAt);
        database.ProbeObservations.Add(new ProbeObservation
        {
            ProbeId = probe.Id,
            ObservedAt = observedAt,
            Succeeded = result.Succeeded,
            LatencyMs = result.LatencyMs,
            Detail = result.Detail,
        });

        await database.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(EventId = 2000, Level = LogLevel.Warning, Message = "No IProbeRunner registered for probe kind {Kind}.")]
    private static partial void LogNoRunnerForKind(ILogger logger, ProbeKind kind);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Warning, Message = "Probe runner threw while polling probe {ProbeId}.")]
    private static partial void LogRunnerThrew(ILogger logger, Guid probeId, Exception exception);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Error, Message = "The scheduler's tick failed.")]
    private static partial void LogTickFailed(ILogger logger, Exception exception);
}
