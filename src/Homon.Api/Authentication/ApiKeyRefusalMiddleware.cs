using Microsoft.AspNetCore.Authentication;

namespace Homon.Api.Authentication;

/// <summary>
/// Refuses a request that presented an API key which did not authenticate.
/// </summary>
/// <remarks>
/// Without this, a revoked or mistyped key would simply demote the caller to anonymous —
/// and since read endpoints are anonymous by default, the script would keep getting 200s
/// from every read while its reports silently 401. Failing the whole request the moment a
/// bad key is seen is what makes a broken key noticeable. Sits directly after
/// <c>UseAuthentication</c>.
/// </remarks>
internal sealed class ApiKeyRefusalMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (ApiKeyAuthenticationHandler.Carries(context)
            && context.User.Identity?.IsAuthenticated is not true)
        {
            // The handler's own challenge writes the problem document with the reason.
            await context.ChallengeAsync(HomonAuthenticationSchemes.ApiKey);
            return;
        }

        await next(context);
    }
}
