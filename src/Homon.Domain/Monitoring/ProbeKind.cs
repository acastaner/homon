namespace Homon.Domain.Monitoring;

/// <summary>
/// What a <see cref="Probe"/> checks. The first four members existed from the day this enum
/// shipped, even though only <see cref="Ping"/> had a runner (plan 002) — <see cref="Http"/>,
/// <see cref="Smb"/> and <see cref="Snmp"/> arrive with plans 003, 004 and 005 respectively,
/// and a probe's <c>Kind</c> cannot change after creation, so that set existed up front rather
/// than growing later.
/// </summary>
/// <remarks>
/// <see cref="Message"/> did grow it, in plan 021, and that was safe for the reason the
/// original comment hinted at: the value is persisted as its <em>name</em> and the column
/// carries no check constraint (see ProbeConfiguration), so a new member renumbers nothing and
/// needs no migration. A kind whose runner needs options still adds its own jsonb column per
/// §3.17; a message probe needs none, because what it watches is named by <c>Probe.Host</c>.
/// </remarks>
public enum ProbeKind
{
    /// <summary>ICMP echo. The only kind plan 002 ships a runner for.</summary>
    Ping,

    /// <summary>HTTP/HTTPS. Runner arrives with plan 003.</summary>
    Http,

    /// <summary>SMB/CIFS. Runner arrives with plan 004.</summary>
    Smb,

    /// <summary>SNMP. Runner arrives with plan 005.</summary>
    Snmp,

    /// <summary>
    /// A push report from a registered reporter, rather than anything Homon reaches out to:
    /// <c>Probe.Host</c> holds the reporter's identifier and the runner reads its newest
    /// message. Plan 021.
    /// </summary>
    Message,
}
