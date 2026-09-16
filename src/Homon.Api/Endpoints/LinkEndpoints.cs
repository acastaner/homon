using Homon.Api.Authentication;
using Homon.Domain.Links;
using Homon.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Homon.Api.Endpoints;

/// <summary>
/// Admin CRUD and reorder for <see cref="Link"/>, under <c>/links</c> — the household's
/// bookmarks shown beside the status cards.
/// </summary>
/// <remarks>
/// <c>GET</c> carries <see cref="HomonPolicies.Reader"/> — the same policy every dashboard
/// read uses. Every write is <see cref="HomonPolicies.Administrator"/>, session only. See
/// plan 006.
/// </remarks>
internal static class LinkEndpoints
{
    internal static RouteGroupBuilder MapLinkEndpoints(this RouteGroupBuilder parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        var group = parent.MapGroup("/links");

        group.MapGet("", GetLinksAsync)
            .RequireAuthorization(HomonPolicies.Reader)
            .WithName("GetLinks")
            .WithSummary("Lists every link, in display order.");

        group.MapPost("", CreateLinkAsync)
            .RequireAuthorization(HomonPolicies.Administrator)
            .WithName("CreateLink")
            .WithSummary("Creates a link, appended at the end of the display order.");

        group.MapPut("/{id:guid}", UpdateLinkAsync)
            .RequireAuthorization(HomonPolicies.Administrator)
            .WithName("UpdateLink")
            .WithSummary("Replaces a link's title, URL and description. Order is untouched.");

        group.MapDelete("/{id:guid}", DeleteLinkAsync)
            .RequireAuthorization(HomonPolicies.Administrator)
            .WithName("DeleteLink")
            .WithSummary("Deletes a link.");

        group.MapPut("/order", ReorderLinksAsync)
            .RequireAuthorization(HomonPolicies.Administrator)
            .WithName("ReorderLinks")
            .WithSummary("Sets the display order for every link.");

        return group;
    }

    private static async Task<Ok<LinkResponse[]>> GetLinksAsync(
        HomonDbContext database, CancellationToken cancellationToken)
    {
        var links = await database.Links
            .OrderBy(l => l.Position)
            .ThenBy(l => l.Id)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(links.Select(ToResponse).ToArray());
    }

    private static async Task<IResult> CreateLinkAsync(
        LinkRequest request, HomonDbContext database, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var validationError = Validate(request, out var title, out var url, out var description);
        if (validationError is not null)
        {
            return TypedResults.ValidationProblem(validationError);
        }

        var maxPosition = await database.Links
            .Select(l => (int?)l.Position)
            .MaxAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();

        var link = new Link
        {
            Id = Guid.NewGuid(),
            Title = title,
            Url = url,
            Description = description,
            Position = (maxPosition ?? -1) + 1,
            CreatedAt = now,
            UpdatedAt = now,
        };

        database.Links.Add(link);
        await database.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/v1/links/{link.Id}", ToResponse(link));
    }

    private static async Task<IResult> UpdateLinkAsync(
        Guid id, LinkRequest request, HomonDbContext database, TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var link = await database.Links.FindAsync([id], cancellationToken);

        if (link is null)
        {
            return TypedResults.NotFound();
        }

        var validationError = Validate(request, out var title, out var url, out var description);
        if (validationError is not null)
        {
            return TypedResults.ValidationProblem(validationError);
        }

        link.Title = title;
        link.Url = url;
        link.Description = description;
        link.UpdatedAt = timeProvider.GetUtcNow();
        // Position is untouched — it is governed only by ReorderLinksAsync.

        await database.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(ToResponse(link));
    }

    private static async Task<IResult> DeleteLinkAsync(
        Guid id, HttpContext httpContext, HomonDbContext database, CancellationToken cancellationToken)
    {
        // DELETE has no bound body, so nothing else demands the application/json content
        // type that closes the CSRF guard — model AuthenticationEndpoints.SignOutAsync's
        // check verbatim.
        if (httpContext.Request.ContentType?.StartsWith(
                "application/json", StringComparison.OrdinalIgnoreCase) is not true)
        {
            return TypedResults.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        var link = await database.Links.FindAsync([id], cancellationToken);

        if (link is null)
        {
            return TypedResults.NotFound();
        }

        database.Links.Remove(link);
        await database.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> ReorderLinksAsync(
        ReorderLinksRequest request, HomonDbContext database, CancellationToken cancellationToken)
    {
        var links = await database.Links.ToListAsync(cancellationToken);

        try
        {
            Link.Reorder(links, request.LinkIds ?? []);
        }
        catch (ArgumentException)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["linkIds"] = ["The list must contain exactly the existing ids — none missing, none extra, none duplicated."],
            });
        }

        await database.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Validates a create/update request. Returns null when valid; otherwise a field-keyed
    /// error dictionary ready for <c>TypedResults.ValidationProblem</c>.
    /// </summary>
    private static Dictionary<string, string[]>? Validate(
        LinkRequest request, out string title, out string url, out string? description)
    {
        title = request.Title?.Trim() ?? string.Empty;
        url = request.Url?.Trim() ?? string.Empty;
        description = NormalizeDescription(request.Description);

        var errors = new Dictionary<string, string[]>();

        if (title.Length is 0 or > Link.TitleMaxLength)
        {
            errors["title"] = [$"Title is required and at most {Link.TitleMaxLength} characters."];
        }

        if (url.Length is 0 or > Link.UrlMaxLength)
        {
            errors["url"] = [$"URL is required and at most {Link.UrlMaxLength} characters."];
        }
        else if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https"))
        {
            // An allow-list, not a denylist of dangerous schemes: the SPA renders href
            // verbatim into a real anchor, so a stored javascript:/data: URL would execute
            // in a reader's tab on click — target="_blank" does not neutralise the scheme.
            // Only a forgotten scheme can slip past an allow-list.
            errors["url"] = ["URL must be an absolute http or https address."];
        }

        if (description is { Length: > Link.DescriptionMaxLength })
        {
            errors["description"] = [$"Description is at most {Link.DescriptionMaxLength} characters."];
        }

        return errors.Count == 0 ? null : errors;
    }

    /// <summary>Trims and turns an empty description into null, so the SPA has one falsy case to check.</summary>
    private static string? NormalizeDescription(string? description)
    {
        var trimmed = description?.Trim();
        return trimmed is { Length: > 0 } ? trimmed : null;
    }

    private static LinkResponse ToResponse(Link link) =>
        new(link.Id, link.Title, link.Url, link.Description, link.CreatedAt, link.UpdatedAt);

    /// <param name="Title">The link's display title — required, at most <see cref="Link.TitleMaxLength"/> characters.</param>
    /// <param name="Url">
    /// The link's target — required, an absolute <c>http</c> or <c>https</c> address, at
    /// most <see cref="Link.UrlMaxLength"/> characters.
    /// </param>
    /// <param name="Description">Optional detail shown beside the title, at most <see cref="Link.DescriptionMaxLength"/> characters.</param>
    internal sealed record LinkRequest(string? Title, string? Url, string? Description);

    /// <param name="LinkIds">Every link's id, in the order they should display.</param>
    internal sealed record ReorderLinksRequest(Guid[] LinkIds);

    /// <summary>A link as the admin page and the dashboard both need it. Order is the array's position, not a field.</summary>
    /// <param name="Id">The link's id.</param>
    /// <param name="Title">The link's display title.</param>
    /// <param name="Url">The link's target — always an absolute <c>http</c> or <c>https</c> address.</param>
    /// <param name="Description">Optional detail shown beside the title.</param>
    /// <param name="CreatedAt">When the link was added.</param>
    /// <param name="UpdatedAt">When the link's fields were last changed.</param>
    public sealed record LinkResponse(
        Guid Id, string Title, string Url, string? Description, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
}
