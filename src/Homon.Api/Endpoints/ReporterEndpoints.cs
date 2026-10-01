using Homon.Api.Authentication;
using Homon.Domain.Auth;
using Homon.Domain.Messaging;
using Homon.Domain.Monitoring;
using Homon.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Homon.Api.Endpoints;

/// <summary>
/// Administration of the message gateway's reporters: registering one (which mints its paired
/// key), editing the administrator's own label and the body-visibility switch, rotating the key,
/// deleting a reporter, and reading its message history.
/// </summary>
/// <remarks>
/// Every route here is <see cref="HomonPolicies.Administrator"/> — session only, per
/// §3.3's "a key never administers". That includes the reads: a reporter's history carries whole
/// command outputs, and the history route is the only place in the application that returns one
/// (plan 021, Decision 11).
/// </remarks>
internal static class ReporterEndpoints
{
    private const int DefaultMessageLimit = 50;
    private const int MaxMessageLimit = 200;

    internal static RouteGroupBuilder MapReporterEndpoints(this RouteGroupBuilder parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        var group = parent.MapGroup("/reporters").RequireAuthorization(HomonPolicies.Administrator);

        group.MapGet("", GetReportersAsync)
            .WithName("GetReporters")
            .WithSummary("Lists every reporter with its key and its latest report.");

        group.MapPost("", CreateReporterAsync)
            .WithName("CreateReporter")
            .WithSummary("Registers a reporter and mints the one key paired with it.");

        group.MapPut("/{id:guid}", UpdateReporterAsync)
            .WithName("UpdateReporter")
            .WithSummary("Renames a reporter or changes who may read its message bodies.");

        group.MapPost("/{id:guid}/key", ReplaceReporterKeyAsync)
            .WithName("ReplaceReporterKey")
            .WithSummary("Mints a replacement key for a reporter and revokes the old one.");

        group.MapDelete("/{id:guid}", DeleteReporterAsync)
            .WithName("DeleteReporter")
            .WithSummary("Deletes a reporter, its messages and its key.");

        group.MapGet("/{id:guid}/messages", GetReporterMessagesAsync)
            .WithName("GetReporterMessages")
            .WithSummary("Lists a reporter's reports, newest first, with their bodies.");

        return group;
    }

    private static async Task<Ok<ReporterResponse[]>> GetReportersAsync(
        HomonDbContext database, CancellationToken cancellationToken)
    {
        var reporters = await database.Reporters
            .AsNoTracking()
            .Join(database.ApiKeys, r => r.ApiKeyId, k => k.Id, (r, k) => new { Reporter = r, Key = k })
            .OrderBy(x => x.Reporter.Name)
            .ThenBy(x => x.Reporter.Id)
            .ToListAsync(cancellationToken);

        var summaries = await LatestByReporterAsync(database, cancellationToken);

        var counts = await database.Messages
            .GroupBy(m => m.ReporterId)
            .Select(g => new { ReporterId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ReporterId, x => x.Count, cancellationToken);

        // One query for every watched identifier rather than one per reporter: the admin page
        // needs this to explain why a delete is refused before the administrator tries it.
        var watched = await database.Probes
            .Where(p => p.Kind == ProbeKind.Message)
            .Select(p => p.Host)
            .ToListAsync(cancellationToken);

        var watchedSet = watched.ToHashSet(StringComparer.Ordinal);

        return TypedResults.Ok(reporters
            .Select(x => ToResponse(
                x.Reporter,
                x.Key,
                summaries.GetValueOrDefault(x.Reporter.Id),
                counts.GetValueOrDefault(x.Reporter.Id),
                watchedSet.Contains(x.Reporter.Identifier)))
            .ToArray());
    }

    private static async Task<Results<Created<ReporterCreatedResponse>, ValidationProblem>> CreateReporterAsync(
        ReporterRequest request,
        HomonDbContext database,
        ApiKeyIssuer issuer,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var error = ValidateFields(request, out var name, out var description, out var visibility);
        if (error is not null)
        {
            return error;
        }

        // ReadWrite with no expiry, and no option to choose otherwise: ingestion refuses a Read
        // key, and a backup key that silently expired would be a silent monitoring failure — the
        // exact thing this module exists to prevent.
        var (key, presented) = await issuer.IssueAsync(name, ApiKeyScope.ReadWrite, expiresAt: null, cancellationToken);

        var reporter = new Reporter
        {
            Id = Guid.NewGuid(),
            Identifier = await NewIdentifierAsync(database, cancellationToken),
            Name = name,
            NormalizedName = Reporter.Normalize(name),
            Description = description,
            BodyVisibility = visibility,
            ApiKeyId = key.Id,
            CreatedAt = timeProvider.GetUtcNow(),
        };

        database.Reporters.Add(reporter);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            return DuplicateName();
        }

        return TypedResults.Created(
            $"/api/v1/reporters/{reporter.Id}",
            new ReporterCreatedResponse(
                ToResponse(reporter, key, latest: null, messageCount: 0, isWatched: false),
                presented));
    }

    private static async Task<Results<Ok<ReporterResponse>, ValidationProblem, NotFound>> UpdateReporterAsync(
        Guid id,
        ReporterRequest request,
        HomonDbContext database,
        CancellationToken cancellationToken)
    {
        var reporter = await database.Reporters.FindAsync([id], cancellationToken);

        if (reporter is null)
        {
            return TypedResults.NotFound();
        }

        var error = ValidateFields(request, out var name, out var description, out var visibility);
        if (error is not null)
        {
            return error;
        }

        // The identifier is deliberately not editable: a message probe stores it in Probe.Host,
        // so renaming it would orphan the probe watching this reporter (Decision 3).
        reporter.Name = name;
        reporter.NormalizedName = Reporter.Normalize(name);
        reporter.Description = description;
        reporter.BodyVisibility = visibility;

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            return DuplicateName();
        }

        var key = await database.ApiKeys.AsNoTracking().SingleAsync(k => k.Id == reporter.ApiKeyId, cancellationToken);
        var summaries = await LatestByReporterAsync(database, cancellationToken);
        var count = await database.Messages.CountAsync(m => m.ReporterId == reporter.Id, cancellationToken);
        var isWatched = await database.Probes
            .AnyAsync(p => p.Kind == ProbeKind.Message && p.Host == reporter.Identifier, cancellationToken);

        return TypedResults.Ok(ToResponse(reporter, key, summaries.GetValueOrDefault(reporter.Id), count, isWatched));
    }

    private static async Task<Results<Ok<ReporterKeyResponse>, NotFound>> ReplaceReporterKeyAsync(
        Guid id,
        HomonDbContext database,
        ApiKeyIssuer issuer,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var reporter = await database.Reporters.FindAsync([id], cancellationToken);

        if (reporter is null)
        {
            return TypedResults.NotFound();
        }

        var previousKeyId = reporter.ApiKeyId;
        var (key, presented) = await issuer.IssueAsync(reporter.Name, ApiKeyScope.ReadWrite, expiresAt: null, cancellationToken);

        reporter.ApiKeyId = key.Id;
        await database.SaveChangesAsync(cancellationToken);

        // Revoked rather than deleted, so the messages it filed stay attributable and the audit
        // trail ApiKey already promises survives the rotation.
        await database.ApiKeys
            .Where(k => k.Id == previousKeyId && k.RevokedAt == null)
            .ExecuteUpdateAsync(k => k.SetProperty(x => x.RevokedAt, timeProvider.GetUtcNow()), cancellationToken);

        return TypedResults.Ok(new ReporterKeyResponse(reporter.Id, key.TokenId, presented));
    }

    private static async Task<Results<NoContent, ProblemHttpResult, NotFound>> DeleteReporterAsync(
        Guid id,
        HomonDbContext database,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var reporter = await database.Reporters.FindAsync([id], cancellationToken);

        if (reporter is null)
        {
            return TypedResults.NotFound();
        }

        // A probe's reference to a reporter is its identifier in Probe.Host, not a foreign key, so
        // this check is what a Restrict would otherwise be: make the assumption loud rather than
        // leaving a probe pointing at nothing (Decision 3).
        var watcher = await database.Probes
            .FirstOrDefaultAsync(p => p.Kind == ProbeKind.Message && p.Host == reporter.Identifier, cancellationToken);

        if (watcher is not null)
        {
            return TypedResults.Problem(
                title: "Reporter is in use",
                detail: $"The probe \"{watcher.Name}\" watches this reporter. Delete that probe, or point it at another reporter, first.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var keyId = reporter.ApiKeyId;

        // Messages cascade with the reporter; the key is revoked instead, because a key that
        // authenticates but resolves to no reporter would otherwise 403 on every report forever.
        database.Reporters.Remove(reporter);
        await database.SaveChangesAsync(cancellationToken);

        await database.ApiKeys
            .Where(k => k.Id == keyId && k.RevokedAt == null)
            .ExecuteUpdateAsync(k => k.SetProperty(x => x.RevokedAt, timeProvider.GetUtcNow()), cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<MessageResponse[]>, NotFound>> GetReporterMessagesAsync(
        Guid id,
        int? limit,
        HomonDbContext database,
        CancellationToken cancellationToken)
    {
        if (!await database.Reporters.AnyAsync(r => r.Id == id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var take = Math.Clamp(limit ?? DefaultMessageLimit, 1, MaxMessageLimit);

        var messages = await database.Messages
            .AsNoTracking()
            .Where(m => m.ReporterId == id)
            .OrderByDescending(m => m.ReceivedAt)
            .ThenByDescending(m => m.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(messages
            .Select(m => new MessageResponse(
                m.Id,
                m.Name,
                m.Description,
                m.Body,
                MessageBody.WasTruncated(m.Body),
                m.Status,
                m.Category,
                m.ReceivedAt,
                m.NextExpectedAt,
                m.RecurrenceDeclaration))
            .ToArray());
    }

    /// <summary>
    /// The newest message for every reporter, in two queries rather than one per reporter. Keyed
    /// on the identity column, not the timestamp, because two reports can share a
    /// <c>ReceivedAt</c> and "newest" has to be a total order.
    /// </summary>
    private static async Task<Dictionary<Guid, MessageSummaryResponse>> LatestByReporterAsync(
        HomonDbContext database, CancellationToken cancellationToken)
    {
        var latestIds = await database.Messages
            .GroupBy(m => m.ReporterId)
            .Select(g => g.Max(m => m.Id))
            .ToListAsync(cancellationToken);

        var latest = await database.Messages
            .AsNoTracking()
            .Where(m => latestIds.Contains(m.Id))
            .Select(m => new
            {
                m.ReporterId,
                Summary = new MessageSummaryResponse(
                    m.Id, m.Name, m.Status, m.Category, m.ReceivedAt, m.NextExpectedAt, m.RecurrenceDeclaration),
            })
            .ToListAsync(cancellationToken);

        return latest.ToDictionary(x => x.ReporterId, x => x.Summary);
    }

    /// <summary>
    /// A fresh handle, retried on the vanishing chance of a collision. Sixteen Crockford base32
    /// characters is about 80 bits, so the loop exists for correctness rather than because it is
    /// expected to spin.
    /// </summary>
    private static async Task<string> NewIdentifierAsync(HomonDbContext database, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidate = ReporterIdentifier.New();

            if (!await database.Reporters.AnyAsync(r => r.Identifier == candidate, cancellationToken))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not generate a free reporter identifier.");
    }

    private static ValidationProblem? ValidateFields(
        ReporterRequest request,
        out string name,
        out string? description,
        out MessageBodyVisibility visibility)
    {
        var errors = new Dictionary<string, string[]>();

        name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > Reporter.NameMaxLength)
        {
            errors["name"] = [$"Name is required and at most {Reporter.NameMaxLength} characters."];
        }

        var trimmedDescription = request.Description?.Trim();
        if (trimmedDescription?.Length > Reporter.DescriptionMaxLength)
        {
            errors["description"] = [$"Description is at most {Reporter.DescriptionMaxLength} characters."];
        }

        description = string.IsNullOrEmpty(trimmedDescription) ? null : trimmedDescription;

        visibility = MessageBodyVisibility.Administrator;
        if (request.BodyVisibility is { } raw && !string.IsNullOrWhiteSpace(raw)
            && !Enum.TryParse(raw.Trim(), ignoreCase: true, out visibility))
        {
            errors["bodyVisibility"] = ["Body visibility must be 'administrator' or 'reader'."];
        }

        return errors.Count > 0 ? TypedResults.ValidationProblem(errors) : null;
    }

    private static ValidationProblem DuplicateName() =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            ["name"] = ["A reporter with this name already exists."],
        });

    private static ReporterResponse ToResponse(
        Reporter reporter, ApiKey key, MessageSummaryResponse? latest, int messageCount, bool isWatched) =>
        new(
            reporter.Id,
            reporter.Identifier,
            reporter.Name,
            reporter.Description,
            reporter.BodyVisibility,
            reporter.CreatedAt,
            key.TokenId,
            key.LastUsedAt,
            key.RevokedAt,
            latest,
            messageCount,
            isWatched);

    /// <param name="Name">The administrator's label. Unique, case-insensitively.</param>
    /// <param name="Description">The administrator's own note about what this reporter is for.</param>
    /// <param name="BodyVisibility"><c>administrator</c> or <c>reader</c>. Omitted means administrator.</param>
    internal sealed record ReporterRequest(string? Name, string? Description, string? BodyVisibility);

    /// <summary>
    /// A reporter as the administrator's page sees it. <c>TokenId</c> is the public half of the
    /// paired key — the secret is never returned by any route — and the three <c>Key…</c> fields
    /// are what let the page say "last used three hours ago" or "revoked". <c>IsWatched</c> says
    /// whether a message probe watches this reporter, so the page can explain a refused delete
    /// before the administrator attempts one.
    /// </summary>
    public sealed record ReporterResponse(
        Guid Id,
        string Identifier,
        string Name,
        string? Description,
        MessageBodyVisibility BodyVisibility,
        DateTimeOffset CreatedAt,
        string TokenId,
        DateTimeOffset? KeyLastUsedAt,
        DateTimeOffset? KeyRevokedAt,
        MessageSummaryResponse? Latest,
        int MessageCount,
        bool IsWatched);

    /// <summary>A reporter's latest report, without its body. Deliberately: see Decision 11.</summary>
    public sealed record MessageSummaryResponse(
        long Id,
        string Name,
        MessageStatus Status,
        string Category,
        DateTimeOffset ReceivedAt,
        DateTimeOffset? NextExpectedAt,
        string? Recurrence);

    /// <summary>
    /// The one response shape in the application that carries a message body, returned by the one
    /// route that is administrator-only for that reason.
    /// </summary>
    public sealed record MessageResponse(
        long Id,
        string Name,
        string? Description,
        string? Body,
        bool Truncated,
        MessageStatus Status,
        string Category,
        DateTimeOffset ReceivedAt,
        DateTimeOffset? NextExpectedAt,
        string? Recurrence);

    /// <summary>The key appears here exactly once and is never recoverable afterwards.</summary>
    public sealed record ReporterCreatedResponse(ReporterResponse Reporter, string Token);

    public sealed record ReporterKeyResponse(Guid ReporterId, string TokenId, string Token);
}
