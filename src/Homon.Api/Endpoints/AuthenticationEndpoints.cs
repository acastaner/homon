using System.Security.Claims;
using Homon.Domain.Auth;
using Homon.Infrastructure.Administration;
using Homon.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace Homon.Api.Endpoints;

/// <summary>Sign in, observe the session, sign out.</summary>
/// <remarks>
/// The CSRF posture: every mutating endpoint requires an <c>application/json</c> request
/// body. A cross-site HTML form can only produce <c>application/x-www-form-urlencoded</c>,
/// <c>text/plain</c> or <c>multipart/form-data</c>, so the content-type check alone refuses
/// it; a scripted cross-origin caller gets no <c>Access-Control-Allow-Origin</c> because the
/// SPA and this API are one origin and no CORS policy is registered. That is why no
/// antiforgery token is issued — see the sign-out handler, where the check is explicit
/// because a parameterless POST would otherwise be form-reachable.
/// </remarks>
internal static class AuthenticationEndpoints
{
    /// <summary>Name of the rate-limiter policy guarding sign-in.</summary>
    internal const string SignInThrottlePolicy = "sign-in";

    /// <summary>Lifetime granted when the administrator ticks "Keep me signed in".</summary>
    /// <remarks>
    /// The unticked default is <c>CookieAuthenticationOptions.ExpireTimeSpan</c> — eight
    /// hours, absolute — configured in Program.cs. Setting ExpiresUtc on the ticket
    /// overrides it for this sign-in only.
    /// </remarks>
    internal static readonly TimeSpan PersistentSessionLifetime = TimeSpan.FromDays(30);

    /// <summary>Password hash used only to spend the same time on an unknown address as on a known one.</summary>
    private static readonly string TimingEqualiserHash =
        AdministratorAuthenticator.HashPassword("homon-user-timing-equaliser");

    internal static RouteGroupBuilder MapAuthenticationEndpoints(this RouteGroupBuilder parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        var group = parent.MapGroup("/auth");

        group.MapPost("/sign-in", SignInAsync)
            .AllowAnonymous()
            .RequireRateLimiting(SignInThrottlePolicy)
            .WithName("SignIn")
            .WithSummary("Exchanges credentials for a session cookie.");

        group.MapGet("/session", GetSession)
            .AllowAnonymous()
            .WithName("GetSession")
            .WithSummary("Describes the current session — administrator or API key — or answers 204 when anonymous.");

        // Cast to Delegate: SignOutAsync's shape — a sole HttpContext parameter returning
        // Task<IResult> — implicitly (and ambiguously) satisfies RequestDelegate too. Left
        // as a method group, the compiler binds the RequestDelegate overload and silently
        // discards the IResult (ASP0016). The cast forces the minimal-API handler overload.
        group.MapPost("/sign-out", (Delegate)SignOutAsync)
            .AllowAnonymous()
            .WithName("SignOut")
            .WithSummary("Discards the session cookie. Idempotent.");

        return group;
    }

    private static async Task<IResult> SignInAsync(
        SignInRequest request,
        AdministratorAuthenticator administrator,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        HttpContext httpContext)
    {
        var email = request.Email?.Trim() ?? string.Empty;
        var password = request.Password ?? string.Empty;

        var isBootstrapAdministrator = administrator.IsAdministrator(email);

        // Verified unconditionally, even when the address does not match. Returning early
        // would skip the deliberately expensive hash comparison and make a wrong-address
        // response measurably faster than a wrong-password one — a timing oracle for which
        // address belongs to the administrator.
        var passwordMatches = administrator.VerifyPassword(password);

        if (isBootstrapAdministrator)
        {
            if (!passwordMatches)
            {
                return SignInFailure();
            }

            var principal = administrator.CreatePrincipal(IdentityConstants.ApplicationScheme);
            await httpContext.SignInAsync(
                IdentityConstants.ApplicationScheme, principal, PropertiesFor(request));

            return TypedResults.NoContent();
        }

        // A database-backed account. None exist in phase 0, but the path is complete so
        // that adding one later is a feature, not an auth change.
        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            _ = userManager.PasswordHasher.VerifyHashedPassword(
                new ApplicationUser(), TimingEqualiserHash, password);

            return SignInFailure();
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            // One answer for every cause, including LockedOut: saying which would tell an
            // anonymous caller that the account exists.
            return SignInFailure();
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await userManager.UpdateAsync(user);

        await signInManager.SignInAsync(user, PropertiesFor(request));

        return TypedResults.NoContent();
    }

    private static IResult GetSession(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated is not true)
        {
            // 204 rather than 401: "nobody is signed in" is a fact, not a failure, and the
            // SPA polls this on every load. apiFetch turns a 204 into `undefined`.
            return TypedResults.NoContent();
        }

        var kind = user.FindFirst(HomonClaimTypes.AuthenticationKind)?.Value
            == HomonClaimTypes.ApiKeyAuthentication
            ? SessionKind.ApiKey
            : user.IsInRole(HomonRoles.Administrator)
                ? SessionKind.Administrator
                : SessionKind.User;

        ApiKeyScope? scope = kind == SessionKind.ApiKey
            && Enum.TryParse<ApiKeyScope>(user.FindFirst(HomonClaimTypes.ApiKeyScope)?.Value, out var parsed)
                ? parsed
                : null;

        return TypedResults.Ok(new SessionResponse(
            Kind: kind,
            Name: user.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty,
            Scope: scope));
    }

    private static async Task<IResult> SignOutAsync(HttpContext httpContext)
    {
        // Sign-out takes no fields, so without this check a cross-site <form method="post">
        // could end a session — a plain form POST lands on the server whatever the CORS
        // policy says, since CORS only governs whether the response can be read back.
        // Requiring JSON blocks it outright: a form cannot set that content type.
        if (httpContext.Request.ContentType?.StartsWith(
                "application/json", StringComparison.OrdinalIgnoreCase) is not true)
        {
            return TypedResults.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        await httpContext.SignOutAsync(IdentityConstants.ApplicationScheme);

        // Idempotent: signing out when already anonymous is a success, not a 401.
        return TypedResults.NoContent();
    }

    private static AuthenticationProperties PropertiesFor(SignInRequest request) =>
        new()
        {
            IsPersistent = request.KeepSignedIn,
            AllowRefresh = false,
            ExpiresUtc = request.KeepSignedIn
                ? DateTimeOffset.UtcNow.Add(PersistentSessionLifetime)
                : null,
        };

    // Concrete ProblemHttpResult rather than IResult: CA1859 fires on a private helper whose
    // every return is one concrete type, and is fatal under TreatWarningsAsErrors.
    private static ProblemHttpResult SignInFailure() =>
        TypedResults.Problem(
            title: "Sign-in failed",
            detail: "The email address or password is incorrect.",
            statusCode: StatusCodes.Status401Unauthorized);

    /// <param name="Email">Sign-in address.</param>
    /// <param name="Password">Plain-text password; verified against a hash, never stored.</param>
    /// <param name="KeepSignedIn">
    /// When true the session becomes a persistent cookie lasting
    /// <see cref="PersistentSessionLifetime"/> instead of the eight-hour default.
    /// </param>
    internal sealed record SignInRequest(string? Email, string? Password, bool KeepSignedIn);

    /// <summary>How the caller authenticated.</summary>
    public enum SessionKind
    {
        /// <summary>A session in the Administrator role — bootstrap or database-backed.</summary>
        Administrator,

        /// <summary>A database-backed session holding no role. None can exist yet.</summary>
        User,

        /// <summary>An API key.</summary>
        ApiKey,
    }

    /// <summary>What the SPA needs to render the signed-in state.</summary>
    /// <param name="Kind">How the caller authenticated.</param>
    /// <param name="Name">The account's address, or the key's administrator-given name.</param>
    /// <param name="Scope">Set only for an <see cref="SessionKind.ApiKey"/> session.</param>
    internal sealed record SessionResponse(SessionKind Kind, string Name, ApiKeyScope? Scope);
}
