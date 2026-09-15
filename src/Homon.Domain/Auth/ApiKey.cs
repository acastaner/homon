namespace Homon.Domain.Auth;

/// <summary>
/// A credential an automation presents instead of a session: the backup scripts on a
/// server, a cron job, anything that is not a person at a browser.
/// </summary>
/// <remarks>
/// <para>
/// A key is <c>hmn_&lt;TokenId&gt;_&lt;secret&gt;</c>. The token id is public — it names the
/// key in a log line or a refusal and is safe to print — and the secret is shown exactly
/// once, when the key is minted; only its SHA-256 digest is stored. A database dump
/// therefore yields nothing that authenticates.
/// </para>
/// <para>
/// Phase 0 mints keys from the command line only (<c>create-api-key</c>). The admin page
/// that lists, mints and revokes them arrives with the Backups module — see
/// <c>docs/MODULES.md</c>.
/// </para>
/// </remarks>
public sealed class ApiKey
{
    /// <summary>Length of <see cref="TokenId"/>, in characters.</summary>
    public const int TokenIdLength = 12;

    /// <summary>Longest name the admin may give a key.</summary>
    public const int NameMaxLength = 100;

    /// <summary>Longest string <see cref="ApiKeyScope"/> is stored as. See ApiKeyConfiguration.</summary>
    public const int ScopeMaxLength = 20;

    public Guid Id { get; set; }

    /// <summary>What the key is for, in the admin's words: "clockmaster restic", say.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The public half of the credential. Unique; the only thing a lookup uses.</summary>
    public string TokenId { get; set; } = string.Empty;

    /// <summary>SHA-256 of the secret half. Never the secret.</summary>
    public byte[] SecretHash { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Stamped on use, at a coarse resolution — see the authentication handler.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>Set when the admin revokes the key. A revoked key is refused, never deleted.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>
    /// What this key may do. Defaults to the least-privileged value for an instance built
    /// without setting it explicitly — the database column's own default differs (ReadWrite),
    /// and exists only to backfill rows that predate this column; see the migration.
    /// </summary>
    public ApiKeyScope Scope { get; set; } = ApiKeyScope.Read;

    /// <summary>
    /// When this key stops authenticating, or null for a key that never expires. Refused
    /// exactly like a revoked key — the whole request fails, never demoted to anonymous.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }
}
