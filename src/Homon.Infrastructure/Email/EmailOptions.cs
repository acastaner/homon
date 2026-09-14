using System.ComponentModel.DataAnnotations;

namespace Homon.Infrastructure.Email;

/// <summary>Sender identity, recipients and API credentials for outbound alert email.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Resend API token. Absent in development, where email is logged instead.</summary>
    public string? ResendApiToken { get; set; }

    /// <summary>
    /// Envelope sender. Must be on a domain verified in Resend, or outbound mail is
    /// rejected; the default is a placeholder that a production deployment overrides
    /// through <c>Email__FromAddress</c>.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [EmailAddress]
    public string FromAddress { get; set; } = "homon@localhost";

    /// <summary>Display name shown beside <see cref="FromAddress"/>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string FromName { get; set; } = "Homon";

    /// <summary>
    /// Who receives an alert when a service turns unstable or down. Empty means alerts are
    /// composed and then dropped with a warning in the log — the alerting module (plan 009)
    /// is where an empty list becomes a startup refusal in Production.
    /// </summary>
    public string[] AlertRecipients { get; set; } = [];

    /// <summary>
    /// True when a real Resend token is configured. When false the application registers
    /// the logging sender instead, so development never sends live mail by accident.
    /// </summary>
    public bool IsResendConfigured => !string.IsNullOrWhiteSpace(ResendApiToken);
}
