using System.Net.Security;
using Homon.Infrastructure.Administration;
using Homon.Infrastructure.Email;
using Homon.Infrastructure.Monitoring;
using Homon.Infrastructure.Pages;
using Homon.Infrastructure.Persistence;
using Homon.Infrastructure.Security;
using Homon.Infrastructure.Weather;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Resend;

namespace Homon.Infrastructure;

/// <summary>
/// Registers everything Homon needs to reach the outside world: the database, the email
/// transport, the configuration-supplied administrator, and the monitoring probes/scheduler.
/// </summary>
/// <remarks>
/// Authentication schemes, cookie settings, and authorisation policies are deliberately
/// <i>not</i> registered here — they live in the API host so the whole auth story can be
/// read in one place.
/// </remarks>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Name of the connection string read from configuration.</summary>
    public const string ConnectionStringName = "Homon";

    public static IServiceCollection AddHomonInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isProduction = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Registered once, here, so every clock read in the infrastructure and API layers
        // goes through the same seam. ProbeScheduler/ProbeObservationRetentionService below
        // reuse this registration rather than adding their own.
        services.AddSingleton(TimeProvider.System);

        services.AddHomonDatabase();
        services.AddHomonEmail(configuration, isProduction);
        services.AddAdministrator(configuration);
        services.AddHomonMonitoring(configuration);
        services.AddPages();
        services.AddWeather(configuration, isProduction);

        return services;
    }

    private static void AddHomonDatabase(this IServiceCollection services)
    {
        // Resolved when the context is built, not here. This method runs before
        // builder.Build(), so a connection string read at this point predates any
        // configuration WebApplicationFactory splices in for tests: every test host would
        // silently fall back to the developer's user secrets and read and write the real
        // development database, whatever its own factory said. IConfiguration resolved from
        // the built container is the merged, final configuration. Same trap, and same fix, as
        // the rate-limiter policy in Homon.Api/Program.cs — see the comment there.
        //
        // The cost is that a missing connection string is no longer a startup failure: it
        // surfaces on first database use, and /health reports it through AddDbContextCheck.
        services.AddDbContext<HomonDbContext>((serviceProvider, options) =>
        {
            var connectionString = serviceProvider.GetRequiredService<IConfiguration>()
                .GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException(
                    $"Connection string '{ConnectionStringName}' is not configured. "
                    + "See docs/postgres-setup-dev.md.");

            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(HomonDbContext).Assembly.FullName));
        });
    }

    private static void AddHomonEmail(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isProduction)
    {
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => options.IsResendConfigured || !isProduction,
                "Email:ResendApiToken is not configured. A Production host will not silently "
                + "log alerts instead of sending them — set the token, or run a non-Production "
                + "environment.")
            .ValidateOnStart();

        // Both senders are registered; which one IAlertEmailSender resolves to is decided
        // from IOptions<EmailOptions> the first time something asks, inside a scope — never
        // here. A WebApplicationFactory-based test overrides configuration through
        // ConfigureAppConfiguration, and that override only lands on the built container's
        // IConfiguration after this method returns. Deciding the sender from a section read
        // right here would choose from whatever configuration existed *before* the override,
        // which on a machine with a real Resend token in user secrets silently defeats a
        // test's attempt to null it out. Same class of bug as the connection string above.
        services.AddResend(_ => { });

        services.AddOptions<ResendClientOptions>()
            .Configure<IOptions<EmailOptions>>(
                (resendOptions, emailOptions) =>
                    resendOptions.ApiToken = emailOptions.Value.ResendApiToken ?? string.Empty);

        services.AddScoped<ResendEmailSender>();
        services.AddScoped<LoggingEmailSender>();

        services.AddScoped<IAlertEmailSender>(serviceProvider =>
        {
            var emailOptions = serviceProvider.GetRequiredService<IOptions<EmailOptions>>().Value;

            // No token: log the message instead of sending. Keeps development machines from
            // mailing real addresses.
            return emailOptions.IsResendConfigured
                ? serviceProvider.GetRequiredService<ResendEmailSender>()
                : serviceProvider.GetRequiredService<LoggingEmailSender>();
        });
    }

    private static void AddAdministrator(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AdministratorOptions>()
            .Bind(configuration.GetSection(AdministratorOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => options.IsConfigured
                    || (string.IsNullOrWhiteSpace(options.Email) && string.IsNullOrWhiteSpace(options.PasswordHash)),
                "Administrator:Email and Administrator:PasswordHash must be set together, or "
                + "neither. Mint the hash with `dotnet run --project src/Homon.Api -- hash-password`.")
            .ValidateOnStart();

        services.AddSingleton<IPasswordHasher<AdministratorIdentity>,
            PasswordHasher<AdministratorIdentity>>();
        services.AddSingleton<AdministratorAuthenticator>();
    }

    private static void AddHomonMonitoring(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MonitoringOptions>()
            .Bind(configuration.GetSection(MonitoringOptions.SectionName))
            .ValidateOnStart();

        // Scoped, not singleton: 003's HttpProbeRunner needs a scoped ISecretProtector, and
        // registering every IProbeRunner the same way means the scheduler never has to care
        // which lifetime a given kind picked.
        services.AddSingleton<IIcmpPinger, SystemIcmpPinger>();
        services.AddScoped<IProbeRunner, PingProbeRunner>();

        // Unconditional: IDataProtectionProvider resolves once the host is built regardless
        // of whether DataProtection:KeyRingPath is configured (that setting only controls
        // *where* keys persist — Program.cs' AddDataProtection().PersistKeysToFileSystem(...)
        // call is conditional on it, but Homon.Api's Microsoft.NET.Sdk.Web hosting defaults
        // register the provider itself unconditionally). See plan 003's Decision 3.
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();

        // One named client, redirects on, TLS opt-out per-request via HttpRequestOptions —
        // not a second client. *Rejected*: two named clients (strict/permissive) — doubles
        // every non-TLS setting for no benefit once the callback can read a per-request flag.
        // See plan 003's Decision 4.
        services.AddHttpClient(HttpProbeRunner.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = true,
                ServerCertificateCustomValidationCallback = (request, _, _, errors) =>
                    errors == SslPolicyErrors.None
                    || (request.Options.TryGetValue(HttpProbeRunner.IgnoreCertificateErrorsOption, out var ignore)
                        && ignore),
            });
        services.AddScoped<IProbeRunner, HttpProbeRunner>();

        services.AddHostedService<ProbeScheduler>();
        services.AddHostedService<ProbeObservationRetentionService>();
    }

    // Singleton: the sanitiser holds only immutable configuration set once in its constructor.
    private static void AddPages(this IServiceCollection services) =>
        services.AddSingleton<IPageHtmlSanitizer, PageHtmlSanitizer>();

    private static void AddWeather(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isProduction)
    {
        services.AddOptions<WeatherOptions>()
            .Bind(configuration.GetSection(WeatherOptions.SectionName))
            .Validate(
                options => options.Provider != WeatherProviderKind.Fake || !isProduction,
                "Weather:Provider=Fake refuses to start in Production.")
            .ValidateOnStart();

        // Defensive, idempotent no-op: AddHomonInfrastructure already registers this as its
        // first line, so this only matters if that registration is ever removed.
        services.TryAddSingleton(TimeProvider.System);

        services.AddHttpClient(OpenMeteoWeatherProvider.HttpClientName, client =>
            client.BaseAddress = new Uri("https://api.open-meteo.com/"));

        // Singleton, not scoped: WeatherCache below is itself a singleton and calls straight
        // through to whichever provider IWeatherProvider resolves to, and neither provider
        // holds a scoped dependency (OpenMeteoWeatherProvider's IHttpClientFactory and
        // FakeWeatherProvider's TimeProvider are both singletons themselves). Registering
        // either as scoped would make it a captive dependency the moment WeatherCache asked
        // for one, and the built-in container's scope validation — on by default in
        // Development, which is how every test host and the e2e API both run — would refuse
        // to start rather than silently capture it.
        services.AddSingleton<OpenMeteoWeatherProvider>();
        services.AddSingleton<FakeWeatherProvider>();

        // Both providers are registered; which one IWeatherProvider resolves to is decided
        // from IOptions<WeatherOptions> the first time something asks — never at registration
        // — for the same test-override reason as AddHomonEmail's IAlertEmailSender above.
        services.AddSingleton<IWeatherProvider>(serviceProvider =>
        {
            var weatherOptions = serviceProvider.GetRequiredService<IOptions<WeatherOptions>>().Value;

            return weatherOptions.Provider == WeatherProviderKind.Fake
                ? serviceProvider.GetRequiredService<FakeWeatherProvider>()
                : serviceProvider.GetRequiredService<OpenMeteoWeatherProvider>();
        });

        services.AddSingleton<WeatherCache>();
    }
}
