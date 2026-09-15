namespace Homon.Infrastructure.Monitoring;

/// <summary>
/// The seam <see cref="PingProbeRunner"/> sends ICMP through. Fake it in a test instead of
/// touching <see cref="SystemIcmpPinger"/> — no unit test ever sends a real ping.
/// </summary>
public interface IIcmpPinger
{
    Task<IcmpPingReply> SendAsync(string host, TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed record IcmpPingReply(bool Succeeded, double? RoundtripMs, string? FailureReason);
