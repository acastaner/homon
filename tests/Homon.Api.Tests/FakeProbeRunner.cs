using Homon.Domain.Monitoring;
using Homon.Infrastructure.Monitoring;

namespace Homon.Api.Tests;

/// <summary>
/// A scripted <see cref="IProbeRunner"/> for <see cref="ProbeKind.Ping"/> probes: it returns, or
/// throws, whatever the test's delegate does. Never touches the network. Shared by the scheduler
/// tests and the "Poll now" endpoint tests (plan 027).
/// </summary>
public sealed class FakeProbeRunner(Func<Probe, CancellationToken, Task<ProbeResult>> run) : IProbeRunner
{
    public ProbeKind Kind => ProbeKind.Ping;

    public Task<ProbeResult> RunAsync(Probe probe, CancellationToken cancellationToken) =>
        run(probe, cancellationToken);
}
