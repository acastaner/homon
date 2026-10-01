using System.Globalization;
using Homon.Api.Authentication;
using Homon.Domain.Weather;
using Homon.Infrastructure.Persistence;
using Homon.Infrastructure.Weather;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Homon.Api.Endpoints;

/// <summary>
/// The household's one weather location, under <c>/weather</c> — <c>GET</c> is the cached
/// reader-facing forecast; <c>/settings</c> is the administrator's CRUD for the location
/// itself. See <c>Homon.Domain/Weather/README.md</c>.
/// </summary>
/// <remarks>
/// <c>GET ""</c> carries <see cref="HomonPolicies.Reader"/>, the same policy every dashboard
/// read uses. Every <c>/settings</c> route is <see cref="HomonPolicies.Administrator"/>,
/// session only — pasting coordinates is an admin-page action, never a script's.
/// </remarks>
internal static class WeatherEndpoints
{
    internal static RouteGroupBuilder MapWeatherEndpoints(this RouteGroupBuilder parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        var group = parent.MapGroup("/weather");

        group.MapGet("", GetWeatherAsync)
            .RequireAuthorization(HomonPolicies.Reader)
            .WithName("GetWeather")
            .WithSummary("The cached forecast for the household's location, or 204 when unconfigured.");

        group.MapGet("/settings", GetWeatherSettingsAsync)
            .RequireAuthorization(HomonPolicies.Administrator)
            .WithName("GetWeatherSettings")
            .WithSummary("The household's weather location, or 204 when unset.");

        group.MapPut("/settings", SaveWeatherSettingsAsync)
            .RequireAuthorization(HomonPolicies.Administrator)
            .WithName("SaveWeatherSettings")
            .WithSummary("Sets the household's weather location, creating or replacing the singleton row.");

        group.MapDelete("/settings", DeleteWeatherSettingsAsync)
            .RequireAuthorization(HomonPolicies.Administrator)
            .WithName("DeleteWeatherSettings")
            .WithSummary("Removes the household's weather location. Idempotent.");

        return group;
    }

    /// <summary>How many hourly rows cross the wire — the weather page's table ceiling.</summary>
    private const int WireHours = 24;

    /// <summary>
    /// How many hourly rows the advisory evaluator reads. Wider than <see cref="WireHours"/>
    /// on purpose: a gale tomorrow afternoon is worth a banner today even though the table
    /// does not reach that far.
    /// </summary>
    private const int AdvisoryHours = 48;

    private static async Task<IResult> GetWeatherAsync(
        HomonDbContext database,
        WeatherCache cache,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var settings = await database.WeatherSettings.FindAsync(
            [WeatherSettings.SingletonId], cancellationToken);

        if (settings is null)
        {
            // Mirrors GetSession's "anonymity is a fact" posture — an unset location is not
            // an error, and lib/weather.ts reuses lib/session.ts's ?? null idiom for it.
            return TypedResults.NoContent();
        }

        var result = await cache.GetAsync(settings, cancellationToken);

        if (!result.IsAvailable || result.Forecast!.Days.Count == 0)
        {
            // A forecast with no days is as useless as no forecast: the page summarises today
            // and the widget shows its extremes, so indexing Days[0] would be the alternative.
            return TypedResults.Problem(
                title: "Weather unavailable",
                detail: "The forecast could not be refreshed and no recent answer is available.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var forecast = result.Forecast!;
        var window = HourlyWindow(forecast, timeProvider, AdvisoryHours);

        // The days the window actually covers — the evaluator must not warn about Saturday
        // from a Thursday window, or the banner would outlive the reason to care.
        var windowDates = window.Select(hour => hour.Date).ToHashSet();
        var warnings = WeatherWarningEvaluator.Evaluate(
            forecast.Days.Where(day => windowDates.Contains(day.Date)).ToArray(), window, settings.Units);

        return TypedResults.Ok(new WeatherResponse(
            settings.Place,
            settings.Units,
            ToCurrentResponse(forecast.Current),
            ToDayResponse(forecast.Days[0]),
            forecast.Days.Skip(1).Select(ToDayResponse).ToArray(),
            window.Take(WireHours).Select(ToHourResponse).ToArray(),
            warnings.Select(ToWarningResponse).ToArray(),
            result.FetchedAt!.Value,
            result.Stale));
    }

    /// <summary>
    /// The next <paramref name="count"/> hourly rows at the location, from the hour it is there
    /// now.
    /// </summary>
    /// <remarks>
    /// Computed per request rather than per fetch. <c>WeatherCache.FreshFor</c> is 15 minutes,
    /// so a window chosen when the snapshot was built would open on an hour already past for
    /// most of a snapshot's life. See plan 020, D6.
    /// </remarks>
    private static WeatherHour[] HourlyWindow(
        WeatherForecast forecast, TimeProvider timeProvider, int count)
    {
        var localNow = timeProvider.GetUtcNow().UtcDateTime.AddSeconds(forecast.UtcOffsetSeconds);
        var fromHour = new DateTime(
            localNow.Year, localNow.Month, localNow.Day, localNow.Hour, 0, 0, DateTimeKind.Unspecified);

        return [.. forecast.Hours.Where(hour => HourStamp(hour) >= fromHour).Take(count)];
    }

    private static DateTime HourStamp(WeatherHour hour) =>
        hour.Date.ToDateTime(TimeOnly.ParseExact(hour.Time, "HH:mm", CultureInfo.InvariantCulture));

    private static async Task<IResult> GetWeatherSettingsAsync(
        HomonDbContext database, CancellationToken cancellationToken)
    {
        var settings = await database.WeatherSettings.FindAsync(
            [WeatherSettings.SingletonId], cancellationToken);

        return settings is null
            ? TypedResults.NoContent()
            : TypedResults.Ok(ToSettingsResponse(settings));
    }

    private static async Task<IResult> SaveWeatherSettingsAsync(
        WeatherSettingsRequest request,
        HomonDbContext database,
        WeatherCache cache,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var validationError = Validate(request, out var latitude, out var longitude, out var place, out var units);
        if (validationError is not null)
        {
            return TypedResults.ValidationProblem(validationError);
        }

        var settings = await database.WeatherSettings.FindAsync(
            [WeatherSettings.SingletonId], cancellationToken);

        var now = timeProvider.GetUtcNow();

        if (settings is null)
        {
            settings = new WeatherSettings
            {
                Id = WeatherSettings.SingletonId,
                CreatedAt = now,
            };
            database.WeatherSettings.Add(settings);
        }

        settings.Latitude = latitude;
        settings.Longitude = longitude;
        settings.Place = place;
        settings.Units = units;
        settings.UpdatedAt = now;

        await database.SaveChangesAsync(cancellationToken);

        // A changed location must never keep serving the old one's forecast, even inside
        // WeatherCache.FreshFor.
        cache.Invalidate();

        return TypedResults.Ok(ToSettingsResponse(settings));
    }

    private static async Task<IResult> DeleteWeatherSettingsAsync(
        HttpContext httpContext, HomonDbContext database, WeatherCache cache, CancellationToken cancellationToken)
    {
        // No bound body, so nothing else demands the application/json content type that
        // closes the CSRF guard — model AuthenticationEndpoints.SignOutAsync's check verbatim.
        if (httpContext.Request.ContentType?.StartsWith(
                "application/json", StringComparison.OrdinalIgnoreCase) is not true)
        {
            return TypedResults.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        var settings = await database.WeatherSettings.FindAsync(
            [WeatherSettings.SingletonId], cancellationToken);

        if (settings is not null)
        {
            database.WeatherSettings.Remove(settings);
            await database.SaveChangesAsync(cancellationToken);
        }

        // Idempotent either way — a delete with nothing to delete still clears the cache, in
        // case a prior write raced this one.
        cache.Invalidate();

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Validates a settings request. Returns null when valid; otherwise a field-keyed error
    /// dictionary ready for <c>TypedResults.ValidationProblem</c>.
    /// </summary>
    private static Dictionary<string, string[]>? Validate(
        WeatherSettingsRequest request,
        out double latitude,
        out double longitude,
        out string? place,
        out WeatherUnits units)
    {
        latitude = request.Latitude ?? 0;
        longitude = request.Longitude ?? 0;
        place = NormalizePlace(request.Place);
        units = default;

        var errors = new Dictionary<string, string[]>();

        if (request.Latitude is not { } lat || lat is < -90 or > 90)
        {
            errors["latitude"] = ["Latitude is required and must be between -90 and 90."];
        }

        if (request.Longitude is not { } lon || lon is < -180 or > 180)
        {
            errors["longitude"] = ["Longitude is required and must be between -180 and 180."];
        }

        if (place is { Length: > WeatherSettings.PlaceMaxLength })
        {
            errors["place"] = [$"Place is at most {WeatherSettings.PlaceMaxLength} characters."];
        }

        if (!Enum.TryParse(request.Units, ignoreCase: true, out WeatherUnits parsedUnits))
        {
            errors["units"] = ["Units is required and must be \"Metric\" or \"Imperial\"."];
        }
        else
        {
            units = parsedUnits;
        }

        return errors.Count == 0 ? null : errors;
    }

    /// <summary>Trims and turns an empty place into null, so the SPA has one falsy case to check.</summary>
    private static string? NormalizePlace(string? place)
    {
        var trimmed = place?.Trim();
        return trimmed is { Length: > 0 } ? trimmed : null;
    }

    private static WeatherSettingsResponse ToSettingsResponse(WeatherSettings settings) =>
        new(settings.Latitude, settings.Longitude, settings.Place, settings.Units);

    private static WeatherCurrentResponse ToCurrentResponse(WeatherCurrent current) =>
        new(current.Temperature, current.ApparentTemperature, current.WindSpeed, current.Condition, current.IsDay);

    private static WeatherDayResponse ToDayResponse(WeatherDay day) =>
        new(
            day.Date, day.Condition, day.High, day.Low, day.PrecipitationSum, day.SnowfallSum,
            day.WindSpeedMax, day.WindGustsMax, day.Sunrise, day.Sunset);

    private static WeatherHourResponse ToHourResponse(WeatherHour hour) =>
        new(
            hour.Date, hour.Time, hour.Condition, hour.Temperature, hour.ApparentTemperature,
            hour.WindSpeed, hour.WindGusts, hour.PrecipitationProbability);

    private static WeatherWarningResponse ToWarningResponse(WeatherWarning warning) =>
        new(warning.Kind, warning.Severity, warning.Value, warning.Date, warning.FromTime, warning.ToTime);

    /// <param name="Latitude">Required, -90 to 90.</param>
    /// <param name="Longitude">Required, -180 to 180.</param>
    /// <param name="Place">Optional label shown beside "Weather" on the dashboard, at most <see cref="WeatherSettings.PlaceMaxLength"/> characters.</param>
    /// <param name="Units">Required; <c>"Metric"</c> or <c>"Imperial"</c>, case-insensitive.</param>
    internal sealed record WeatherSettingsRequest(double? Latitude, double? Longitude, string? Place, string? Units);

    /// <param name="Latitude">The household's latitude.</param>
    /// <param name="Longitude">The household's longitude.</param>
    /// <param name="Place">Optional label shown beside "Weather" on the dashboard.</param>
    /// <param name="Units">The unit system the forecast is reported in.</param>
    public sealed record WeatherSettingsResponse(double Latitude, double Longitude, string? Place, WeatherUnits Units);

    /// <param name="Place">Optional label shown beside "Weather" on the dashboard.</param>
    /// <param name="Units">The unit system every value below is reported in.</param>
    /// <param name="Current">Conditions right now.</param>
    /// <param name="Today">Today, including its high and low — which <paramref name="Current"/> cannot give.</param>
    /// <param name="Forecast">The next seven days, starting tomorrow. The dashboard widget shows the first three.</param>
    /// <param name="Hourly">Up to 24 hourly rows, from the current hour at the location.</param>
    /// <param name="Warnings">
    /// Up to three advisories for the next 48 hours, severest first. <b>Derived by Homon</b>
    /// from thresholds applied to the forecast — Open-Meteo publishes no warnings endpoint, so
    /// this is never a relayed official advisory. Empty when nothing trips.
    /// </param>
    /// <param name="FetchedAt">When this forecast was fetched from the provider.</param>
    /// <param name="Stale">True when this forecast is older than the cache's fresh window because a refresh failed.</param>
    public sealed record WeatherResponse(
        string? Place,
        WeatherUnits Units,
        WeatherCurrentResponse Current,
        WeatherDayResponse Today,
        WeatherDayResponse[] Forecast,
        WeatherHourResponse[] Hourly,
        WeatherWarningResponse[] Warnings,
        DateTimeOffset FetchedAt,
        bool Stale);

    /// <param name="Temperature">Current temperature, in <see cref="WeatherResponse.Units"/>.</param>
    /// <param name="ApparentTemperature">"Feels like" temperature, in <see cref="WeatherResponse.Units"/>.</param>
    /// <param name="WindSpeed">Current wind speed, in <see cref="WeatherResponse.Units"/>.</param>
    /// <param name="Condition">The simplified condition word.</param>
    /// <param name="IsDay">Whether it is currently daytime at the location.</param>
    public sealed record WeatherCurrentResponse(
        double Temperature, double ApparentTemperature, double WindSpeed, WeatherCondition Condition, bool IsDay);

    /// <param name="Date">The day's calendar date, in the location's own timezone.</param>
    /// <param name="Condition">The simplified condition word.</param>
    /// <param name="High">The day's high, in <see cref="WeatherResponse.Units"/>.</param>
    /// <param name="Low">The day's low, in <see cref="WeatherResponse.Units"/>.</param>
    /// <param name="PrecipitationSum">Total precipitation — millimetres under metric, inches under imperial — or null when unreported.</param>
    /// <param name="SnowfallSum">Total snowfall — <b>centimetres</b> under metric, inches under imperial — or null when unreported.</param>
    /// <param name="WindSpeedMax">The day's peak mean wind speed, or null when unreported.</param>
    /// <param name="WindGustsMax">The day's peak gust, or null when unreported.</param>
    /// <param name="Sunrise">Sunrise on the location's own clock, <c>"HH:mm"</c>, or null where the sun does not rise.</param>
    /// <param name="Sunset">Sunset, same shape and caveat as <paramref name="Sunrise"/>.</param>
    public sealed record WeatherDayResponse(
        DateOnly Date,
        WeatherCondition Condition,
        double High,
        double Low,
        double? PrecipitationSum,
        double? SnowfallSum,
        double? WindSpeedMax,
        double? WindGustsMax,
        string? Sunrise,
        string? Sunset);

    /// <param name="Date">The hour's calendar date, in the location's own timezone.</param>
    /// <param name="Time">
    /// The hour on the location's own clock, <c>"HH:mm"</c>. A string, not a timestamp: the
    /// provider already answered in the location's zone, and re-parsing it in the browser
    /// would re-interpret it in the reader's.
    /// </param>
    /// <param name="Condition">The simplified condition word.</param>
    /// <param name="Temperature">Temperature at the hour, in <see cref="WeatherResponse.Units"/>.</param>
    /// <param name="ApparentTemperature">"Feels like" temperature, in <see cref="WeatherResponse.Units"/>.</param>
    /// <param name="WindSpeed">Mean wind speed, in <see cref="WeatherResponse.Units"/>.</param>
    /// <param name="WindGusts">Peak gust, in <see cref="WeatherResponse.Units"/>, or null when unreported.</param>
    /// <param name="PrecipitationProbability">Chance of precipitation, 0–100, or null when unreported.</param>
    public sealed record WeatherHourResponse(
        DateOnly Date,
        string Time,
        WeatherCondition Condition,
        double Temperature,
        double ApparentTemperature,
        double WindSpeed,
        double? WindGusts,
        int? PrecipitationProbability);

    /// <param name="Kind">What the advisory is about.</param>
    /// <param name="Severity"><c>"caution"</c> or <c>"severe"</c>.</param>
    /// <param name="Value">The figure that tripped the threshold, in <see cref="WeatherResponse.Units"/>, or null for a kind with no figure.</param>
    /// <param name="Date">The day it applies to, in the location's own timezone.</param>
    /// <param name="FromTime">Start on the location's clock, <c>"HH:mm"</c>, or null for a whole-day advisory.</param>
    /// <param name="ToTime">End, same shape and null meaning as <paramref name="FromTime"/>.</param>
    public sealed record WeatherWarningResponse(
        WeatherWarningKind Kind,
        WeatherWarningSeverity Severity,
        double? Value,
        DateOnly Date,
        string? FromTime,
        string? ToTime);
}
