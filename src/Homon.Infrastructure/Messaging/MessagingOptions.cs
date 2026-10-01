namespace Homon.Infrastructure.Messaging;

/// <summary>
/// The message gateway's one configuration section. Everything else about a reporter — its
/// schedule, its tolerance, what counts as failure — belongs to the reporter itself, which is
/// the whole point of plan 021.
/// </summary>
public sealed class MessagingOptions
{
    public const string SectionName = "Messaging";

    /// <summary>
    /// How long a message is kept. Thirty-two days rather than Monitoring's thirty: a calendar
    /// month plus slack, so a monthly reporter's previous message outlives a 31-day month. The
    /// two windows are independent on purpose — an observation is a measurement, a message is
    /// evidence.
    /// </summary>
    public int RetentionWindowDays { get; set; } = 32;

    /// <summary>How often the sweep runs. Hourly, like the observation sweep.</summary>
    public TimeSpan RetentionSweepInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// The kill switch. Set false for every test host, the same way
    /// <c>Monitoring:RetentionEnabled</c> is, so a sweep loop never runs against a factory's
    /// deliberately unreachable connection string.
    /// </summary>
    public bool RetentionEnabled { get; set; } = true;
}
