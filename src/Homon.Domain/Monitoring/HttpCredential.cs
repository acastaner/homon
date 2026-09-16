namespace Homon.Domain.Monitoring;

/// <summary>What kind of credential an <see cref="HttpProbeOptions"/> probe presents, if any.</summary>
public enum HttpCredentialType
{
    None,
    Bearer,
    Basic,
}

/// <summary>
/// Credentials an HTTP probe presents on every request. Owned by <see cref="HttpProbeOptions"/>
/// and stored in the same jsonb document (plan 003's Decision 1) — never a separate table.
/// </summary>
public sealed class HttpCredential
{
    public HttpCredentialType Type { get; set; } = HttpCredentialType.None;

    /// <summary>Basic auth's username only — not a secret, round-trips in the clear.</summary>
    public string? Username { get; set; }

    /// <summary>
    /// The bearer token, or basic auth's password, encrypted with
    /// <c>Homon.Infrastructure.Security.ISecretProtector</c>. Never plaintext at rest, and
    /// never read into a response DTO — <c>ProbeEndpoints</c> emits only a derived
    /// <c>hasSecret</c> boolean. See plan 003's Decision 2 for the write-only wire semantics.
    /// </summary>
    public string? ProtectedSecret { get; set; }
}
