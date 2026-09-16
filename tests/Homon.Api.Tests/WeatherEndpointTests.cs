using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Homon.Api.Authentication;
using Homon.Domain.Auth;
using Homon.Domain.Weather;
using Homon.Infrastructure.Persistence;
using Homon.Infrastructure.Weather;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Homon.Api.Tests;

public class WeatherEndpointTests(WeatherEndpointTests.ConfiguredFactory factory) : IClassFixture<WeatherEndpointTests.ConfiguredFactory>
{
    [DatabaseFact]
    public async Task GET_weather_on_a_fresh_database_returns_204()
    {
        using var client = TestClient.Create(factory);

        var response = await client.GetAsync("/api/v1/weather");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [DatabaseFact]
    public async Task PUT_then_GET_returns_200_with_the_fake_forecast()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var put = await client.PutAsJsonAsync(
            "/api/v1/weather/settings",
            new { latitude = 51.5, longitude = -0.12, place = "Test location", units = "metric" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var response = await client.GetAsync("/api/v1/weather");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Test location", body.GetProperty("place").GetString());
        Assert.Equal("metric", body.GetProperty("units").GetString());
        Assert.Equal(18, body.GetProperty("current").GetProperty("temperature").GetDouble());
        Assert.True(body.GetProperty("current").GetProperty("isDay").GetBoolean());
        Assert.Equal(3, body.GetProperty("forecast").GetArrayLength());
        Assert.False(body.GetProperty("stale").GetBoolean());

        // Clean up — this fixture's database clone is shared across the whole class.
        await client.SendAsync(DeleteSettingsRequest());
    }

    [DatabaseTheory]
    [InlineData(91, 0, "metric", "latitude")]
    [InlineData(-91, 0, "metric", "latitude")]
    [InlineData(0, 181, "metric", "longitude")]
    [InlineData(0, -181, "metric", "longitude")]
    [InlineData(0, 0, "not-a-unit", "units")]
    public async Task PUT_rejects_invalid_fields(double latitude, double longitude, string units, string failingField)
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var response = await client.PutAsJsonAsync(
            "/api/v1/weather/settings", new { latitude, longitude, units });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty(failingField, out _));
    }

    [DatabaseFact]
    public async Task PUT_rejects_a_missing_units_field()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var response = await client.PutAsJsonAsync(
            "/api/v1/weather/settings", new { latitude = 0, longitude = 0, units = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("units", out _));
    }

    [DatabaseFact]
    public async Task PUT_rejects_an_over_length_place()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        var response = await client.PutAsJsonAsync(
            "/api/v1/weather/settings",
            new { latitude = 0, longitude = 0, place = new string('x', WeatherSettings.PlaceMaxLength + 1), units = "metric" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("place", out _));
    }

    [DatabaseFact]
    public async Task PUT_twice_never_creates_a_second_row()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        await client.PutAsJsonAsync(
            "/api/v1/weather/settings", new { latitude = 1, longitude = 1, units = "metric" });
        await client.PutAsJsonAsync(
            "/api/v1/weather/settings", new { latitude = 2, longitude = 2, units = "imperial" });

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        Assert.Equal(1, await database.WeatherSettings.CountAsync());

        await client.SendAsync(DeleteSettingsRequest());
    }

    [DatabaseFact]
    public async Task DELETE_requires_the_json_content_type_and_is_idempotent()
    {
        using var client = TestClient.Create(factory);
        await client.SignInAsync();

        await client.PutAsJsonAsync(
            "/api/v1/weather/settings", new { latitude = 1, longitude = 1, units = "metric" });

        using var form = new FormUrlEncodedContent([]);
        using var noContentTypeRequest = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/weather/settings")
        {
            Content = form,
        };
        var refused = await client.SendAsync(noContentTypeRequest);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, refused.StatusCode);

        var first = await client.SendAsync(DeleteSettingsRequest());
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var second = await client.SendAsync(DeleteSettingsRequest());
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        var afterDelete = await client.GetAsync("/api/v1/weather/settings");
        Assert.Equal(HttpStatusCode.NoContent, afterDelete.StatusCode);
    }

    [DatabaseFact]
    public async Task GET_weather_is_open_to_an_anonymous_caller_by_default()
    {
        using var anonymous = TestClient.Create(factory);

        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.GetAsync("/api/v1/weather")).StatusCode);
    }

    [DatabaseFact]
    public async Task The_write_auth_matrix_refuses_anonymous_callers_and_every_api_key()
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        var (_, presented) = await new ApiKeyIssuer(database, TimeProvider.System)
            .IssueAsync("weather write attempt", ApiKeyScope.ReadWrite);

        using var anonymous = TestClient.Create(factory);
        using var keyed = TestClient.Create(factory);
        keyed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", presented);

        var body = new { latitude = 0, longitude = 0, units = "metric" };

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync("/api/v1/weather/settings", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await keyed.PutAsJsonAsync("/api/v1/weather/settings", body)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/weather/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await keyed.GetAsync("/api/v1/weather/settings")).StatusCode);

        using var anonymousDelete = DeleteSettingsRequest();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(anonymousDelete)).StatusCode);

        using var keyedDelete = DeleteSettingsRequest();
        Assert.Equal(HttpStatusCode.Forbidden, (await keyed.SendAsync(keyedDelete)).StatusCode);
    }

    [Fact]
    public async Task GET_weather_is_refused_anonymously_once_readers_must_sign_in()
    {
        // Needs no database at all — same pattern as LinkEndpointTests' own
        // GET_is_refused_anonymously_once_readers_must_sign_in, on the deliberately
        // unreachable connection string every plain HomonApiFactory carries.
        using var gated = new ReaderGatedFactory();
        using var client = TestClient.Create(gated);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/weather")).StatusCode);
    }

    [DatabaseFact]
    public async Task A_provider_failure_with_no_prior_snapshot_answers_503()
    {
        using var failing = new FailingProviderFactory();
        using var client = TestClient.Create(failing);
        await client.SignInAsync();

        await client.PutAsJsonAsync(
            "/api/v1/weather/settings", new { latitude = 0, longitude = 0, units = "metric" });

        var response = await client.GetAsync("/api/v1/weather");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Weather unavailable", problem.GetProperty("title").GetString());

        await client.SendAsync(DeleteSettingsRequest());
    }

    private static HttpRequestMessage DeleteSettingsRequest() =>
        new(HttpMethod.Delete, "/api/v1/weather/settings") { Content = JsonContent.Create(new { }) };

    /// <summary>
    /// An <see cref="ApiDatabaseFactory"/> with <c>Weather:Provider=Fake</c>, so every test in
    /// this class runs against <see cref="FakeWeatherProvider"/>'s deterministic forecast and
    /// never touches the live network.
    /// </summary>
    public sealed class ConfiguredFactory : ApiDatabaseFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Weather:Provider"] = "Fake",
                }));
        }
    }

    /// <summary>A <see cref="HomonApiFactory"/> with <c>Auth:RequireSignInForReaders</c> forced on.</summary>
    private sealed class ReaderGatedFactory : HomonApiFactory
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

    /// <summary>An <see cref="ApiDatabaseFactory"/> whose <see cref="IWeatherProvider"/> always throws.</summary>
    private sealed class FailingProviderFactory : ApiDatabaseFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IWeatherProvider>();
                services.AddSingleton<IWeatherProvider>(new AlwaysFailingWeatherProvider());
            });
        }
    }

    private sealed class AlwaysFailingWeatherProvider : IWeatherProvider
    {
        public Task<WeatherForecast> GetForecastAsync(WeatherSettings settings, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Open-Meteo is down.");
    }
}
