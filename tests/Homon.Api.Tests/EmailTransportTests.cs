using Homon.Infrastructure.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Homon.Api.Tests;

public class EmailTransportTests
{
    [Fact]
    public void Without_a_token_the_logging_sender_is_resolved()
    {
        using var factory = new HomonApiFactory();
        using var scope = factory.Services.CreateScope();

        Assert.IsType<LoggingEmailSender>(scope.ServiceProvider.GetRequiredService<IAlertEmailSender>());
    }

    [Fact]
    public void With_a_token_the_resend_sender_is_resolved()
    {
        using var factory = new ConfiguredFactory(Environments.Development, "re_test_token");
        using var scope = factory.Services.CreateScope();

        Assert.IsType<ResendEmailSender>(scope.ServiceProvider.GetRequiredService<IAlertEmailSender>());
    }

    [Fact]
    public void A_production_host_refuses_to_start_without_a_token()
    {
        using var factory = new ConfiguredFactory(Environments.Production, null);

        var exception = Assert.ThrowsAny<Exception>(() => factory.Services);

        Assert.Contains("Email:ResendApiToken is not configured", Flatten(exception), StringComparison.Ordinal);
    }

    [Fact]
    public void Administrator_options_must_come_as_a_pair()
    {
        using var factory = new AdministratorOverrideFactory("admin@example.test", null);

        var exception = Assert.ThrowsAny<Exception>(() => factory.Services);

        Assert.Contains("must be set together", Flatten(exception), StringComparison.Ordinal);
    }

    [Fact]
    public void Alert_recipients_bind_as_a_list()
    {
        using var factory = new HomonApiFactory();
        using var scope = factory.Services.CreateScope();

        var options = scope.ServiceProvider.GetRequiredService<IOptions<EmailOptions>>().Value;

        Assert.Empty(options.AlertRecipients);
        Assert.Equal("Homon", options.FromName);
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

    private sealed class ConfiguredFactory(string environment, string? token) : HomonApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.UseEnvironment(environment);

            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Email:ResendApiToken"] = token,
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
