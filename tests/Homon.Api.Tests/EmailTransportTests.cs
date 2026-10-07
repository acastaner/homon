using Homon.Infrastructure.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Homon.Api.Tests;

public class EmailTransportTests
{
    [Fact]
    public void The_test_host_logs_instead_of_sending()
    {
        // HomonApiFactory sets Email:Transport to Log: the gate never mails anyone.
        using var factory = new HomonApiFactory();
        using var scope = factory.Services.CreateScope();

        Assert.IsType<LoggingEmailSender>(scope.ServiceProvider.GetRequiredService<IAlertEmailSender>());
    }

    [Fact]
    public void By_default_the_resend_sender_is_resolved()
    {
        // Resend is the default, so a host that says nothing sends for real — which is why the
        // test factory has to say Log, and why this test has to ask for Resend explicitly.
        using var factory = new ConfiguredFactory(Environments.Development, "Resend");
        using var scope = factory.Services.CreateScope();

        Assert.IsType<ResendEmailSender>(scope.ServiceProvider.GetRequiredService<IAlertEmailSender>());
    }

    [Fact]
    public void With_the_log_transport_the_logging_sender_is_resolved()
    {
        using var factory = new ConfiguredFactory(Environments.Development, "Log");
        using var scope = factory.Services.CreateScope();

        Assert.IsType<LoggingEmailSender>(scope.ServiceProvider.GetRequiredService<IAlertEmailSender>());
    }

    [Fact]
    public void A_production_host_refuses_to_start_with_the_log_transport()
    {
        using var factory = new ConfiguredFactory(Environments.Production, "Log");

        var exception = Assert.ThrowsAny<Exception>(() => factory.Services);

        Assert.Contains("Email:Transport is Log", Flatten(exception), StringComparison.Ordinal);
    }

    [Fact]
    public void Administrator_options_must_come_as_a_pair()
    {
        using var factory = new AdministratorOverrideFactory("admin@example.test", null);

        var exception = Assert.ThrowsAny<Exception>(() => factory.Services);

        Assert.Contains("must be set together", Flatten(exception), StringComparison.Ordinal);
    }

    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }

    private sealed class ConfiguredFactory(string environment, string transport) : HomonApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.UseEnvironment(environment);

            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Email:Transport"] = transport,
                }));
        }
    }

    private sealed class AdministratorOverrideFactory(string? email, string? hash) : HomonApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Administrator:Email"] = email,
                    ["Administrator:PasswordHash"] = hash,
                }));
        }
    }
}
