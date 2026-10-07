namespace Homon.Infrastructure.Email;

/// <summary>Which <see cref="IAlertEmailSender"/> the host uses (plan 026, Decision 4).</summary>
public enum EmailTransport
{
    /// <summary>Send through Resend, with the key and sender stored on the Alerts admin page.</summary>
    Resend,

    /// <summary>Write the message to the log instead of sending it. Refused in Production.</summary>
    Log,
}
