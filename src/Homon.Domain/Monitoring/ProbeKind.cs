namespace Homon.Domain.Monitoring;

/// <summary>
/// What a <see cref="Probe"/> checks. All four members exist from the day this enum ships,
/// even though only <see cref="Ping"/> has a runner (plan 002) — <see cref="Http"/>,
/// <see cref="Smb"/> and <see cref="Snmp"/> arrive with plans 003, 004 and 005 respectively,
/// and a probe's <c>Kind</c> cannot change after creation, so the full set exists up front
/// rather than growing the enum (and the stored strings — see ProbeConfiguration) later.
/// </summary>
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
}
