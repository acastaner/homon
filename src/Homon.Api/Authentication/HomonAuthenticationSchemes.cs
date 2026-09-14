namespace Homon.Api.Authentication;

/// <summary>Authentication scheme names Homon registers.</summary>
internal static class HomonAuthenticationSchemes
{
    /// <summary>
    /// The default scheme: a policy scheme that forwards each request to the cookie handler
    /// or to <see cref="ApiKey"/>, depending on what the request actually carries.
    /// </summary>
    internal const string Default = "Homon";

    /// <summary>An API key presented as <c>Authorization: Bearer hmn_…</c> or <c>X-Api-Key</c>.</summary>
    internal const string ApiKey = "ApiKey";
}
