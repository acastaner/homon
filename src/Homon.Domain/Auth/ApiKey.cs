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
}
