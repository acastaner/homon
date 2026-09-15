namespace Homon.Domain.Monitoring;

/// <summary>
/// The brief's rule, verbatim: N-or-more consecutive failures is down, N-or-more consecutive
/// successes is up, anything in between is unstable. Pure — no I/O, no clock — so it is
/// exercised directly by <c>ProbeStateMachineTests</c> without a database or a fake clock.
/// </summary>
public static class ProbeStateMachine
{
    /// <summary>
    /// One poll outcome. Resets the opposite streak to zero — a single success clears a
    /// failure streak and vice versa, so "consecutive" means what it says.
    /// </summary>
    public static (int Successes, int Failures, ProbeStatus Status) Apply(
        int consecutiveSuccesses, int consecutiveFailures, int failureThreshold, bool succeeded)
    {
        var successes = succeeded ? consecutiveSuccesses + 1 : 0;
        var failures = succeeded ? 0 : consecutiveFailures + 1;

        return (successes, failures, Derive(successes, failures, failureThreshold, everPolled: true));
    }

    /// <summary>
    /// Recomputes <see cref="ProbeStatus"/> from the counters alone, without a poll — used
    /// when an admin unpauses a probe or edits its <c>FailureThreshold</c>. Not a transition
    /// in its own right: it just re-reads what the counters already imply under the current
    /// (possibly just-changed) threshold.
    /// </summary>
    public static ProbeStatus Derive(
        int consecutiveSuccesses, int consecutiveFailures, int failureThreshold, bool everPolled)
    {
        if (!everPolled)
        {
            return ProbeStatus.Unknown;
        }

        if (consecutiveFailures >= failureThreshold)
        {
            return ProbeStatus.Down;
        }

        if (consecutiveSuccesses >= failureThreshold)
        {
            return ProbeStatus.Up;
        }

        return ProbeStatus.Unstable;
    }
}
