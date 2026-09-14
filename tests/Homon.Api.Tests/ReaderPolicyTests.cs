using System.Security.Claims;
using Homon.Api.Authentication;
using Homon.Api.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Homon.Api.Tests;

/// <summary>
/// The one switch that closes the dashboard to anonymous readers, evaluated through the
/// real <see cref="IAuthorizationService"/> so the requirement, the handler and the policy
/// registration are all under test.
/// </summary>
public class ReaderPolicyTests
{
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());

    private static readonly ClaimsPrincipal SignedIn =
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "someone")], "Test"));

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Anonymous_readers_are_admitted_only_while_the_switch_is_off(bool requireSignIn, bool admitted)
    {
        var authorization = Build(requireSignIn);

        var result = await authorization.AuthorizeAsync(Anonymous, resource: null, HomonPolicies.Reader);

        Assert.Equal(admitted, result.Succeeded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_signed_in_principal_is_always_admitted(bool requireSignIn)
    {
        var authorization = Build(requireSignIn);

        var result = await authorization.AuthorizeAsync(SignedIn, resource: null, HomonPolicies.Reader);

        Assert.True(result.Succeeded);
    }

    private static IAuthorizationService Build(bool requireSignIn)
    {
        var services = new ServiceCollection();

        services.AddLogging(logging => logging.ClearProviders());
        services.AddSingleton<IOptionsMonitor<AuthOptions>>(
            new StaticOptionsMonitor<AuthOptions>(new AuthOptions { RequireSignInForReaders = requireSignIn }));
        services.AddSingleton<IAuthorizationHandler, ReaderHandler>();
        services.AddAuthorizationBuilder().AddHomonPolicies();

        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
