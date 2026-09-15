namespace Homon.Domain.Monitoring;

/// <summary>
/// One poll outcome. Append-only and unbounded — what the hourly retention sweep prunes and
/// what uptime and the sparkline are computed over — unlike a <see cref="Probe"/>'s own live
/// state, which this table deliberately does not duplicate; see plan 002's Decision 1.
/// </summary>
public sealed class ProbeObservation
{
    /// <summary>
    /// A <c>long</c> identity column, not a <c>Guid</c> — the one entity in this module where
    /// an append-only, time-ordered, high-volume table makes a random-order key actively
    /// worse (index bloat, no clustering benefit) for no benefit a surrogate Guid usually
    /// buys.
    /// </summary>
    public long Id { get; set; }

    public Guid ProbeId { get; set; }

    public DateTimeOffset ObservedAt { get; set; }

    public bool Succeeded { get; set; }

    /// <summary>Null when the poll failed, or the kind does not report a latency.</summary>
    public double? LatencyMs { get; set; }

    /// <summary>Null on success. A short, human-readable failure reason otherwise.</summary>
    public string? Detail { get; set; }
}
