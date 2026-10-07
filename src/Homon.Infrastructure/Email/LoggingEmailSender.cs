using Microsoft.Extensions.Logging;

namespace Homon.Infrastructure.Email;

/// <summary>
/// Development stand-in for <see cref="IAlertEmailSender"/>: writes the message to the log
/// instead of sending mail. Registered when <c>Email:Transport</c> is <c>Log</c> — the setting the
/// gate uses so it can never mail real people (plan 026, Decision 4).
/// </summary>
/// <remarks>
/// The recipients and subject are logged at <see cref="LogLevel.Warning"/> so the fact
/// that mail <i>would</i> have gone out is visible by default; the body is logged separately
/// at <see cref="LogLevel.Debug"/>, so it does not sit in a default-level log stream. A
/// developer who wants to read the alert enables Debug for this category — the Playwright
/// suite does exactly that.
/// </remarks>
public sealed partial class LoggingEmailSender(ILogger<LoggingEmailSender> logger)
    : IAlertEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        LogNotSent(logger, string.Join(", ", message.To), message.Subject);
        LogBody(logger, message.Subject, message.TextBody);

        return Task.CompletedTask;
    }

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Warning,
        Message = "Email not sent (Email:Transport is Log). To {Recipients}: {Subject}")]
    private static partial void LogNotSent(ILogger logger, string recipients, string subject);

    [LoggerMessage(
        EventId = 1100,
        Level = LogLevel.Debug,
        Message = "Body of '{Subject}':\n{TextBody}")]
    private static partial void LogBody(ILogger logger, string subject, string textBody);
}
