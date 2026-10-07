using System.Security.Cryptography;
using Homon.Domain.Alerts;
using Homon.Infrastructure.Persistence;
using Homon.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Resend;

namespace Homon.Infrastructure.Email;

/// <summary>
/// Sends mail through Resend (https://resend.com), using the community .NET client, with the
/// key and sender read from the <see cref="AlertSettings"/> row on every send.
/// </summary>
/// <remarks>
/// Rendering is the caller's business — this class carries the message to the transport
/// and nothing else. The point of <see cref="IAlertEmailSender"/> is that swapping Resend
/// for direct REST calls, or for SMTP, touches this file alone.
/// <para>
/// The client is built per send with <c>ResendClient.Create</c> rather than registered with
/// the package's typed-client helper: the key lives in the database and the administrator can change it between
/// two sends, and that helper binds its token once from <c>IOptionsSnapshot</c>. The
/// token never appears in an exception message or a log line.
/// </para>
/// </remarks>
public sealed class ResendEmailSender(
    HomonDbContext database,
    ISecretProtector secrets,
    IHttpClientFactory httpClients) : IAlertEmailSender
{
    /// <summary>The named <see cref="HttpClient"/> the Resend client sends through.</summary>
    public const string HttpClientName = "Resend";

    /// <summary>
    /// Ceiling on one provider call. A send that has not answered in ten seconds is treated
    /// as down: the caller logs and continues rather than holding a poll open. Enforced
    /// here, at the call site, rather than on the SDK's HttpClient, so it does not depend on
    /// how the Resend package happens to register that client.
    /// </summary>
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var settings = await database.AlertSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == AlertSettings.SingletonId, cancellationToken);

        if (settings?.ProtectedApiKey is null)
        {
            throw new EmailSendException("No Resend API key is set.");
        }

        string token;
        try
        {
            token = secrets.Unprotect(settings.ProtectedApiKey);
        }
        catch (CryptographicException)
        {
            // The inner exception is deliberately not kept: nothing in it helps, and a
            // cryptographic failure message is not somewhere to risk a fragment of a secret.
            throw new EmailSendException(
                "The stored Resend API key can no longer be read (the data-protection key ring "
                + "changed) — enter it again on the Alerts page.");
        }

        var resend = ResendClient.Create(
            new ResendClientOptions { ApiToken = token },
            httpClients.CreateClient(HttpClientName));

        var outbound = new Resend.EmailMessage
        {
            From = $"{settings.FromName} <{settings.FromAddress}>",
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
