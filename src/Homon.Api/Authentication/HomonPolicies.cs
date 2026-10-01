using Homon.Api.Configuration;
using Homon.Domain.Auth;
using Homon.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Homon.Api.Authentication;

/// <summary>The five authorisation policies, and what each admits.</summary>
public static class HomonPolicies
{
    /// <summary>
    /// Anyone who may read. Admits everybody while <see cref="AuthOptions.RequireSignInForReaders"/>
    /// is off; admits only authenticated principals — a session or an API key — once it is on.
    /// Every read endpoint carries this, so the switch is one line of configuration.
    /// </summary>
    public const string Reader = "Reader";

    /// <summary>A session in the <see cref="HomonRoles.Administrator"/> role. Every write.</summary>
    public const string Administrator = "Administrator";

    /// <summary>
    /// An API key, and only an API key: the endpoints automations report to. A session is
    /// deliberately refused here so a browser can never be tricked into filing a report.
    /// </summary>
    public const string ApiKey = "ApiKey";

    /// <summary>
    /// A session in the Administrator role, or any authenticated API key of either scope. The
    /// shape plan 002's probe-list read (<c>GET /probes</c>) needs. Scope is not discriminated
    /// here — narrowing to ReadWrite only is a write concern and no write uses this policy
    /// (writes stay <see cref="Administrator"/>, session only, per docs/ARCHITECTURE.md §3.3).
    /// The ReadWrite-only variant §3.13 and plan 013 both deferred is
    /// <see cref="ApiKeyWrite"/>, added by plan 021.
    /// </summary>
    public const string AdministratorOrApiKey = "AdministratorOrApiKey";

    /// <summary>
    /// An API key whose scope is <see cref="ApiKeyScope.ReadWrite"/>, and only that: the message
    /// gateway's ingestion endpoint. A <see cref="ApiKeyScope.Read"/> key is refused with 403
    /// rather than 401 — it authenticated perfectly well, it simply may not report — and a
    /// session is refused for the same reason <see cref="ApiKey"/> refuses one.
    /// </summary>
    /// <remarks>
    /// This is the first consumer of the distinction plan 013 introduced and §3.13 recorded as
    /// existing "for the Backups module (008), whose report endpoint is the first thing that
    /// should refuse a Read key". Plan 021 supersedes 008 and inherited that sentence.
    /// </remarks>
    public const string ApiKeyWrite = "ApiKeyWrite";

    public static AuthorizationBuilder AddHomonPolicies(this AuthorizationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .AddPolicy(Reader, policy => policy.AddRequirements(new ReaderRequirement()))
            .AddPolicy(Administrator, policy => policy.RequireRole(HomonRoles.Administrator))
            .AddPolicy(ApiKey, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(HomonClaimTypes.AuthenticationKind, HomonClaimTypes.ApiKeyAuthentication))
            .AddPolicy(ApiKeyWrite, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(HomonClaimTypes.AuthenticationKind, HomonClaimTypes.ApiKeyAuthentication)
                // The claim carries the scope's name, not its number — ApiKeyAuthenticationHandler
                // writes key.Scope.ToString(), and ApiKeyConfiguration persists the same text.
                .RequireClaim(HomonClaimTypes.ApiKeyScope, nameof(ApiKeyScope.ReadWrite)))
            .AddPolicy(AdministratorOrApiKey, policy => policy.RequireAssertion(context =>
                context.User.IsInRole(HomonRoles.Administrator)
                || (context.User.Identity?.IsAuthenticated is true
                    && context.User.HasClaim(HomonClaimTypes.AuthenticationKind, HomonClaimTypes.ApiKeyAuthentication))));
    }
}

/// <summary>Marker requirement; <see cref="ReaderHandler"/> decides it.</summary>
public sealed class ReaderRequirement : IAuthorizationRequirement;

/// <summary>
/// Reads <see cref="AuthOptions"/> from the built container on every evaluation rather than
/// at registration, so a test host's configuration override — and a future reload — are
/// honoured. Cheap: one options lookup and one boolean.
/// </summary>
public sealed class ReaderHandler(IOptionsMonitor<AuthOptions> options)
    : AuthorizationHandler<ReaderRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ReaderRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!options.CurrentValue.RequireSignInForReaders
            || context.User.Identity?.IsAuthenticated is true)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
