using Homon.Domain.Monitoring;
using Homon.Infrastructure.Monitoring;

namespace Homon.Api.Tests;

public class PingProbeRunnerTests
{
    private static Probe MakeProbe() => new() { Id = Guid.NewGuid(), Name = "NAS", Host = "nas.local", Kind = ProbeKind.Ping };

    [Fact]
    public async Task A_successful_reply_reports_success_with_latency_and_no_detail()
    {
        var pinger = new FakeIcmpPinger { Reply = new IcmpPingReply(true, 12.5, null) };
        var runner = new PingProbeRunner(pinger);

        var result = await runner.RunAsync(MakeProbe(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(12.5, result.LatencyMs);
        Assert.Null(result.Detail);
    }

    [Fact]
    public async Task A_timeout_shaped_reply_reports_the_ping_detail_sentence()
    {
        var pinger = new FakeIcmpPinger { Reply = new IcmpPingReply(false, null, "TimedOut") };
        var runner = new PingProbeRunner(pinger);

        var result = await runner.RunAsync(MakeProbe(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.LatencyMs);
        Assert.Equal("ping: TimedOut", result.Detail);
    }

    [Fact]
    public async Task An_access_denied_reply_reports_the_permission_denied_sentence()
    {
        var pinger = new FakeIcmpPinger
        {
            Reply = new IcmpPingReply(false, null, "permission denied — check net.ipv4.ping_group_range"),
        };
        var runner = new PingProbeRunner(pinger);

        var result = await runner.RunAsync(MakeProbe(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("ping: permission denied — check net.ipv4.ping_group_range", result.Detail);
    }
}
