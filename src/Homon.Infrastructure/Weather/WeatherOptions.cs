namespace Homon.Infrastructure.Weather;

/// <summary>Which <see cref="IWeatherProvider"/> the host resolves.</summary>
public sealed class WeatherOptions
{
    public const string SectionName = "Weather";

    /// <summary>
    /// Defaults to <see cref="WeatherProviderKind.OpenMeteo"/>. <see cref="WeatherProviderKind.Fake"/>
    /// exists only so the e2e gate — and any other automated run — never reaches the live
    /// network; it is refused outside Development/Testing, see
    /// <c>InfrastructureServiceCollectionExtensions.AddWeather</c>.
    /// </summary>
    public WeatherProviderKind Provider { get; set; } = WeatherProviderKind.OpenMeteo;
}

/// <summary>Which <see cref="IWeatherProvider"/> implementation is resolved.</summary>
public enum WeatherProviderKind
{
    /// <summary>The real provider, over HTTPS, no API key required.</summary>
    OpenMeteo,

    /// <summary>A fixed, deterministic forecast. Only reachable via <c>Weather:Provider=Fake</c>.</summary>
    Fake,
}
