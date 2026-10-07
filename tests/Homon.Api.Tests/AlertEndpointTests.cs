using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Homon.Api.Authentication;
using Homon.Domain.Alerts;
using Homon.Domain.Auth;
using Homon.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homon.Api.Tests;

public class AlertEndpointTests(ApiDatabaseFactory factory) : IClassFixture<ApiDatabaseFactory>
{
    private const string FakeKey = "re_plain";

    [DatabaseFact]
    public async Task A_fresh_database_reports_the_defaults()
    {
        await ResetAsync();
        using var client = await SignedInAsync();

        var response = await client.GetAsync("/api/v1/alerts/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("isEnabled").GetBoolean());
        Assert.False(body.GetProperty("hasApiKey").GetBoolean());
        Assert.Equal("Homon", body.GetProperty("fromName").GetString());
        Assert.Equal(0, body.GetProperty("recipients").GetArrayLength());
    }

    [DatabaseFact]
    public async Task The_api_key_is_write_only_and_stored_protected()
    {
        await ResetAsync();
        using var client = await SignedInAsync();

        var put = await client.PutAsJsonAsync("/api/v1/alerts/settings", Body(apiKey: FakeKey));

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var text = await put.Content.ReadAsStringAsync();
        Assert.DoesNotContain(FakeKey, text, StringComparison.Ordinal);
        Assert.True(JsonDocument.Parse(text).RootElement.GetProperty("hasApiKey").GetBoolean());

        var stored = await StoredKeyAsync();
        Assert.NotNull(stored);
        Assert.NotEqual(FakeKey, stored);
        Assert.DoesNotContain(FakeKey, stored, StringComparison.Ordinal);

        var get = await client.GetStringAsync("/api/v1/alerts/settings");
        Assert.DoesNotContain(FakeKey, get, StringComparison.Ordinal);

        // null keeps the stored key.
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/v1/alerts/settings", Body(apiKey: null))).StatusCode);
        Assert.Equal(stored, await StoredKeyAsync());

        // An empty string clears it.
        var cleared = await client.PutAsJsonAsync("/api/v1/alerts/settings", Body(apiKey: "", isEnabled: false));
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Null(await StoredKeyAsync());
        Assert.False((await cleared.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("hasApiKey").GetBoolean());
    }

    [DatabaseFact]
    public async Task Saving_round_trips_the_sender_and_recipients()
    {
        await ResetAsync();
        using var client = await SignedInAsync();

        await client.PutAsJsonAsync("/api/v1/alerts/settings", Body(apiKey: FakeKey));
        var body = await client.GetFromJsonAsync<JsonElement>("/api/v1/alerts/settings");

        Assert.True(body.GetProperty("isEnabled").GetBoolean());
        Assert.Equal("alerts@example.test", body.GetProperty("fromAddress").GetString());
        Assert.Equal(
            ["a@example.test", "b@example.test"],
            body.GetProperty("recipients").EnumerateArray().Select(e => e.GetString()));
    }

    [DatabaseFact]
    public async Task Invalid_settings_are_refused_with_a_field_keyed_problem()
    {
        await ResetAsync();
        using var client = await SignedInAsync();

        // Switching on without a key.
        await AssertInvalidAsync(client, Body(apiKey: null), "isEnabled");
        // A bad recipient, a duplicate (case-insensitive), too many.
        await AssertInvalidAsync(client, Body(apiKey: FakeKey, recipients: ["not an address"]), "recipients");
        await AssertInvalidAsync(client, Body(apiKey: FakeKey, recipients: ["a@example.test", "A@Example.test"]), "recipients");
        await AssertInvalidAsync(
            client, Body(apiKey: FakeKey, recipients: [.. Enumerable.Range(0, 11).Select(i => $"r{i}@example.test")]), "recipients");
        // Header injection through the From name, and a display name smuggled into the address.
        await AssertInvalidAsync(client, Body(apiKey: FakeKey, fromName: "Homon\nBcc: x@y.z"), "fromName");
        await AssertInvalidAsync(client, Body(apiKey: FakeKey, fromAddress: "X <a@b.c>"), "fromAddress");
        // A key with whitespace in it.
        await AssertInvalidAsync(client, Body(apiKey: "re bad"), "apiKey");

        Assert.Null(await StoredKeyAsync());
    }

    [DatabaseFact]
    public async Task Alerts_are_administrator_only()
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        var (_, presented) = await new ApiKeyIssuer(database, TimeProvider.System)
            .IssueAsync("a key reaching for the alerts", ApiKeyScope.ReadWrite);

        using var anonymous = TestClient.Create(factory);
        using var keyed = TestClient.Create(factory);
        keyed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", presented);

        foreach (var (client, expected) in new[] { (anonymous, HttpStatusCode.Unauthorized), (keyed, HttpStatusCode.Forbidden) })
        {
            Assert.Equal(expected, (await client.GetAsync("/api/v1/alerts/settings")).StatusCode);
            Assert.Equal(expected, (await client.PutAsJsonAsync("/api/v1/alerts/settings", Body(apiKey: FakeKey))).StatusCode);
            Assert.Equal(expected, (await client.GetAsync("/api/v1/alerts/deliveries")).StatusCode);
            Assert.Equal(expected, (await client.PostAsJsonAsync("/api/v1/alerts/test", new { })).StatusCode);
        }
    }

    [DatabaseFact]
    public async Task A_test_cannot_be_queued_before_alerts_are_set_up()
    {
        await ResetAsync();
        using var client = await SignedInAsync();

        var response = await client.PostAsJsonAsync("/api/v1/alerts/test", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountAsync(AlertKind.Test));
    }

    [DatabaseFact]
    public async Task A_test_is_queued_once_however_often_it_is_asked_for()
    {
        await ResetAsync();
        using var client = await SignedInAsync();
        await client.PutAsJsonAsync("/api/v1/alerts/settings", Body(apiKey: FakeKey));

        var first = await client.PostAsJsonAsync("/api/v1/alerts/test", new { });
        var second = await client.PostAsJsonAsync("/api/v1/alerts/test", new { });

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(1, await CountAsync(AlertKind.Test));
        var body = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("test", body.GetProperty("kind").GetString());
        Assert.Equal("pending", body.GetProperty("state").GetString());
    }

    [DatabaseFact]
    public async Task A_bodiless_test_request_without_a_json_content_type_is_refused()
    {
        await ResetAsync();
        using var client = await SignedInAsync();
        await client.PutAsJsonAsync("/api/v1/alerts/settings", Body(apiKey: FakeKey));

        var response = await client.PostAsync(
            "/api/v1/alerts/test", new StringContent("{}", Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(0, await CountAsync(AlertKind.Test));
    }

    [DatabaseFact]
    public async Task Deliveries_are_newest_first_and_capped_at_twenty()
    {
        await ResetAsync();
        var start = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
            for (var i = 0; i < 25; i++)
            {
                database.AlertNotifications.Add(new AlertNotification
                {
                    Kind = AlertKind.Down,
                    ProbeName = $"probe-{i:00}",
                    OccurredAt = start.AddMinutes(i),
                    State = AlertDeliveryState.Sent,
                    NextAttemptAt = start,
                });
            }

            await database.SaveChangesAsync();
        }

        using var client = await SignedInAsync();
        var body = await client.GetFromJsonAsync<JsonElement>("/api/v1/alerts/deliveries");

        var names = body.EnumerateArray().Select(e => e.GetProperty("probeName").GetString()).ToList();
        Assert.Equal(20, names.Count);
        Assert.Equal("probe-24", names[0]);
        Assert.Equal("probe-05", names[^1]);
    }

    [DatabaseFact]
    public async Task The_openapi_document_lists_the_alert_routes()
    {
        using var client = await SignedInAsync();

        var document = await client.GetFromJsonAsync<JsonElement>("/api/v1/openapi.json");
        var paths = document.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();

        Assert.Contains("/api/v1/alerts/settings", paths);
        Assert.Contains("/api/v1/alerts/deliveries", paths);
        Assert.Contains("/api/v1/alerts/test", paths);
    }

    private static object Body(
        string? apiKey,
        bool isEnabled = true,
        string fromAddress = "alerts@example.test",
        string fromName = "Homon",
        string[]? recipients = null) =>
        new
        {
            isEnabled,
            apiKey,
            fromAddress,
            fromName,
            recipients = recipients ?? ["a@example.test", "b@example.test"],
        };

    private static async Task AssertInvalidAsync(HttpClient client, object body, string field)
    {
        var response = await client.PutAsJsonAsync("/api/v1/alerts/settings", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), $"expected an error on {field}");
    }

    private async Task<HttpClient> SignedInAsync()
    {
        var client = TestClient.Create(factory);
        await client.SignInAsync();
        return client;
    }

    private async Task ResetAsync()
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        await database.AlertNotifications.ExecuteDeleteAsync();
        await database.AlertSettings.ExecuteDeleteAsync();
    }

    private async Task<string?> StoredKeyAsync()
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        return await database.AlertSettings
            .AsNoTracking()
            .Select(s => s.ProtectedApiKey)
            .SingleOrDefaultAsync();
    }

    private async Task<int> CountAsync(AlertKind kind)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        return await database.AlertNotifications.CountAsync(n => n.Kind == kind);
    }
}
