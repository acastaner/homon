using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Homon.Domain.Auth;

namespace Homon.Api.Authentication;

/// <summary>
/// The format an API key travels in, and how it is minted, parsed and checked. Both the
/// authentication handler and the minter derive their answers from here, so the two cannot
/// drift apart.
/// </summary>
/// <remarks>
/// The secret is hashed with SHA-256 rather than the Identity password hasher: a key is 32
/// random bytes, not a human-chosen password, so there is no dictionary to slow down and a
/// per-request PBKDF2 would only cost latency on every automated call.
/// </remarks>
internal static class ApiKeyRules
{
    /// <summary>
    /// Marks a Homon key wherever it turns up. Deliberately distinctive so a secret scanner
    /// can match it in a commit, a log or a paste, and so a request carrying some other
    /// bearer token is passed to the cookie handler rather than being refused.
    /// </summary>
    internal const string Prefix = "hmn_";

    /// <summary>The alternative to the Authorization header, for callers that find it easier.</summary>
    internal const string HeaderName = "X-Api-Key";

    /// <summary>
    /// Crockford's base32, which drops I, L, O and U — the letters that turn into digits
    /// when a token id is read aloud or typed from a screenshot.
    /// </summary>
    private const string TokenIdAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private const int SecretBytes = 32;

    /// <summary>
    /// The credential a request presents, or null when it carries nothing shaped like one
    /// of ours. Never touches the database, so the policy scheme may call it per request.
    /// </summary>
    internal static string? Presented(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var header = request.Headers.Authorization.ToString();

        if (AuthenticationHeaderValue.TryParse(header, out var parsed)
            && string.Equals(parsed.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            && parsed.Parameter is { } bearer
            && bearer.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return bearer;
        }

        var direct = request.Headers[HeaderName].ToString();

        return direct.StartsWith(Prefix, StringComparison.Ordinal) ? direct : null;
    }

    /// <summary>
    /// Mints a new credential: the public token id, the string to show the administrator
    /// exactly once, and the digest to store.
    /// </summary>
    internal static (string TokenId, string Presented, byte[] SecretHash) Mint()
    {
        var tokenId = RandomTokenId();
        var secret = Base64Url(RandomNumberGenerator.GetBytes(SecretBytes));

        return (tokenId, $"{Prefix}{tokenId}_{secret}", Hash(secret));
    }

    /// <summary>
    /// Splits a presented credential into its public and secret halves. Returns false for
    /// anything not shaped like one of ours.
    /// </summary>
    internal static bool TryParse(
        string? presented,
        [NotNullWhen(true)] out string? tokenId,
        [NotNullWhen(true)] out string? secret)
    {
        tokenId = null;
        secret = null;

        if (presented is null || !presented.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var body = presented.AsSpan(Prefix.Length);

        if (body.Length <= ApiKey.TokenIdLength || body[ApiKey.TokenIdLength] != '_')
        {
            return false;
        }

        var id = body[..ApiKey.TokenIdLength];

        foreach (var character in id)
        {
            if (!TokenIdAlphabet.Contains(character, StringComparison.Ordinal))
            {
                return false;
            }
        }

        tokenId = id.ToString();
        secret = body[(ApiKey.TokenIdLength + 1)..].ToString();

        return secret.Length > 0;
    }

    /// <summary>
    /// Whether a presented secret matches a stored digest, in time that does not depend on
    /// how much of it matched.
    /// </summary>
    internal static bool Matches(byte[] storedHash, string secret) =>
        CryptographicOperations.FixedTimeEquals(Hash(secret), storedHash);

    private static byte[] Hash(string secret) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    private static string RandomTokenId()
    {
        var characters = new char[ApiKey.TokenIdLength];

        for (var index = 0; index < characters.Length; index++)
        {
            characters[index] = TokenIdAlphabet[RandomNumberGenerator.GetInt32(TokenIdAlphabet.Length)];
        }

        return new string(characters);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
