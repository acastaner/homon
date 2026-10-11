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
/// a per-probe timer. See plan 002's Decision 3 for the rejected alternative and why. The poll
/// itself is <see cref="ProbePoller"/>'s, shared with "Poll now" (plan 027); this class only
/// decides which probes are due.
/// </summary>
public sealed partial class ProbeScheduler(
    IServiceScopeFactory scopeFactory,
    ProbePoller poller,
    IOptionsMonitor<MonitoringOptions> options,
    TimeProvider timeProvider,
    ILogger<ProbeScheduler> logger) : BackgroundService
{
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

        // The poller's in-flight set, not this query, is what stops a probe still mid-poll — from
        // the previous tick or from "Poll now" — being polled twice at once: its LastObservedAt is
        // only written when that poll finishes, so it still reads as due here. Such a probe takes a
        // gate slot only for the instant PollAsync needs to return AlreadyPolling.
        var tasks = due.Select(async id =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                await poller.PollAsync(id, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    [LoggerMessage(EventId = 2002, Level = LogLevel.Error, Message = "The scheduler's tick failed.")]
    private static partial void LogTickFailed(ILogger logger, Exception exception);
}
