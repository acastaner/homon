using System.Net;
using Homon.Domain.Weather;
using Homon.Infrastructure.Weather;

namespace Homon.Api.Tests;

public class OpenMeteoWeatherProviderTests
{
    /// <summary>
    /// Confirmed live against api.open-meteo.com on 2026-09-15 — see plan 010's Context. If
    /// Open-Meteo ever changes this shape, this fixture is the one place to refresh (from a
    /// fresh curl); the gate would otherwise stay green on the fake provider even if the real
    /// integration had silently broken.
    /// </summary>
    private const string SampleBody = """
        {"current":{"temperature_2m":25.0,"apparent_temperature":26.8,"weather_code":0,
          "wind_speed_10m":20.9,"is_day":0},
         "daily":{"time":["2026-09-15","2026-09-16","2026-09-17","2026-09-18"],
          "weather_code":[51,51,51,51],
          "temperature_2m_max":[25.0,25.0,24.6,24.5],"temperature_2m_min":[24.3,24.3,24.2,23.9]}}
        """;

    [Fact]
    public async Task The_confirmed_shape_maps_to_current_plus_exactly_three_forecast_days()
    {
        var (provider, handler) = MakeProvider();
        handler.Handler = (_, _) => Task.FromResult(MakeResponse(HttpStatusCode.OK, SampleBody));

        var forecast = await provider.GetForecastAsync(MakeSettings(), CancellationToken.None);

        Assert.Equal(25.0, forecast.Current.Temperature);
        Assert.Equal(26.8, forecast.Current.ApparentTemperature);
        Assert.Equal(20.9, forecast.Current.WindSpeed);
        Assert.Equal(WeatherCondition.Clear, forecast.Current.Condition);
        Assert.False(forecast.Current.IsDay);

        Assert.Equal(3, forecast.Days.Count);
        Assert.Equal(new DateOnly(2026, 9, 16), forecast.Days[0].Date);
        Assert.Equal(new DateOnly(2026, 9, 17), forecast.Days[1].Date);
        Assert.Equal(new DateOnly(2026, 9, 18), forecast.Days[2].Date);
        Assert.All(forecast.Days, day => Assert.Equal(WeatherCondition.Drizzle, day.Condition));
        Assert.Equal(25.0, forecast.Days[0].High);
        Assert.Equal(24.3, forecast.Days[0].Low);
        Assert.Equal(24.5, forecast.Days[2].High);
        Assert.Equal(23.9, forecast.Days[2].Low);
    }

    [Fact]
    public async Task Metric_settings_add_no_unit_query_parameters()
    {
        var (provider, handler) = MakeProvider();
        handler.Handler = (_, _) => Task.FromResult(MakeResponse(HttpStatusCode.OK, SampleBody));

        await provider.GetForecastAsync(MakeSettings(units: WeatherUnits.Metric), CancellationToken.None);

        var query = handler.LastRequest!.RequestUri!.Query;
        Assert.DoesNotContain("temperature_unit", query, StringComparison.Ordinal);
        Assert.DoesNotContain("wind_speed_unit", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Imperial_settings_request_Fahrenheit_and_mph()
    {
        var (provider, handler) = MakeProvider();
        handler.Handler = (_, _) => Task.FromResult(MakeResponse(HttpStatusCode.OK, SampleBody));

        await provider.GetForecastAsync(MakeSettings(units: WeatherUnits.Imperial), CancellationToken.None);

        var query = handler.LastRequest!.RequestUri!.Query;
        Assert.Contains("temperature_unit=fahrenheit", query, StringComparison.Ordinal);
        Assert.Contains("wind_speed_unit=mph", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_response_delayed_past_the_request_timeout_throws()
    {
        var (provider, handler) = MakeProvider(requestTimeout: TimeSpan.FromMilliseconds(50));
        handler.Handler = async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return MakeResponse(HttpStatusCode.OK, SampleBody);
        };

        await Assert.ThrowsAsync<TimeoutException>(
            () => provider.GetForecastAsync(MakeSettings(), CancellationToken.None));
    }

    [Fact]
    public async Task A_non_2xx_response_throws()
    {
        var (provider, handler) = MakeProvider();
        handler.Handler = (_, _) => Task.FromResult(MakeResponse(HttpStatusCode.ServiceUnavailable, "down"));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.GetForecastAsync(MakeSettings(), CancellationToken.None));
    }

    [Fact]
    public async Task A_malformed_body_throws()
    {
        var (provider, handler) = MakeProvider();
        handler.Handler = (_, _) => Task.FromResult(MakeResponse(HttpStatusCode.OK, "not json"));

        await Assert.ThrowsAnyAsync<Exception>(
            () => provider.GetForecastAsync(MakeSettings(), CancellationToken.None));
    }

    private static (OpenMeteoWeatherProvider Provider, StubHttpMessageHandler Handler) MakeProvider(TimeSpan? requestTimeout = null)
    {
        var handler = new StubHttpMessageHandler();
        var httpClientFactory = new FakeHttpClientFactory(
            OpenMeteoWeatherProvider.HttpClientName, handler, new Uri("https://api.open-meteo.com/"));

        var provider = requestTimeout is { } timeout
            ? new OpenMeteoWeatherProvider(httpClientFactory, timeout)
            : new OpenMeteoWeatherProvider(httpClientFactory);

        return (provider, handler);
    }

    private static WeatherSettings MakeSettings(WeatherUnits units = WeatherUnits.Metric) => new()
    {
        Latitude = 51.5,
        Longitude = -0.12,
        Units = units,
    };

    private static HttpResponseMessage MakeResponse(HttpStatusCode statusCode, string body) =>
        new(statusCode) { Content = new StringContent(body) };
}
