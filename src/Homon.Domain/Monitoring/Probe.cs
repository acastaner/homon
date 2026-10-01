namespace Homon.Domain.Monitoring;

/// <summary>
/// Something the household wants to know is up: a server, a share, a service's health
/// endpoint. Carries its own live state — <see cref="Status"/>, the two streak counters,
/// <see cref="LastObservedAt"/>, <see cref="LastLatencyMs"/>, <see cref="LastDetail"/> —
/// rather than a separate one-to-one state table, because every read that needs "is this
/// probe up right now" needs exactly these fields alongside the probe's own columns; see
/// docs/ARCHITECTURE.md §3.14 and plan 002's Decision 1.
/// </summary>
public sealed class Probe
{
    /// <summary>Longest <see cref="Name"/> the admin may give a probe.</summary>
    public const int NameMaxLength = 100;

    /// <summary>Longest <see cref="Host"/> the admin may give a probe.</summary>
    public const int HostMaxLength = 255;

    /// <summary>Shortest <see cref="PollInterval"/> allowed, in seconds.</summary>
    public const int MinPollIntervalSeconds = 15;

    /// <summary>Longest <see cref="PollInterval"/> allowed, in seconds — one day.</summary>
    public const int MaxPollIntervalSeconds = 86_400;

    /// <summary>Smallest <see cref="FailureThreshold"/> allowed — a strictly binary probe.</summary>
    public const int MinFailureThreshold = 1;

    /// <summary>Largest <see cref="FailureThreshold"/> allowed.</summary>
    public const int MaxFailureThreshold = 10;

    /// <summary>What a newly created probe gets when its request omits the field.</summary>
    public const int DefaultFailureThreshold = 2;

    public Guid Id { get; set; }

    /// <summary>What the admin calls it — "NAS", "Jellyfin", say.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// A bare hostname or IP for every kind this plan or 003–005 add. HTTP's own path and
    /// scheme live in 003's <c>HttpProbeOptions</c>, not this field.
    /// </summary>
    /// <remarks>
    /// For <see cref="ProbeKind.Message"/> this is the identifier of the reporter the probe
    /// watches — the thing it listens to, exactly as a ping probe's host is the thing it pings
    /// (plan 021, Decision 3). The API validates on write that a reporter by that identifier
    /// exists, and refuses to delete a reporter while a probe names it.
    /// </remarks>
    public string Host { get; set; } = string.Empty;

    /// <summary>Immutable after creation — 003–005 attach kind-specific options a kind change would orphan.</summary>
    public ProbeKind Kind { get; set; }

    /// <summary>How often the scheduler polls this probe, once it is due.</summary>
    public TimeSpan PollInterval { get; set; }

    /// <summary>Consecutive polls, in either direction, that flip <see cref="Status"/>.</summary>
    public int FailureThreshold { get; set; } = DefaultFailureThreshold;

    /// <summary>The admin switched this probe off. The scheduler never polls it while true.</summary>
    public bool IsPaused { get; set; }

    /// <summary>
    /// An admin-set global order. Governs the ungrouped dashboard section, and the whole
    /// dashboard when no <see cref="ProbeGroup"/> exists at all. Not a unique index — see
    /// ProbeConfiguration.
    /// </summary>
    public int Position { get; set; }

    /// <summary>The probe's current state, per <see cref="ProbeStateMachine"/>.</summary>
    public ProbeStatus Status { get; set; } = ProbeStatus.Unknown;

    /// <summary>Consecutive successful polls. Reset to 0 by a failure.</summary>
    public int ConsecutiveSuccessCount { get; set; }

    /// <summary>Consecutive failed polls. Reset to 0 by a success.</summary>
    public int ConsecutiveFailureCount { get; set; }

    /// <summary>When the most recent poll completed, or null before the first one.</summary>
    public DateTimeOffset? LastObservedAt { get; set; }

    /// <summary>The most recent poll's latency, or null when it failed or none has run.</summary>
    public double? LastLatencyMs { get; set; }

    /// <summary>The most recent poll's detail string — null on a successful poll.</summary>
    public string? LastDetail { get; set; }

    /// <summary>
    /// Options for <see cref="ProbeKind.Http"/> probes only — null for every other kind.
    /// Owned, stored as its own jsonb column; see plan 003's Decision 1.
    /// </summary>
    public HttpProbeOptions? HttpOptions { get; set; }

    /// <summary>
    /// Records one poll outcome: advances the streak counters through
    /// <see cref="ProbeStateMachine.Apply"/>, derives the new <see cref="Status"/>, and
    /// stamps the live-state fields. The caller is responsible for also appending a
    /// <see cref="ProbeObservation"/> row — this method only updates the probe's own state.
    /// </summary>
    /// <param name="succeeded">Whether this poll reached the thing it was checking.</param>
    /// <param name="latencyMs">The round trip, where the kind measures one; null otherwise.</param>
    /// <param name="detail">A short, reader-facing line about this outcome.</param>
    /// <param name="observedAt">When the poll ran, from the scheduler's <c>TimeProvider</c>.</param>
    /// <param name="derivedStatus">
    /// Supplied only by a kind whose state is a derivation rather than a streak — a
    /// <see cref="ProbeKind.Message"/> probe, whose authority is the reporter's own verdict, so
    /// one "warning" report must stay <see cref="ProbeStatus.Unstable"/> instead of
    /// accumulating into <see cref="ProbeStatus.Down"/> as the same message is re-read on every
    /// tick (plan 021, Decision 5). The streak counters still advance underneath it, so
    /// <see cref="Unpause"/> and <see cref="ChangeFailureThreshold"/> have something to read;
    /// for such a probe that re-derived status is a placeholder the next poll corrects within
    /// one scheduler tick. Null for every polled kind, which keeps the state machine's answer.
    /// </param>
    public void RecordObservation(
        bool succeeded,
        double? latencyMs,
        string? detail,
        DateTimeOffset observedAt,
        ProbeStatus? derivedStatus = null)
    {
        var (successes, failures, status) = ProbeStateMachine.Apply(
            ConsecutiveSuccessCount, ConsecutiveFailureCount, FailureThreshold, succeeded);

        ConsecutiveSuccessCount = successes;
        ConsecutiveFailureCount = failures;
        Status = derivedStatus ?? status;
        LastObservedAt = observedAt;
        LastLatencyMs = latencyMs;
        LastDetail = detail;
    }

    /// <summary>
    /// Switches the probe off. Sets <see cref="IsPaused"/> and <see cref="Status"/> directly,
    /// without touching the counters or <see cref="LastObservedAt"/> — pausing is not a poll
    /// outcome.
    /// </summary>
    public void Pause()
    {
        IsPaused = true;
        Status = ProbeStatus.Paused;
    }

    /// <summary>
    /// Switches the probe back on and re-derives <see cref="Status"/> from the unchanged
    /// counters — a probe paused while <see cref="ProbeStatus.Down"/> comes back
    /// <see cref="ProbeStatus.Down"/>, not <see cref="ProbeStatus.Unknown"/>, because its
    /// last real observation still says so. Does not force an immediate poll; the scheduler
    /// picks it back up on its own next tick.
    /// </summary>
    public void Unpause()
    {
        IsPaused = false;
        Status = ProbeStateMachine.Derive(
            ConsecutiveSuccessCount, ConsecutiveFailureCount, FailureThreshold,
            everPolled: LastObservedAt is not null);
    }

    /// <summary>
    /// Changes <see cref="FailureThreshold"/> and re-derives <see cref="Status"/> from the
    /// unchanged counters under the new threshold, unless the probe is paused (in which case
    /// the new threshold applies the next time it is unpaused or polled) — see plan 002's
    /// Decision 2. Lowering the threshold below the current failure streak flips a probe
    /// straight to <see cref="ProbeStatus.Down"/> without waiting for the next poll; raising
    /// it can pull a probe back from Down/Up into Unstable. Deliberate: the number on screen
    /// should never lag an admin's own configuration change.
    /// </summary>
    public void ChangeFailureThreshold(int failureThreshold)
    {
        FailureThreshold = failureThreshold;

        if (!IsPaused)
        {
            Status = ProbeStateMachine.Derive(
                ConsecutiveSuccessCount, ConsecutiveFailureCount, FailureThreshold,
                everPolled: LastObservedAt is not null);
        }
    }
}
