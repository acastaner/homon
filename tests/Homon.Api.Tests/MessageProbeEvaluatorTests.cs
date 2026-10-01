using Homon.Domain.Messaging;
using Homon.Domain.Monitoring;

namespace Homon.Api.Tests;

/// <summary>
/// Plan 021's Decision 6 table, row for row, plus the interactions it depends on. Pure — no
/// database, no clock — like <see cref="ProbeStateMachineTests"/>.
/// </summary>
public class MessageProbeEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_reporter_that_has_never_reported_is_unknown_and_counts_as_no_verdict()
    {
        var evaluation = MessageProbeEvaluator.Evaluate(null, Now);

        Assert.Equal(ProbeStatus.Unknown, evaluation.Status);
        Assert.False(evaluation.Succeeded);
        Assert.Equal("message: no report received yet", evaluation.Detail);
    }

    [Theory]
    [InlineData(MessageStatus.Success, true, ProbeStatus.Up)]
    [InlineData(MessageStatus.Warning, false, ProbeStatus.Unstable)]
    [InlineData(MessageStatus.Failure, false, ProbeStatus.Down)]
    [InlineData(MessageStatus.None, true, ProbeStatus.Up)]
    [InlineData(MessageStatus.Unknown, true, ProbeStatus.Unknown)]
    public void The_reported_status_decides_when_nothing_is_overdue(
        MessageStatus reported, bool expectedSucceeded, ProbeStatus expectedStatus)
    {
        var latest = new MessageSnapshot(reported, Now.AddHours(-1), Now.AddHours(24));

        var evaluation = MessageProbeEvaluator.Evaluate(latest, Now);

        Assert.Equal(expectedStatus, evaluation.Status);
        Assert.Equal(expectedSucceeded, evaluation.Succeeded);
    }

    [Theory]
    [InlineData(MessageStatus.Success)]
    [InlineData(MessageStatus.Warning)]
    [InlineData(MessageStatus.Failure)]
    [InlineData(MessageStatus.None)]
    [InlineData(MessageStatus.Unknown)]
    public void Overdue_beats_every_reported_status(MessageStatus reported)
    {
        // A success from three days ago is not evidence about today, and the reporter said so
        // itself by promising another message.
        var latest = new MessageSnapshot(reported, Now.AddDays(-3), Now.AddDays(-2));

        var evaluation = MessageProbeEvaluator.Evaluate(latest, Now);

        Assert.Equal(ProbeStatus.Down, evaluation.Status);
        Assert.False(evaluation.Succeeded);
        Assert.StartsWith("message: overdue — ", evaluation.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_message_with_no_deadline_is_never_overdue_however_old_it_is()
    {
        var latest = new MessageSnapshot(MessageStatus.Success, Now.AddYears(-2), NextExpectedAt: null);

        var evaluation = MessageProbeEvaluator.Evaluate(latest, Now);

        Assert.Equal(ProbeStatus.Up, evaluation.Status);
    }

    [Fact]
    public void The_deadline_itself_is_not_yet_overdue()
    {
        var latest = new MessageSnapshot(MessageStatus.Success, Now.AddHours(-25), Now);

        Assert.Equal(ProbeStatus.Up, MessageProbeEvaluator.Evaluate(latest, Now).Status);
        Assert.Equal(ProbeStatus.Down, MessageProbeEvaluator.Evaluate(latest, Now.AddTicks(1)).Status);
    }

    [Fact]
    public void Every_detail_names_a_utc_instant_to_the_minute()
    {
        var latest = new MessageSnapshot(
            MessageStatus.Success,
            new DateTimeOffset(2026, 10, 1, 4, 30, 0, TimeSpan.FromHours(2)),
            null);

        // 04:30+02:00 is 02:30Z — a probe detail must not pretend to a local clock (§3.20).
        Assert.Equal("message: success, reported 2026-10-01 02:30Z", MessageProbeEvaluator.Evaluate(latest, Now).Detail);
    }

    [Theory]
    [InlineData(MessageStatus.Success)]
    [InlineData(MessageStatus.Warning)]
    [InlineData(MessageStatus.Failure)]
    [InlineData(MessageStatus.None)]
    [InlineData(MessageStatus.Unknown)]
    public void No_detail_can_carry_reporter_free_text(MessageStatus reported)
    {
        // Structural, not a promise: MessageSnapshot has no name, description or body to leak
        // onto the reader-facing payload (Decision 12, A2). This test pins the shape so that
        // widening the snapshot later has to come past it.
        var properties = typeof(MessageSnapshot).GetProperties().Select(p => p.Name).ToArray();

        Assert.Equal(["Status", "ReceivedAt", "NextExpectedAt"], properties);
        Assert.DoesNotContain(
            "clockmaster",
            MessageProbeEvaluator.Evaluate(new MessageSnapshot(reported, Now, null), Now).Detail,
            StringComparison.OrdinalIgnoreCase);
    }
}
