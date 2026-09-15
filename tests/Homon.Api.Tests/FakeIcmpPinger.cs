using Homon.Infrastructure.Monitoring;

namespace Homon.Api.Tests;

/// <summary>
/// A scripted <see cref="IIcmpPinger"/> — a reply to return, or an exception to throw. No
/// unit test that uses this ever sends real ICMP.
/// </summary>
public sealed class FakeIcmpPinger : IIcmpPinger
{
    public IcmpPingReply? Reply { get; set; }

    public Exception? ThrowsException { get; set; }

    /// <summary>Set by <see cref="SendAsync"/> so a test can assert it was reached, or block it.</summary>
    public Func<string, TimeSpan, CancellationToken, Task>? OnSend { get; set; }

    public async Task<IcmpPingReply> SendAsync(string host, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (OnSend is not null)
        {
            await OnSend(host, timeout, cancellationToken);
        }

        if (ThrowsException is not null)
        {
            throw ThrowsException;
        }

        return Reply ?? new IcmpPingReply(true, 1.0, null);
    }
}
