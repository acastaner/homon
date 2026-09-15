namespace Homon.Infrastructure.Monitoring;

/// <summary>One poll's outcome, as an <see cref="IProbeRunner"/> reports it.</summary>
public sealed record ProbeResult(bool Succeeded, double? LatencyMs, string? Detail);
