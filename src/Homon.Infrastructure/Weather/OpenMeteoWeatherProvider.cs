using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Homon.Domain.Weather;

namespace Homon.Infrastructure.Weather;

/// <summary>
/// Fetches current conditions and a short forecast from Open-Meteo
/// (https://api.open-meteo.com), which needs no API key. Outbound-HTTP-with-timeout follows
/// <c>ResendEmailSender.cs</c>: a linked <see cref="CancellationTokenSource"/>, not
/// <see cref="HttpClient.Timeout"/>, because the client is shared via the factory.
/// </summary>
public sealed class OpenMeteoWeatherProvider : IWeatherProvider
{
    /// <summary>The name <c>AddHttpClient</c> registers this provider's client under.</summary>
    internal const string HttpClientName = "OpenMeteo";

    /// <summary>
    /// Ceiling on one provider call. Enforced with a linked <see cref="CancellationTokenSource"/>
    /// so it applies regardless of how the shared <see cref="HttpClient"/> is configured.
    /// </summary>
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(10);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeSpan _requestTimeout;

    public OpenMeteoWeatherProvider(IHttpClientFactory httpClientFactory)
        : this(httpClientFactory, DefaultRequestTimeout)
    {
    }

    /// <summary>
    /// Test-only seam: a short <paramref name="requestTimeout"/> so a hang test's real wait
    /// is milliseconds, not the real 10 seconds. Internal, so dependency injection — which
    /// only ever sees public constructors — always resolves the single-argument one above.
    /// </summary>
    internal OpenMeteoWeatherProvider(IHttpClientFactory httpClientFactory, TimeSpan requestTimeout)
    {
        _httpClientFactory = httpClientFactory;
        _requestTimeout = requestTimeout;
    }

    public async Task<WeatherForecast> GetForecastAsync(WeatherSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var client = _httpClientFactory.CreateClient(HttpClientName);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_requestTimeout);

        try
        {
            var response = await client
                .GetAsync(BuildRequestUri(settings), timeoutSource.Token)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var body = await response.Content
                .ReadFromJsonAsync<OpenMeteoResponse>(timeoutSource.Token)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("Open-Meteo returned an empty body.");

            return ToForecast(body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Open-Meteo did not answer within {_requestTimeout.TotalSeconds:0} seconds.");
        }
    }

    private static Uri BuildRequestUri(WeatherSettings settings)
    {
        var latitude = settings.Latitude.ToString(CultureInfo.InvariantCulture);
        var longitude = settings.Longitude.ToString(CultureInfo.InvariantCulture);

        // Confirmed against the live API on 2026-10-01 — see plan 020's Step 0. forecast_days=8
        // is today plus the seven the weather page tabulates; `daily[0]` is today, which is
        // kept now (the widget shows today's high and low) where it used to be dropped. The
        // hourly block comes back anchored at today 00:00 *local*, 24 rows per day, and the
        // endpoint slices the window it wants per request.
        //
        // wind_gusts_10m and precipitation_probability exist for the derived advisories and the
        // hourly table; wind_gusts_10m_max, precipitation_sum and snowfall_sum for the daily
        // ones. temperature_unit/wind_speed_unit/precipitation_unit are sent only for Imperial
        // — Metric is Open-Meteo's own default, so nothing here converts a value.
        var query = $"v1/forecast?latitude={latitude}&longitude={longitude}"
            + "&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m,is_day"
            + "&hourly=temperature_2m,apparent_temperature,weather_code,wind_speed_10m"
            + ",wind_gusts_10m,precipitation_probability"
            + "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_sum"
            + ",snowfall_sum,wind_speed_10m_max,wind_gusts_10m_max,sunrise,sunset"
            + "&timezone=auto&forecast_days=8";

        if (settings.Units == WeatherUnits.Imperial)
        {
            // precipitation_unit covers snowfall_sum as well as precipitation_sum (verified
            // 2026-10-01): without it a US household would read its rain and snow in
            // millimetres and centimetres beside Fahrenheit and miles per hour.
            query += "&temperature_unit=fahrenheit&wind_speed_unit=mph&precipitation_unit=inch";
        }

        return new Uri(query, UriKind.Relative);
    }

    private static WeatherForecast ToForecast(OpenMeteoResponse response)
    {
        var current = response.Current;

        var currentResult = new WeatherCurrent(
            Temperature: current.Temperature2m,
            ApparentTemperature: current.ApparentTemperature,
            WindSpeed: current.WindSpeed10m,
            Condition: WmoWeatherCodeMap.Map(current.WeatherCode),
            IsDay: current.IsDay != 0);

        return new WeatherForecast(
            currentResult,
            ToDays(response.Daily),
            ToHours(response.Hourly),
            response.UtcOffsetSeconds);
    }

    /// <summary>
    /// Every day Open-Meteo answered with, today first. Open-Meteo returns parallel arrays, so
    /// a shorter one means a variable this model does not report — <see cref="At"/> yields null
    /// rather than throwing, and a null figure never trips an advisory.
    /// </summary>
    private static List<WeatherDay> ToDays(OpenMeteoDaily daily)
    {
        var days = new List<WeatherDay>(daily.Time.Length);

        for (var i = 0; i < daily.Time.Length; i++)
        {
            days.Add(new WeatherDay(
                Date: DateOnly.Parse(daily.Time[i], CultureInfo.InvariantCulture),
                Condition: WmoWeatherCodeMap.Map(At(daily.WeatherCode, i) ?? -1),
                High: At(daily.TemperatureMax, i) ?? 0,
                Low: At(daily.TemperatureMin, i) ?? 0,
                PrecipitationSum: At(daily.PrecipitationSum, i),
                SnowfallSum: At(daily.SnowfallSum, i),
                WindSpeedMax: At(daily.WindSpeedMax, i),
                WindGustsMax: At(daily.WindGustsMax, i),
                Sunrise: ClockTime(At(daily.Sunrise, i)),
                Sunset: ClockTime(At(daily.Sunset, i))));
        }

        return days;
    }

    private static List<WeatherHour> ToHours(OpenMeteoHourly? hourly)
    {
        if (hourly is null)
        {
            return [];
        }

        var hours = new List<WeatherHour>(hourly.Time.Length);

        for (var i = 0; i < hourly.Time.Length; i++)
        {
            // "2026-10-01T13:00" — a local, zone-less stamp. The date is parsed; the clock time
            // is carried across the wire as the string it already is (plan 020, D7).
            var stamp = hourly.Time[i];
            var time = ClockTime(stamp);

            if (time is null)
            {
                continue;
            }

            var wmoCode = At(hourly.WeatherCode, i) ?? -1;

            hours.Add(new WeatherHour(
                Date: DateOnly.Parse(stamp.Split('T')[0], CultureInfo.InvariantCulture),
                Time: time,
                Condition: WmoWeatherCodeMap.Map(wmoCode),
                WmoCode: wmoCode,
                Temperature: At(hourly.Temperature2m, i) ?? 0,
                ApparentTemperature: At(hourly.ApparentTemperature, i) ?? 0,
                WindSpeed: At(hourly.WindSpeed10m, i) ?? 0,
                WindGusts: At(hourly.WindGusts10m, i),
                PrecipitationProbability: At(hourly.PrecipitationProbability, i)));
        }

        return hours;
    }

    /// <summary>
    /// The <c>"HH:mm"</c> half of a local ISO stamp, or null when the value is absent or not a
    /// stamp. <c>sunrise</c> and <c>sunset</c> are null where the sun does not rise, which a
    /// polar installation of a generic product will meet.
    /// </summary>
    private static string? ClockTime(string? isoLocal)
    {
        if (string.IsNullOrEmpty(isoLocal))
        {
            return null;
        }

        var separator = isoLocal.IndexOf('T', StringComparison.Ordinal);

        return separator < 0 || isoLocal.Length < separator + 6
            ? null
            : isoLocal.Substring(separator + 1, 5);
    }

    /// <summary>
    /// <paramref name="values"/>[<paramref name="index"/>] when the array is present and long
    /// enough, else null. Open-Meteo's arrays are parallel by contract, but a variable a model
    /// does not report comes back absent or short, and a forecast is not worth throwing over.
    /// </summary>
    private static T? At<T>(T?[]? values, int index)
        where T : struct =>
        values is not null && index < values.Length ? values[index] : null;

    private static string? At(string?[]? values, int index) =>
        values is not null && index < values.Length ? values[index] : null;

    private sealed record OpenMeteoResponse(
        [property: JsonPropertyName("utc_offset_seconds")] int UtcOffsetSeconds,
        [property: JsonPropertyName("current")] OpenMeteoCurrent Current,
        [property: JsonPropertyName("hourly")] OpenMeteoHourly? Hourly,
        [property: JsonPropertyName("daily")] OpenMeteoDaily Daily);

    private sealed record OpenMeteoCurrent(
        [property: JsonPropertyName("temperature_2m")] double Temperature2m,
        [property: JsonPropertyName("apparent_temperature")] double ApparentTemperature,
        [property: JsonPropertyName("weather_code")] int WeatherCode,
        [property: JsonPropertyName("wind_speed_10m")] double WindSpeed10m,
        [property: JsonPropertyName("is_day")] int IsDay);

    // Every numeric array is a nullable element type on purpose: Open-Meteo answers `null` for
    // an hour or day a model has no value for, and a `double[]` throws on a null element.
    private sealed record OpenMeteoHourly(
        [property: JsonPropertyName("time")] string[] Time,
        [property: JsonPropertyName("temperature_2m")] double?[]? Temperature2m,
        [property: JsonPropertyName("apparent_temperature")] double?[]? ApparentTemperature,
        [property: JsonPropertyName("weather_code")] int?[]? WeatherCode,
        [property: JsonPropertyName("wind_speed_10m")] double?[]? WindSpeed10m,
        [property: JsonPropertyName("wind_gusts_10m")] double?[]? WindGusts10m,
        [property: JsonPropertyName("precipitation_probability")] int?[]? PrecipitationProbability);

    private sealed record OpenMeteoDaily(
        [property: JsonPropertyName("time")] string[] Time,
        [property: JsonPropertyName("weather_code")] int?[]? WeatherCode,
        [property: JsonPropertyName("temperature_2m_max")] double?[]? TemperatureMax,
        [property: JsonPropertyName("temperature_2m_min")] double?[]? TemperatureMin,
        [property: JsonPropertyName("precipitation_sum")] double?[]? PrecipitationSum,
        [property: JsonPropertyName("snowfall_sum")] double?[]? SnowfallSum,
        [property: JsonPropertyName("wind_speed_10m_max")] double?[]? WindSpeedMax,
        [property: JsonPropertyName("wind_gusts_10m_max")] double?[]? WindGustsMax,
        [property: JsonPropertyName("sunrise")] string?[]? Sunrise,
        [property: JsonPropertyName("sunset")] string?[]? Sunset);
}
