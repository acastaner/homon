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

        // Confirmed against the live API — see plan 010's Context. Only current + daily[1..3]
        // are used (today is already covered by `current`); forecast_days=4 asks for exactly
        // that window. temperature_unit/wind_speed_unit are added only for Imperial — Metric
        // is Open-Meteo's own default, so nothing here converts a value.
        var query = $"v1/forecast?latitude={latitude}&longitude={longitude}"
            + "&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m,is_day"
            + "&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=4";

        if (settings.Units == WeatherUnits.Imperial)
        {
            query += "&temperature_unit=fahrenheit&wind_speed_unit=mph";
        }

        return new Uri(query, UriKind.Relative);
    }

    private static WeatherForecast ToForecast(OpenMeteoResponse response)
    {
        var current = response.Current;
        var daily = response.Daily;

        var currentResult = new WeatherCurrent(
            Temperature: current.Temperature2m,
            ApparentTemperature: current.ApparentTemperature,
            WindSpeed: current.WindSpeed10m,
            Condition: WmoWeatherCodeMap.Map(current.WeatherCode),
            IsDay: current.IsDay != 0);

        // daily[0] is today, already covered by `current` — the three forecast rows are
        // daily[1..3].
        var days = new List<WeatherForecastDay>();

        for (var i = 1; i < daily.Time.Length && days.Count < 3; i++)
        {
            days.Add(new WeatherForecastDay(
                Date: DateOnly.Parse(daily.Time[i], CultureInfo.InvariantCulture),
                Condition: WmoWeatherCodeMap.Map(daily.WeatherCode[i]),
                High: daily.TemperatureMax[i],
                Low: daily.TemperatureMin[i]));
        }

        return new WeatherForecast(currentResult, days);
    }

    private sealed record OpenMeteoResponse(
        [property: JsonPropertyName("current")] OpenMeteoCurrent Current,
        [property: JsonPropertyName("daily")] OpenMeteoDaily Daily);

    private sealed record OpenMeteoCurrent(
        [property: JsonPropertyName("temperature_2m")] double Temperature2m,
        [property: JsonPropertyName("apparent_temperature")] double ApparentTemperature,
        [property: JsonPropertyName("weather_code")] int WeatherCode,
        [property: JsonPropertyName("wind_speed_10m")] double WindSpeed10m,
        [property: JsonPropertyName("is_day")] int IsDay);

    private sealed record OpenMeteoDaily(
        [property: JsonPropertyName("time")] string[] Time,
        [property: JsonPropertyName("weather_code")] int[] WeatherCode,
        [property: JsonPropertyName("temperature_2m_max")] double[] TemperatureMax,
        [property: JsonPropertyName("temperature_2m_min")] double[] TemperatureMin);
}
