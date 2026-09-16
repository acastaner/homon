using Homon.Domain.Weather;
using Microsoft.Extensions.Logging;

namespace Homon.Infrastructure.Weather;

/// <summary>
/// Holds the last forecast fetched for whatever <see cref="WeatherSettings"/> is current,
/// with single-flight refresh and a stale-while-error window. A bespoke wrapper rather than
/// raw <c>IMemoryCache</c>: neither the single-flight semaphore nor the stale-while-error
/// fallback comes free from that, so the extra abstraction would buy nothing.
/// </summary>
/// <remarks>
/// One process-wide singleton — fine for Homon's one-<c>api</c>-replica deployment
/// (docs/ARCHITECTURE.md §3.6). A future multi-instance deployment would need a distributed
/// cache; not before then.
/// </remarks>
public sealed partial class WeatherCache : IDisposable
{
    /// <summary>
    /// Inside this window since the last successful fetch, nothing calls the provider.
    /// Matches Open-Meteo's own update cadence.
    /// </summary>
    public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Past <see cref="FreshFor"/>, a failed refresh still serves the last snapshot — marked
    /// <c>Stale</c> — as long as it is younger than this. Older than this, or with no
    /// snapshot at all, the cache reports unavailable.
    /// </summary>
    public static readonly TimeSpan StaleTolerance = TimeSpan.FromHours(6);

    private readonly IWeatherProvider _provider;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WeatherCache> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    /// <summary>
    /// Bumped by <see cref="Invalidate"/>, outside the lock, so a refresh already in flight
    /// under the settings <see cref="Invalidate"/> is discarding can detect that its result is
    /// no longer current and must not overwrite the just-cleared snapshot.
    /// </summary>
    private int _generation;

    private volatile WeatherSnapshot? _snapshot;

    public WeatherCache(IWeatherProvider provider, TimeProvider timeProvider, ILogger<WeatherCache> logger)
    {
        _provider = provider;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<WeatherCacheResult> GetAsync(WeatherSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (TryServeFresh(out var fresh))
        {
            return fresh;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A concurrent caller may have already refreshed the snapshot while this one
            // waited for the lock — re-check before calling the provider ourselves, so
            // concurrent callers past FreshFor share exactly one provider call.
            if (TryServeFresh(out fresh))
            {
                return fresh;
            }

            var priorSnapshot = _snapshot;
            var generationAtStart = _generation;

            WeatherForecast? fetched = null;
            DateTimeOffset fetchedAt;

            try
            {
                fetched = await _provider.GetForecastAsync(settings, cancellationToken).ConfigureAwait(false);
                fetchedAt = _timeProvider.GetUtcNow();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                fetchedAt = _timeProvider.GetUtcNow();
                LogFetchFailed(_logger, exception);
            }

            if (fetched is not null)
            {
                // Invalidate() ran while this fetch was in flight, for settings that are no
                // longer current — discard the result rather than caching it, but still hand
                // it back to this caller, since it is the true answer for the settings this
                // call was given.
                if (generationAtStart == _generation)
                {
                    _snapshot = new WeatherSnapshot(fetched, fetchedAt);
                }

                return new WeatherCacheResult(fetched, fetchedAt, Stale: false, IsAvailable: true);
            }

            if (priorSnapshot is not null
                && _timeProvider.GetUtcNow() - priorSnapshot.FetchedAt < StaleTolerance)
            {
                return new WeatherCacheResult(priorSnapshot.Forecast, priorSnapshot.FetchedAt, Stale: true, IsAvailable: true);
            }

            return new WeatherCacheResult(null, null, Stale: false, IsAvailable: false);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Clears the snapshot and bumps the generation counter, so a refresh already in flight
    /// for the settings just replaced cannot land its answer afterwards. Called synchronously
    /// from the settings endpoint handler — never awaited against a fetch in progress — so
    /// this runs outside <see cref="_refreshLock"/> by design; see the class remarks.
    /// </summary>
    public void Invalidate()
    {
        Interlocked.Increment(ref _generation);
        _snapshot = null;
    }

    public void Dispose() => _refreshLock.Dispose();

    private bool TryServeFresh(out WeatherCacheResult result)
    {
        var snapshot = _snapshot;

        if (snapshot is not null && _timeProvider.GetUtcNow() - snapshot.FetchedAt < FreshFor)
        {
            result = new WeatherCacheResult(snapshot.Forecast, snapshot.FetchedAt, Stale: false, IsAvailable: true);
            return true;
        }

        result = default!;
        return false;
    }

    [LoggerMessage(EventId = 2200, Level = LogLevel.Warning, Message = "Weather provider fetch failed.")]
    private static partial void LogFetchFailed(ILogger logger, Exception exception);

    private sealed record WeatherSnapshot(WeatherForecast Forecast, DateTimeOffset FetchedAt);
}

/// <param name="Forecast">The forecast, or null when <paramref name="IsAvailable"/> is false.</param>
/// <param name="FetchedAt">When <paramref name="Forecast"/> was fetched, or null when unavailable.</param>
/// <param name="Stale">True when <paramref name="Forecast"/> is being served past <see cref="WeatherCache.FreshFor"/> because a refresh failed.</param>
/// <param name="IsAvailable">False when nothing survives <see cref="WeatherCache.StaleTolerance"/>.</param>
public sealed record WeatherCacheResult(WeatherForecast? Forecast, DateTimeOffset? FetchedAt, bool Stale, bool IsAvailable);
