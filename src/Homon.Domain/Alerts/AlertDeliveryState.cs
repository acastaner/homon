namespace Homon.Domain.Alerts;

/// <summary>Where an <see cref="AlertNotification"/> is in the outbox (plan 026, Decision 2).</summary>
public enum AlertDeliveryState
{
    /// <summary>Waiting for the dispatcher, or waiting for its next retry.</summary>
    Pending,

    /// <summary>The email transport accepted it.</summary>
    Sent,

    /// <summary>Every attempt failed; <see cref="AlertNotification.LastError"/> says why.</summary>
    Failed,

    /// <summary>Deliberately not sent — alerts were off or not set up. The reason is in <see cref="AlertNotification.LastError"/>.</summary>
    Skipped,
}
