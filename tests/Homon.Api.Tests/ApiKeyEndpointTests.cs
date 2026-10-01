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

public class ApiKeyEndpointTests(ApiDatabaseFactory factory) : IClassFixture<ApiDatabaseFactory>
{
    [DatabaseFact]
    public async Task An_api_key_can_never_administer_api_keys()
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        var (key, presented) = await new ApiKeyIssuer(database, TimeProvider.System)
            .IssueAsync("a key reaching for the keys", ApiKeyScope.ReadWrite);

        using var anonymous = TestClient.Create(factory);
        using var keyed = TestClient.Create(factory);
        keyed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", presented);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/api-keys")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await keyed.GetAsync("/api/v1/api-keys")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/api-keys", new { name = "n" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await keyed.PostAsJsonAsync("/api/v1/api-keys", new { name = "n" })).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"/api/v1/api-keys/{key.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await keyed.DeleteAsync($"/api/v1/api-keys/{key.Id}")).StatusCode);
    }

    [DatabaseFact]
    public async Task Minting_reveals_the_key_once_and_the_list_never_does()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var created = await client.PostAsJsonAsync("/api/v1/api-keys", new { name = "a reading script", scope = "read" });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString()!;

        Assert.StartsWith("hmn_", token, StringComparison.Ordinal);
        Assert.Equal("read", body.GetProperty("scope").GetString());

        var listed = await (await client.GetAsync("/api/v1/api-keys")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(token, listed, StringComparison.Ordinal);
        Assert.DoesNotContain("\"token\"", listed, StringComparison.Ordinal);
    }

    [DatabaseFact]
    public async Task An_omitted_scope_is_read_because_least_privilege_is_the_default()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var created = await client.PostAsJsonAsync("/api/v1/api-keys", new { name = "no scope given" });

        Assert.Equal("read", (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("scope").GetString());
    }

    [DatabaseTheory]
    [InlineData("", null, null, "name")]
    [InlineData("n", "write-only", null, "scope")]
    [InlineData("n", null, "2000-01-01T00:00:00Z", "expiresAt")]
    public async Task An_invalid_key_request_names_the_field_at_fault(
        string name, string? scope, string? expiresAt, string expectedField)
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var response = await client.PostAsJsonAsync("/api/v1/api-keys", new { name, scope, expiresAt });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty(expectedField, out _));
    }

    [DatabaseFact]
    public async Task The_list_keeps_revoked_keys_and_computes_whether_one_has_expired()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var created = await client.PostAsJsonAsync("/api/v1/api-keys", new { name = $"to revoke {Guid.NewGuid():N}" });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/api-keys/{id}")).StatusCode);

        var row = await FindAsync(client, id);
        Assert.NotEqual(JsonValueKind.Null, row.GetProperty("revokedAt").ValueKind);
        Assert.False(row.GetProperty("isExpired").GetBoolean());

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        await database.ApiKeys.Where(k => k.Id == id)
            .ExecuteUpdateAsync(k => k.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddDays(-1)));

        Assert.True((await FindAsync(client, id)).GetProperty("isExpired").GetBoolean());
    }

    [DatabaseFact]
    public async Task Revoking_is_idempotent_and_keeps_the_first_revocations_timestamp()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var created = await client.PostAsJsonAsync("/api/v1/api-keys", new { name = $"twice {Guid.NewGuid():N}" });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/api-keys/{id}")).StatusCode);
        var first = (await FindAsync(client, id)).GetProperty("revokedAt").GetDateTimeOffset();

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/api-keys/{id}")).StatusCode);
        Assert.Equal(first, (await FindAsync(client, id)).GetProperty("revokedAt").GetDateTimeOffset());

        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/v1/api-keys/{Guid.NewGuid()}")).StatusCode);
    }

    [DatabaseFact]
    public async Task A_reporters_own_key_is_named_on_the_list_and_cannot_be_revoked_from_here()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var name = $"reporter-{Guid.NewGuid():N}";
        var created = await client.PostAsJsonAsync("/api/v1/reporters", new { name });
        var reporterId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("reporter").GetProperty("id").GetGuid();

        Guid keyId;
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
            keyId = (await database.Reporters.SingleAsync(r => r.Id == reporterId)).ApiKeyId;
        }

        Assert.Equal(name, (await FindAsync(client, keyId)).GetProperty("reporterName").GetString());

        // Revoking here would leave the reporter holding a dead credential with nothing on its own
        // page explaining why its reports stopped.
        var refused = await client.DeleteAsync($"/api/v1/api-keys/{keyId}");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains(
            name,
            (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString(),
            StringComparison.Ordinal);
    }

    private static async Task<JsonElement> FindAsync(HttpClient client, Guid id)
    {
        var listed = await (await client.GetAsync("/api/v1/api-keys")).Content.ReadFromJsonAsync<JsonElement>();

        return listed.EnumerateArray().Single(k => k.GetProperty("id").GetGuid() == id);
    }
}
