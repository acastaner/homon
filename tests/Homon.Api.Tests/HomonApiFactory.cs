using Homon.Infrastructure.Administration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Homon.Api.Tests;

/// <summary>
/// Boots the API in-process for tests.
/// </summary>
/// <remarks>
/// Supplies the configuration the host needs — a connection string, and the administrator's
/// address with a real password hash minted at runtime — so the host starts without a
/// developer environment. It does <i>not</i> stand up a database: its connection string names
/// an unreachable <c>homon_test</c> database, so only endpoints that never touch one can be
/// exercised through this factory. Anything data-backed goes through
/// <see cref="ApiDatabaseFactory"/> and a <c>[DatabaseFact]</c>.
/// </remarks>
public class HomonApiFactory : WebApplicationFactory<Program>
{
    /// <summary>The administrator's address in tests.</summary>
    internal const string AdministratorEmail = "admin@example.test";

    /// <summary>
    /// The administrator's password in tests. Real, because the sign-in endpoint verifies a
    /// hash unconditionally — a syntactically invalid hash throws FormatException inside
    /// Convert.FromBase64String rather than simply failing to match.
    /// </summary>
    internal const string AdministratorPassword = "correct-horse-battery-staple";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(Environments.Development);

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Homon"] =
                    "Host=localhost;Port=5432;Database=homon_test;Username=test;Password=test",
                ["Administrator:Email"] = AdministratorEmail,
                ["Administrator:PasswordHash"] =
                    AdministratorAuthenticator.HashPassword(AdministratorPassword),
                ["Email:ResendApiToken"] = null,

                // Raised well clear of the suite's own attempt count so unrelated tests
                // cannot exhaust the window and make each other flaky. SignInThrottleTests
                // uses its own factory with a low limit to exercise rejection.
                ["SignInThrottle:PermitLimit"] = "1000",
                ["SignInThrottle:WindowMinutes"] = "1",

                // This factory's connection string above is deliberately unreachable. Once
                // AddHostedService<ProbeScheduler>() is registered, any test host built from
                // this factory would otherwise start ticking against that unreachable
                // database the moment the host starts — a connection attempt and a logged
                // error on every unrelated test in the suite. ApiDatabaseFactory calls
                // base.ConfigureWebHost first, so it inherits both keys automatically; every
                // scheduler/retention test in this plan constructs ProbeScheduler/
                // ProbeObservationRetentionService by hand and calls TickAsync/SweepAsync
                // directly, never through the hosted loop.
                ["Monitoring:SchedulerEnabled"] = "false",
                ["Monitoring:RetentionEnabled"] = "false",
            }));
    }
}
