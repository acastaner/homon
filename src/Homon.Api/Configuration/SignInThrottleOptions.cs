namespace Homon.Api.Configuration;

/// <summary>
/// Fixed-window throttle applied to the sign-in endpoint, partitioned by client address as
/// reported by the trusted reverse proxy in production (see <see cref="ReverseProxyOptions"/>).
/// </summary>
/// <remarks>
/// The bootstrap administrator has no database row, so ASP.NET Core Identity's lockout —
/// which counts failed attempts against a user record — cannot protect it. This throttle is
/// the only thing standing between a configuration-supplied password hash and an unbounded
/// online guessing attack. The values are configurable so tests can raise the limit out of
/// the way, or lower it to exercise rejection.
/// </remarks>
public sealed class SignInThrottleOptions
{
    public const string SectionName = "SignInThrottle";

    /// <summary>Attempts allowed per client address per <see cref="WindowMinutes"/>.</summary>
    public int PermitLimit { get; set; } = 10;

    /// <summary>Length of the fixed window, in minutes.</summary>
    public int WindowMinutes { get; set; } = 5;
}
