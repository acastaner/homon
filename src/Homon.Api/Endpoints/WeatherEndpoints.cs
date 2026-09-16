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

    private static async Task<IResult> GetWeatherAsync(
        HomonDbContext database, WeatherCache cache, CancellationToken cancellationToken)
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

        if (!result.IsAvailable)
        {
            return TypedResults.Problem(
                title: "Weather unavailable",
                detail: "The forecast could not be refreshed and no recent answer is available.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var forecast = result.Forecast!;

        return TypedResults.Ok(new WeatherResponse(
            settings.Place,
            settings.Units,
            ToCurrentResponse(forecast.Current),
            forecast.Days.Select(ToForecastDayResponse).ToArray(),
            result.FetchedAt!.Value,
            result.Stale));
    }

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

    private static WeatherForecastDayResponse ToForecastDayResponse(WeatherForecastDay day) =>
        new(day.Date, day.Condition, day.High, day.Low);

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
    /// <param name="Forecast">Up to three upcoming days.</param>
    /// <param name="FetchedAt">When this forecast was fetched from the provider.</param>
    /// <param name="Stale">True when this forecast is older than the cache's fresh window because a refresh failed.</param>
    public sealed record WeatherResponse(
        string? Place,
        WeatherUnits Units,
        WeatherCurrentResponse Current,
        WeatherForecastDayResponse[] Forecast,
        DateTimeOffset FetchedAt,
        bool Stale);

    /// <param name="Temperature">Current temperature, in <see cref="WeatherResponse.Units"/>.</param>
    /// <param name="ApparentTemperature">"Feels like" temperature, in <see cref="WeatherResponse.Units"/>.</param>
    /// <param name="WindSpeed">Current wind speed, in <see cref="WeatherResponse.Units"/>.</param>
    /// <param name="Condition">The simplified condition word.</param>
    /// <param name="IsDay">Whether it is currently daytime at the location.</param>
    public sealed record WeatherCurrentResponse(
        double Temperature, double ApparentTemperature, double WindSpeed, WeatherCondition Condition, bool IsDay);

    /// <param name="Date">The forecast day's calendar date, in the location's own timezone.</param>
    /// <param name="Condition">The simplified condition word.</param>
    /// <param name="High">The day's forecast high, in <see cref="WeatherResponse.Units"/>.</param>
    /// <param name="Low">The day's forecast low, in <see cref="WeatherResponse.Units"/>.</param>
    public sealed record WeatherForecastDayResponse(DateOnly Date, WeatherCondition Condition, double High, double Low);
}
