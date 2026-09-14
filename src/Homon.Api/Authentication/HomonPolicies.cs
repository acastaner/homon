using Homon.Api.Configuration;
using Homon.Domain.Auth;
using Homon.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Homon.Api.Authentication;

/// <summary>The three authorisation policies, and what each admits.</summary>
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

    public static AuthorizationBuilder AddHomonPolicies(this AuthorizationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .AddPolicy(Reader, policy => policy.AddRequirements(new ReaderRequirement()))
            .AddPolicy(Administrator, policy => policy.RequireRole(HomonRoles.Administrator))
            .AddPolicy(ApiKey, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(HomonClaimTypes.AuthenticationKind, HomonClaimTypes.ApiKeyAuthentication));
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
