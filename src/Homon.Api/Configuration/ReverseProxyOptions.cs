namespace Homon.Api.Configuration;

/// <summary>
/// The reverse proxy this API trusts to report the real client, if any.
/// </summary>
/// <remarks>
/// In production the SPA container's nginx proxies <c>/api/</c> to this container, so every
/// request Kestrel sees arrives from that proxy rather than from the client. Forwarded-headers
/// middleware recovers the real client address from <c>X-Forwarded-For</c>, but only for
/// peers named here — an unnamed proxy is not trusted, and a client with no proxy in front
/// of it (development, tests, CI) gets <see cref="KnownNetwork"/> left <see langword="null"/>,
/// which leaves the middleware unregistered.
/// </remarks>
public sealed class ReverseProxyOptions
{
    public const string SectionName = "ReverseProxy";

    /// <summary>
    /// The CIDR network the trusted proxy connects from, e.g. <c>172.16.0.0/12</c> for
    /// Docker's default bridge pools. <see langword="null"/> means no proxy is trusted — the
    /// development default — and forwarded headers are never honoured.
    /// </summary>
    public string? KnownNetwork { get; set; }
}
