using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Homon.Infrastructure.Monitoring;

/// <summary>
/// The one place <see cref="System.Net.NetworkInformation.Ping"/> is touched.
/// <c>PingProbeRunnerTests</c> fakes <see cref="IIcmpPinger"/> instead, so no unit test ever
/// sends real ICMP.
/// </summary>
public sealed class SystemIcmpPinger : IIcmpPinger
{
    public async Task<IcmpPingReply> SendAsync(
        string host, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var ping = new Ping();

        try
        {
            var reply = await ping
                .SendPingAsync(host, (int)timeout.TotalMilliseconds)
                .WaitAsync(cancellationToken);

            return reply.Status == IPStatus.Success
                ? new IcmpPingReply(true, reply.RoundtripTime, null)
                : new IcmpPingReply(false, null, reply.Status.ToString());
        }
        catch (PingException ex) when (ex.InnerException is SocketException { SocketErrorCode: SocketError.AccessDenied })
        {
            // The rootless-Docker ICMP case docs/deployment-runbook.md's "ICMP" section
            // already documents — the container's net.ipv4.ping_group_range is empty.
            return new IcmpPingReply(
                false, null, "permission denied — check net.ipv4.ping_group_range");
        }
        catch (PingException ex)
        {
            return new IcmpPingReply(false, null, ex.InnerException?.Message ?? ex.Message);
        }
    }
}
