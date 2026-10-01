using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Homon.Api.Authentication;
using Homon.Domain.Auth;
using Homon.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homon.Api.Tests;

public class ReporterEndpointTests(ApiDatabaseFactory factory) : IClassFixture<ApiDatabaseFactory>
{
    [DatabaseFact]
    public async Task The_auth_matrix_refuses_anonymous_callers_and_every_api_key_on_every_route()
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        var (_, presented) = await new ApiKeyIssuer(database, TimeProvider.System)
            .IssueAsync("an administrative attempt", ApiKeyScope.ReadWrite);

        using var anonymous = TestClient.Create(factory);
        using var keyed = TestClient.Create(factory);
        keyed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", presented);

        var id = Guid.NewGuid();
        var body = new { name = "n" };

        // Reads are administrator-only here too: a reporter's history carries whole command
        // outputs, and the list carries the key handles.
        foreach (var route in new[] { "/api/v1/reporters", $"/api/v1/reporters/{id}/messages" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await keyed.GetAsync(route)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/reporters", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await keyed.PostAsJsonAsync("/api/v1/reporters", body)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync($"/api/v1/reporters/{id}", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await keyed.PutAsJsonAsync($"/api/v1/reporters/{id}", body)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync($"/api/v1/reporters/{id}/key", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await keyed.PostAsJsonAsync($"/api/v1/reporters/{id}/key", body)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"/api/v1/reporters/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await keyed.DeleteAsync($"/api/v1/reporters/{id}")).StatusCode);
    }

    [DatabaseFact]
    public async Task Creating_a_reporter_mints_a_read_write_key_and_reveals_it_exactly_once()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var name = $"clockmaster backup {Guid.NewGuid():N}";
        var created = await client.PostAsJsonAsync("/api/v1/reporters", new { name });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString()!;

        Assert.StartsWith("hmn_", token, StringComparison.Ordinal);
        Assert.Equal(16, body.GetProperty("reporter").GetProperty("identifier").GetString()!.Length);
        Assert.Equal("administrator", body.GetProperty("reporter").GetProperty("bodyVisibility").GetString());

        // The list is the surface an administrator refreshes; the secret must not be on it.
        var listed = await (await client.GetAsync("/api/v1/reporters")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(token, listed, StringComparison.Ordinal);
        Assert.DoesNotContain("\"token\"", listed, StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        var reporter = await database.Reporters.SingleAsync(r => r.Name == name);
        var key = await database.ApiKeys.SingleAsync(k => k.Id == reporter.ApiKeyId);

        Assert.Equal(ApiKeyScope.ReadWrite, key.Scope);
        Assert.Null(key.ExpiresAt);
    }

    [DatabaseFact]
    public async Task Two_reporters_cannot_differ_only_by_case()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var name = $"remontoire nas {Guid.NewGuid():N}";
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/reporters", new { name })).StatusCode);

        var duplicate = await client.PostAsJsonAsync("/api/v1/reporters", new { name = name.ToUpperInvariant() });

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        var problem = await duplicate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("name", out _));
    }

    [DatabaseTheory]
    [InlineData("", "name")]
    [InlineData("everyone", "bodyVisibility")]
    public async Task An_invalid_reporter_names_the_field_at_fault(string value, string expectedField)
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        object body = expectedField == "name"
            ? new { name = value }
            : new { name = $"r-{Guid.NewGuid():N}", bodyVisibility = value };

        var response = await client.PostAsJsonAsync("/api/v1/reporters", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty(expectedField, out _));
    }

    [DatabaseFact]
    public async Task The_history_is_the_only_route_that_returns_a_body()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var (reporterId, token) = await CreateReporterAsync(client);
        const string Secret = "repository /srv/backups on clockmaster";
        await ReportAsync(token, new { name = "clockmaster backup", status = "success", message = Secret });

        var listed = await (await client.GetAsync("/api/v1/reporters")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(Secret, listed, StringComparison.Ordinal);

        var history = await client.GetAsync($"/api/v1/reporters/{reporterId}/messages");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        Assert.Contains(Secret, await history.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [DatabaseFact]
    public async Task The_history_is_newest_first_and_its_limit_is_clamped()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var (reporterId, token) = await CreateReporterAsync(client);

        foreach (var index in Enumerable.Range(1, 3))
        {
            await ReportAsync(token, new { name = $"report {index}", status = "success" });
        }

        var all = await ReadHistoryAsync(client, reporterId, limit: null);
        Assert.Equal(3, all.GetArrayLength());
        Assert.Equal("report 3", all[0].GetProperty("name").GetString());

        Assert.Equal(1, (await ReadHistoryAsync(client, reporterId, limit: 1)).GetArrayLength());
        // Out-of-range limits are clamped rather than refused — a report list is not a place to
        // teach an administrator about pagination.
        Assert.Equal(3, (await ReadHistoryAsync(client, reporterId, limit: 0)).GetArrayLength());
        Assert.Equal(3, (await ReadHistoryAsync(client, reporterId, limit: 9999)).GetArrayLength());
    }

    [DatabaseFact]
    public async Task The_latest_report_and_the_message_count_appear_on_the_list()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var (reporterId, token) = await CreateReporterAsync(client);
        await ReportAsync(token, new { name = "first", status = "failure" });
        await ReportAsync(token, new { name = "second", status = "success", recurrence = "PT25H", category = "backup" });

        var reporter = await ReadReporterAsync(client, reporterId);

        Assert.Equal(2, reporter.GetProperty("messageCount").GetInt32());
        Assert.False(reporter.GetProperty("isWatched").GetBoolean());

        var latest = reporter.GetProperty("latest");
        Assert.Equal("second", latest.GetProperty("name").GetString());
        Assert.Equal("success", latest.GetProperty("status").GetString());
        Assert.Equal("backup", latest.GetProperty("category").GetString());
        Assert.Equal("PT25H", latest.GetProperty("recurrence").GetString());
    }

    [DatabaseFact]
    public async Task Replacing_a_key_mints_a_new_one_and_stops_the_old_one_working()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var (reporterId, oldToken) = await CreateReporterAsync(client);

        var rotated = await client.PostAsJsonAsync($"/api/v1/reporters/{reporterId}/key", new { });
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var newToken = (await rotated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;

        Assert.NotEqual(oldToken, newToken);
        Assert.Equal(HttpStatusCode.Unauthorized, await ReportStatusAsync(oldToken));
        Assert.Equal(HttpStatusCode.Created, await ReportStatusAsync(newToken));
    }

    [DatabaseFact]
    public async Task A_reporter_cannot_be_deleted_while_a_probe_watches_it()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var (reporterId, _) = await CreateReporterAsync(client);
        var identifier = (await ReadReporterAsync(client, reporterId)).GetProperty("identifier").GetString()!;

        var probe = await client.PostAsJsonAsync("/api/v1/probes", new
        {
            name = $"watcher-{Guid.NewGuid():N}",
            host = identifier,
            kind = "message",
            pollIntervalSeconds = 900,
        });
        Assert.Equal(HttpStatusCode.Created, probe.StatusCode);
        var probeBody = await probe.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True((await ReadReporterAsync(client, reporterId)).GetProperty("isWatched").GetBoolean());

        var refused = await client.DeleteAsync($"/api/v1/reporters/{reporterId}");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(
            probeBody.GetProperty("name").GetString()!,
            problem.GetProperty("detail").GetString(),
            StringComparison.Ordinal);

        await client.DeleteAsync($"/api/v1/probes/{probeBody.GetProperty("id").GetGuid()}");

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/reporters/{reporterId}")).StatusCode);
    }

    [DatabaseFact]
    public async Task Deleting_a_reporter_takes_its_messages_and_revokes_its_key()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var (reporterId, token) = await CreateReporterAsync(client);
        await ReportAsync(token, new { name = "n", status = "success" });

        Guid keyId;
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
            keyId = (await database.Reporters.SingleAsync(r => r.Id == reporterId)).ApiKeyId;
        }

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/reporters/{reporterId}")).StatusCode);

        using var verifyScope = factory.Services.CreateScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<HomonDbContext>();

        Assert.Equal(0, await verify.Messages.CountAsync(m => m.ReporterId == reporterId));
        // Revoked, not deleted: the row is the audit trail, and a key that authenticated but
        // resolved to nobody would 403 on every report with a confusing sentence.
        Assert.NotNull((await verify.ApiKeys.SingleAsync(k => k.Id == keyId)).RevokedAt);
    }

    [DatabaseFact]
    public async Task Updating_a_reporter_changes_its_label_and_visibility_but_never_its_identifier()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var (reporterId, _) = await CreateReporterAsync(client);
        var identifier = (await ReadReporterAsync(client, reporterId)).GetProperty("identifier").GetString();

        var updated = await client.PutAsJsonAsync($"/api/v1/reporters/{reporterId}", new
        {
            name = $"renamed-{Guid.NewGuid():N}",
            description = "Daily restic backup",
            bodyVisibility = "reader",
        });

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var body = await updated.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("reader", body.GetProperty("bodyVisibility").GetString());
        Assert.Equal("Daily restic backup", body.GetProperty("description").GetString());
        Assert.Equal(identifier, body.GetProperty("identifier").GetString());
    }

    [DatabaseFact]
    public async Task Unknown_reporters_are_not_found_rather_than_refused()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/v1/reporters/{id}", new { name = "n" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/v1/reporters/{id}/key", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/v1/reporters/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/reporters/{id}/messages")).StatusCode);
    }

    private static async Task<(Guid Id, string Token)> CreateReporterAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/reporters", new { name = $"reporter-{Guid.NewGuid():N}" });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        return (body.GetProperty("reporter").GetProperty("id").GetGuid(), body.GetProperty("token").GetString()!);
    }

    private static async Task<JsonElement> ReadReporterAsync(HttpClient client, Guid id)
    {
        var listed = await (await client.GetAsync("/api/v1/reporters")).Content.ReadFromJsonAsync<JsonElement>();

        return listed.EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id);
    }

    private static async Task<JsonElement> ReadHistoryAsync(HttpClient client, Guid id, int? limit)
    {
        var route = limit is null
            ? $"/api/v1/reporters/{id}/messages"
            : $"/api/v1/reporters/{id}/messages?limit={limit}";

        return await (await client.GetAsync(route)).Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task ReportAsync(string token, object body) =>
        Assert.Equal(HttpStatusCode.Created, await ReportStatusAsync(token, body));

    private async Task<HttpStatusCode> ReportStatusAsync(string token, object? body = null)
    {
        using var client = TestClient.Create(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/messages", body ?? new { name = "n", status = "success" });

        return response.StatusCode;
    }
}
