using Homon.Domain.Monitoring;

namespace Homon.Infrastructure.Monitoring;

public sealed class PingProbeRunner(IIcmpPinger pinger) : IProbeRunner
{
    /// <summary>Not admin-configurable this phase — same posture as 003's HttpProbeRunner constant.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public ProbeKind Kind => ProbeKind.Ping;

    public async Task<ProbeResult> RunAsync(Probe probe, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(probe);

        var reply = await pinger.SendAsync(probe.Host, Timeout, cancellationToken);

        return reply.Succeeded
            ? new ProbeResult(true, reply.RoundtripMs, null)
            : new ProbeResult(false, null, $"ping: {reply.FailureReason}");
    }
}
