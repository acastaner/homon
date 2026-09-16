using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Homon.Api.Authentication;
using Homon.Domain.Auth;
using Homon.Domain.Monitoring;
using Homon.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homon.Api.Tests;

public class ProbeEndpointTests(ApiDatabaseFactory factory) : IClassFixture<ApiDatabaseFactory>
{
    [DatabaseFact]
    public async Task POST_creates_a_probe_appended_at_the_end_of_the_display_order()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var first = await CreateAsync(client, name: "First");
        var second = await CreateAsync(client, name: "Second");

        Assert.Equal(HttpStatusCode.Created, first.Response.StatusCode);
        // Not necessarily 0/1 — this class's database clone is shared across its own test
        // methods (one clone per class, per DatabaseBackedFactory), so only the relative
        // order between two probes created back to back is guaranteed. The class dedicated
        // to whole-table behaviour (ProbeOrderingEndpointTests) asserts the fresh-database
        // and exact-position cases.
        Assert.Equal(first.Body.GetProperty("position").GetInt32() + 1, second.Body.GetProperty("position").GetInt32());
        Assert.Equal("ping", first.Body.GetProperty("kind").GetString());
        Assert.Equal("unknown", first.Body.GetProperty("status").GetString());
    }

    [DatabaseFact]
    public async Task POST_omitting_failureThreshold_defaults_to_two()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var response = await client.PostAsJsonAsync("/api/v1/probes", new
        {
            name = "No threshold",
            host = "host.test",
            kind = "ping",
            pollIntervalSeconds = 30,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(Probe.DefaultFailureThreshold, body.GetProperty("failureThreshold").GetInt32());
    }

    [DatabaseFact]
    public async Task PUT_omitting_failureThreshold_is_rejected()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var created = await CreateAsync(client, name: "Editable");
        var id = created.Body.GetProperty("id").GetGuid();

        var response = await client.PutAsJsonAsync($"/api/v1/probes/{id}", new
        {
            name = "Editable",
            host = "host.test",
            pollIntervalSeconds = 30,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [DatabaseTheory]
    [MemberData(nameof(InvalidCreateBodies))]
    public async Task POST_rejects_invalid_bodies(object body, string expectedField)
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var response = await client.PostAsJsonAsync("/api/v1/probes", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty(expectedField, out _));
    }

    public static IEnumerable<object[]> InvalidCreateBodies()
    {
        yield return [new { name = "", host = "h.test", kind = "ping", pollIntervalSeconds = 30, failureThreshold = 2 }, "name"];
        yield return [new { name = "n", host = "", kind = "ping", pollIntervalSeconds = 30, failureThreshold = 2 }, "host"];
        yield return [new { name = new string('x', Probe.NameMaxLength + 1), host = "h.test", kind = "ping", pollIntervalSeconds = 30, failureThreshold = 2 }, "name"];
        yield return [new { name = "n", host = new string('x', Probe.HostMaxLength + 1), kind = "ping", pollIntervalSeconds = 30, failureThreshold = 2 }, "host"];
        yield return [new { name = "n", host = "h.test", kind = "ping", pollIntervalSeconds = Probe.MinPollIntervalSeconds - 1, failureThreshold = 2 }, "pollIntervalSeconds"];
        yield return [new { name = "n", host = "h.test", kind = "ping", pollIntervalSeconds = Probe.MaxPollIntervalSeconds + 1, failureThreshold = 2 }, "pollIntervalSeconds"];
        yield return [new { name = "n", host = "h.test", kind = "ping", pollIntervalSeconds = 30, failureThreshold = Probe.MinFailureThreshold - 1 }, "failureThreshold"];
        yield return [new { name = "n", host = "h.test", kind = "ping", pollIntervalSeconds = 30, failureThreshold = Probe.MaxFailureThreshold + 1 }, "failureThreshold"];
        yield return [new { name = "n", host = "h.test", kind = "smb", pollIntervalSeconds = 30, failureThreshold = 2 }, "kind"];
        yield return [new { name = "n", host = "h.test", kind = "snmp", pollIntervalSeconds = 30, failureThreshold = 2 }, "kind"];
        yield return [new { name = "n", host = "h.test", kind = "ping", pollIntervalSeconds = 30, failureThreshold = 2, groupIds = new[] { Guid.NewGuid() } }, "groupIds"];

        // kind == "http" is accepted from plan 003 on (Step 5's lift of 002's rejection) —
        // these are the http-object shaped 400s that replace the old "kind" rejection case.
        yield return [new { name = "n", host = "h.test", kind = "http", pollIntervalSeconds = 30, failureThreshold = 2 }, "http"];
        yield return
        [
            new
            {
                name = "n", host = "h.test", kind = "ping", pollIntervalSeconds = 30, failureThreshold = 2,
                http = new { method = "get", path = "api/health" },
            },
            "http",
        ];
        yield return
        [
            new
            {
                name = "n", host = "h.test", kind = "http", pollIntervalSeconds = 30, failureThreshold = 2,
                http = new { method = "head", path = "api/health", expectedBodyText = "ok" },
            },
            "http.expectedBodyText",
        ];
        yield return
        [
            new
            {
                name = "n", host = "h.test", kind = "http", pollIntervalSeconds = 30, failureThreshold = 2,
                http = new { method = "get", path = "http://evil.test/api" },
            },
            "http.path",
        ];
        yield return
        [
            new
            {
                name = "n", host = "h.test", kind = "http", pollIntervalSeconds = 30, failureThreshold = 2,
                http = new { method = "get", path = "api/health", credential = new { type = "basic" } },
            },
            "http.credential.username",
        ];
        yield return
        [
            new
            {
                name = "n", host = "h.test", kind = "http", pollIntervalSeconds = 30, failureThreshold = 2,
                http = new { method = "get", path = "api/health", timeoutSeconds = 0 },
            },
            "http.timeoutSeconds",
        ];
        yield return
        [
            new
            {
                name = "n", host = "h.test", kind = "http", pollIntervalSeconds = 30, failureThreshold = 2,
                http = new { method = "get", path = "api/health", timeoutSeconds = 26 },
            },
            "http.timeoutSeconds",
        ];
    }

    [DatabaseFact]
    public async Task PUT_edits_fields_and_leaves_kind_and_position_unchanged()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var created = await CreateAsync(client, name: "Original");
        var id = created.Body.GetProperty("id").GetGuid();
        var originalPosition = created.Body.GetProperty("position").GetInt32();

        var response = await client.PutAsJsonAsync($"/api/v1/probes/{id}", new
        {
            name = "Renamed",
            host = "renamed.test",
            pollIntervalSeconds = 45,
            failureThreshold = 3,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Renamed", body.GetProperty("name").GetString());
        Assert.Equal("renamed.test", body.GetProperty("host").GetString());
        Assert.Equal(45, body.GetProperty("pollIntervalSeconds").GetInt32());
        Assert.Equal(3, body.GetProperty("failureThreshold").GetInt32());
        Assert.Equal("ping", body.GetProperty("kind").GetString());
        Assert.Equal(originalPosition, body.GetProperty("position").GetInt32());
    }

    [DatabaseFact]
    public async Task PUT_on_an_unknown_id_is_404()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var response = await client.PutAsJsonAsync($"/api/v1/probes/{Guid.NewGuid()}", new
        {
            name = "n",
            host = "h.test",
            pollIntervalSeconds = 30,
            failureThreshold = 2,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [DatabaseFact]
    public async Task Pausing_and_unpausing_re_derives_status_without_a_new_observation()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var created = await CreateAsync(client, name: "Flappy", failureThreshold: 2);
        var id = created.Body.GetProperty("id").GetGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
            var probe = await database.Probes.SingleAsync(p => p.Id == id);

            var now = DateTimeOffset.UtcNow;
            probe.RecordObservation(false, null, "ping: TimedOut", now);
            probe.RecordObservation(false, null, "ping: TimedOut", now);
            await database.SaveChangesAsync();
        }

        var pauseResponse = await client.PutAsJsonAsync($"/api/v1/probes/{id}/pause", new { isPaused = true });
        var pauseBody = await pauseResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("paused", pauseBody.GetProperty("status").GetString());

        var unpauseResponse = await client.PutAsJsonAsync($"/api/v1/probes/{id}/pause", new { isPaused = false });
        var unpauseBody = await unpauseResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("down", unpauseBody.GetProperty("status").GetString());

        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
            Assert.Equal(0, await database.ProbeObservations.CountAsync(o => o.ProbeId == id));
        }
    }

    [DatabaseFact]
    public async Task DELETE_removes_the_probe_and_its_observations()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var created = await CreateAsync(client, name: "Doomed");
        var id = created.Body.GetProperty("id").GetGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
            database.ProbeObservations.Add(new ProbeObservation
            {
                ProbeId = id,
                ObservedAt = DateTimeOffset.UtcNow,
                Succeeded = true,
                LatencyMs = 1.0,
            });
            await database.SaveChangesAsync();
        }

        var deleteResponse = await client.DeleteAsync($"/api/v1/probes/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var missingResponse = await client.DeleteAsync($"/api/v1/probes/{id}");
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
            Assert.False(await database.Probes.AnyAsync(p => p.Id == id));
            Assert.Equal(0, await database.ProbeObservations.CountAsync(o => o.ProbeId == id));
        }
    }

    [DatabaseFact]
    public async Task Group_membership_round_trips_without_moving_other_members()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        Guid groupId;
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
            var group = new ProbeGroup { Id = Guid.NewGuid(), Name = "Hosts", NormalizedName = "HOSTS", CreatedAt = DateTimeOffset.UtcNow };
            database.ProbeGroups.Add(group);
            await database.SaveChangesAsync();
            groupId = group.Id;
        }

        var first = await CreateAsync(client, name: "First", groupIds: [groupId]);
        var second = await CreateAsync(client, name: "Second", groupIds: [groupId]);
        var firstId = first.Body.GetProperty("id").GetGuid();
        var secondId = second.Body.GetProperty("id").GetGuid();

        var third = await CreateAsync(client, name: "Third", groupIds: [groupId]);
        var thirdId = third.Body.GetProperty("id").GetGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
            var memberships = await database.Set<ProbeGroupMembership>()
                .Where(m => m.GroupId == groupId)
                .OrderBy(m => m.Position)
                .ToListAsync();

            Assert.Equal([firstId, secondId, thirdId], memberships.Select(m => m.ProbeId));
            Assert.Equal(0, memberships.Single(m => m.ProbeId == firstId).Position);
            Assert.Equal(1, memberships.Single(m => m.ProbeId == secondId).Position);
        }

        // Removing the third probe's group membership must not move the first two.
        var response = await client.PutAsJsonAsync($"/api/v1/probes/{thirdId}", new
        {
            name = "Third",
            host = "host.test",
            pollIntervalSeconds = 30,
            failureThreshold = 2,
            groupIds = Array.Empty<Guid>(),
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
            var memberships = await database.Set<ProbeGroupMembership>()
                .Where(m => m.GroupId == groupId)
                .ToListAsync();

            Assert.Equal(2, memberships.Count);
            Assert.DoesNotContain(memberships, m => m.ProbeId == thirdId);
            Assert.Equal(0, memberships.Single(m => m.ProbeId == firstId).Position);
            Assert.Equal(1, memberships.Single(m => m.ProbeId == secondId).Position);
        }
    }

    [DatabaseFact]
    public async Task POST_creates_an_http_probe_and_round_trips_its_options()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var response = await client.PostAsJsonAsync("/api/v1/probes", new
        {
            name = "API",
            host = "api.test",
            kind = "http",
            pollIntervalSeconds = 30,
            failureThreshold = 2,
            http = new
            {
                method = "get",
                path = "api/health",
                useHttps = true,
                expectedStatusCode = 200,
                expectedBodyText = "ok",
            },
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("http", body.GetProperty("kind").GetString());
        var http = body.GetProperty("http");
        Assert.Equal("get", http.GetProperty("method").GetString());
        Assert.Equal("api/health", http.GetProperty("path").GetString());
        Assert.True(http.GetProperty("useHttps").GetBoolean());
        // Omitted on create defaults to HttpProbeOptions.DefaultTimeoutSeconds (plan 003's
        // Decision 4a) — asserted against the domain constant rather than a bare "10" so this
        // test cannot silently drift from that constant.
        Assert.Equal(10, http.GetProperty("timeoutSeconds").GetInt32());
        Assert.Equal(200, http.GetProperty("expectedStatusCode").GetInt32());
        Assert.Equal("ok", http.GetProperty("expectedBodyText").GetString());
        Assert.Equal("none", http.GetProperty("credential").GetProperty("type").GetString());
        Assert.False(http.GetProperty("credential").GetProperty("hasSecret").GetBoolean());
    }

    [DatabaseFact]
    public async Task PUT_on_an_http_probe_keeps_timeoutSeconds_and_secret_when_omitted_and_clears_the_secret_on_an_empty_string()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var created = await client.PostAsJsonAsync("/api/v1/probes", new
        {
            name = "Secured",
            host = "secured.test",
            kind = "http",
            pollIntervalSeconds = 30,
            failureThreshold = 2,
            http = new
            {
                method = "get",
                path = "api/health",
                timeoutSeconds = 20,
                credential = new { type = "bearer", secret = "super-secret-token" },
            },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdRaw = await created.Content.ReadAsStringAsync();
        // Never plaintext, never the raw secret, anywhere in the response — only hasSecret.
        Assert.DoesNotContain("super-secret-token", createdRaw, StringComparison.Ordinal);
        var createdBody = JsonSerializer.Deserialize<JsonElement>(createdRaw);
        var id = createdBody.GetProperty("id").GetGuid();
        Assert.True(createdBody.GetProperty("http").GetProperty("credential").GetProperty("hasSecret").GetBoolean());

        // Omitting timeoutSeconds and credential.secret keeps both.
        var keepResponse = await client.PutAsJsonAsync($"/api/v1/probes/{id}", new
        {
            name = "Secured",
            host = "secured.test",
            pollIntervalSeconds = 30,
            failureThreshold = 2,
            http = new { method = "get", path = "api/health", credential = new { type = "bearer" } },
        });
        Assert.Equal(HttpStatusCode.OK, keepResponse.StatusCode);
        var keepBody = await keepResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(20, keepBody.GetProperty("http").GetProperty("timeoutSeconds").GetInt32());
        Assert.True(keepBody.GetProperty("http").GetProperty("credential").GetProperty("hasSecret").GetBoolean());

        // credential.secret: "" clears it.
        var clearResponse = await client.PutAsJsonAsync($"/api/v1/probes/{id}", new
        {
            name = "Secured",
            host = "secured.test",
            pollIntervalSeconds = 30,
            failureThreshold = 2,
            http = new { method = "get", path = "api/health", credential = new { type = "bearer", secret = "" } },
        });
        Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);
        var clearBody = await clearResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(clearBody.GetProperty("http").GetProperty("credential").GetProperty("hasSecret").GetBoolean());

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        var probe = await database.Probes.SingleAsync(p => p.Id == id);
        Assert.Null(probe.HttpOptions!.Credential.ProtectedSecret);
    }

    [DatabaseFact]
    public async Task The_GET_auth_matrix_matches_AdministratorOrApiKey()
    {
        using var anonymous = TestClient.Create(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/probes")).StatusCode);

        using var admin = TestClient.Create(factory);
        await admin.SignInAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/probes")).StatusCode);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        var issuer = new ApiKeyIssuer(database, TimeProvider.System);

        var (_, readKey) = await issuer.IssueAsync("read key", ApiKeyScope.Read);
        using var readClient = TestClient.Create(factory);
        readClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readKey);
        Assert.Equal(HttpStatusCode.OK, (await readClient.GetAsync("/api/v1/probes")).StatusCode);

        var (_, readWriteKey) = await issuer.IssueAsync("read-write key", ApiKeyScope.ReadWrite);
        using var readWriteClient = TestClient.Create(factory);
        readWriteClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readWriteKey);
        Assert.Equal(HttpStatusCode.OK, (await readWriteClient.GetAsync("/api/v1/probes")).StatusCode);

        var (expiredKeyEntity, expiredKey) = await issuer.IssueAsync(
            "about to expire", ApiKeyScope.Read, DateTimeOffset.UtcNow.AddMinutes(1));
        await database.ApiKeys.Where(k => k.Id == expiredKeyEntity.Id)
            .ExecuteUpdateAsync(k => k.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddDays(-1)));
        using var expiredClient = TestClient.Create(factory);
        expiredClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expiredKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await expiredClient.GetAsync("/api/v1/probes")).StatusCode);

        var (revokedKey, revokedPresented) = await issuer.IssueAsync("to revoke", ApiKeyScope.Read);
        await database.ApiKeys.Where(k => k.Id == revokedKey.Id)
            .ExecuteUpdateAsync(k => k.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
        using var revokedClient = TestClient.Create(factory);
        revokedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", revokedPresented);
        Assert.Equal(HttpStatusCode.Unauthorized, (await revokedClient.GetAsync("/api/v1/probes")).StatusCode);
    }

    [DatabaseFact]
    public async Task The_write_auth_matrix_refuses_anonymous_callers_and_every_api_key()
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        var (_, presented) = await new ApiKeyIssuer(database, TimeProvider.System)
            .IssueAsync("write attempt", ApiKeyScope.ReadWrite);

        using var anonymous = TestClient.Create(factory);
        using var keyed = TestClient.Create(factory);
        keyed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", presented);

        var body = new { name = "n", host = "h.test", kind = "ping", pollIntervalSeconds = 30, failureThreshold = 2 };

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/probes", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await keyed.PostAsJsonAsync("/api/v1/probes", body)).StatusCode);

        var probeId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync($"/api/v1/probes/{probeId}", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await keyed.PutAsJsonAsync($"/api/v1/probes/{probeId}", body)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync($"/api/v1/probes/{probeId}/pause", new { isPaused = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await keyed.PutAsJsonAsync($"/api/v1/probes/{probeId}/pause", new { isPaused = true })).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"/api/v1/probes/{probeId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await keyed.DeleteAsync($"/api/v1/probes/{probeId}")).StatusCode);

        var order = new { probeIds = Array.Empty<Guid>() };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync("/api/v1/probes/order", order)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await keyed.PutAsJsonAsync("/api/v1/probes/order", order)).StatusCode);
    }

    private static async Task<(HttpResponseMessage Response, JsonElement Body)> CreateAsync(
        HttpClient client, string name, int failureThreshold = 2, Guid[]? groupIds = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/probes", new
        {
            name,
            host = $"{name}.test",
            kind = "ping",
            pollIntervalSeconds = 30,
            failureThreshold,
            groupIds = groupIds ?? [],
        });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        return (response, body);
    }
}
