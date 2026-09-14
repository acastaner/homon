using Homon.Domain.Auth;
using Homon.Infrastructure.Administration;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Homon.Api.Tests;

public class AdministratorAuthenticatorTests
{
    [Fact]
    public void A_hash_from_hash_password_verifies_and_the_principal_is_an_administrator()
    {
        var authenticator = Build(new AdministratorOptions
        {
            Email = "admin@example.test",
            PasswordHash = AdministratorAuthenticator.HashPassword("hunter2hunter2"),
        });

        Assert.True(authenticator.IsConfigured);
        Assert.True(authenticator.IsAdministrator("ADMIN@example.test"));
        Assert.False(authenticator.IsAdministrator("someone@example.test"));
        Assert.True(authenticator.VerifyPassword("hunter2hunter2"));
        Assert.False(authenticator.VerifyPassword("hunter2"));

        var principal = authenticator.CreatePrincipal("Test");

        Assert.True(principal.IsInRole(HomonRoles.Administrator));
        Assert.Equal("admin@example.test", principal.Identity?.Name);
    }

    [Fact]
    public void Unconfigured_administrator_matches_nobody_and_verifies_nothing()
    {
        var authenticator = Build(new AdministratorOptions());

        Assert.False(authenticator.IsConfigured);
        Assert.False(authenticator.IsAdministrator(string.Empty));
        Assert.False(authenticator.VerifyPassword("anything"));
    }

    private static AdministratorAuthenticator Build(AdministratorOptions options) =>
        new(Options.Create(options), new PasswordHasher<AdministratorIdentity>());
}
