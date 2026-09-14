using Homon.Infrastructure.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Homon.Api.Tests;

/// <summary>
/// <see cref="HomonApiFactory"/> pointed at a real PostgreSQL clone of its own, with the
/// email sender replaced by <see cref="RecordingEmailSender"/>.
/// </summary>
public class ApiDatabaseFactory : DatabaseBackedFactory
{
    public RecordingEmailSender Emails { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Homon"] = ConnectionString,
                ["FrontEnd:PublicBaseUrl"] = "https://homon.test",
            }));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAlertEmailSender>();
            services.AddSingleton<IAlertEmailSender>(Emails);
        });
    }
}
