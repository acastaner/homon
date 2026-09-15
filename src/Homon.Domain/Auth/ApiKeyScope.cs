namespace Homon.Domain.Auth;

/// <summary>
/// What an <see cref="ApiKey"/> may do. A key never administers regardless of scope
/// (docs/ARCHITECTURE.md §3.3) — <see cref="ReadWrite"/> only distinguishes a read endpoint
/// from a report endpoint (the Backups module, plan 008), which is the first thing that
/// gates on it. Persisted as this name, not a number — see ApiKeyConfiguration.cs.
/// </summary>
public enum ApiKeyScope
{
    /// <summary>The default for a newly minted key. May reach a read endpoint only.</summary>
    Read,

    /// <summary>May reach a read endpoint or a report endpoint. Still never a write.</summary>
    ReadWrite,
}
