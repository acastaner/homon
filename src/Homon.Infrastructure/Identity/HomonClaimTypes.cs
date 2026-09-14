namespace Homon.Infrastructure.Identity;

/// <summary>Claim types Homon issues itself.</summary>
public static class HomonClaimTypes
{
    /// <summary>
    /// How the principal authenticated, present only when that was <i>not</i> the session
    /// cookie. Its one value today is <see cref="ApiKeyAuthentication"/>.
    /// </summary>
    /// <remarks>
    /// Absence means a cookie session, so the common path costs one failed claim lookup and
    /// no allocation.
    /// </remarks>
    public const string AuthenticationKind = "homon:auth";

    /// <summary>The value <see cref="AuthenticationKind"/> carries for an API key.</summary>
    public const string ApiKeyAuthentication = "api-key";

    /// <summary>
    /// The public half of the API key that authenticated this request. Safe to log — it is
    /// not the secret.
    /// </summary>
    public const string ApiKeyId = "homon:key-id";

    /// <summary>The key's administrator-given name, for "reported by" labels.</summary>
    public const string ApiKeyName = "homon:key-name";
}
