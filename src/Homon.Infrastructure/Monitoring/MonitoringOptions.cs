namespace Homon.Infrastructure.Monitoring;

/// <summary>
/// The scheduler and retention job's tuning knobs. Environment-configurable, not admin-UI
/// configurable this phase — see plan 002's "Deferred" note.
/// </summary>
public sealed class MonitoringOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Monitoring";

    /// <summary>
    /// How often <see cref="ProbeScheduler"/> wakes to scan for due probes. Read fresh every
    /// tick from <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>.
    /// </summary>
    public TimeSpan TickInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The most probes <see cref="ProbeScheduler"/> polls at once, across every kind.</summary>
    public int MaxConcurrentPolls { get; set; } = 8;

    /// <summary>
    /// Wraps every runner call regardless of kind, protecting the scheduler from a runner
    /// that hangs entirely — a kind's own runner may still time out sooner internally.
    /// </summary>
    public TimeSpan PerPollTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How often <see cref="ProbeObservationRetentionService"/> sweeps old observations.</summary>
    public TimeSpan RetentionSweepInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How many days of <c>ProbeObservation</c> rows are retained.</summary>
    public int RetentionWindowDays { get; set; } = 30;

    /// <summary>How many equal-width buckets the ping sparkline's 30-day window is split into.</summary>
    public int SparklineBucketCount { get; set; } = 30;

    /// <summary>
    /// Whether <see cref="ProbeScheduler"/> ticks at all. Left <c>true</c> by default — the
    /// e2e suite runs it for real, with seeded probes paused for determinism — and set
    /// <c>false</c> in every xunit test factory (<c>HomonApiFactory</c>), which every
    /// <c>[DatabaseFact]</c> class shares, so no unit or API test ever sends real ICMP.
    /// </summary>
    public bool SchedulerEnabled { get; set; } = true;

    /// <summary>Whether <see cref="ProbeObservationRetentionService"/> sweeps at all. Same posture as <see cref="SchedulerEnabled"/>.</summary>
    public bool RetentionEnabled { get; set; } = true;
}
