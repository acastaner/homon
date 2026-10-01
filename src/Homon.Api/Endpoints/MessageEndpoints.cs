using System.Text.RegularExpressions;
using Homon.Api.Authentication;
using Homon.Domain.Messaging;
using Homon.Infrastructure.Identity;
using Homon.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Homon.Api.Endpoints;

/// <summary>
/// The ingestion point: one route, which a script or an agent on another host posts a report to.
/// Everything the report claims — what it is called, whether it worked, when the next one is due
/// — is the reporter's own judgement; this endpoint validates the shape, derives one deadline,
/// and stores it (plan 021).
/// </summary>
/// <remarks>
/// Gated on <see cref="HomonPolicies.ApiKeyWrite"/>: a <c>Read</c> key and a browser session are
/// both refused. The reporter is resolved from the key, never from the body, so a stolen key
/// cannot file a report as somebody else — an <c>identifier</c> in the body is a typo guard only.
/// </remarks>
internal static partial class MessageEndpoints
{
    /// <summary>The rate-limiter policy name, registered in <c>Program.cs</c>.</summary>
    internal const string ReportThrottlePolicy = "message-report";

    /// <summary>
    /// Thirty reports per five minutes per key. Generous for a reporter that speaks once a day
    /// and tight enough that a script in a crash-restart loop cannot fill the table. Hard-coded
    /// rather than configurable, unlike the sign-in throttle: that one is a setting only because
    /// its own tests need a low limit, and nothing here needs to vary per installation.
    /// </summary>
    internal const int ReportPermitLimit = 30;

    internal static readonly TimeSpan ReportWindow = TimeSpan.FromMinutes(5);

    internal static RouteGroupBuilder MapMessageEndpoints(this RouteGroupBuilder parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        var group = parent.MapGroup("/messages");

        group.MapPost("", ReportAsync)
            .RequireAuthorization(HomonPolicies.ApiKeyWrite)
            .RequireRateLimiting(ReportThrottlePolicy)
            .WithName("ReportMessage")
            .WithSummary("Files one report from a registered reporter.");

        return group;
    }

    private static async Task<Results<Created<MessageAcceptedResponse>, ValidationProblem, ProblemHttpResult>> ReportAsync(
        MessageRequest request,
        HttpContext httpContext,
        HomonDbContext database,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        // The claim carries the key's public token id, not its row id — see
        // ApiKeyAuthenticationHandler. One join rather than two round trips.
        var tokenId = httpContext.User.FindFirst(HomonClaimTypes.ApiKeyId)?.Value;

        var pairing = await (from reporter in database.Reporters
                             join key in database.ApiKeys on reporter.ApiKeyId equals key.Id
                             where key.TokenId == tokenId
                             select new { Reporter = reporter, KeyId = key.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (pairing is null)
        {
            // Authenticated, but not as anybody who may report. Never auto-create a reporter
            // here: plan 008's reasoning, carried — a key with no reporter is an administrator's
            // mistake, and inventing one would hide it behind a reporter nobody is watching.
            return TypedResults.Problem(
                title: "Not a reporter",
                detail: "This API key is not paired with a reporter. An administrator creates one under Reporters.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var now = timeProvider.GetUtcNow();
        var errors = new Dictionary<string, string[]>();

        var identifier = request.Identifier?.Trim();
        if (!string.IsNullOrEmpty(identifier)
            && !string.Equals(identifier, pairing.Reporter.Identifier, StringComparison.Ordinal))
        {
            // Naming the key's own identifier leaks nothing the caller does not already hold, and
            // it is the one line that debugs a script pointed at the wrong reporter.
            errors["identifier"] =
                [$"This key reports as \"{pairing.Reporter.Identifier}\"; the identifier in the body does not match."];
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > Message.NameMaxLength)
        {
            errors["name"] = [$"Name is required and at most {Message.NameMaxLength} characters."];
        }

        var description = request.Description?.Trim();
        if (description?.Length > Message.DescriptionMaxLength)
        {
            errors["description"] = [$"Description is at most {Message.DescriptionMaxLength} characters."];
        }

        var status = MessageStatus.None;
        if (request.Status is { } rawStatus && !string.IsNullOrWhiteSpace(rawStatus))
        {
            if (!Enum.TryParse(rawStatus.Trim(), ignoreCase: true, out status))
            {
                errors["status"] =
                    ["Status must be one of 'success', 'warning', 'failure', 'unknown' or 'none'."];
            }
        }

        var category = NormalizeCategory(request.Category);
        if (category.Length > Message.CategoryMaxLength || !CategoryRegex().IsMatch(category))
        {
            errors["category"] =
                [$"Category is at most {Message.CategoryMaxLength} characters, lower-case kebab-case (for example \"backup\")."];
        }

        var recurrence = MessageRecurrence.Resolve(request.Recurrence, request.ExpectNextBy, now);
        if (recurrence.ErrorField is { } field)
        {
            errors[field] = [recurrence.ErrorMessage!];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // Never rejected for length — a truncated proof-of-run beats a failed report at 02:00.
        var body = MessageBody.Truncate(request.Message);

        var message = new Message
        {
            ReporterId = pairing.Reporter.Id,
            ReceivedAt = now,
            Name = name,
            Description = string.IsNullOrEmpty(description) ? null : description,
            Body = body,
            Status = status,
            Category = category,
            RecurrenceDeclaration = recurrence.Declaration,
            NextExpectedAt = recurrence.NextExpectedAt,
            ReportedByKeyId = pairing.KeyId,
        };

        database.Messages.Add(message);
        await database.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/api/v1/reporters/{pairing.Reporter.Id}/messages",
            new MessageAcceptedResponse(
                message.Id,
                pairing.Reporter.Identifier,
                message.Status,
                message.Category,
                message.ReceivedAt,
                message.NextExpectedAt,
                message.RecurrenceDeclaration,
                MessageBody.WasTruncated(body)));
    }

    private static string NormalizeCategory(string? category)
    {
        var trimmed = category?.Trim();

        return string.IsNullOrEmpty(trimmed)
            ? Message.DefaultCategory
            : trimmed.ToLowerInvariant();
    }

    // Matches Message.CategoryPattern exactly — source-generated, so it is compiled once rather
    // than rebuilt on every report.
    [GeneratedRegex(Message.CategoryPattern)]
    private static partial Regex CategoryRegex();

    /// <param name="Identifier">
    /// Optional, and only ever checked for a match: the key decides who is reporting. Sending the
    /// wrong one is a 400, not a silent redirection.
    /// </param>
    /// <param name="Name">What the reporter calls itself, shown on the administrator's page.</param>
    /// <param name="Description">A sentence about this message or this reporter.</param>
    /// <param name="Message">
    /// Free text — a command's output, typically. Never rejected for length; a body over 64 KiB
    /// keeps its last 64 KiB behind a truncation marker.
    /// </param>
    /// <param name="Recurrence">
    /// An ISO 8601 duration measured from arrival: <c>PT25H</c>, <c>P1D</c>, <c>P5Y</c>. A month
    /// counts as 30 days and a year as 365. Mutually exclusive with <paramref name="ExpectNextBy"/>.
    /// </param>
    /// <param name="ExpectNextBy">An absolute instant, for a reporter that needs an exact calendar.</param>
    /// <param name="Status">
    /// <c>success</c>, <c>warning</c>, <c>failure</c>, <c>unknown</c> or <c>none</c>. Omitted
    /// means <c>none</c> — checked in, claiming nothing.
    /// </param>
    /// <param name="Category">A lower-case kebab-case slug. Omitted means <c>other</c>.</param>
    internal sealed record MessageRequest(
        string? Identifier,
        string? Name,
        string? Description,
        string? Message,
        string? Recurrence,
        DateTimeOffset? ExpectNextBy,
        string? Status,
        string? Category);

    /// <summary>
    /// What Homon understood. <c>nextExpectedAt</c> is echoed so a wrapper script can log the
    /// deadline it just set, which is the cheapest way to catch a wrong recurrence on the first
    /// run rather than on the first outage.
    /// </summary>
    public sealed record MessageAcceptedResponse(
        long Id,
        string Identifier,
        MessageStatus Status,
        string Category,
        DateTimeOffset ReceivedAt,
        DateTimeOffset? NextExpectedAt,
        string? Recurrence,
        bool Truncated);
}
