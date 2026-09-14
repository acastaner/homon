using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Homon.Infrastructure.Persistence;

/// <summary>
/// Builds a context for <c>dotnet ef</c> at design time.
/// </summary>
/// <remarks>
/// <para>
/// Without this, EF would boot the API host to find the context, which drags in options
/// validation and everything else a developer adding a migration should not have to
/// satisfy.
/// </para>
/// <para>
/// EF prefers a design-time factory over the startup project's service provider, so this
/// type is the <i>only</i> source of the connection string for <c>dotnet ef</c>. It must
/// therefore read the same user secrets the API reads at run time; a hard-coded fallback
/// would silently send migrations at the wrong database. Set
/// <c>HOMON_DESIGNTIME_CONNECTION</c> to point a single command somewhere else — a scratch
/// database, or a deployed one — without touching secrets. Adding a migration needs no
/// live server at all: any syntactically valid Npgsql connection string will do.
/// </para>
/// </remarks>
public sealed class HomonDbContextFactory : IDesignTimeDbContextFactory<HomonDbContext>
{
    /// <summary>Environment variable that overrides the configured connection string.</summary>
    public const string ConnectionOverrideVariable = "HOMON_DESIGNTIME_CONNECTION";

    public HomonDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<HomonDbContext>()
            .UseNpgsql(ResolveConnectionString(), npgsql =>
                npgsql.MigrationsAssembly(typeof(HomonDbContext).Assembly.FullName))
            .Options;

        return new HomonDbContext(options);
    }

    private static string ResolveConnectionString()
    {
        var connectionOverride = Environment.GetEnvironmentVariable(ConnectionOverrideVariable);

        if (!string.IsNullOrWhiteSpace(connectionOverride))
        {
            return connectionOverride;
        }

        // Shares the API's UserSecretsId, declared in this project's .csproj, so one store
        // serves both the running application and the migration tooling.
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<HomonDbContextFactory>(optional: true)
            .Build();

        return configuration.GetConnectionString(
                InfrastructureServiceCollectionExtensions.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"No design-time connection string. Set the '{InfrastructureServiceCollectionExtensions.ConnectionStringName}' "
                + $"connection string in user secrets, or set {ConnectionOverrideVariable}. "
                + "See docs/postgres-setup-dev.md.");
    }
}
