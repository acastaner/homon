using Homon.Domain.Monitoring;

namespace Homon.Infrastructure.Monitoring;

/// <summary>One runner per <see cref="ProbeKind"/>. <see cref="ProbeScheduler"/> picks the matching one by <see cref="Kind"/>.</summary>
public interface IProbeRunner
{
    ProbeKind Kind { get; }

    Task<ProbeResult> RunAsync(Probe probe, CancellationToken cancellationToken);
}
