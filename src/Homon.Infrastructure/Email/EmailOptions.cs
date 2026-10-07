namespace Homon.Infrastructure.Email;

/// <summary>
/// The one piece of email configuration that stays in configuration: which transport the host
/// uses.
/// </summary>
/// <remarks>
/// The Resend API key, the sender and the recipients moved to the <c>AlertSettings</c> database
/// row, edited on the Alerts admin page (plan 026, Decision 3) — an administrator can fix them
/// from a browser, with no redeploy. <see cref="EmailTransport.Log"/> exists so the gate can
/// never mail anyone: the Playwright suite and the API test host set it, which replaces the
/// old "no token configured" test as the way a machine is kept from sending live mail.
/// </remarks>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>
    /// The transport behind <see cref="IAlertEmailSender"/>. Production refuses
    /// <see cref="EmailTransport.Log"/>: a Production host will not silently log alerts.
    /// </summary>
    public EmailTransport Transport { get; set; } = EmailTransport.Resend;
}
