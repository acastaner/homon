using System.Security.Claims;
using Homon.Api.Authentication;
using Homon.Domain.Auth;
using Homon.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Homon.Api.Tests;

/// <summary>
/// <see cref="HomonPolicies.AdministratorOrApiKey"/> evaluated through the real
/// <see cref="IAuthorizationService"/> — no <see cref="ReaderHandler"/> registration needed,
/// since these tests never evaluate <see cref="HomonPolicies.Reader"/>.
/// </summary>
public class AdministratorOrApiKeyPolicyTests
{
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());

    private static readonly ClaimsPrincipal Administrator =
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, HomonRoles.Administrator)], "Test"));

    [Fact]
    public async Task Anonymous_is_refused()
    {
        var authorization = Build();

        var result = await authorization.AuthorizeAsync(
            Anonymous, resource: null, HomonPolicies.AdministratorOrApiKey);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task An_administrator_session_is_admitted()
    {
        var authorization = Build();

        var result = await authorization.AuthorizeAsync(
            Administrator, resource: null, HomonPolicies.AdministratorOrApiKey);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(ApiKeyScope.Read)]
    [InlineData(ApiKeyScope.ReadWrite)]
    public async Task An_api_key_of_either_scope_is_admitted(ApiKeyScope scope)
    {
        var authorization = Build();

        var apiKeyPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(HomonClaimTypes.AuthenticationKind, HomonClaimTypes.ApiKeyAuthentication),
                new Claim(HomonClaimTypes.ApiKeyScope, scope.ToString()),
            ],
            "Test"));

        var result = await authorization.AuthorizeAsync(
            apiKeyPrincipal, resource: null, HomonPolicies.AdministratorOrApiKey);

        Assert.True(result.Succeeded);
    }

    private static IAuthorizationService Build()
    {
        var services = new ServiceCollection();

        services.AddLogging(logging => logging.ClearProviders());
        services.AddAuthorizationBuilder().AddHomonPolicies();

        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }
}
