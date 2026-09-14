using System.Security.Claims;
using Homon.Domain.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Homon.Infrastructure.Administration;

/// <summary>
/// Verifies the configuration-supplied administrator credentials and builds the principal
/// for that account. The sign-in endpoint consults this <i>before</i> looking the address
/// up in the user store.
/// </summary>
public sealed class AdministratorAuthenticator(
    IOptions<AdministratorOptions> options,
    IPasswordHasher<AdministratorIdentity> passwordHasher)
{
    /// <summary>
    /// Stable subject identifier for the bootstrap administrator. Not a database key — no
    /// row with this id exists, or can exist — and not a Guid, which is how the cookie's
    /// principal validation tells it apart from a database-backed account.
    /// </summary>
    public const string SubjectId = "bootstrap-administrator";

    /// <summary>
    /// Password hash used only to spend the same time on an unknown address as on the
    /// configured one — and when no administrator is configured at all. Without it, a
    /// non-matching address would skip the hash comparison and answer measurably faster
    /// than a wrong password does, which is a timing oracle for the administrator's address.
    /// </summary>
    private static readonly string TimingEqualiserHash = HashPassword("homon-timing-equaliser");

    private readonly AdministratorOptions _options = options.Value;

    /// <summary>Whether configuration supplies an administrator at all.</summary>
    public bool IsConfigured => _options.IsConfigured;

    /// <summary>
    /// True when <paramref name="email"/> is the configured administrator address. Compared
    /// case-insensitively, the same way Identity normalises addresses.
    /// </summary>
    public bool IsAdministrator(string email) =>
        _options.IsConfigured
        && string.Equals(email, _options.Email, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Verifies a password against the configured hash. Always runs a hash comparison, so
    /// its cost does not depend on whether an administrator is configured.
    /// </summary>
    /// <remarks>
    /// Callers must return the same generic failure the normal path returns, so responses
    /// do not reveal which address belongs to the administrator.
    /// </remarks>
    public bool VerifyPassword(string password)
    {
        var hash = _options.IsConfigured ? _options.PasswordHash! : TimingEqualiserHash;

        var result = passwordHasher.VerifyHashedPassword(AdministratorIdentity.Instance, hash, password);

        return _options.IsConfigured && result is not PasswordVerificationResult.Failed;
    }

    /// <summary>Builds the claims principal signed in as the bootstrap administrator.</summary>
    public ClaimsPrincipal CreatePrincipal(string authenticationScheme)
    {
        var identity = new ClaimsIdentity(
            authenticationScheme,
            nameType: ClaimTypes.Name,
            roleType: ClaimTypes.Role);

        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, SubjectId));
        identity.AddClaim(new Claim(ClaimTypes.Name, _options.Email!));
        identity.AddClaim(new Claim(ClaimTypes.Email, _options.Email!));
        identity.AddClaim(new Claim(ClaimTypes.Role, HomonRoles.Administrator));

        return new ClaimsPrincipal(identity);
    }

    /// <summary>Produces a hash suitable for the PasswordHash setting.</summary>
    public static string HashPassword(string password) =>
        new PasswordHasher<AdministratorIdentity>()
            .HashPassword(AdministratorIdentity.Instance, password);
}

/// <summary>
/// Placeholder type parameter for <see cref="IPasswordHasher{TUser}"/>. Identity's hasher
/// is generic over a user type but never inspects the instance; the bootstrap administrator
/// has no user record, so this stands in for one.
/// </summary>
public sealed class AdministratorIdentity
{
    public static AdministratorIdentity Instance { get; } = new();

    private AdministratorIdentity()
    {
    }
}
