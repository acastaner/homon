using System.Collections.Concurrent;
using Homon.Infrastructure.Email;

namespace Homon.Api.Tests;

/// <summary>
/// Captures what the API would have mailed. Registered as a singleton in place of the real
/// sender, because the handlers resolve it per request scope and the assertions run outside
/// any scope.
/// </summary>
public sealed class RecordingEmailSender : IAlertEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public IReadOnlyCollection<EmailMessage> Sent => _sent;

    /// <summary>When set, <see cref="SendAsync"/> throws it instead of recording — a failing transport.</summary>
    public Exception? ThrowOnSend { get; set; }

    public void Clear() => _sent.Clear();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (ThrowOnSend is { } failure)
        {
            throw failure;
        }

        _sent.Enqueue(message);
        return Task.CompletedTask;
    }
}
