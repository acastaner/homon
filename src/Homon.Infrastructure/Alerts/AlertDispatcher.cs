using Homon.Domain.Alerts;
using Homon.Infrastructure.Email;
using Homon.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Homon.Infrastructure.Alerts;

/// <summary>
/// Drains the <see cref="AlertNotification"/> outbox: every few seconds it sends each due
/// <c>Pending</c> row, retries a failed send with backoff, and prunes old finished rows
/// (plan 026, Decision 2).
/// </summary>
/// <remarks>
/// Rows are written by <c>ProbeScheduler</c> in the same save as the status change they
/// announce, so a restart between the save and the send loses nothing: the row is still
/// <c>Pending</c> and this service picks it up. *Rejected*: an in-memory channel — a Resend
/// outage or a restart would silently lose the mail, and the Alerts page could not show
/// delivery. Single-instance by design (§1); two dispatchers would race on Pending rows.
/// </remarks>
public sealed partial class AlertDispatcher(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<AlertOptions> options,
    TimeProvider timeProvider,
    ILogger<AlertDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(1);

    private DateTimeOffset? _lastPrunedAt;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (options.CurrentValue.DispatcherEnabled)
            {
                try
                {
                    await DispatchOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Shutting down — the loop's own check ends it, not this catch.
                }
                catch (Exception ex)
                {
                    // Same reasoning as ProbeScheduler.ExecuteAsync's catch: an unhandled
                    // exception would stop the whole host, for a job that can simply try
                    // again on the next pass.
                    LogDispatchFailed(logger, ex);
                }
            }

            await Task.Delay(options.CurrentValue.DispatchInterval, timeProvider, stoppingToken);
        }
    }

    /// <summary>One pass over the outbox. Returns how many rows it processed.</summary>
    internal async Task<int> DispatchOnceAsync(CancellationToken cancellationToken)
    {
        var opts = options.CurrentValue;

        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IAlertEmailSender>();

        var now = timeProvider.GetUtcNow();
        var settings = await database.AlertSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == AlertSettings.SingletonId, cancellationToken);

        var due = await database.AlertNotifications
            .Where(n => n.State == AlertDeliveryState.Pending && n.NextAttemptAt <= now)
            .OrderBy(n => n.OccurredAt)
            .ThenBy(n => n.Id)
            .Take(opts.BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var row in due)
        {
            await ProcessAsync(row, settings, sender, opts, now, cancellationToken);

            // One save per row, so one bad row never causes the others to be sent twice.
            await database.SaveChangesAsync(cancellationToken);
        }

        if (_lastPrunedAt is null || now - _lastPrunedAt >= PruneInterval)
        {
            _lastPrunedAt = now;
            var cutoff = now - TimeSpan.FromDays(opts.RetentionDays);
            await database.AlertNotifications
                .Where(n => n.OccurredAt < cutoff && n.State != AlertDeliveryState.Pending)
                .ExecuteDeleteAsync(cancellationToken);
        }

        return due.Count;
    }

    private async Task ProcessAsync(
        AlertNotification row,
        AlertSettings? settings,
        IAlertEmailSender sender,
        AlertOptions opts,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // A test is sent even while alerts are off, so the administrator can prove the key
        // before switching them on — but never when they are not set up (Decision 7).
        if (row.Kind != AlertKind.Test && settings?.IsEnabled != true)
        {
            row.State = AlertDeliveryState.Skipped;
            row.LastError = "Alerts were switched off.";
            return;
        }

        if (settings?.IsReady != true)
        {
            row.State = AlertDeliveryState.Skipped;
            row.LastError = "Alerts were not set up.";
            return;
        }

        try
        {
            await sender.SendAsync(
                AlertMessageBuilder.Build(row, settings.Recipients, opts.PublicBaseUrl),
                cancellationToken);

            row.State = AlertDeliveryState.Sent;
            row.SentAt = now;
            row.LastError = null;
        }
        catch (EmailSendException ex)
        {
            row.Attempts++;
            var reason = ex.InnerException?.Message ?? ex.Message;
            row.LastError = reason.Length > AlertNotification.LastErrorMaxLength ? reason[..AlertNotification.LastErrorMaxLength] : reason;

            if (row.Attempts >= opts.MaxAttempts)
            {
                row.State = AlertDeliveryState.Failed;
            }
            else
            {
                row.NextAttemptAt = now + TimeSpan.FromMinutes(Math.Pow(2, row.Attempts - 1));
            }

            // The count, never the addresses, and never the key.
            LogSendFailed(logger, row.Id, row.Kind, settings.Recipients.Count, row.Attempts);
        }
    }

    [LoggerMessage(EventId = 2300, Level = LogLevel.Error, Message = "The alert dispatch pass failed.")]
    private static partial void LogDispatchFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2301,
        Level = LogLevel.Warning,
        Message = "Alert {NotificationId} ({Kind}) could not be sent to {RecipientCount} recipient(s); attempt {Attempts}.")]
    private static partial void LogSendFailed(
        ILogger logger, long notificationId, AlertKind kind, int recipientCount, int attempts);
}
