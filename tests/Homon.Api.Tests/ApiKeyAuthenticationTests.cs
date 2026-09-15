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

public class ApiKeyAuthenticationTests(ApiDatabaseFactory factory) : IClassFixture<ApiDatabaseFactory>
{
    [DatabaseFact]
    public async Task A_minted_key_authenticates_as_itself_through_either_header()
    {
        var (key, presented) = await IssueAsync("clockmaster restic");

        using var bearer = TestClient.Create(factory);
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", presented);

        var viaBearer = await bearer.GetFromJsonAsync<JsonElement>("/api/v1/auth/session");

        Assert.Equal("apiKey", viaBearer.GetProperty("kind").GetString());
        Assert.Equal("clockmaster restic", viaBearer.GetProperty("name").GetString());

        using var direct = TestClient.Create(factory);
        direct.DefaultRequestHeaders.Add(ApiKeyRules.HeaderName, presented);

        var viaHeader = await direct.GetFromJsonAsync<JsonElement>("/api/v1/auth/session");

        Assert.Equal("apiKey", viaHeader.GetProperty("kind").GetString());

        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<HomonDbContext>()
            .ApiKeys.SingleAsync(k => k.Id == key.Id);

        Assert.NotNull(stored.LastUsedAt);
        Assert.StartsWith(ApiKeyRules.Prefix + stored.TokenId + "_", presented, StringComparison.Ordinal);
    }

    [DatabaseFact]
    public async Task A_revoked_key_is_refused_with_a_reason()
    {
        var (key, presented) = await IssueAsync("retired");

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<HomonDbContext>()
                .ApiKeys.Where(k => k.Id == key.Id)
                .ExecuteUpdateAsync(k => k.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
        }

        using var client = TestClient.Create(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", presented);

        var response = await client.GetAsync("/api/v1/auth/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("That API key has been revoked.", problem.GetProperty("detail").GetString());
    }

    [DatabaseFact]
    public async Task A_bad_key_fails_the_request_rather_than_demoting_it_to_anonymous()
    {
        // /auth/session is anonymous-friendly (204), which is exactly the trap: without the
        // refusal middleware a mistyped key would be indistinguishable from no key at all.
        var (_, presented) = await IssueAsync("real");
        var tampered = presented[..^4] + "XXXX";

        using var client = TestClient.Create(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tampered);

        var response = await client.GetAsync("/api/v1/auth/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("That API key is not recognised.", problem.GetProperty("detail").GetString());
    }

    [DatabaseFact]
    public async Task An_expired_key_is_refused_with_a_reason()
    {
        var (key, presented) = await IssueAsync("retired by time");

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<HomonDbContext>()
                .ApiKeys.Where(k => k.Id == key.Id)
                .ExecuteUpdateAsync(k => k.SetProperty(
                    x => x.ExpiresAt, DateTimeOffset.UtcNow.AddDays(-1)));
        }

        using var client = TestClient.Create(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", presented);

        var response = await client.GetAsync("/api/v1/auth/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("That API key has expired.", problem.GetProperty("detail").GetString());
    }

    [DatabaseFact]
    public async Task A_key_with_a_future_expiry_still_authenticates()
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();

        var (_, presented) = await new ApiKeyIssuer(database, TimeProvider.System).IssueAsync(
            "not yet retired", ApiKeyScope.Read, DateTimeOffset.UtcNow.AddDays(1));

        using var client = TestClient.Create(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", presented);

        var session = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/session");

        Assert.Equal("apiKey", session.GetProperty("kind").GetString());
    }

    [DatabaseTheory]
    [InlineData(ApiKeyScope.Read, "read")]
    [InlineData(ApiKeyScope.ReadWrite, "readWrite")]
    public async Task The_session_reports_the_keys_scope(ApiKeyScope scope, string expected)
    {
        using var issuingScope = factory.Services.CreateScope();
        var database = issuingScope.ServiceProvider.GetRequiredService<HomonDbContext>();

        var (_, presented) = await new ApiKeyIssuer(database, TimeProvider.System)
            .IssueAsync("scoped key", scope);

        using var client = TestClient.Create(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", presented);

        var session = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/session");

        Assert.Equal(expected, session.GetProperty("scope").GetString());
    }

    private async Task<(Homon.Domain.Auth.ApiKey Key, string Presented)> IssueAsync(string name)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();

        return await new ApiKeyIssuer(database, TimeProvider.System).IssueAsync(name);
    }
}
