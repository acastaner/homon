using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Homon.Api.Tests;

public class MetaEndpointTests(HomonApiFactory factory) : IClassFixture<HomonApiFactory>
{
    [Fact]
    public async Task Meta_is_anonymous_and_identifies_the_api()
    {
        using var client = TestClient.Create(factory);

        var response = await client.GetAsync("/api/v1/meta");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Homon", body.GetProperty("name").GetString());
        Assert.Equal("v1", body.GetProperty("apiVersion").GetString());
        Assert.Equal("dev", body.GetProperty("release").GetString());
        Assert.Equal("Development", body.GetProperty("environment").GetString());
        Assert.False(body.GetProperty("requireSignInForReaders").GetBoolean());
        Assert.True(body.GetProperty("administratorConfigured").GetBoolean());
        Assert.Equal("/api/v1/openapi.json", body.GetProperty("openapi").GetString());
    }

    [Fact]
    public async Task Meta_reports_the_release_and_the_reader_switch_from_configuration()
    {
        using var configured = new ConfiguredFactory(new Dictionary<string, string?>
        {
            ["Homon:Version"] = "1.2.3",
            ["Auth:RequireSignInForReaders"] = "true",
        });
        using var client = TestClient.Create(configured);

        var body = await client.GetFromJsonAsync<JsonElement>("/api/v1/meta");

        Assert.Equal("1.2.3", body.GetProperty("release").GetString());
        Assert.True(body.GetProperty("requireSignInForReaders").GetBoolean());
    }

    [Fact]
    public async Task Meta_says_when_no_administrator_is_configured()
    {
        using var unconfigured = new ConfiguredFactory(new Dictionary<string, string?>
        {
            ["Administrator:Email"] = null,
            ["Administrator:PasswordHash"] = null,
        });
        using var client = TestClient.Create(unconfigured);

        var body = await client.GetFromJsonAsync<JsonElement>("/api/v1/meta");

        Assert.False(body.GetProperty("administratorConfigured").GetBoolean());
    }

    [Fact]
    public async Task Every_response_carries_the_security_headers_and_a_request_id()
    {
        using var client = TestClient.Create(factory);

        var response = await client.GetAsync("/api/v1/meta");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Matches("^[0-9a-f]{32}$", response.Headers.GetValues("X-Request-Id").Single());
    }

    [Fact]
    public async Task The_openapi_document_is_served_anonymously_under_the_versioned_path()
    {
        using var client = TestClient.Create(factory);

        var document = await client.GetFromJsonAsync<JsonElement>("/api/v1/openapi.json");

        Assert.Equal("Homon", document.GetProperty("info").GetProperty("title").GetString());

        var paths = document.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();

        Assert.Contains("/api/v1/meta", paths);
        Assert.Contains("/api/v1/auth/sign-in", paths);
        Assert.Contains("/api/v1/auth/session", paths);
    }

    /// <summary>A <see cref="HomonApiFactory"/> with a few configuration keys overridden.</summary>
    private sealed class ConfiguredFactory(Dictionary<string, string?> overrides) : HomonApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(overrides));
        }
    }
}
