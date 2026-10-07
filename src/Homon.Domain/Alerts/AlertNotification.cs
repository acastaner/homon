using Homon.Domain.Monitoring;

namespace Homon.Domain.Alerts;

/// <summary>
/// One alert email waiting to be sent, or the record of one that was — the outbox the
/// <c>AlertDispatcher</c> drains (plan 026, Decision 2). Written in the same save as the status
/// change it announces, so it is neither lost nor sent twice.
/// </summary>
public sealed class AlertNotification
{
    public long Id { get; set; }

    public AlertKind Kind { get; set; }

    /// <summary>The probe concerned; null for a test, and after the probe is deleted.</summary>
    public Guid? ProbeId { get; set; }

    /// <summary>The probe's name at the event — a snapshot, so it survives the probe's deletion.</summary>
    public string ProbeName { get; set; } = string.Empty;

    /// <summary>The probe's last detail line at the event.</summary>
    public string? Detail { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>When the outage this alert belongs to began. Null for a test.</summary>
    public DateTimeOffset? DownSince { get; set; }

    public AlertDeliveryState State { get; set; }

    public int Attempts { get; set; }

    /// <summary>The earliest the dispatcher may try this row again.</summary>
    public DateTimeOffset NextAttemptAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    /// <summary>Why the last attempt failed, or why the row was skipped. Never holds a secret.</summary>
    public string? LastError { get; set; }

    /// <summary>
    /// The outbox row for an outage change. <paramref name="downSinceBefore"/> is the probe's
    /// <see cref="Probe.DownSince"/> from before the observation, because a recovery clears it.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="change"/> is <see cref="ProbeOutageChange.None"/>.</exception>
    public static AlertNotification ForOutage(
        Probe probe, ProbeOutageChange change, DateTimeOffset? downSinceBefore, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(probe);

        var (kind, downSince) = change switch
        {
            ProbeOutageChange.WentDown => (AlertKind.Down, probe.DownSince),
            ProbeOutageChange.Recovered => (AlertKind.Up, downSinceBefore),
            _ => throw new ArgumentOutOfRangeException(nameof(change), change, "Only an outage change produces an alert."),
        };

        return new AlertNotification
        {
            Kind = kind,
            ProbeId = probe.Id,
            ProbeName = probe.Name,
            Detail = probe.LastDetail,
            OccurredAt = at,
            DownSince = downSince,
            State = AlertDeliveryState.Pending,
            NextAttemptAt = at,
        };
    }
}
