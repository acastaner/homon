using System.Globalization;
using Homon.Domain.Weather;

namespace Homon.Infrastructure.Weather;

/// <summary>
/// A fixed, deterministic forecast — no network call ever made. Exists so the e2e gate (and
/// any other automated run) never reaches the live Open-Meteo API. Only reachable via
/// <c>Weather:Provider=Fake</c>, which
/// <c>InfrastructureServiceCollectionExtensions.AddWeather</c> refuses in Production.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every weather assertion in the e2e and endpoint suites is against these numbers.</b>
/// Current conditions (18 °C, feels like 17, wind 12, clear, daytime) and the first three
/// forecast days (19/11, 21/12, 16/9) are byte-identical to what plan 010 shipped, because
/// <c>dashboard-page.test.tsx</c> and <c>e2e/weather.spec.ts</c> match those strings.
/// </para>
/// <para>
/// Plan 020 added today (high 20, low 12 — chosen so neither figure collides with the
/// current 18 that existing regex assertions match on), four more days, 192 hourly rows, and
/// one deliberate three-hour gust run at 94 km/h so the weather page's advisory banner is
/// reachable from a test. The run is positioned <em>relative to the clock</em>, two hours
/// ahead of now, because the endpoint slices its window from the current hour and a run at a
/// fixed wall-clock time would fall outside it for most of the day.
/// </para>
/// </remarks>
public sealed class FakeWeatherProvider(TimeProvider timeProvider) : IWeatherProvider
{
    /// <summary>The gust the planted run reaches, in km/h — above the 90 km/h severe threshold.</summary>
    private const double PlantedGust = 94;

    /// <summary>How far ahead of the current hour the planted gust run starts.</summary>
    private const int PlantedGustOffsetHours = 2;

    private const int PlantedGustLengthHours = 3;

    public Task<WeatherForecast> GetForecastAsync(WeatherSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);

        var current = new WeatherCurrent(
            Temperature: 18,
            ApparentTemperature: 17,
            WindSpeed: 12,
            Condition: WeatherCondition.Clear,
            IsDay: true);

        // Today first, then seven more. Precipitation and snowfall stay well under the
        // advisory thresholds and the temperatures stay temperate, so the planted gust run is
        // the *only* advisory this fake produces — which is what lets a test assert on one.
        var days = new List<WeatherDay>
        {
            Day(today, WeatherCondition.Clear, high: 20, low: 12, precipitation: 0),
            Day(today.AddDays(1), WeatherCondition.PartlyCloudy, high: 19, low: 11, precipitation: 1),
            Day(today.AddDays(2), WeatherCondition.Clear, high: 21, low: 12, precipitation: 0),
            Day(today.AddDays(3), WeatherCondition.Rain, high: 16, low: 9, precipitation: 6),
            Day(today.AddDays(4), WeatherCondition.Cloudy, high: 17, low: 10, precipitation: 2),
            Day(today.AddDays(5), WeatherCondition.Clear, high: 20, low: 11, precipitation: 0),
            Day(today.AddDays(6), WeatherCondition.PartlyCloudy, high: 18, low: 10, precipitation: 1),
            Day(today.AddDays(7), WeatherCondition.Drizzle, high: 15, low: 8, precipitation: 4),
        };

        var midnight = today.ToDateTime(TimeOnly.MinValue);

        // The index, in the hourly array, where the planted gust run begins.
        var gustStart = (int)(now - midnight).TotalHours + PlantedGustOffsetHours;

        var hours = new List<WeatherHour>(days.Count * 24);

        for (var i = 0; i < days.Count * 24; i++)
        {
            var stamp = midnight.AddHours(i);
            var gusty = i >= gustStart && i < gustStart + PlantedGustLengthHours;

            hours.Add(new WeatherHour(
                Date: DateOnly.FromDateTime(stamp),
                Time: stamp.ToString("HH:mm", CultureInfo.InvariantCulture),
                Condition: WeatherCondition.Clear,
                WmoCode: 0,
                Temperature: 16,
                ApparentTemperature: 15,
                WindSpeed: 12,
                WindGusts: gusty ? PlantedGust : 12,
                PrecipitationProbability: gusty ? 40 : 10));
        }

        // UtcOffsetSeconds is zero: this fake's clock *is* UTC, so the endpoint's
        // "current hour at the location" lands on the same hour the loop above built.
        return Task.FromResult(new WeatherForecast(current, days, hours, UtcOffsetSeconds: 0));
    }

    private static WeatherDay Day(
        DateOnly date, WeatherCondition condition, double high, double low, double precipitation) =>
        new(
            Date: date,
            Condition: condition,
            High: high,
            Low: low,
            PrecipitationSum: precipitation,
            SnowfallSum: 0,
            WindSpeedMax: 18,
            WindGustsMax: 30,
            Sunrise: "07:10",
            Sunset: "18:53");
}
