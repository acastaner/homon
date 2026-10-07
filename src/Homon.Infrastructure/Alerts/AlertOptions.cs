namespace Homon.Infrastructure.Alerts;

/// <summary>
/// The alert dispatcher's tuning knobs. The things an administrator chooses — the key, the
/// sender, the recipients, the on/off switch — are not here: they are the <c>AlertSettings</c>
/// row (plan 026, Decision 3).
/// </summary>
public sealed class AlertOptions
{
    public const string SectionName = "Alerts";

    /// <summary>
    /// Whether <see cref="AlertDispatcher"/> loops at all. Left <c>true</c> by default — the e2e
    /// suite runs it for real, so a test email visibly turns Sent — and set <c>false</c> in the
    /// xunit factory, where tests drive <c>DispatchOnceAsync</c> by hand. Same posture as
    /// <c>MonitoringOptions.SchedulerEnabled</c>.
    /// </summary>
    public bool DispatcherEnabled { get; set; } = true;

    /// <summary>How long the dispatcher sleeps between looks at the outbox.</summary>
    public TimeSpan DispatchInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Attempts before a row is <c>Failed</c>. Backoff between them is 1, 2, 4, 8 minutes.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Days a finished row (anything not Pending) is kept.</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>The most rows one dispatch pass handles.</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>
    /// The browser-facing base URL used for links in the mail. Filled from the API host's
    /// <c>FrontEnd:PublicBaseUrl</c> in <c>Program.cs</c>, because Infrastructure cannot see
    /// the API project's <c>FrontEndOptions</c>.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;
}
