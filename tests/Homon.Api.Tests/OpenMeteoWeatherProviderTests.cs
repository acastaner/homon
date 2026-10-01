using System.Net;
using Homon.Domain.Weather;
using Homon.Infrastructure.Weather;

namespace Homon.Api.Tests;

public class OpenMeteoWeatherProviderTests
{
    /// <summary>
    /// Captured live from api.open-meteo.com on 2026-10-01 — see plan 020's Step 0 — then
    /// trimmed to 8 daily and 48 hourly rows and doctored in exactly four places, each of which
    /// a test below depends on:
    /// <list type="bullet">
    ///   <item><c>hourly.precipitation_probability[5]</c> and <c>daily.sunrise[7]</c> and
    ///   <c>daily.wind_gusts_10m_max[7]</c> nulled, because Open-Meteo answers null for a
    ///   variable a model does not report and a <c>double[]</c> throws on a null element;</item>
    ///   <item><c>hourly.wind_gusts_10m[14..16]</c> raised to 94 km/h, a real severe-advisory
    ///   figure.</item>
    /// </list>
    /// If Open-Meteo ever changes this shape, this fixture is the one place to refresh (from a
    /// fresh curl); the gate would otherwise stay green on the fake provider even if the real
    /// integration had silently broken.
    /// </summary>
    private const string SampleBody = """
        {"utc_offset_seconds":3600,"current":{"time":"2026-10-01T08:45","interval":900,"temperature_2m":14.9,
        "apparent_temperature":13.8,"weather_code":0,"wind_speed_10m":11.2,"is_day":1},
        "hourly":{"time":["2026-10-01T00:00","2026-10-01T01:00","2026-10-01T02:00","2026-10-01T03:00",
        "2026-10-01T04:00","2026-10-01T05:00","2026-10-01T06:00","2026-10-01T07:00","2026-10-01T08:00",
        "2026-10-01T09:00","2026-10-01T10:00","2026-10-01T11:00","2026-10-01T12:00","2026-10-01T13:00",
        "2026-10-01T14:00","2026-10-01T15:00","2026-10-01T16:00","2026-10-01T17:00","2026-10-01T18:00",
        "2026-10-01T19:00","2026-10-01T20:00","2026-10-01T21:00","2026-10-01T22:00","2026-10-01T23:00",
        "2026-10-02T00:00","2026-10-02T01:00","2026-10-02T02:00","2026-10-02T03:00","2026-10-02T04:00",
        "2026-10-02T05:00","2026-10-02T06:00","2026-10-02T07:00","2026-10-02T08:00","2026-10-02T09:00",
        "2026-10-02T10:00","2026-10-02T11:00","2026-10-02T12:00","2026-10-02T13:00","2026-10-02T14:00",
        "2026-10-02T15:00","2026-10-02T16:00","2026-10-02T17:00","2026-10-02T18:00","2026-10-02T19:00",
        "2026-10-02T20:00","2026-10-02T21:00","2026-10-02T22:00","2026-10-02T23:00"],"temperature_2m":[18.2,16.6,
        16.0,15.4,15.4,15.0,14.8,14.6,14.4,15.1,16.0,17.3,18.3,19.3,19.8,20.0,20.4,20.1,19.7,18.9,18.0,17.0,16.4,
        15.7,15.1,14.5,13.9,13.5,13.0,12.8,13.1,12.6,13.2,14.9,17.2,18.6,19.5,20.0,20.6,20.9,20.8,20.4,19.5,18.5,
        17.8,17.2,16.6,16.1],"apparent_temperature":[17.1,16.1,16.0,14.9,15.3,14.9,14.7,14.2,13.4,14.0,14.8,15.7,
        16.6,16.9,17.3,17.0,17.1,17.0,17.0,16.4,15.3,14.8,14.7,14.1,13.8,13.4,13.0,12.6,12.1,11.9,12.0,11.7,12.2,
        13.9,15.8,16.2,16.9,17.3,17.7,18.0,17.8,17.3,16.4,15.8,15.8,15.7,15.3,15.0],"weather_code":[2,51,3,3,2,1,2,
        0,1,0,0,0,1,2,2,2,1,1,1,0,1,3,1,0,0,0,0,0,0,0,0,0,0,0,0,2,3,2,2,2,1,1,0,0,3,3,3,3],"wind_speed_10m":[10.1,
        11.2,7.9,10.1,8.6,7.9,7.2,8.3,11.5,10.8,10.4,10.1,9.7,12.2,13.0,14.0,14.8,13.7,12.2,11.5,13.3,12.2,9.4,8.6,
        6.8,5.8,4.7,4.0,3.2,3.6,5.4,4.7,5.8,6.1,9.4,14.0,14.0,14.0,15.5,14.8,15.8,15.5,15.8,15.1,12.6,10.4,8.6,7.6],
        "wind_gusts_10m":[21.6,23.0,16.6,20.5,18.7,19.1,16.6,17.6,23.8,22.3,23.8,24.5,25.6,29.5,94.0,94.0,94.0,25.9,
        28.4,24.8,24.8,23.4,24.1,19.4,16.9,15.5,10.4,9.4,10.4,7.6,8.3,7.6,10.4,13.7,22.0,30.6,31.7,34.2,33.8,33.8,
        35.3,32.8,31.3,29.2,25.9,23.0,19.4,17.3],"precipitation_probability":[43,55,48,32,18,null,6,2,0,0,0,0,0,0,0,
        0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0]},"daily":{"time":["2026-10-01",
        "2026-10-02","2026-10-03","2026-10-04","2026-10-05","2026-10-06","2026-10-07","2026-10-08"],
        "weather_code":[51,3,51,3,3,51,3,1],"temperature_2m_max":[20.4,20.9,20.0,20.9,21.3,19.0,16.7,14.7],
        "temperature_2m_min":[14.4,12.6,14.7,13.9,16.2,14.2,10.0,7.8],"precipitation_sum":[0.1,0.0,0.6,0.0,0.0,0.9,
        0.0,0.0],"snowfall_sum":[0.0,0.0,0.0,0.0,0.0,0.0,0.0,0.0],"wind_speed_10m_max":[14.8,15.8,9.4,11.2,15.8,
        18.4,14.0,14.2],"wind_gusts_10m_max":[33.5,35.3,21.2,26.3,33.1,37.8,36.7,null],
        "sunrise":["2026-10-01T07:01","2026-10-02T07:02","2026-10-03T07:04","2026-10-04T07:05","2026-10-05T07:07",
        "2026-10-06T07:09","2026-10-07T07:10",null],"sunset":["2026-10-01T18:38","2026-10-02T18:36",
        "2026-10-03T18:33","2026-10-04T18:31","2026-10-05T18:29","2026-10-06T18:27","2026-10-07T18:24",
        "2026-10-08T18:22"]}}
        """;

    [Fact]
    public async Task The_confirmed_shape_maps_to_current_plus_eight_days_today_first()
    {
        var forecast = await Fetch();

        Assert.Equal(14.9, forecast.Current.Temperature);
        Assert.Equal(13.8, forecast.Current.ApparentTemperature);
        Assert.Equal(11.2, forecast.Current.WindSpeed);
        Assert.Equal(WeatherCondition.Clear, forecast.Current.Condition);
        Assert.True(forecast.Current.IsDay);

        // Eight days, today first — plan 020, D9. Before it, the provider dropped daily[0].
        Assert.Equal(8, forecast.Days.Count);
        Assert.Equal(new DateOnly(2026, 10, 1), forecast.Days[0].Date);
        Assert.Equal(new DateOnly(2026, 10, 8), forecast.Days[7].Date);
    }

    [Fact]
    public async Task A_day_carries_every_variable_the_page_tabulates()
    {
        var today = (await Fetch()).Days[0];

        Assert.Equal(WeatherCondition.Drizzle, today.Condition);  // WMO 51
        Assert.Equal(20.4, today.High);
        Assert.Equal(14.4, today.Low);
        Assert.Equal(0.1, today.PrecipitationSum);
        Assert.Equal(0, today.SnowfallSum);
        Assert.Equal(14.8, today.WindSpeedMax);
        Assert.Equal(33.5, today.WindGustsMax);

        // "2026-10-01T07:01" becomes "07:01": the location's own clock, as a string (D7).
        Assert.Equal("07:01", today.Sunrise);
        Assert.Equal("18:38", today.Sunset);
    }

    [Fact]
    public async Task The_hourly_block_is_parsed_whole_and_anchored_at_local_midnight()
    {
        var forecast = await Fetch();

        // The provider slices nothing — the endpoint does, per request (D6).
        Assert.Equal(48, forecast.Hours.Count);
        Assert.Equal("00:00", forecast.Hours[0].Time);
        Assert.Equal(new DateOnly(2026, 10, 1), forecast.Hours[0].Date);
        Assert.Equal("23:00", forecast.Hours[23].Time);
        Assert.Equal(new DateOnly(2026, 10, 2), forecast.Hours[24].Date);
    }

    [Fact]
    public async Task An_hour_carries_every_variable_the_table_and_the_advisories_need()
    {
        var hour = (await Fetch()).Hours[0];

        Assert.Equal(18.2, hour.Temperature);
        Assert.Equal(17.1, hour.ApparentTemperature);
        Assert.Equal(10.1, hour.WindSpeed);
        Assert.Equal(21.6, hour.WindGusts);
        Assert.Equal(43, hour.PrecipitationProbability);

        // The raw WMO code is kept beside the condition word, because Thunderstorm flattens
        // 95 together with 96 and 99 and the advisory evaluator grades those differently.
        Assert.Equal(2, hour.WmoCode);
        Assert.Equal(WeatherCondition.PartlyCloudy, hour.Condition);
    }

    [Fact]
    public async Task The_timezone_offset_is_read_so_the_endpoint_can_find_the_current_local_hour()
    {
        Assert.Equal(3600, (await Fetch()).UtcOffsetSeconds);
    }

    [Fact]
    public async Task A_null_element_becomes_null_rather_than_zero_or_a_throw()
    {
        var forecast = await Fetch();

        Assert.Null(forecast.Hours[5].PrecipitationProbability);
        Assert.Null(forecast.Days[7].Sunrise);
        Assert.Null(forecast.Days[7].WindGustsMax);

        // Its neighbours are unaffected — a null is one absent reading, not a broken array.
        Assert.NotNull(forecast.Hours[4].PrecipitationProbability);
        Assert.NotNull(forecast.Days[7].Sunset);
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
        Assert.DoesNotContain("precipitation_unit", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Imperial_settings_request_Fahrenheit_mph_and_inches()
    {
        var (provider, handler) = MakeProvider();
        handler.Handler = (_, _) => Task.FromResult(MakeResponse(HttpStatusCode.OK, SampleBody));

        await provider.GetForecastAsync(MakeSettings(units: WeatherUnits.Imperial), CancellationToken.None);

        var query = handler.LastRequest!.RequestUri!.Query;
        Assert.Contains("temperature_unit=fahrenheit", query, StringComparison.Ordinal);
        Assert.Contains("wind_speed_unit=mph", query, StringComparison.Ordinal);

        // precipitation_unit covers snowfall_sum as well as precipitation_sum (verified
        // 2026-10-01). Without it an imperial household reads millimetres and centimetres.
        Assert.Contains("precipitation_unit=inch", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_request_asks_for_the_hourly_and_daily_variables_the_page_needs()
    {
        var (provider, handler) = MakeProvider();
        handler.Handler = (_, _) => Task.FromResult(MakeResponse(HttpStatusCode.OK, SampleBody));

        await provider.GetForecastAsync(MakeSettings(), CancellationToken.None);

        var query = Uri.UnescapeDataString(handler.LastRequest!.RequestUri!.Query);

        Assert.Contains("forecast_days=8", query, StringComparison.Ordinal);
        Assert.Contains("timezone=auto", query, StringComparison.Ordinal);

        foreach (var variable in new[]
        {
            "wind_gusts_10m", "precipitation_probability", "precipitation_sum", "snowfall_sum",
            "wind_speed_10m_max", "wind_gusts_10m_max", "sunrise", "sunset",
        })
        {
            Assert.Contains(variable, query, StringComparison.Ordinal);
        }
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

    /// <summary>The sample body through the provider — the arrangement nine facts above share.</summary>
    private static async Task<WeatherForecast> Fetch()
    {
        var (provider, handler) = MakeProvider();
        handler.Handler = (_, _) => Task.FromResult(MakeResponse(HttpStatusCode.OK, SampleBody));

        return await provider.GetForecastAsync(MakeSettings(), CancellationToken.None);
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
