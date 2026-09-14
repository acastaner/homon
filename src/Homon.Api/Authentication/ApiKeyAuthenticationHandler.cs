using System.Security.Claims;
using System.Text.Encodings.Web;
using Homon.Infrastructure.Identity;
using Homon.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Homon.Api.Authentication;

/// <summary>
/// Authenticates a request carrying a Homon API key as that key: a principal with no user
/// id, no role, and the claims an endpoint needs to say "reported by the clockmaster key".
/// </summary>
/// <remarks>
/// A request whose bearer token is not one of ours produces
/// <see cref="AuthenticateResult.NoResult"/> rather than a failure, so the policy scheme's
/// choice is never load-bearing and some other bearer token is simply not us.
/// </remarks>
internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    HomonDbContext database)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>
    /// How stale <c>LastUsedAt</c> is allowed to get before it is worth a write. A busy key
    /// must not cost a database round trip per request, and the column is shown as
    /// "3 hours ago" — a five-minute granularity is invisible there.
    /// </summary>
    private static readonly TimeSpan LastUsedResolution = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Whether this request carries something shaped like a Homon key. Used by the policy
    /// scheme to pick a handler, so it must not touch the database or throw.
    /// </summary>
    internal static bool Carries(HttpContext context) =>
        ApiKeyRules.Presented(context.Request) is not null;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!ApiKeyRules.TryParse(ApiKeyRules.Presented(Request), out var tokenId, out var secret))
        {
            return AuthenticateResult.NoResult();
        }

        var now = DateTimeOffset.UtcNow;

        // The unique index on TokenId is the only thing this looks anything up by; the
        // secret never reaches the database.
        var key = await database.ApiKeys.AsNoTracking()
            .FirstOrDefaultAsync(k => k.TokenId == tokenId, Context.RequestAborted);

        if (key is null)
        {
            return AuthenticateResult.Fail("That API key is not recognised.");
        }

        if (key.RevokedAt is not null)
        {
            return AuthenticateResult.Fail("That API key has been revoked.");
        }

        // Last, and in constant time: everything above is a property of a key that exists,
        // and none of it is worth learning without the secret.
        if (!ApiKeyRules.Matches(key.SecretHash, secret))
        {
            return AuthenticateResult.Fail("That API key is not recognised.");
        }

        if (key.LastUsedAt is not { } stamped || now - stamped >= LastUsedResolution)
        {
            await database.ApiKeys
                .Where(k => k.Id == key.Id)
                .ExecuteUpdateAsync(k => k.SetProperty(x => x.LastUsedAt, now), Context.RequestAborted);
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, key.Name),
                new Claim(HomonClaimTypes.AuthenticationKind, HomonClaimTypes.ApiKeyAuthentication),
                new Claim(HomonClaimTypes.ApiKeyId, key.TokenId),
                new Claim(HomonClaimTypes.ApiKeyName, key.Name),
            ],
            HomonAuthenticationSchemes.ApiKey);

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    /// <summary>
    /// Answers 401 with a problem document rather than a bare status. The reasons here are
    /// all about a credential its holder already possesses, so none of them leak anything.
    /// </summary>
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";

        var failure = (await HandleAuthenticateOnceSafeAsync())?.Failure?.Message;

        await Results.Problem(
            title: "API key refused",
            detail: failure ?? $"Send a Homon API key as: Authorization: Bearer {ApiKeyRules.Prefix}…",
            statusCode: StatusCodes.Status401Unauthorized)
            .ExecuteAsync(Context);
    }
}
