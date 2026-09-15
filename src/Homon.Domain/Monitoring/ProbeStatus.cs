namespace Homon.Domain.Monitoring;

/// <summary>
/// A <see cref="Probe"/>'s current state, derived by <see cref="ProbeStateMachine"/> from its
/// consecutive success/failure counters — never set directly except <see cref="Paused"/>
/// (<see cref="Probe.Pause"/>) and the initial <see cref="Unknown"/>.
/// </summary>
public enum ProbeStatus
{
    /// <summary>Never polled, or paused since before its first poll.</summary>
    Unknown,

    /// <summary>Consecutive successes at or above the probe's <c>FailureThreshold</c>.</summary>
    Up,

    /// <summary>Between the two thresholds — neither enough successes nor enough failures.</summary>
    Unstable,

    /// <summary>Consecutive failures at or above the probe's <c>FailureThreshold</c>.</summary>
    Down,

    /// <summary>
    /// The administrator switched this probe off. Set directly by <see cref="Probe.Pause"/>,
    /// never derived — the scheduler never polls a paused probe, so nothing would ever call
    /// <see cref="ProbeStateMachine.Apply"/> for it anyway.
    /// </summary>
    Paused,
}
