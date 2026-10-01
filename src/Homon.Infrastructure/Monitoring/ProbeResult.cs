using Homon.Domain.Monitoring;

namespace Homon.Infrastructure.Monitoring;

/// <summary>One poll's outcome, as an <see cref="IProbeRunner"/> reports it.</summary>
/// <param name="Succeeded">Whether the runner reached what it was checking.</param>
/// <param name="LatencyMs">The round trip, where the kind measures one; null otherwise.</param>
/// <param name="Detail">A short, reader-facing line about this outcome.</param>
/// <param name="DerivedStatus">
/// The status to show, where the kind derives it rather than earning it through the streak
/// counters — only <see cref="ProbeKind.Message"/> does, because a push probe's authority is the
/// reporter's own verdict (plan 021, Decision 5). Null for every polled kind, which keeps
/// <see cref="ProbeStateMachine"/>'s answer. A derived <see cref="ProbeStatus.Unknown"/> means
/// the runner reached no verdict at all, and the scheduler records no observation for it.
/// </param>
public sealed record ProbeResult(
    bool Succeeded,
    double? LatencyMs,
    string? Detail,
    ProbeStatus? DerivedStatus = null);
