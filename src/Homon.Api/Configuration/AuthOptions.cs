namespace Homon.Api.Configuration;

/// <summary>
/// Who may <i>read</i>. The administrator always signs in; this decides whether everybody
/// else must too.
/// </summary>
/// <remarks>
/// The intended deployment is a trusted home network where the reverse proxy's access
/// rules are the gate and the family never sees a sign-in form — so the default is open.
/// Flip <see cref="RequireSignInForReaders"/> to close every read endpoint to anonymous
/// callers; the <c>Reader</c> policy in <c>HomonPolicies</c> is the one place that reads
/// it, and every read endpoint carries that policy, so no endpoint needs to know.
/// </remarks>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// When true, read endpoints refuse anonymous requests (401). When false — the default
    /// — anyone who can reach the API can read it. <c>GET /api/v1/meta</c> and
    /// <c>/health</c> are anonymous either way.
    /// </summary>
    public bool RequireSignInForReaders { get; set; }
}
