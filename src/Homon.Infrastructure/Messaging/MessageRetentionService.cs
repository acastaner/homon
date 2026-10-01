using Homon.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Homon.Infrastructure.Messaging;

/// <summary>
/// An hourly sweep that deletes <c>Message</c> rows older than the retention window —
/// structurally the same job as <c>ProbeObservationRetentionService</c>, with one exception
/// that is not tidiness but correctness: a reporter's <em>newest</em> message is never deleted,
/// however old it is.
/// </summary>
/// <remarks>
/// A reporter that has been silent for 33 days is the one case this whole module exists for.
/// Deleting its last message would take <c>NextExpectedAt</c> with it, and the probe watching it
/// would flip from a loud red "overdue by 33 days" to a quiet grey "no report received yet" —
/// from the noisiest state on the dashboard to the most reassuring one, exactly 32 days after the
/// thing died. See plan 021's Decision 15.
/// </remarks>
public sealed partial class MessageRetentionService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<MessagingOptions> options,
    TimeProvider timeProvider,
    ILogger<MessageRetentionService> logger) : BackgroundService
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
                    // As in ProbeObservationRetentionService: an unhandled exception here would
                    // stop the whole host (BackgroundServiceExceptionBehavior defaults to
                    // StopHost) over a job that can simply try again next hour.
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

        // The EXISTS clause is the keep-the-latest rule: delete an expired message only if
        // something newer survives for the same reporter. Id, a monotonic identity column, is
        // what makes "newer" exact rather than approximate — two messages can share a ReceivedAt.
        return await database.Messages
            .Where(m => m.ReceivedAt < cutoff)
            .Where(m => database.Messages.Any(newer => newer.ReporterId == m.ReporterId && newer.Id > m.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    [LoggerMessage(EventId = 2200, Level = LogLevel.Error, Message = "The message retention sweep failed.")]
    private static partial void LogSweepFailed(ILogger logger, Exception exception);
}
