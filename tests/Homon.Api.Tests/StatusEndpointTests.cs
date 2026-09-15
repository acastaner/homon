using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Homon.Domain.Monitoring;
using Homon.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Homon.Api.Tests;

public class StatusEndpointTests(ApiDatabaseFactory factory) : IClassFixture<ApiDatabaseFactory>
{
    [DatabaseFact]
    public async Task With_zero_probes_totals_are_empty_and_there_is_one_implicit_Services_grouping()
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();

        // This class shares one database clone across its methods — clear whatever earlier
        // tests left behind so "zero probes" is actually zero.
        await database.Database.ExecuteSqlRawAsync(
            "TRUNCATE \"ProbeObservations\", \"ProbeGroupMemberships\", \"ProbeGroups\", \"Probes\" CASCADE;");

        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var body = await client.GetFromJsonAsync<JsonElement>("/api/v1/status");
        var totals = body.GetProperty("totals");

        Assert.Equal(0, totals.GetProperty("up").GetInt32());
        Assert.Equal(0, totals.GetProperty("unstable").GetInt32());
        Assert.Equal(0, totals.GetProperty("down").GetInt32());
        Assert.Equal(0, totals.GetProperty("unknown").GetInt32());
        Assert.Equal(0, totals.GetProperty("paused").GetInt32());
        Assert.Equal(JsonValueKind.Null, totals.GetProperty("uptimePercent").ValueKind);

        Assert.Equal(0, body.GetProperty("groups").GetArrayLength());
        Assert.Equal(0, body.GetProperty("ungroupedProbeIds").GetArrayLength());
        Assert.Equal(0, body.GetProperty("probes").GetArrayLength());
    }

    [DatabaseFact]
    public async Task A_probes_uptime_matches_a_hand_computed_ratio_and_is_null_with_no_observations()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var withObservations = await CreateProbeAsync(client, "Observed");
        var withoutObservations = await CreateProbeAsync(client, "Unobserved");

        var observedId = withObservations.GetProperty("id").GetGuid();
        var unobservedId = withoutObservations.GetProperty("id").GetGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
            var now = DateTimeOffset.UtcNow;

            database.ProbeObservations.AddRange(
                new ProbeObservation { ProbeId = observedId, ObservedAt = now.AddMinutes(-3), Succeeded = true, LatencyMs = 1 },
                new ProbeObservation { ProbeId = observedId, ObservedAt = now.AddMinutes(-2), Succeeded = true, LatencyMs = 1 },
                new ProbeObservation { ProbeId = observedId, ObservedAt = now.AddMinutes(-1), Succeeded = false });

            await database.SaveChangesAsync();
        }

        var body = await client.GetFromJsonAsync<JsonElement>("/api/v1/status");
        var probes = body.GetProperty("probes").EnumerateArray().ToArray();

        var observedResponse = probes.Single(p => p.GetProperty("id").GetGuid() == observedId);
        var unobservedResponse = probes.Single(p => p.GetProperty("id").GetGuid() == unobservedId);

        Assert.Equal(66.67, observedResponse.GetProperty("uptimePercent").GetDouble());
        Assert.Equal(JsonValueKind.Null, unobservedResponse.GetProperty("uptimePercent").ValueKind);
    }

    [DatabaseFact]
    public async Task A_probe_shared_by_two_groups_is_listed_once_counted_once_and_appears_in_both_groups()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var groupA = await CreateGroupAsync(client, "Shared A");
        var groupB = await CreateGroupAsync(client, "Shared B");
        var groupAId = groupA.GetProperty("id").GetGuid();
        var groupBId = groupB.GetProperty("id").GetGuid();

        var shared = await CreateProbeAsync(client, "Shared probe", groupIds: [groupAId, groupBId]);
        var sharedId = shared.GetProperty("id").GetGuid();

        var body = await client.GetFromJsonAsync<JsonElement>("/api/v1/status");

        var probes = body.GetProperty("probes").EnumerateArray()
            .Where(p => p.GetProperty("id").GetGuid() == sharedId)
            .ToArray();
        Assert.Single(probes);

        var groups = body.GetProperty("groups").EnumerateArray().ToArray();
        var groupAResponse = groups.Single(g => g.GetProperty("id").GetGuid() == groupAId);
        var groupBResponse = groups.Single(g => g.GetProperty("id").GetGuid() == groupBId);

        Assert.Contains(groupAResponse.GetProperty("probeIds").EnumerateArray().Select(p => p.GetGuid()), id => id == sharedId);
        Assert.Contains(groupBResponse.GetProperty("probeIds").EnumerateArray().Select(p => p.GetGuid()), id => id == sharedId);
    }

    [DatabaseFact]
    public async Task An_empty_group_is_absent_and_a_group_with_only_paused_probes_is_present()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var empty = await CreateGroupAsync(client, "Truly empty");
        var emptyId = empty.GetProperty("id").GetGuid();

        var pausedOnly = await CreateGroupAsync(client, "Paused only");
        var pausedOnlyId = pausedOnly.GetProperty("id").GetGuid();

        var pausedProbe = await CreateProbeAsync(client, "Paused member", groupIds: [pausedOnlyId]);
        var pausedProbeId = pausedProbe.GetProperty("id").GetGuid();
        await client.PutAsJsonAsync($"/api/v1/probes/{pausedProbeId}/pause", new { isPaused = true });

        var body = await client.GetFromJsonAsync<JsonElement>("/api/v1/status");
        var groupIds = body.GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("id").GetGuid()).ToArray();

        Assert.DoesNotContain(emptyId, groupIds);
        Assert.Contains(pausedOnlyId, groupIds);
    }

    [DatabaseFact]
    public async Task A_ping_probe_with_rtt_observations_gets_a_non_empty_sparkline_and_one_with_none_gets_empty()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var withRtt = await CreateProbeAsync(client, "Sparkline probe");
        var withoutRtt = await CreateProbeAsync(client, "Flat probe");
        var withRttId = withRtt.GetProperty("id").GetGuid();
        var withoutRttId = withoutRtt.GetProperty("id").GetGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
            database.ProbeObservations.Add(new ProbeObservation
            {
                ProbeId = withRttId,
                ObservedAt = DateTimeOffset.UtcNow.AddHours(-1),
                Succeeded = true,
                LatencyMs = 12.5,
            });
            await database.SaveChangesAsync();
        }

        var body = await client.GetFromJsonAsync<JsonElement>("/api/v1/status");
        var probes = body.GetProperty("probes").EnumerateArray().ToArray();

        var withRttResponse = probes.Single(p => p.GetProperty("id").GetGuid() == withRttId);
        var withoutRttResponse = probes.Single(p => p.GetProperty("id").GetGuid() == withoutRttId);

        Assert.True(withRttResponse.GetProperty("sparkline").GetArrayLength() > 0);
        Assert.Equal(0, withoutRttResponse.GetProperty("sparkline").GetArrayLength());
    }

    [DatabaseFact]
    public async Task LastCheckedAt_matches_the_most_recent_observations_ObservedAt()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var probe = await CreateProbeAsync(client, "Checked probe");
        var probeId = probe.GetProperty("id").GetGuid();
        var latest = DateTimeOffset.UtcNow;

        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
            var tracked = await database.Probes.SingleAsync(p => p.Id == probeId);
            tracked.RecordObservation(true, 1.0, null, latest.AddMinutes(-5));
            tracked.RecordObservation(true, 1.0, null, latest);
            await database.SaveChangesAsync();
        }

        var body = await client.GetFromJsonAsync<JsonElement>("/api/v1/status");
        var response = body.GetProperty("probes").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == probeId);

        var lastCheckedAt = response.GetProperty("lastCheckedAt").GetDateTimeOffset();
        Assert.Equal(latest, lastCheckedAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task GET_is_refused_anonymously_once_readers_must_sign_in()
    {
        using var gated = new ConfiguredFactory();
        using var client = TestClient.Create(gated);

        var response = await client.GetAsync("/api/v1/status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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

    private static async Task<JsonElement> CreateGroupAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/probe-groups", new { name });

        return await response.Content.ReadFromJsonAsync<JsonElement>();
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
