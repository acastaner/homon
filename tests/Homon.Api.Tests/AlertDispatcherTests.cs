using Homon.Domain.Alerts;
using Homon.Infrastructure.Alerts;
using Homon.Infrastructure.Email;
using Homon.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Homon.Api.Tests;

/// <summary>
/// Drives <see cref="AlertDispatcher.DispatchOnceAsync"/> by hand against a real database, with
/// the factory's <see cref="RecordingEmailSender"/> as the transport (the hosted loop stays
/// disabled for every test host; see <c>HomonApiFactory</c>).
/// </summary>
public class AlertDispatcherTests(ApiDatabaseFactory factory) : IClassFixture<ApiDatabaseFactory>
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task A_pending_down_alert_is_sent_to_every_recipient()
    {
        await ResetAsync(enabled: true);
        var id = await SeedAsync(AlertKind.Down);

        var processed = await BuildDispatcher().DispatchOnceAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        var row = await LoadAsync(id);
        Assert.Equal(AlertDeliveryState.Sent, row.State);
        Assert.Equal(Now, row.SentAt);
        var message = Assert.Single(factory.Emails.Sent);
        Assert.Equal(["a@example.test", "b@example.test"], message.To);
        Assert.Equal("[Homon] NAS is down", message.Subject);
        Assert.Contains("NAS is down.", message.TextBody, StringComparison.Ordinal);
    }

    [DatabaseFact]
    public async Task With_alerts_off_a_down_alert_is_skipped_and_nothing_is_sent()
    {
        await ResetAsync(enabled: false);
        var id = await SeedAsync(AlertKind.Down);

        await BuildDispatcher().DispatchOnceAsync(CancellationToken.None);

        var row = await LoadAsync(id);
        Assert.Equal(AlertDeliveryState.Skipped, row.State);
        Assert.Contains("switched off", row.LastError, StringComparison.Ordinal);
        Assert.Empty(factory.Emails.Sent);
    }

    [DatabaseFact]
    public async Task A_test_is_sent_even_while_alerts_are_off()
    {
        await ResetAsync(enabled: false);
        var id = await SeedAsync(AlertKind.Test);

        await BuildDispatcher().DispatchOnceAsync(CancellationToken.None);

        Assert.Equal(AlertDeliveryState.Sent, (await LoadAsync(id)).State);
        Assert.Single(factory.Emails.Sent);
    }

    [DatabaseFact]
    public async Task Without_recipients_even_a_test_is_skipped_as_not_set_up()
    {
        await ResetAsync(enabled: true, recipients: []);
        var down = await SeedAsync(AlertKind.Down);
        var test = await SeedAsync(AlertKind.Test);

        await BuildDispatcher().DispatchOnceAsync(CancellationToken.None);

        foreach (var id in new[] { down, test })
        {
            var row = await LoadAsync(id);
            Assert.Equal(AlertDeliveryState.Skipped, row.State);
            Assert.Contains("not set up", row.LastError, StringComparison.Ordinal);
        }

        Assert.Empty(factory.Emails.Sent);
    }

    [DatabaseFact]
    public async Task A_failing_send_stays_pending_and_backs_off_one_minute()
    {
        await ResetAsync(enabled: true);
        var id = await SeedAsync(AlertKind.Down);
        factory.Emails.ThrowOnSend = new EmailSendException("boom");

        try
        {
            await BuildDispatcher().DispatchOnceAsync(CancellationToken.None);
        }
        finally
        {
            factory.Emails.ThrowOnSend = null;
        }

        var row = await LoadAsync(id);
        Assert.Equal(AlertDeliveryState.Pending, row.State);
        Assert.Equal(1, row.Attempts);
        Assert.Equal(Now.AddMinutes(1), row.NextAttemptAt);
        Assert.Equal("boom", row.LastError);
    }

    [DatabaseFact]
    public async Task The_last_failing_attempt_marks_the_row_failed()
    {
        await ResetAsync(enabled: true);
        var id = await SeedAsync(AlertKind.Down, attempts: 4);
        factory.Emails.ThrowOnSend = new EmailSendException("still boom");

        try
        {
            await BuildDispatcher().DispatchOnceAsync(CancellationToken.None);
        }
        finally
        {
            factory.Emails.ThrowOnSend = null;
        }

        var row = await LoadAsync(id);
        Assert.Equal(AlertDeliveryState.Failed, row.State);
        Assert.Equal(5, row.Attempts);
        Assert.Equal("still boom", row.LastError);
    }

    [DatabaseFact]
    public async Task A_row_that_is_not_due_yet_is_left_alone()
    {
        await ResetAsync(enabled: true);
        var id = await SeedAsync(AlertKind.Down, nextAttemptAt: Now.AddMinutes(3));

        var processed = await BuildDispatcher().DispatchOnceAsync(CancellationToken.None);

        Assert.Equal(0, processed);
        Assert.Equal(AlertDeliveryState.Pending, (await LoadAsync(id)).State);
        Assert.Empty(factory.Emails.Sent);
    }

    [DatabaseFact]
    public async Task Old_finished_rows_are_pruned_but_an_old_pending_row_is_not()
    {
        await ResetAsync(enabled: true);
        var old = Now.AddDays(-31);
        var sent = await SeedAsync(AlertKind.Down, occurredAt: old, state: AlertDeliveryState.Sent);
        var pending = await SeedAsync(AlertKind.Down, occurredAt: old, nextAttemptAt: Now.AddMinutes(30));

        await BuildDispatcher().DispatchOnceAsync(CancellationToken.None);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        Assert.False(await database.AlertNotifications.AnyAsync(n => n.Id == sent));
        Assert.True(await database.AlertNotifications.AnyAsync(n => n.Id == pending));
    }

    private AlertDispatcher BuildDispatcher() =>
        new(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor<AlertOptions>(new AlertOptions { PublicBaseUrl = "https://homon.test" }),
            new FixedTimeProvider(Now),
            NullLogger<AlertDispatcher>.Instance);

    private async Task ResetAsync(bool enabled, string[]? recipients = null)
    {
        factory.Emails.Clear();

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        await database.AlertNotifications.ExecuteDeleteAsync();
        await database.AlertSettings.ExecuteDeleteAsync();

        database.AlertSettings.Add(new AlertSettings
        {
            IsEnabled = enabled,
            ProtectedApiKey = "protected:x",
            FromAddress = "alerts@example.test",
            FromName = "Homon",
            Recipients = [.. recipients ?? ["a@example.test", "b@example.test"]],
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        await database.SaveChangesAsync();
    }

    private async Task<long> SeedAsync(
        AlertKind kind,
        int attempts = 0,
        DateTimeOffset? occurredAt = null,
        DateTimeOffset? nextAttemptAt = null,
        AlertDeliveryState state = AlertDeliveryState.Pending)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();

        var row = new AlertNotification
        {
            Kind = kind,
            ProbeId = null,
            ProbeName = kind == AlertKind.Test ? "Test alert" : "NAS",
            OccurredAt = occurredAt ?? Now.AddMinutes(-1),
            DownSince = kind == AlertKind.Test ? null : Now.AddMinutes(-1),
            State = state,
            Attempts = attempts,
            NextAttemptAt = nextAttemptAt ?? Now.AddMinutes(-1),
        };
        database.AlertNotifications.Add(row);
        await database.SaveChangesAsync();

        return row.Id;
    }

    private async Task<AlertNotification> LoadAsync(long id)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();

        return await database.AlertNotifications.AsNoTracking().SingleAsync(n => n.Id == id);
    }
}
