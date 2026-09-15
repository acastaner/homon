using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Homon.Api.Tests;

/// <summary>
/// The whole-table behaviour of <c>/probes</c> — an empty list on a fresh database, and
/// <c>PUT /probes/order</c>'s "must be an exact permutation of every existing id" rule —
/// needs a database clone with nothing else in it, so this gets its own
/// <see cref="ApiDatabaseFactory"/> rather than sharing <see cref="ProbeEndpointTests"/>' one.
/// </summary>
public class ProbeOrderingEndpointTests(ApiDatabaseFactory factory) : IClassFixture<ApiDatabaseFactory>
{
    [DatabaseFact]
    public async Task GET_on_a_fresh_database_returns_an_empty_array()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var response = await client.GetAsync("/api/v1/probes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetArrayLength());
    }

    [DatabaseFact]
    public async Task Order_accepts_a_permutation_and_rejects_missing_extra_or_duplicate_ids()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var a = await CreateAsync(client, "A");
        var b = await CreateAsync(client, "B");
        var c = await CreateAsync(client, "C");

        var aId = a.GetProperty("id").GetGuid();
        var bId = b.GetProperty("id").GetGuid();
        var cId = c.GetProperty("id").GetGuid();

        var reordered = await client.PutAsJsonAsync("/api/v1/probes/order", new { probeIds = new[] { cId, aId, bId } });
        Assert.Equal(HttpStatusCode.NoContent, reordered.StatusCode);

        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/probes");
        var order = list.EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToArray();
        Assert.Equal([cId, aId, bId], order);

        var missing = await client.PutAsJsonAsync("/api/v1/probes/order", new { probeIds = new[] { aId, bId } });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var extra = await client.PutAsJsonAsync(
            "/api/v1/probes/order", new { probeIds = new[] { aId, bId, cId, Guid.NewGuid() } });
        Assert.Equal(HttpStatusCode.BadRequest, extra.StatusCode);

        var duplicate = await client.PutAsJsonAsync(
            "/api/v1/probes/order", new { probeIds = new[] { aId, aId, bId } });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
    }

    private static async Task<JsonElement> CreateAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/probes", new
        {
            name,
            host = $"{name}.test",
            kind = "ping",
            pollIntervalSeconds = 30,
            failureThreshold = 2,
        });

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
