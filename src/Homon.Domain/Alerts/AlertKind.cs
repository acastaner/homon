namespace Homon.Domain.Alerts;

/// <summary>What an <see cref="AlertNotification"/> announces.</summary>
public enum AlertKind
{
    /// <summary>A probe was declared Down.</summary>
    Down,

    /// <summary>A probe was declared Up again after an outage.</summary>
    Up,

    /// <summary>The administrator asked for a test email from the Alerts page.</summary>
    Test,
}
