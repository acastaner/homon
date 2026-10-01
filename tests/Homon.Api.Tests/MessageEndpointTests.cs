using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Homon.Api.Authentication;
using Homon.Domain.Auth;
using Homon.Domain.Messaging;
using Homon.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homon.Api.Tests;

public class MessageEndpointTests(ApiDatabaseFactory factory) : IClassFixture<ApiDatabaseFactory>
{
    [DatabaseFact]
    public async Task The_ingest_auth_matrix_admits_only_a_read_write_key_paired_with_a_reporter()
    {
        var (_, token) = await SeedReporterAsync();
        var body = new { name = "clockmaster backup", status = "success" };

        using var anonymous = TestClient.Create(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/messages", body)).StatusCode);

        // A browser must never be able to file a report, even as the administrator.
        using var admin = TestClient.Create(factory);
        await admin.SignInAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/v1/messages", body)).StatusCode);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        var issuer = new ApiKeyIssuer(database, TimeProvider.System);

        // Authenticates, but may not write: 403, not 401.
        var (_, readKey) = await issuer.IssueAsync("a read key", ApiKeyScope.Read);
        Assert.Equal(HttpStatusCode.Forbidden, (await Keyed(readKey).PostAsJsonAsync("/api/v1/messages", body)).StatusCode);

        // May write, but is nobody's credential: 403 with a sentence that says so.
        var (_, unpaired) = await issuer.IssueAsync("an unpaired key", ApiKeyScope.ReadWrite);
        var unpairedResponse = await Keyed(unpaired).PostAsJsonAsync("/api/v1/messages", body);
        Assert.Equal(HttpStatusCode.Forbidden, unpairedResponse.StatusCode);
        var problem = await unpairedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("not paired with a reporter", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.Created, (await Keyed(token).PostAsJsonAsync("/api/v1/messages", body)).StatusCode);
    }

    [DatabaseFact]
    public async Task A_revoked_key_stops_reporting_rather_than_being_demoted_to_anonymous()
    {
        var (reporter, token) = await SeedReporterAsync();

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        await database.ApiKeys
            .Where(k => k.Id == reporter.ApiKeyId)
            .ExecuteUpdateAsync(k => k.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));

        var response = await Keyed(token).PostAsJsonAsync("/api/v1/messages", new { name = "n", status = "success" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [DatabaseFact]
    public async Task An_identifier_that_is_not_the_keys_reporter_is_refused_and_files_nothing()
    {
        var (reporter, token) = await SeedReporterAsync();

        var response = await Keyed(token).PostAsJsonAsync(
            "/api/v1/messages",
            new { identifier = "SOMEBODYELSE0000", name = "n", status = "success" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("identifier", out _));

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        Assert.Equal(0, await database.Messages.CountAsync(m => m.ReporterId == reporter.Id));
    }

    [DatabaseFact]
    public async Task The_keys_own_identifier_is_accepted_as_a_typo_guard()
    {
        var (reporter, token) = await SeedReporterAsync();

        var response = await Keyed(token).PostAsJsonAsync(
            "/api/v1/messages",
            new { identifier = reporter.Identifier, name = "n", status = "success" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [DatabaseTheory]
    [MemberData(nameof(InvalidBodies))]
    public async Task An_invalid_report_names_the_field_at_fault(object body, string expectedField)
    {
        var (_, token) = await SeedReporterAsync();

        var response = await Keyed(token).PostAsJsonAsync("/api/v1/messages", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(
            problem.GetProperty("errors").TryGetProperty(expectedField, out _),
            $"expected an error on '{expectedField}', got {problem.GetProperty("errors")}");
    }

    public static TheoryData<object, string> InvalidBodies() => new()
    {
        { new { status = "success" }, "name" },
        { new { name = "", status = "success" }, "name" },
        { new { name = new string('n', 101), status = "success" }, "name" },
        { new { name = "n", description = new string('d', 281), status = "success" }, "description" },
        { new { name = "n", status = "exploded" }, "status" },
        { new { name = "n", status = "success", category = "Backup Daily" }, "category" },
        { new { name = "n", status = "success", category = "-backup" }, "category" },
        { new { name = "n", status = "success", recurrence = "25h" }, "recurrence" },
        { new { name = "n", status = "success", recurrence = "PT0S" }, "recurrence" },
        { new { name = "n", status = "success", recurrence = "P99Y" }, "recurrence" },
        { new { name = "n", status = "success", expectNextBy = "2000-01-01T00:00:00Z" }, "expectNextBy" },
        { new { name = "n", status = "success", recurrence = "PT25H", expectNextBy = "2099-01-01T00:00:00Z" }, "recurrence" },
    };

    [DatabaseFact]
    public async Task A_duration_becomes_a_deadline_the_response_echoes_back()
    {
        var (_, token) = await SeedReporterAsync();

        var response = await Keyed(token).PostAsJsonAsync(
            "/api/v1/messages",
            new { name = "n", status = "success", recurrence = "PT25H" });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var receivedAt = body.GetProperty("receivedAt").GetDateTimeOffset();
        var nextExpectedAt = body.GetProperty("nextExpectedAt").GetDateTimeOffset();

        Assert.Equal(receivedAt.AddHours(25), nextExpectedAt);
        Assert.Equal("PT25H", body.GetProperty("recurrence").GetString());
    }

    [DatabaseFact]
    public async Task A_report_with_no_recurrence_has_no_deadline_and_defaults_its_vocabulary()
    {
        var (_, token) = await SeedReporterAsync();

        var response = await Keyed(token).PostAsJsonAsync("/api/v1/messages", new { name = "n" });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(JsonValueKind.Null, body.GetProperty("nextExpectedAt").ValueKind);
        Assert.Equal("none", body.GetProperty("status").GetString());
        Assert.Equal(Message.DefaultCategory, body.GetProperty("category").GetString());
    }

    [DatabaseFact]
    public async Task An_oversized_body_is_truncated_rather_than_refused()
    {
        var (reporter, token) = await SeedReporterAsync();
        var huge = new string('x', 200_000) + "snapshot a1b2c3 saved";

        var response = await Keyed(token).PostAsJsonAsync(
            "/api/v1/messages",
            new { name = "n", status = "success", message = huge });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("truncated").GetBoolean());

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        var stored = await database.Messages.SingleAsync(m => m.ReporterId == reporter.Id);

        Assert.StartsWith(MessageBody.TruncationMarker, stored.Body, StringComparison.Ordinal);
        Assert.EndsWith("snapshot a1b2c3 saved", stored.Body, StringComparison.Ordinal);
        Assert.True(stored.Body!.Length <= Message.BodyMaxBytes + MessageBody.TruncationMarker.Length);
    }

    [DatabaseFact]
    public async Task A_category_is_normalised_rather_than_refused_for_its_case()
    {
        var (_, token) = await SeedReporterAsync();

        var response = await Keyed(token).PostAsJsonAsync(
            "/api/v1/messages",
            new { name = "n", status = "success", category = "NAS-Array" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("nas-array", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("category").GetString());
    }

    [DatabaseFact]
    public async Task A_report_without_a_json_content_type_is_refused()
    {
        var (_, token) = await SeedReporterAsync();
        using var client = Keyed(token);

        using var content = new StringContent("{\"name\":\"n\"}");
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        var response = await client.PostAsync("/api/v1/messages", content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    private HttpClient Keyed(string token)
    {
        var client = TestClient.Create(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private async Task<(Reporter Reporter, string Token)> SeedReporterAsync()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/reporters",
            new { name = $"reporter-{Guid.NewGuid():N}" });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("reporter").GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();

        return (await database.Reporters.SingleAsync(r => r.Id == id), body.GetProperty("token").GetString()!);
    }
}
