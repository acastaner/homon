namespace Homon.Infrastructure.Security;

/// <summary>
/// Encrypts and decrypts every secret a probe carries (SMB password, HTTP bearer token) —
/// one abstraction for every kind, so 004 (SMB) and 011 (calendar credentials) reuse this
/// instead of inventing their own. See plan 003's Decision 2.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    /// <summary>
    /// Throws <see cref="System.Security.Cryptography.CryptographicException"/> when the key
    /// ring cannot unprotect the value — typically because the <c>dataprotection-keys</c>
    /// volume was lost. Callers reading a credential at poll time MUST catch this and turn it
    /// into a failed observation, never let it reach the scheduler.
    /// </summary>
    string Unprotect(string protectedValue);
}
