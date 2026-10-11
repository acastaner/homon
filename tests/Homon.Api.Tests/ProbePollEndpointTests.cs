using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Homon.Api.Authentication;
using Homon.Domain.Auth;
using Homon.Domain.Monitoring;
using Homon.Infrastructure.Monitoring;
using Homon.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Homon.Api.Tests;

/// <summary>
/// <c>POST /api/v1/probes/{id}/poll</c> — "Poll now" (plan 027). A class of its own because its
/// host swaps every real <see cref="IProbeRunner"/> for <see cref="PollProbeFactory.Runner"/>:
/// the gate must never ping or fetch anything real, and the other probe classes need the real
/// runners registered.
/// </summary>
public class ProbePollEndpointTests(ProbePollEndpointTests.PollProbeFactory factory)
    : IClassFixture<ProbePollEndpointTests.PollProbeFactory>
{
    [DatabaseFact]
    public async Task Polling_records_one_observation_and_returns_the_probe_as_it_now_stands()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();
        var id = await SeedProbeAsync();

        var response = await client.PostAsJsonAsync($"/api/v1/probes/{id}/poll", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, body.GetProperty("id").GetGuid());
        Assert.Equal("polled by the fake runner", body.GetProperty("lastDetail").GetString());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("lastCheckedAt").ValueKind);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        var observation = await database.ProbeObservations.SingleAsync(o => o.ProbeId == id);
        Assert.True(observation.Succeeded);
    }

    [DatabaseFact]
    public async Task Polling_an_unknown_probe_is_404()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var response = await client.PostAsJsonAsync($"/api/v1/probes/{Guid.NewGuid()}/poll", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [DatabaseFact]
    public async Task Polling_a_paused_probe_is_a_409_problem_and_polls_nothing()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();
        var id = await SeedProbeAsync(isPaused: true);

        var response = await client.PostAsJsonAsync($"/api/v1/probes/{id}/poll", new { });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("Unpause", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        Assert.Equal(0, await database.ProbeObservations.CountAsync(o => o.ProbeId == id));
    }

    [DatabaseFact]
    public async Task Polling_without_a_json_content_type_is_415()
    {
        // The CSRF guard: a cross-site form can POST text/plain with the session cookie attached,
        // but cannot send application/json without a preflight.
        using var client = TestClient.Create(factory);
        await client.SignInAsync();
        var id = await SeedProbeAsync();

        var response = await client.PostAsync(
            $"/api/v1/probes/{id}/poll", new StringContent("{}", Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [DatabaseFact]
    public async Task Polling_refuses_anonymous_callers_and_every_api_key()
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        var (_, presented) = await new ApiKeyIssuer(database, TimeProvider.System)
            .IssueAsync("poll attempt", ApiKeyScope.ReadWrite);

        using var anonymous = TestClient.Create(factory);
        using var keyed = TestClient.Create(factory);
        keyed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", presented);

        var id = await SeedProbeAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync($"/api/v1/probes/{id}/poll", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await keyed.PostAsJsonAsync($"/api/v1/probes/{id}/poll", new { })).StatusCode);
    }

    [DatabaseFact]
    public async Task The_openapi_document_describes_every_response_of_the_poll_route()
    {
        // Anonymous: the document is served to anyone (Program.cs, MapOpenApi).
        using var client = TestClient.Create(factory);

        var document = await client.GetFromJsonAsync<JsonElement>("/api/v1/openapi.json");
        var post = document.GetProperty("paths").GetProperty("/api/v1/probes/{id}/poll").GetProperty("post");

        Assert.Equal("PollProbe", post.GetProperty("operationId").GetString());
        var responses = post.GetProperty("responses");
        Assert.Equal(
            ["200", "404", "409", "415"],
            responses.EnumerateObject().Select(r => r.Name).Order(StringComparer.Ordinal));
        Assert.True(responses.GetProperty("200").GetProperty("content").TryGetProperty("application/json", out _));
        Assert.True(responses.GetProperty("409").GetProperty("content").TryGetProperty("application/problem+json", out _));
    }

    private async Task<Guid> SeedProbeAsync(bool isPaused = false)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();

        var probe = new Probe
        {
            Id = Guid.NewGuid(),
            Name = $"probe-{Guid.NewGuid():N}",
            Host = "probe.test",
            Kind = ProbeKind.Ping,
            PollInterval = TimeSpan.FromHours(1),
            FailureThreshold = 2,
            IsPaused = isPaused,
            Status = isPaused ? ProbeStatus.Paused : ProbeStatus.Unknown,
        };

        database.Probes.Add(probe);
        await database.SaveChangesAsync();

        return probe.Id;
    }

    /// <summary><see cref="ApiDatabaseFactory"/> with one fake runner standing in for every real one.</summary>
    public sealed class PollProbeFactory : ApiDatabaseFactory
    {
        public FakeProbeRunner Runner { get; } = new((_, _) =>
            Task.FromResult(new ProbeResult(true, 3.5, "polled by the fake runner")));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            base.ConfigureWebHost(builder);

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IProbeRunner>();
                services.AddSingleton<IProbeRunner>(Runner);
            });
        }
    }
}
