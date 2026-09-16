using Homon.Domain.Weather;
using Homon.Infrastructure.Weather;
using Microsoft.Extensions.Logging.Abstractions;

namespace Homon.Api.Tests;

public class WeatherCacheTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_first_call_populates_the_cache_and_calls_the_provider_once()
    {
        var (cache, provider, _) = MakeCache();

        var result = await cache.GetAsync(MakeSettings(), CancellationToken.None);

        Assert.Equal(1, provider.CallCount);
        Assert.True(result.IsAvailable);
        Assert.False(result.Stale);
        Assert.NotNull(result.Forecast);
    }

    [Fact]
    public async Task A_second_call_inside_FreshFor_does_not_call_the_provider_again()
    {
        var (cache, provider, _) = MakeCache();
        var settings = MakeSettings();

        await cache.GetAsync(settings, CancellationToken.None);
        await cache.GetAsync(settings, CancellationToken.None);

        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task A_call_past_FreshFor_calls_the_provider_again()
    {
        var (cache, provider, time) = MakeCache();
        var settings = MakeSettings();

        await cache.GetAsync(settings, CancellationToken.None);
        time.Advance(WeatherCache.FreshFor + TimeSpan.FromSeconds(1));
        await cache.GetAsync(settings, CancellationToken.None);

        Assert.Equal(2, provider.CallCount);
    }

    [Fact]
    public async Task A_failure_inside_StaleTolerance_serves_the_old_forecast_marked_stale()
    {
        var (cache, provider, time) = MakeCache();
        var settings = MakeSettings();

        var first = await cache.GetAsync(settings, CancellationToken.None);

        provider.Handler = _ => throw new InvalidOperationException("Open-Meteo is down.");
        time.Advance(WeatherCache.FreshFor + TimeSpan.FromMinutes(1));

        var second = await cache.GetAsync(settings, CancellationToken.None);

        Assert.True(second.IsAvailable);
        Assert.True(second.Stale);
        Assert.Equal(first.Forecast, second.Forecast);
    }

    [Fact]
    public async Task A_failure_past_StaleTolerance_reports_unavailable()
    {
        var (cache, provider, time) = MakeCache();
        var settings = MakeSettings();

        await cache.GetAsync(settings, CancellationToken.None);

        provider.Handler = _ => throw new InvalidOperationException("Open-Meteo is down.");
        time.Advance(WeatherCache.StaleTolerance + TimeSpan.FromMinutes(1));

        var result = await cache.GetAsync(settings, CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Null(result.Forecast);
    }

    [Fact]
    public async Task A_failure_with_no_prior_snapshot_reports_unavailable()
    {
        var (cache, provider, _) = MakeCache();
        provider.Handler = _ => throw new InvalidOperationException("Open-Meteo is down.");

        var result = await cache.GetAsync(MakeSettings(), CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Null(result.Forecast);
    }

    [Fact]
    public async Task Two_concurrent_calls_past_FreshFor_share_exactly_one_provider_call()
    {
        var (cache, provider, time) = MakeCache();
        var settings = MakeSettings();

        await cache.GetAsync(settings, CancellationToken.None);
        time.Advance(WeatherCache.FreshFor + TimeSpan.FromSeconds(1));

        // Yields so both calls genuinely race for the refresh lock instead of the first
        // completing synchronously before the second even starts.
        provider.Handler = async _ =>
        {
            await Task.Yield();
            return SampleForecast();
        };

        await Task.WhenAll(
            cache.GetAsync(settings, CancellationToken.None),
            cache.GetAsync(settings, CancellationToken.None));

        Assert.Equal(2, provider.CallCount);
    }

    [Fact]
    public async Task Invalidate_forces_a_refresh_even_inside_FreshFor()
    {
        var (cache, provider, _) = MakeCache();
        var settings = MakeSettings();

        await cache.GetAsync(settings, CancellationToken.None);
        cache.Invalidate();
        await cache.GetAsync(settings, CancellationToken.None);

        Assert.Equal(2, provider.CallCount);
    }

    [Fact]
    public async Task An_invalidate_racing_an_in_flight_fetch_discards_that_fetchs_result_without_losing_the_answer_to_its_own_caller()
    {
        var (cache, provider, _) = MakeCache();
        var settings = MakeSettings();

        var inFlight = new TaskCompletionSource<WeatherForecast>();
        provider.Handler = call => call == 1 ? inFlight.Task : Task.FromResult(SampleForecast());

        // Starts the first fetch — synchronous execution inside GetAsync runs up to awaiting
        // inFlight.Task, so provider.CallCount is already 1 by the time control returns here.
        var firstCall = cache.GetAsync(settings, CancellationToken.None);
        Assert.Equal(1, provider.CallCount);

        // An invalidation for a location change lands while that fetch (for the old settings)
        // is still in flight.
        cache.Invalidate();

        inFlight.SetResult(SampleForecast());
        var firstResult = await firstCall;

        // The in-flight caller still gets the true answer for the settings it was given...
        Assert.True(firstResult.IsAvailable);
        Assert.NotNull(firstResult.Forecast);

        // ...but that answer must not have landed in the snapshot: the next call triggers its
        // own fresh provider call rather than reusing what the pre-invalidate fetch produced.
        await cache.GetAsync(settings, CancellationToken.None);
        Assert.Equal(2, provider.CallCount);
    }

    private static (WeatherCache Cache, StubWeatherProvider Provider, FixedTimeProvider Time) MakeCache()
    {
        var time = new FixedTimeProvider(Epoch);
        var provider = new StubWeatherProvider();
        var cache = new WeatherCache(provider, time, NullLogger<WeatherCache>.Instance);

        return (cache, provider, time);
    }

    private static WeatherSettings MakeSettings() => new()
    {
        Latitude = 51.5,
        Longitude = -0.12,
        Units = WeatherUnits.Metric,
    };

    private static WeatherForecast SampleForecast() => new(
        new WeatherCurrent(18, 17, 12, WeatherCondition.Clear, IsDay: true),
        [
            new WeatherForecastDay(new DateOnly(2026, 1, 2), WeatherCondition.PartlyCloudy, 19, 11),
            new WeatherForecastDay(new DateOnly(2026, 1, 3), WeatherCondition.Clear, 21, 12),
            new WeatherForecastDay(new DateOnly(2026, 1, 4), WeatherCondition.Rain, 16, 9),
        ]);

    /// <summary>Scripts one response (or throws) per call, counting invocations.</summary>
    private sealed class StubWeatherProvider : IWeatherProvider
    {
        public int CallCount { get; private set; }

        /// <summary>Called with the 1-based call number. Defaults to always succeeding.</summary>
        public Func<int, Task<WeatherForecast>> Handler { get; set; } = _ => Task.FromResult(SampleForecast());

        public Task<WeatherForecast> GetForecastAsync(WeatherSettings settings, CancellationToken cancellationToken)
        {
            CallCount++;
            return Handler(CallCount);
        }
    }
}
