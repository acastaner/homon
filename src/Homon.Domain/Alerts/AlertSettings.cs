namespace Homon.Domain.Alerts;

/// <summary>
/// How alert email is sent and to whom. The table holds at most one row, always at
/// <see cref="SingletonId"/>, exactly as <c>WeatherSettings</c> does — the Alerts admin page is
/// the only way it is written (plan 026, Decision 3).
/// </summary>
public sealed class AlertSettings
{
    /// <summary>Most addresses <see cref="Recipients"/> may hold.</summary>
    public const int MaxRecipients = 10;

    /// <summary>The only id an <see cref="AlertSettings"/> row is ever stored at.</summary>
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public Guid Id { get; set; } = SingletonId;

    /// <summary>Alerts are mailed only while this is true. Test emails are the exception (Decision 7).</summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// The Resend API key, protected through <c>ISecretProtector</c>. Write-only on the wire: the
    /// API reports only whether one is set.
    /// </summary>
    public string? ProtectedApiKey { get; set; }

    /// <summary>The address mail is sent from. It must be on a domain verified in Resend.</summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>The display name mail is sent under.</summary>
    public string FromName { get; set; } = "Homon";

    /// <summary>Who is mailed.</summary>
    public List<string> Recipients { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>True when there is a key, a sender and at least one recipient — enough to send.</summary>
    public bool IsReady => ProtectedApiKey is not null && FromAddress.Length > 0 && Recipients.Count > 0;
}
