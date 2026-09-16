using Homon.Domain.Weather;

namespace Homon.Infrastructure.Weather;

/// <summary>
/// A fixed, deterministic forecast — clear, 18 °C, no network call ever made. Exists so the
/// e2e gate (and any other automated run) never reaches the live Open-Meteo API. Only
/// reachable via <c>Weather:Provider=Fake</c>, which
/// <c>InfrastructureServiceCollectionExtensions.AddWeather</c> refuses in Production.
/// </summary>
public sealed class FakeWeatherProvider(TimeProvider timeProvider) : IWeatherProvider
{
    public Task<WeatherForecast> GetForecastAsync(WeatherSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var current = new WeatherCurrent(
            Temperature: 18,
            ApparentTemperature: 17,
            WindSpeed: 12,
            Condition: WeatherCondition.Clear,
            IsDay: true);

        var days = new[]
        {
            new WeatherForecastDay(today.AddDays(1), WeatherCondition.PartlyCloudy, High: 19, Low: 11),
            new WeatherForecastDay(today.AddDays(2), WeatherCondition.Clear, High: 21, Low: 12),
            new WeatherForecastDay(today.AddDays(3), WeatherCondition.Rain, High: 16, Low: 9),
        };

        return Task.FromResult(new WeatherForecast(current, days));
    }
}
