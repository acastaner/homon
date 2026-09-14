using Homon.Infrastructure.Administration;
using Homon.Infrastructure.Email;
using Homon.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Resend;

namespace Homon.Infrastructure;

/// <summary>
/// Registers everything Homon needs to reach the outside world: the database, the email
/// transport, and the configuration-supplied administrator.
/// </summary>
/// <remarks>
/// Authentication schemes, cookie settings, and authorisation policies are deliberately
/// <i>not</i> registered here — they live in the API host so the whole auth story can be
/// read in one place. The probe runners and the scheduler will register here when the
/// Monitoring module lands.
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

        services.AddHomonDatabase();
        services.AddHomonEmail(configuration, isProduction);
        services.AddAdministrator(configuration);

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
}
