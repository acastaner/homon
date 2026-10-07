using System.Net;
using System.Text;
using Homon.Domain.Alerts;
using Homon.Infrastructure.Email;
using Homon.Infrastructure.Persistence;
using Homon.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homon.Api.Tests;

/// <summary>
/// <see cref="ResendEmailSender"/> against a real settings row and a stubbed
/// <see cref="HttpClient"/> — the gate never reaches the network.
/// </summary>
public class ResendEmailSenderTests(ApiDatabaseFactory factory) : IClassFixture<ApiDatabaseFactory>
{
    private static readonly EmailMessage Message =
        new(["a@example.test", "b@example.test"], "[Homon] Test alert", "text body", "<p>html body</p>");

    [DatabaseFact]
    public async Task Without_a_settings_row_the_send_fails_without_touching_the_network()
    {
        await ClearAsync();
        var handler = new StubHttpMessageHandler();

        var exception = await Assert.ThrowsAsync<EmailSendException>(() => BuildSender(handler).SendAsync(Message));

        Assert.Contains("No Resend API key", exception.Message, StringComparison.Ordinal);
        Assert.Null(handler.LastRequest);
    }

    [DatabaseFact]
    public async Task A_key_the_key_ring_cannot_read_says_to_enter_it_again()
    {
        await SeedAsync(protectedKey: "not-a-protected-payload");
        var handler = new StubHttpMessageHandler();

        var exception = await Assert.ThrowsAsync<EmailSendException>(() => BuildSender(handler).SendAsync(Message));

        Assert.Contains("can no longer be read", exception.Message, StringComparison.Ordinal);
        Assert.Null(handler.LastRequest);
    }

    [DatabaseFact]
    public async Task A_send_goes_to_resend_with_the_stored_key_and_sender()
    {
        using var scope = factory.Services.CreateScope();
        var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
        await SeedAsync(protectedKey: protector.Protect("re_plain"));

        var handler = new StubHttpMessageHandler
        {
            Handler = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":\"11111111-1111-1111-1111-111111111111\"}", Encoding.UTF8, "application/json"),
            }),
        };

        await BuildSender(handler).SendAsync(Message);

        var request = Assert.IsType<HttpRequestMessage>(handler.LastRequest);
        Assert.Equal("api.resend.com", request.RequestUri!.Host);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("re_plain", request.Headers.Authorization.Parameter);
        using var payload = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        Assert.Equal("Homon <alerts@example.test>", payload.RootElement.GetProperty("from").GetString());
        Assert.Equal(
            ["a@example.test", "b@example.test"],
            payload.RootElement.GetProperty("to").EnumerateArray().Select(e => e.GetString()));
    }

    [DatabaseFact]
    public async Task A_refusal_from_resend_becomes_an_email_send_exception_that_never_carries_the_key()
    {
        using var scope = factory.Services.CreateScope();
        var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
        await SeedAsync(protectedKey: protector.Protect("re_plain"));

        var handler = new StubHttpMessageHandler
        {
            Handler = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(
                    "{\"statusCode\":401,\"name\":\"validation_error\",\"message\":\"API key is invalid\"}",
                    Encoding.UTF8,
                    "application/json"),
            }),
        };

        var exception = await Assert.ThrowsAsync<EmailSendException>(() => BuildSender(handler).SendAsync(Message));

        Assert.DoesNotContain("re_plain", exception.ToString(), StringComparison.Ordinal);
    }

    private ResendEmailSender BuildSender(StubHttpMessageHandler handler)
    {
        var scope = factory.Services.CreateScope();

        return new ResendEmailSender(
            scope.ServiceProvider.GetRequiredService<HomonDbContext>(),
            scope.ServiceProvider.GetRequiredService<ISecretProtector>(),
            new FakeHttpClientFactory(ResendEmailSender.HttpClientName, handler));
    }

    private async Task ClearAsync()
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        await database.AlertSettings.ExecuteDeleteAsync();
    }

    private async Task SeedAsync(string protectedKey)
    {
        await ClearAsync();

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        database.AlertSettings.Add(new AlertSettings
        {
            IsEnabled = true,
            ProtectedApiKey = protectedKey,
            FromAddress = "alerts@example.test",
            FromName = "Homon",
            Recipients = ["a@example.test"],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await database.SaveChangesAsync();
    }
}
