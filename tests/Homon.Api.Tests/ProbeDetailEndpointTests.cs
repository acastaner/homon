using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Homon.Domain.Monitoring;
using Homon.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Homon.Api.Tests;

/// <summary>
/// <c>GET /status/probes/{id}</c> — one probe's page (plan 023). The privacy cases
/// (<c>The_response_never_carries_the_host</c>, the anonymous read) are the contract; the rest
/// pin the bucket arithmetic.
/// </summary>
public class ProbeDetailEndpointTests(ApiDatabaseFactory factory) : IClassFixture<ApiDatabaseFactory>
{
    [DatabaseFact]
    public async Task An_unknown_probe_is_404()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var response = await client.GetAsync($"/api/v1/status/probes/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [DatabaseFact]
    public async Task An_unknown_range_is_a_validation_problem_naming_range()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();
        var probeId = await CreateProbeAsync(client, "Range probe");

        var response = await client.GetAsync($"/api/v1/status/probes/{probeId}?range=1y");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("errors").TryGetProperty("range", out _));
    }

    [DatabaseFact]
    public async Task The_default_range_is_24h_in_96_buckets_of_15_minutes()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();
        var probeId = await CreateProbeAsync(client, "Default range probe");

        var body = await client.GetFromJsonAsync<JsonElement>($"/api/v1/status/probes/{probeId}");

        Assert.Equal("24h", body.GetProperty("range").GetString());
        Assert.Equal(900, body.GetProperty("bucketSeconds").GetInt32());
        Assert.Equal(96, body.GetProperty("latency").GetArrayLength());
    }

    [DatabaseFact]
    public async Task Seven_and_thirty_days_have_168_and_120_buckets()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();
        var probeId = await CreateProbeAsync(client, "Long range probe");

        var week = await client.GetFromJsonAsync<JsonElement>($"/api/v1/status/probes/{probeId}?range=7d");
        var month = await client.GetFromJsonAsync<JsonElement>($"/api/v1/status/probes/{probeId}?range=30d");

        Assert.Equal(168, week.GetProperty("latency").GetArrayLength());
        Assert.Equal(3600, week.GetProperty("bucketSeconds").GetInt32());
        Assert.Equal(120, month.GetProperty("latency").GetArrayLength());
        Assert.Equal(21600, month.GetProperty("bucketSeconds").GetInt32());
    }

    [DatabaseFact]
    public async Task A_bucket_averages_successful_latency_and_counts_every_poll()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();
        var probeId = await CreateProbeAsync(client, "Bucket probe");

        // 35/36/37 minutes ago sit inside [now-45m, now-30m) even if the server's clock is a few
        // milliseconds later than ours; exact 15-minute multiples would straddle a boundary.
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(
            new ProbeObservation { ProbeId = probeId, ObservedAt = now.AddMinutes(-35), Succeeded = true, LatencyMs = 10 },
            new ProbeObservation { ProbeId = probeId, ObservedAt = now.AddMinutes(-36), Succeeded = true, LatencyMs = 20 },
            new ProbeObservation { ProbeId = probeId, ObservedAt = now.AddMinutes(-37), Succeeded = false });

        var body = await client.GetFromJsonAsync<JsonElement>($"/api/v1/status/probes/{probeId}");
        var buckets = body.GetProperty("latency").EnumerateArray().ToArray();

        var populated = Assert.Single(buckets, b => b.GetProperty("polls").GetInt32() == 3);
        Assert.Equal(1, populated.GetProperty("failures").GetInt32());
        Assert.Equal(15.0, populated.GetProperty("averageLatencyMs").GetDouble());
        Assert.Equal(3, buckets.Sum(b => b.GetProperty("polls").GetInt32()));
    }

    [DatabaseFact]
    public async Task Range_uptime_and_thirty_day_uptime_use_their_own_windows()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();
        var probeId = await CreateProbeAsync(client, "Uptime probe");

        var now = DateTimeOffset.UtcNow;
        await SeedAsync(
            new ProbeObservation { ProbeId = probeId, ObservedAt = now.AddMinutes(-35), Succeeded = true, LatencyMs = 10 },
            new ProbeObservation { ProbeId = probeId, ObservedAt = now.AddMinutes(-36), Succeeded = true, LatencyMs = 20 },
            new ProbeObservation { ProbeId = probeId, ObservedAt = now.AddMinutes(-37), Succeeded = false },
            new ProbeObservation { ProbeId = probeId, ObservedAt = now.AddDays(-3), Succeeded = false });

        var body = await client.GetFromJsonAsync<JsonElement>($"/api/v1/status/probes/{probeId}?range=24h");

        Assert.Equal(66.67, body.GetProperty("rangeUptimePercent").GetDouble());
        Assert.Equal(50.0, body.GetProperty("uptimePercent").GetDouble());
    }

    [DatabaseFact]
    public async Task Recent_polls_are_the_newest_fifty_newest_first()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();
        var probeId = await CreateProbeAsync(client, "Recent probe");

        var now = DateTimeOffset.UtcNow;
        await SeedAsync(Enumerable.Range(1, 55)
            .Select(minutes => new ProbeObservation
            {
                ProbeId = probeId,
                ObservedAt = now.AddMinutes(-minutes),
                Succeeded = true,
                LatencyMs = minutes,
            })
            .ToArray());

        var body = await client.GetFromJsonAsync<JsonElement>($"/api/v1/status/probes/{probeId}");
        var recent = body.GetProperty("recentObservations").EnumerateArray().ToArray();

        Assert.Equal(50, recent.Length);
        Assert.Equal(1.0, recent[0].GetProperty("latencyMs").GetDouble());

        var times = recent.Select(r => r.GetProperty("observedAt").GetDateTimeOffset()).ToArray();
        for (var i = 1; i < times.Length; i++)
        {
            Assert.True(times[i] < times[i - 1], "recent polls must be strictly newest first");
        }
    }

    [DatabaseFact]
    public async Task The_response_never_carries_the_host()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var created = await client.PostAsJsonAsync("/api/v1/probes", new
        {
            name = "Leaky probe",
            host = "do-not-leak.test",
            kind = "ping",
            pollIntervalSeconds = 30,
            failureThreshold = 2,
            groupIds = Array.Empty<Guid>(),
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var probeId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var raw = await client.GetStringAsync($"/api/v1/status/probes/{probeId}");

        Assert.DoesNotContain("do-not-leak.test", raw, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(raw);
        Assert.DoesNotContain("host", PropertyNames(document.RootElement), StringComparer.OrdinalIgnoreCase);
    }

    [DatabaseFact]
    public async Task An_anonymous_reader_can_read_it()
    {
        Guid probeId;

        using (var admin = TestClient.Create(factory))
        {
            await admin.SignInAsync();
            probeId = await CreateProbeAsync(admin, "Anonymous probe");
        }

        using var anonymous = TestClient.Create(factory);
        var response = await anonymous.GetAsync($"/api/v1/status/probes/{probeId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task It_requires_sign_in_when_readers_must()
    {
        using var gated = new ConfiguredFactory();
        using var client = TestClient.Create(gated);

        var response = await client.GetAsync($"/api/v1/status/probes/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static IEnumerable<string> PropertyNames(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    yield return property.Name;

                    foreach (var nested in PropertyNames(property.Value))
                    {
                        yield return nested;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in PropertyNames(item))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }

    private async Task SeedAsync(params ProbeObservation[] observations)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        database.ProbeObservations.AddRange(observations);
        await database.SaveChangesAsync();
    }

    private static async Task<Guid> CreateProbeAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/probes", new
        {
            name,
            host = $"{name}.test",
            kind = "ping",
            pollIntervalSeconds = 30,
            failureThreshold = 2,
            groupIds = Array.Empty<Guid>(),
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>A <see cref="HomonApiFactory"/> with <c>Auth:RequireSignInForReaders</c> forced on. No database needed.</summary>
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
