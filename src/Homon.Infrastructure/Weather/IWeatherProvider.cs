using Homon.Domain.Weather;

namespace Homon.Infrastructure.Weather;

/// <summary>
/// Fetches a fresh forecast for a location. Throws on any failure — transport, timeout, or
/// an unparsable response — and leaves deciding what that means to <see cref="WeatherCache"/>.
/// </summary>
public interface IWeatherProvider
{
    Task<WeatherForecast> GetForecastAsync(WeatherSettings settings, CancellationToken cancellationToken);
}
