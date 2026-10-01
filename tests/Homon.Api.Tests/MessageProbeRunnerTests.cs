using Homon.Domain.Auth;
using Homon.Domain.Messaging;
using Homon.Domain.Monitoring;
using Homon.Infrastructure.Messaging;
using Homon.Infrastructure.Monitoring;
using Homon.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Homon.Api.Tests;

/// <summary>
/// The runner end to end against a real database: the derivation itself is pinned by
/// <see cref="MessageProbeEvaluatorTests"/>, so what these prove is that the right message is
/// picked and that the result is shaped the way the scheduler expects.
/// </summary>
public class MessageProbeRunnerTests(ApiDatabaseFactory factory) : IClassFixture<ApiDatabaseFactory>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task A_host_naming_no_reporter_reads_unknown_rather_than_throwing()
    {
        var probe = MessageProbe("NOSUCHREPORTER00");

        var result = await RunAsync(probe);

        Assert.Equal(ProbeStatus.Unknown, result.DerivedStatus);
        Assert.False(result.Succeeded);
        Assert.Equal("message: no reporter has this identifier", result.Detail);
    }

    [DatabaseFact]
    public async Task A_reporter_that_has_never_reported_reaches_no_verdict()
    {
        var reporter = await SeedReporterAsync();

        var result = await RunAsync(MessageProbe(reporter.Identifier));

        // ProbeStatus.Unknown is also what tells the scheduler to record no observation, so this
        // reporter's uptime stays an em dash rather than 0.00%.
        Assert.Equal(ProbeStatus.Unknown, result.DerivedStatus);
        Assert.Equal("message: no report received yet", result.Detail);
    }

    [DatabaseFact]
    public async Task The_newest_message_decides_and_carries_no_latency()
    {
        var reporter = await SeedReporterAsync();
        await AddMessageAsync(reporter, MessageStatus.Failure, Now.AddHours(-3), Now.AddHours(24));
        await AddMessageAsync(reporter, MessageStatus.Success, Now.AddHours(-1), Now.AddHours(24));

        var result = await RunAsync(MessageProbe(reporter.Identifier));

        Assert.Equal(ProbeStatus.Up, result.DerivedStatus);
        Assert.True(result.Succeeded);
        Assert.Null(result.LatencyMs);
    }

    [DatabaseFact]
    public async Task A_reporter_past_its_own_deadline_is_down()
    {
        var reporter = await SeedReporterAsync();
        await AddMessageAsync(reporter, MessageStatus.Success, Now.AddDays(-2), Now.AddDays(-1));

        var result = await RunAsync(MessageProbe(reporter.Identifier));

        Assert.Equal(ProbeStatus.Down, result.DerivedStatus);
        Assert.StartsWith("message: overdue — ", result.Detail, StringComparison.Ordinal);
    }

    [DatabaseFact]
    public async Task One_reporters_messages_never_decide_anothers()
    {
        var watched = await SeedReporterAsync();
        var other = await SeedReporterAsync();
        await AddMessageAsync(other, MessageStatus.Success, Now.AddMinutes(-1), Now.AddHours(24));

        var result = await RunAsync(MessageProbe(watched.Identifier));

        Assert.Equal(ProbeStatus.Unknown, result.DerivedStatus);
    }

    private static Probe MessageProbe(string identifier) => new()
    {
        Id = Guid.NewGuid(),
        Name = $"probe-{Guid.NewGuid():N}",
        Host = identifier,
        Kind = ProbeKind.Message,
        PollInterval = TimeSpan.FromMinutes(15),
        FailureThreshold = 1,
    };

    private async Task<ProbeResult> RunAsync(Probe probe)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();

        var runner = new MessageProbeRunner(database, new FixedTimeProvider(Now));

        return await runner.RunAsync(probe, CancellationToken.None);
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

    private async Task AddMessageAsync(
        Reporter reporter, MessageStatus status, DateTimeOffset receivedAt, DateTimeOffset? nextExpectedAt)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();

        database.Messages.Add(new Message
        {
            ReporterId = reporter.Id,
            ReceivedAt = receivedAt,
            Name = reporter.Name,
            Status = status,
            Category = Message.DefaultCategory,
            NextExpectedAt = nextExpectedAt,
            ReportedByKeyId = reporter.ApiKeyId,
        });

        await database.SaveChangesAsync();
    }
}
