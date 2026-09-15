using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Homon.Api.Authentication;
using Homon.Api.Configuration;
using Homon.Domain.Auth;
using Homon.Domain.Monitoring;
using Homon.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Homon.Api.Tests;

public class ProbeGroupEndpointTests(ApiDatabaseFactory factory) : IClassFixture<ApiDatabaseFactory>
{
    [DatabaseFact]
    public async Task POST_creates_a_group_and_puts_it_last()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var first = await CreateGroupAsync(client, "First group");
        var second = await CreateGroupAsync(client, "Second group");

        Assert.Equal(HttpStatusCode.Created, first.Response.StatusCode);
        Assert.Equal(0, first.Body.GetProperty("probeIds").GetArrayLength());

        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/probe-groups");
        var names = list.EnumerateArray().Select(g => g.GetProperty("name").GetString()).ToArray();
        var firstIndex = Array.IndexOf(names, "First group");
        var secondIndex = Array.IndexOf(names, "Second group");

        Assert.True(secondIndex > firstIndex);
    }

    [DatabaseTheory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task POST_rejects_an_empty_name(string name)
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var response = await client.PostAsJsonAsync("/api/v1/probe-groups", new { name });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [DatabaseFact]
    public async Task POST_rejects_a_name_over_the_max_length()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/probe-groups", new { name = new string('x', ProbeGroup.NameMaxLength + 1) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [DatabaseFact]
    public async Task POST_rejects_a_duplicate_name_differing_only_in_case()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        await CreateGroupAsync(client, "Storage Group");

        var response = await client.PostAsJsonAsync("/api/v1/probe-groups", new { name = "storage group" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [DatabaseFact]
    public async Task Order_accepts_a_permutation_and_rejects_missing_extra_or_duplicate_ids()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var a = await CreateGroupAsync(client, "Order A");
        var b = await CreateGroupAsync(client, "Order B");
        var c = await CreateGroupAsync(client, "Order C");

        var aId = a.Body.GetProperty("id").GetGuid();
        var bId = b.Body.GetProperty("id").GetGuid();
        var cId = c.Body.GetProperty("id").GetGuid();

        // The class shares one database clone across its methods, so "every existing group"
        // includes whatever earlier tests created too — read the full set back first, rather
        // than assuming these three are the whole table.
        var existing = await client.GetFromJsonAsync<JsonElement>("/api/v1/probe-groups");
        var allIds = existing.EnumerateArray().Select(g => g.GetProperty("id").GetGuid()).ToList();

        var reordered = await client.PutAsJsonAsync("/api/v1/probe-groups/order", new { groupIds = allIds });
        Assert.Equal(HttpStatusCode.NoContent, reordered.StatusCode);

        var missing = await client.PutAsJsonAsync(
            "/api/v1/probe-groups/order", new { groupIds = new[] { aId, bId } });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var extra = await client.PutAsJsonAsync(
            "/api/v1/probe-groups/order", new { groupIds = allIds.Append(Guid.NewGuid()).ToArray() });
        Assert.Equal(HttpStatusCode.BadRequest, extra.StatusCode);

        var duplicate = await client.PutAsJsonAsync(
            "/api/v1/probe-groups/order", new { groupIds = new[] { aId, aId, bId, cId } });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
    }

    [DatabaseFact]
    public async Task Members_sets_the_order_and_rejects_unknown_or_duplicate_ids()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var group = await CreateGroupAsync(client, "Members group");
        var groupId = group.Body.GetProperty("id").GetGuid();

        var probeA = await CreateProbeAsync(client, "Member A");
        var probeB = await CreateProbeAsync(client, "Member B");
        var aId = probeA.GetProperty("id").GetGuid();
        var bId = probeB.GetProperty("id").GetGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/v1/probe-groups/{groupId}/members", new { probeIds = new[] { bId, aId } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var probeIds = body.GetProperty("probeIds").EnumerateArray().Select(p => p.GetGuid()).ToArray();
        Assert.Equal([bId, aId], probeIds);

        var unknown = await client.PutAsJsonAsync(
            $"/api/v1/probe-groups/{groupId}/members", new { probeIds = new[] { Guid.NewGuid() } });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        var duplicate = await client.PutAsJsonAsync(
            $"/api/v1/probe-groups/{groupId}/members", new { probeIds = new[] { aId, aId } });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
    }

    [DatabaseFact]
    public async Task Deleting_a_group_keeps_its_probes()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var group = await CreateGroupAsync(client, "Doomed group");
        var groupId = group.Body.GetProperty("id").GetGuid();

        var probe = await CreateProbeAsync(client, "Surviving probe", groupIds: [groupId]);
        var probeId = probe.GetProperty("id").GetGuid();

        var deleteResponse = await client.DeleteAsync($"/api/v1/probe-groups/{groupId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var probeResponse = await client.GetFromJsonAsync<JsonElement>($"/api/v1/probes/{probeId}");
        Assert.Equal("Surviving probe", probeResponse.GetProperty("name").GetString());
        Assert.Equal(0, probeResponse.GetProperty("groupIds").GetArrayLength());
    }

    [DatabaseFact]
    public async Task Deleting_a_probe_leaves_the_other_members_relative_order_intact()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var group = await CreateGroupAsync(client, "Ordered group");
        var groupId = group.Body.GetProperty("id").GetGuid();

        var a = await CreateProbeAsync(client, "Ordered A");
        var b = await CreateProbeAsync(client, "Ordered B");
        var c = await CreateProbeAsync(client, "Ordered C");
        var aId = a.GetProperty("id").GetGuid();
        var bId = b.GetProperty("id").GetGuid();
        var cId = c.GetProperty("id").GetGuid();

        await client.PutAsJsonAsync($"/api/v1/probe-groups/{groupId}/members", new { probeIds = new[] { aId, bId, cId } });

        var deleteResponse = await client.DeleteAsync($"/api/v1/probes/{bId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var groupResponse = await client.GetFromJsonAsync<JsonElement>($"/api/v1/probe-groups/{groupId}");
        var remaining = groupResponse.GetProperty("probeIds").EnumerateArray().Select(p => p.GetGuid()).ToArray();
        Assert.Equal([aId, cId], remaining);
    }

    [DatabaseFact]
    public async Task An_unknown_group_is_404()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/probe-groups/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/v1/probe-groups/{id}", new { name = "n" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/v1/probe-groups/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/v1/probe-groups/{id}/members", new { probeIds = Array.Empty<Guid>() })).StatusCode);
    }

    [DatabaseFact]
    public async Task The_write_auth_matrix_refuses_anonymous_callers_and_every_api_key()
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        var (_, presented) = await new ApiKeyIssuer(database, TimeProvider.System)
            .IssueAsync("group write attempt", ApiKeyScope.ReadWrite);

        using var anonymous = TestClient.Create(factory);
        using var keyed = TestClient.Create(factory);
        keyed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", presented);

        var body = new { name = "n" };

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/probe-groups", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await keyed.PostAsJsonAsync("/api/v1/probe-groups", body)).StatusCode);

        var groupId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync($"/api/v1/probe-groups/{groupId}", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await keyed.PutAsJsonAsync($"/api/v1/probe-groups/{groupId}", body)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"/api/v1/probe-groups/{groupId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await keyed.DeleteAsync($"/api/v1/probe-groups/{groupId}")).StatusCode);
    }

    [DatabaseFact]
    public async Task GET_is_open_by_default()
    {
        using var anonymous = TestClient.Create(factory);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/v1/probe-groups")).StatusCode);
    }

    [Fact]
    public async Task GET_is_refused_anonymously_once_readers_must_sign_in()
    {
        // The ReaderHandler refuses an anonymous caller before the endpoint ever runs, so
        // this needs no database at all — same pattern as MetaEndpointTests.cs's
        // ConfiguredFactory, on the deliberately unreachable connection string every plain
        // HomonApiFactory carries.
        using var gated = new ConfiguredFactory();
        using var gatedClient = TestClient.Create(gated);

        Assert.Equal(HttpStatusCode.Unauthorized, (await gatedClient.GetAsync("/api/v1/probe-groups")).StatusCode);
    }

    private static async Task<(HttpResponseMessage Response, JsonElement Body)> CreateGroupAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/probe-groups", new { name });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        return (response, body);
    }

    private static async Task<JsonElement> CreateProbeAsync(HttpClient client, string name, Guid[]? groupIds = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/probes", new
        {
            name,
            host = $"{name}.test",
            kind = "ping",
            pollIntervalSeconds = 30,
            failureThreshold = 2,
            groupIds = groupIds ?? [],
        });

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>A <see cref="HomonApiFactory"/> with <c>Auth:RequireSignInForReaders</c> forced on.</summary>
    private sealed class ConfiguredFactory : HomonApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:RequireSignInForReaders"] = "true",
                }));
        }
    }
}
