using Homon.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Homon.Infrastructure.Monitoring;

/// <summary>
/// An hourly sweep that deletes <c>ProbeObservation</c> rows older than the retention window.
/// One <c>DELETE … WHERE "ObservedAt" &lt; @cutoff</c>, no batching — see plan 002's Decision 6.
/// </summary>
public sealed partial class ProbeObservationRetentionService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<MonitoringOptions> options,
    TimeProvider timeProvider,
    ILogger<ProbeObservationRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (options.CurrentValue.RetentionEnabled)
            {
                try
                {
                    await SweepAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Shutting down — the loop's own check ends it, not this catch.
                }
                catch (Exception ex)
                {
                    // Same reasoning as ProbeScheduler.ExecuteAsync's catch: an unhandled
                    // exception here would stop the whole host (BackgroundServiceException
                    // Behavior defaults to StopHost), for a job that can simply try again next
                    // hour.
                    LogSweepFailed(logger, ex);
                }
            }

            await Task.Delay(options.CurrentValue.RetentionSweepInterval, timeProvider, stoppingToken);
        }
    }

    internal async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        var cutoff = timeProvider.GetUtcNow() - TimeSpan.FromDays(options.CurrentValue.RetentionWindowDays);

        return await database.ProbeObservations
            .Where(o => o.ObservedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    [LoggerMessage(EventId = 2100, Level = LogLevel.Error, Message = "The retention sweep failed.")]
    private static partial void LogSweepFailed(ILogger logger, Exception exception);
}
