namespace Homon.Domain.Monitoring;

/// <summary>
/// What one <see cref="Probe.RecordObservation"/> did to the probe's outage, if anything — the
/// scheduler turns <see cref="WentDown"/> and <see cref="Recovered"/> into an alert (plan 026,
/// Decision 1).
/// </summary>
/// <remarks>
/// <see cref="WentDown"/> is returned once, when the probe reaches
/// <see cref="ProbeStatus.Down"/> with no outage open. <see cref="Recovered"/> is returned once,
/// when it reaches <see cref="ProbeStatus.Up"/> with an outage open. Everything in between —
/// Down to Unstable to Down flapping, Unstable, Unknown — is <see cref="None"/>, so one outage
/// is one pair of mails however the probe wobbles inside it.
/// </remarks>
public enum ProbeOutageChange
{
    /// <summary>The outage did not open or close.</summary>
    None,

    /// <summary>The probe was declared Down and no outage was open: an outage began.</summary>
    WentDown,

    /// <summary>The probe was declared Up while an outage was open: the outage ended.</summary>
    Recovered,
}
