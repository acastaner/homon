namespace Homon.Infrastructure.Email;

/// <summary>
/// The one way Homon sends mail. Alerts are the only kind of message the application has
/// today, and the transport — Resend, or the log in development — is chosen behind this
/// seam so callers never learn how mail is delivered.
/// </summary>
/// <remarks>
/// Deliberately a single generic <c>SendAsync</c> rather than one method per template: the
/// alerting module (plan 009) owns the templates, and this interface should not have to
/// change when it gains one. If the set of messages grows past alerts, revisit — a closed
/// set of named methods is the better shape once there are several.
/// </remarks>
public interface IAlertEmailSender
{
    /// <exception cref="EmailSendException">The transport refused or timed out.</exception>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
