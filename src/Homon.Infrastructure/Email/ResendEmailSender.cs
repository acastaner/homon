using Microsoft.Extensions.Options;
using Resend;

namespace Homon.Infrastructure.Email;

/// <summary>
/// Sends mail through Resend (https://resend.com), using the community .NET client.
/// </summary>
/// <remarks>
/// Rendering is the caller's business — this class carries the message to the transport
/// and nothing else. The point of <see cref="IAlertEmailSender"/> is that swapping Resend
/// for direct REST calls, or for SMTP, touches this file alone.
/// </remarks>
public sealed class ResendEmailSender(IResend resend, IOptions<EmailOptions> options)
    : IAlertEmailSender
{
    /// <summary>
    /// Ceiling on one provider call. A send that has not answered in ten seconds is treated
    /// as down: the caller logs and continues rather than holding a poll open. Enforced
    /// here, at the call site, rather than on the SDK's HttpClient, so it does not depend on
    /// how the Resend package happens to register that client.
    /// </summary>
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

    private readonly EmailOptions _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var outbound = new Resend.EmailMessage
        {
            From = $"{_options.FromName} <{_options.FromAddress}>",
            Subject = message.Subject,
            TextBody = message.TextBody,
            HtmlBody = message.HtmlBody,
        };

        foreach (var recipient in message.To)
        {
            outbound.To.Add(recipient);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SendTimeout);

        try
        {
            await resend.EmailSendAsync(outbound, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new EmailSendException(
                $"Resend did not answer within {SendTimeout.TotalSeconds:0} seconds.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new EmailSendException("Resend rejected or failed the send.", exception);
        }
    }
}
