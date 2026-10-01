using Homon.Domain.Auth;
using Homon.Domain.Messaging;
using Homon.Infrastructure.Messaging;
using Homon.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Homon.Api.Tests;

/// <summary>
/// Exercises <see cref="MessageRetentionService.SweepAsync"/> directly with a
/// <see cref="FixedTimeProvider"/>, never through the hosted loop — the
/// <see cref="ProbeObservationRetentionServiceTests"/> shape.
/// </summary>
public class MessageRetentionServiceTests(ApiDatabaseFactory factory) : IClassFixture<ApiDatabaseFactory>
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task SweepAsync_deletes_only_messages_older_than_the_retention_window()
    {
        var reporter = await SeedReporterAsync();

        await AddMessagesAsync(
            reporter,
            Now - TimeSpan.FromDays(40),
            Now - TimeSpan.FromDays(33),
            Now - TimeSpan.FromDays(1));

        Assert.Equal(2, await BuildService().SweepAsync(CancellationToken.None));

        var remaining = await RemainingAsync(reporter);
        Assert.Single(remaining);
        Assert.Equal(Now - TimeSpan.FromDays(1), remaining[0].ReceivedAt);
    }

    [DatabaseFact]
    public async Task A_silent_reporters_last_message_is_never_deleted_however_old_it_is()
    {
        // The case the whole module exists for. Deleting this row would take NextExpectedAt with
        // it and turn a red "overdue by 40 days" into a grey "no report received yet" — see
        // plan 021's Decision 15.
        var reporter = await SeedReporterAsync();

        await AddMessagesAsync(
            reporter,
            Now - TimeSpan.FromDays(60),
            Now - TimeSpan.FromDays(50),
            Now - TimeSpan.FromDays(40));

        Assert.Equal(2, await BuildService().SweepAsync(CancellationToken.None));

        var remaining = await RemainingAsync(reporter);
        Assert.Single(remaining);
        Assert.Equal(Now - TimeSpan.FromDays(40), remaining[0].ReceivedAt);
        Assert.NotNull(remaining[0].NextExpectedAt);
    }

    [DatabaseFact]
    public async Task A_reporter_whose_only_message_is_ancient_keeps_it_and_nothing_is_deleted()
    {
        var reporter = await SeedReporterAsync();

        await AddMessagesAsync(reporter, Now - TimeSpan.FromDays(400));

        Assert.Equal(0, await BuildService().SweepAsync(CancellationToken.None));
        Assert.Single(await RemainingAsync(reporter));
    }

    [DatabaseFact]
    public async Task The_keep_rule_is_per_reporter_not_global()
    {
        // A chatty reporter's fresh messages must not license deleting a quiet one's last word.
        var chatty = await SeedReporterAsync();
        var quiet = await SeedReporterAsync();

        await AddMessagesAsync(chatty, Now - TimeSpan.FromDays(40), Now - TimeSpan.FromHours(1));
        await AddMessagesAsync(quiet, Now - TimeSpan.FromDays(45));

        Assert.Equal(1, await BuildService().SweepAsync(CancellationToken.None));

        Assert.Single(await RemainingAsync(chatty));
        Assert.Single(await RemainingAsync(quiet));
    }

    [DatabaseFact]
    public async Task A_second_sweep_has_nothing_left_to_do()
    {
        var reporter = await SeedReporterAsync();

        await AddMessagesAsync(reporter, Now - TimeSpan.FromDays(40), Now - TimeSpan.FromDays(35));

        var service = BuildService();

        Assert.Equal(1, await service.SweepAsync(CancellationToken.None));
        Assert.Equal(0, await service.SweepAsync(CancellationToken.None));
    }

    private async Task<Reporter> SeedReporterAsync()
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();

        var name = $"reporter-{Guid.NewGuid():N}";
        var key = new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = name,
            TokenId = Guid.NewGuid().ToString("N")[..ApiKey.TokenIdLength].ToUpperInvariant(),
            SecretHash = [1, 2, 3],
            CreatedAt = Now,
            Scope = ApiKeyScope.ReadWrite,
        };

        var reporter = new Reporter
        {
            Id = Guid.NewGuid(),
            Identifier = ReporterIdentifier.New(),
            Name = name,
            NormalizedName = Reporter.Normalize(name),
            ApiKeyId = key.Id,
            CreatedAt = Now,
        };

        database.ApiKeys.Add(key);
        database.Reporters.Add(reporter);
        await database.SaveChangesAsync();

        return reporter;
    }

    private async Task AddMessagesAsync(Reporter reporter, params DateTimeOffset[] receivedAt)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();

        foreach (var at in receivedAt)
        {
            database.Messages.Add(new Message
            {
                ReporterId = reporter.Id,
                ReceivedAt = at,
                Name = reporter.Name,
                Status = MessageStatus.Success,
                Category = Message.DefaultCategory,
                NextExpectedAt = at + TimeSpan.FromHours(25),
                ReportedByKeyId = reporter.ApiKeyId,
            });
        }

        await database.SaveChangesAsync();
    }

    private async Task<List<Message>> RemainingAsync(Reporter reporter)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();

        return await database.Messages
            .Where(m => m.ReporterId == reporter.Id)
            .OrderBy(m => m.ReceivedAt)
            .ToListAsync();
    }

    private MessageRetentionService BuildService() =>
        new(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor<MessagingOptions>(new MessagingOptions()),
            new FixedTimeProvider(Now),
            NullLogger<MessageRetentionService>.Instance);
}
