using System.Globalization;
using Homon.Domain.Monitoring;

namespace Homon.Domain.Messaging;

/// <summary>
/// Turns a reporter's newest message into one poll outcome. Pure — no I/O, no clock, the
/// <c>ProbeStateMachine</c> posture — so the whole table it implements is exercised without a
/// database or a fake clock.
/// </summary>
/// <remarks>
/// This is the seam plan 009 reads. It needs none of its own machinery for a message probe:
/// because the outcome below travels through <c>Probe.RecordObservation</c> like any other
/// poll, a reporter going overdue produces an ordinary probe transition, and 009's
/// <c>IProbeTransitionPublisher</c> covers it with no message-specific code.
/// </remarks>
public static class MessageProbeEvaluator
{
    /// <summary>
    /// The outcome for <paramref name="latest"/> as at <paramref name="now"/>. A null snapshot
    /// is a reporter that has never reported.
    /// </summary>
    public static MessageEvaluation Evaluate(MessageSnapshot? latest, DateTimeOffset now)
    {
        if (latest is null)
        {
            return new MessageEvaluation(false, ProbeStatus.Unknown, "message: no report received yet");
        }

        // Overdue is checked before the reported status and beats it. A successful report from
        // three days ago is not evidence about today, and the reporter promised otherwise.
        if (latest.NextExpectedAt is { } due && now > due)
        {
            return new MessageEvaluation(
                false,
                ProbeStatus.Down,
                $"message: overdue — none since {Stamp(latest.ReceivedAt)}, expected by {Stamp(due)}");
        }

        var reportedAt = Stamp(latest.ReceivedAt);

        return latest.Status switch
        {
            MessageStatus.Success => new MessageEvaluation(true, ProbeStatus.Up, $"message: success, reported {reportedAt}"),

            // Not Down: a warning is the reporter saying "look at this", not "this failed", and
            // the derived status keeps it there instead of letting repeated polls of the same
            // message accumulate a failure streak into Down.
            MessageStatus.Warning => new MessageEvaluation(false, ProbeStatus.Unstable, $"message: warning, reported {reportedAt}"),

            MessageStatus.Failure => new MessageEvaluation(false, ProbeStatus.Down, $"message: failure, reported {reportedAt}"),

            // It checked in and claimed nothing, which is all a heartbeat reporter ever does —
            // so it counts as a success for the uptime ratio and reads green.
            MessageStatus.None => new MessageEvaluation(true, ProbeStatus.Up, $"message: reported {reportedAt}"),

            // It checked in and cannot tell. ProbeStatus.Unknown is also the signal the
            // scheduler reads as "no verdict" and writes no observation for (plan 021, A1), so
            // this does not count either way towards uptime.
            MessageStatus.Unknown => new MessageEvaluation(true, ProbeStatus.Unknown, $"message: status unknown, reported {reportedAt}"),

            _ => new MessageEvaluation(true, ProbeStatus.Unknown, $"message: status unknown, reported {reportedAt}"),
        };
    }

    /// <summary>
    /// UTC to the minute. Short enough for a table cell, and unambiguous without a locale —
    /// the dashboard's own freshness banner already tells the reader its clock is the browser's
    /// (§3.20), so a probe detail must not pretend to local time.
    /// </summary>
    private static string Stamp(DateTimeOffset at) =>
        at.ToUniversalTime().ToString("yyyy-MM-dd HH:mm'Z'", CultureInfo.InvariantCulture);
}

/// <summary>
/// Everything <see cref="MessageProbeEvaluator"/> needs about a message, and deliberately
/// nothing else. It carries no name, description or body, so the detail strings it produces
/// <em>cannot</em> leak reporter free text onto the reader-facing payload — the rule is
/// structural rather than a promise (plan 021, D12 and A2).
/// </summary>
public sealed record MessageSnapshot(
    MessageStatus Status,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? NextExpectedAt);

/// <summary>One message probe poll's outcome.</summary>
/// <param name="Succeeded">Whether this counts as a good poll for the streak counters and the uptime ratio.</param>
/// <param name="Status">
/// The status to show. Supplied rather than derived from the streaks, because a push probe's
/// authority is the reporter's own verdict — see plan 021's Decision 5.
/// </param>
/// <param name="Detail">Status and timing words only; never reporter free text.</param>
public sealed record MessageEvaluation(bool Succeeded, ProbeStatus Status, string Detail);
