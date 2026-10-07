using System.Net.Mail;
using Homon.Api.Authentication;
using Homon.Domain.Alerts;
using Homon.Infrastructure.Persistence;
using Homon.Infrastructure.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Homon.Api.Endpoints;

/// <summary>
/// Alert email set-up and history, under <c>/alerts</c>: the settings singleton, the outbox's
/// recent rows, and the test send. See <c>Homon.Domain/Alerts/README.md</c> and
/// docs/ARCHITECTURE.md §3.31.
/// </summary>
/// <remarks>
/// Every route is <see cref="HomonPolicies.Administrator"/>, session only — the Resend key is an
/// admin-page entry, never a script's. The key is write-only on the wire (plan 003 / §3.17): a
/// response carries <c>hasApiKey</c> and nothing that could reveal it.
/// </remarks>
internal static class AlertEndpoints
{
    /// <summary>How many outbox rows <c>GET /alerts/deliveries</c> returns.</summary>
    private const int DeliveryListSize = 20;

    private const int ApiKeyMaxLength = 200;

    private const int FromAddressMaxLength = 254;

    private const int FromNameMaxLength = 100;

    internal static RouteGroupBuilder MapAlertEndpoints(this RouteGroupBuilder parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        var group = parent.MapGroup("/alerts");

        group.MapGet("/settings", GetSettingsAsync)
            .RequireAuthorization(HomonPolicies.Administrator)
            .WithName("GetAlertSettings")
            .WithSummary("The alert email settings. Always 200: a fresh database reports the defaults. Never carries the Resend key.");

        group.MapPut("/settings", SaveSettingsAsync)
            .RequireAuthorization(HomonPolicies.Administrator)
            .WithName("SaveAlertSettings")
            .WithSummary("Replaces the alert email settings. The Resend key is write-only: null keeps it, an empty string clears it.");

        group.MapGet("/deliveries", GetDeliveriesAsync)
            .RequireAuthorization(HomonPolicies.Administrator)
            .WithName("ListAlertDeliveries")
            .WithSummary("The 20 newest alert emails, newest first, with their delivery state.");

        group.MapPost("/test", SendTestAsync)
            .RequireAuthorization(HomonPolicies.Administrator)
            .WithName("SendTestAlert")
            .WithSummary("Queues a test email. 202 with the queued row; the dispatcher sends it within seconds.");

        return group;
    }

    private static async Task<IResult> GetSettingsAsync(
        HomonDbContext database, CancellationToken cancellationToken)
    {
        var settings = await database.AlertSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == AlertSettings.SingletonId, cancellationToken);

        return TypedResults.Ok(ToSettingsResponse(settings ?? new AlertSettings()));
    }

    private static async Task<IResult> SaveSettingsAsync(
        AlertSettingsRequest request,
        HomonDbContext database,
        ISecretProtector secretProtector,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var settings = await database.AlertSettings.FindAsync(
            [AlertSettings.SingletonId], cancellationToken);

        var isEnabled = request.IsEnabled;
        var apiKey = request.ApiKey?.Trim();
        var fromAddress = request.FromAddress?.Trim() ?? string.Empty;
        var fromName = request.FromName?.Trim() ?? string.Empty;
        var recipients = (request.Recipients ?? []).Select(r => r?.Trim() ?? string.Empty).ToList();

        var errors = Validate(
            isEnabled, apiKey, fromAddress, fromName, recipients, hasStoredKey: settings?.ProtectedApiKey is not null);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var now = timeProvider.GetUtcNow();

        if (settings is null)
        {
            settings = new AlertSettings { Id = AlertSettings.SingletonId, CreatedAt = now };
            database.AlertSettings.Add(settings);
        }

        settings.IsEnabled = isEnabled;
        settings.ProtectedApiKey = ResolveProtectedSecret(secretProtector, apiKey, settings.ProtectedApiKey);
        settings.FromAddress = fromAddress;
        settings.FromName = fromName;
        settings.Recipients = recipients;
        settings.UpdatedAt = now;

        await database.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(ToSettingsResponse(settings));
    }

    private static async Task<IResult> GetDeliveriesAsync(
        HomonDbContext database, CancellationToken cancellationToken)
    {
        var rows = await database.AlertNotifications
            .AsNoTracking()
            .OrderByDescending(n => n.OccurredAt)
            .ThenByDescending(n => n.Id)
            .Take(DeliveryListSize)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(rows.Select(ToDeliveryResponse).ToArray());
    }

    private static async Task<IResult> SendTestAsync(
        HttpContext httpContext,
        HomonDbContext database,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        // No bound body, so nothing else demands the application/json content type that
        // closes the CSRF guard — same check as WeatherEndpoints.DeleteWeatherSettingsAsync.
        if (httpContext.Request.ContentType?.StartsWith(
                "application/json", StringComparison.OrdinalIgnoreCase) is not true)
        {
            return TypedResults.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        var settings = await database.AlertSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == AlertSettings.SingletonId, cancellationToken);

        if (settings?.IsReady != true)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Alerts are not set up",
                detail: "Save a Resend API key, a From address and at least one recipient first.");
        }

        // A double click queues one mail, not two.
        var pending = await database.AlertNotifications
            .AsNoTracking()
            .Where(n => n.Kind == AlertKind.Test && n.State == AlertDeliveryState.Pending)
            .OrderByDescending(n => n.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (pending is not null)
        {
            return TypedResults.Accepted("/api/v1/alerts/deliveries", ToDeliveryResponse(pending));
        }

        var now = timeProvider.GetUtcNow();
        var row = new AlertNotification
        {
            Kind = AlertKind.Test,
            ProbeName = "Test alert",
            OccurredAt = now,
            NextAttemptAt = now,
            State = AlertDeliveryState.Pending,
        };
        database.AlertNotifications.Add(row);
        await database.SaveChangesAsync(cancellationToken);

        return TypedResults.Accepted("/api/v1/alerts/deliveries", ToDeliveryResponse(row));
    }

    /// <summary>
    /// Validates the (already trimmed) request. Returns a camelCase field-keyed error
    /// dictionary, empty when valid.
    /// </summary>
    private static Dictionary<string, string[]> Validate(
        bool isEnabled,
        string? apiKey,
        string fromAddress,
        string fromName,
        List<string> recipients,
        bool hasStoredKey)
    {
        var errors = new Dictionary<string, string[]>();

        if (apiKey is not null && (apiKey.Length > ApiKeyMaxLength || apiKey.Any(char.IsWhiteSpace)))
        {
            errors["apiKey"] = [$"The API key is at most {ApiKeyMaxLength} characters, with no spaces."];
        }

        if (fromAddress.Length > FromAddressMaxLength)
        {
            errors["fromAddress"] = [$"The From address is at most {FromAddressMaxLength} characters."];
        }
        else if (fromAddress.Length == 0 ? isEnabled : !IsPlainAddress(fromAddress))
        {
            errors["fromAddress"] = ["The From address must be a plain email address, such as alerts@example.com."];
        }

        if (fromName.Length == 0 || fromName.Length > FromNameMaxLength
            || fromName.IndexOfAny(['\r', '\n', '<', '>', '"']) >= 0)
        {
            errors["fromName"] = [$"The From name is required, at most {FromNameMaxLength} characters, and cannot contain line breaks, < > or quotes."];
        }

        if (recipients.Count > AlertSettings.MaxRecipients)
        {
            errors["recipients"] = [$"At most {AlertSettings.MaxRecipients} recipients."];
        }
        else if (recipients.Any(r => !IsPlainAddress(r)))
        {
            errors["recipients"] = ["Every recipient must be a plain email address."];
        }
        else if (recipients.Select(r => r.ToUpperInvariant()).Distinct().Count() != recipients.Count)
        {
            errors["recipients"] = ["Each recipient can be listed once."];
        }

        if (isEnabled)
        {
            // The key that will be stored after this save: a new non-empty one, or the stored
            // one when the request leaves it alone (null).
            var effectiveKey = apiKey is null ? hasStoredKey : apiKey.Length > 0;
            if (!effectiveKey || fromAddress.Length == 0 || recipients.Count == 0)
            {
                errors["isEnabled"] =
                    ["Add a Resend API key, a From address and at least one recipient before switching alerts on."];
            }
        }

        return errors;
    }

    /// <summary>
    /// True for a bare address. Parsing alone would accept <c>"Name &lt;a@b.c&gt;"</c>, which
    /// would let a display name ride into the From header, so the parsed address must also equal
    /// the input.
    /// </summary>
    private static bool IsPlainAddress(string value) =>
        value.Length is > 0 and <= FromAddressMaxLength
        && MailAddress.TryCreate(value, out var parsed)
        && string.Equals(parsed.Address, value, StringComparison.Ordinal);

    /// <summary>
    /// The write-only secret rule (plan 003's Decision 2): null keeps the stored value, an empty
    /// string clears it, non-empty is protected and replaces it.
    /// </summary>
    private static string? ResolveProtectedSecret(ISecretProtector secretProtector, string? secret, string? existingProtectedSecret)
    {
        if (secret is null)
        {
            return existingProtectedSecret;
        }

        return secret.Length == 0 ? null : secretProtector.Protect(secret);
    }

    private static AlertSettingsResponse ToSettingsResponse(AlertSettings settings) =>
        new(
            settings.IsEnabled,
            settings.ProtectedApiKey is not null,
            settings.FromAddress,
            settings.FromName,
            settings.Recipients,
            settings.UpdatedAt == default ? null : settings.UpdatedAt);

    private static AlertDeliveryResponse ToDeliveryResponse(AlertNotification row) =>
        new(
            row.Id, row.Kind, row.ProbeId, row.ProbeName, row.OccurredAt, row.DownSince,
            row.State, row.Attempts, row.SentAt, row.LastError);

    /// <param name="IsEnabled">Whether down and up events are mailed. A test email is sent either way.</param>
    /// <param name="ApiKey">
    /// The Resend API key. <b>Write-only</b>: <c>null</c> (or absent) keeps the stored key, an
    /// empty string removes it, anything else replaces it. At most 200 characters, no spaces.
    /// </param>
    /// <param name="FromAddress">A plain address on a domain verified in Resend. May be empty only while alerts are off.</param>
    /// <param name="FromName">Display name, required, at most 100 characters, no line breaks, angle brackets or quotes.</param>
    /// <param name="Recipients">Up to 10 distinct plain addresses.</param>
    internal sealed record AlertSettingsRequest(
        bool IsEnabled, string? ApiKey, string FromAddress, string FromName, IReadOnlyList<string>? Recipients);

    /// <param name="IsEnabled">Whether down and up events are mailed.</param>
    /// <param name="HasApiKey">Whether a Resend key is stored. The key itself is never returned.</param>
    /// <param name="FromAddress">The sender address.</param>
    /// <param name="FromName">The sender's display name.</param>
    /// <param name="Recipients">Who is mailed.</param>
    /// <param name="UpdatedAt">When the settings were last saved; null when they never were.</param>
    public sealed record AlertSettingsResponse(
        bool IsEnabled,
        bool HasApiKey,
        string FromAddress,
        string FromName,
        IReadOnlyList<string> Recipients,
        DateTimeOffset? UpdatedAt);

    /// <param name="Id">The outbox row id.</param>
    /// <param name="Kind"><c>"down"</c>, <c>"up"</c> or <c>"test"</c>.</param>
    /// <param name="ProbeId">The probe concerned; null for a test or once the probe is deleted.</param>
    /// <param name="ProbeName">The probe's name at the event.</param>
    /// <param name="OccurredAt">When the event happened.</param>
    /// <param name="DownSince">When the outage began; for an up event this gives the downtime.</param>
    /// <param name="State"><c>"pending"</c>, <c>"sent"</c>, <c>"failed"</c> or <c>"skipped"</c>.</param>
    /// <param name="Attempts">Failed send attempts so far.</param>
    /// <param name="SentAt">When it was sent; null otherwise.</param>
    /// <param name="LastError">Why the last attempt failed, or why it was skipped. Never a secret.</param>
    public sealed record AlertDeliveryResponse(
        long Id,
        AlertKind Kind,
        Guid? ProbeId,
        string ProbeName,
        DateTimeOffset OccurredAt,
        DateTimeOffset? DownSince,
        AlertDeliveryState State,
        int Attempts,
        DateTimeOffset? SentAt,
        string? LastError);
}
