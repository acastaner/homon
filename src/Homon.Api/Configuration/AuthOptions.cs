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

    /// <summary>
    /// When true, the session cookie is marked <c>Secure</c> only for requests that actually
    /// arrived over HTTPS, instead of always. When false — the default — it is always marked
    /// <c>Secure</c> outside Development.
    /// </summary>
    /// <remarks>
    /// Set this for a LAN-first deployment reached over plain HTTP, where an always-Secure
    /// cookie is discarded by the browser outright and no administrator can hold a session.
    /// It is only safe in combination with a front end that forwards the client's real scheme:
    /// <c>src/Homon.Web/nginx.conf</c> passes an upstream <c>X-Forwarded-Proto</c> through, so a
    /// WAF-fronted request is still seen as HTTPS and still receives a <c>Secure</c> cookie.
    /// Set it back to false once the plain-HTTP origin gains TLS; nothing else has to change.
    /// </remarks>
    public bool AllowPlainTextSessions { get; set; }
}
