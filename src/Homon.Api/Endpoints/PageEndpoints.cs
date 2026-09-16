using System.Security.Claims;
using System.Text.RegularExpressions;
using Homon.Api.Authentication;
using Homon.Domain.Auth;
using Homon.Domain.Pages;
using Homon.Infrastructure.Pages;
using Homon.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Homon.Api.Endpoints;

/// <summary>
/// CRUD for <see cref="Page"/>, under <c>/pages</c> for readers and writers, and
/// <c>/admin/pages</c> for the admin list (which, unlike <c>GET /pages</c>, includes drafts).
/// </summary>
/// <remarks>
/// The stored <c>BodyHtml</c> is rendered verbatim by the SPA
/// (<c>dangerouslySetInnerHTML</c> in <c>page-page.tsx</c>), so every write runs the body
/// through <see cref="IPageHtmlSanitizer"/> before it reaches the database — the sanitiser,
/// not the TipTap editor, is the security boundary. See
/// <c>plans/007-pages-and-wysiwyg-editor.md</c>.
/// </remarks>
internal static partial class PageEndpoints
{
    internal static RouteGroupBuilder MapPageEndpoints(this RouteGroupBuilder parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        var pages = parent.MapGroup("/pages");

        pages.MapGet("", GetPublishedPagesAsync)
            .RequireAuthorization(HomonPolicies.Reader)
            .WithName("GetPages")
            .WithSummary("Lists every published page, ordered by title.");

        pages.MapGet("/{slug}", GetPageBySlugAsync)
            .RequireAuthorization(HomonPolicies.Reader)
            .WithName("GetPage")
            .WithSummary("Reads one page by slug. An administrator may preview an unpublished page; nobody else can tell it exists.");

        pages.MapPost("", CreatePageAsync)
            .RequireAuthorization(HomonPolicies.Administrator)
            .WithName("CreatePage")
            .WithSummary("Creates a page. The body is sanitised before it is stored.");

        pages.MapPut("/{id:guid}", UpdatePageAsync)
            .RequireAuthorization(HomonPolicies.Administrator)
            .WithName("UpdatePage")
            .WithSummary("Replaces a page's slug, title, body and published state.");

        pages.MapDelete("/{id:guid}", DeletePageAsync)
            .RequireAuthorization(HomonPolicies.Administrator)
            .WithName("DeletePage")
            .WithSummary("Deletes a page. No redirect is left behind for its old slug.");

        var adminPages = parent.MapGroup("/admin/pages");

        adminPages.MapGet("", GetAdminPagesAsync)
            .RequireAuthorization(HomonPolicies.Administrator)
            .WithName("GetAdminPages")
            .WithSummary("Lists every page, published or draft, most recently updated first.");

        return pages;
    }

    private static async Task<Ok<PageSummaryResponse[]>> GetPublishedPagesAsync(
        HomonDbContext database, CancellationToken cancellationToken)
    {
        var pages = await database.Pages
            .Where(p => p.IsPublished)
            .OrderBy(p => p.Title)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(pages.Select(p => new PageSummaryResponse(p.Slug, p.Title)).ToArray());
    }

    private static async Task<Ok<AdminPageSummaryResponse[]>> GetAdminPagesAsync(
        HomonDbContext database, CancellationToken cancellationToken)
    {
        var pages = await database.Pages
            .OrderByDescending(p => p.UpdatedAt)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(pages.Select(ToAdminSummary).ToArray());
    }

    private static async Task<Results<Ok<PageResponse>, NotFound>> GetPageBySlugAsync(
        string slug, ClaimsPrincipal user, HomonDbContext database, CancellationToken cancellationToken)
    {
        var normalizedSlug = slug.Trim().ToLowerInvariant();

        var page = await database.Pages
            .FirstOrDefaultAsync(p => p.Slug == normalizedSlug, cancellationToken);

        // Indistinguishable from an unknown slug for anyone but an administrator, so a
        // reader can never tell a draft exists — a 403 would leak that fact.
        if (page is null || (!page.IsPublished && !user.IsInRole(HomonRoles.Administrator)))
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(ToResponse(page));
    }

    private static async Task<Results<Created<PageResponse>, ValidationProblem>> CreatePageAsync(
        PageRequest request, HomonDbContext database, IPageHtmlSanitizer sanitizer,
        TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var (validationError, slug, title, bodyHtml) = await ValidateAsync(
            request, database, sanitizer, excludingId: null, cancellationToken);
        if (validationError is not null)
        {
            return validationError;
        }

        var now = timeProvider.GetUtcNow();

        var page = new Page
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            Title = title,
            BodyHtml = bodyHtml,
            IsPublished = request.IsPublished,
            CreatedAt = now,
            UpdatedAt = now,
        };

        database.Pages.Add(page);

        var saveError = await SaveOrConflictAsync(database, cancellationToken);
        if (saveError is not null)
        {
            return saveError;
        }

        return TypedResults.Created($"/api/v1/pages/{page.Slug}", ToResponse(page));
    }

    private static async Task<Results<Ok<PageResponse>, ValidationProblem, NotFound>> UpdatePageAsync(
        Guid id, PageRequest request, HomonDbContext database, IPageHtmlSanitizer sanitizer,
        TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var page = await database.Pages.FindAsync([id], cancellationToken);

        if (page is null)
        {
            return TypedResults.NotFound();
        }

        var (validationError, slug, title, bodyHtml) = await ValidateAsync(
            request, database, sanitizer, excludingId: id, cancellationToken);
        if (validationError is not null)
        {
            return validationError;
        }

        page.Slug = slug;
        page.Title = title;
        page.BodyHtml = bodyHtml;
        page.IsPublished = request.IsPublished;
        page.UpdatedAt = timeProvider.GetUtcNow();

        var saveError = await SaveOrConflictAsync(database, cancellationToken);
        if (saveError is not null)
        {
            return saveError;
        }

        return TypedResults.Ok(ToResponse(page));
    }

    private static async Task<Results<NoContent, NotFound>> DeletePageAsync(
        Guid id, HomonDbContext database, CancellationToken cancellationToken)
    {
        var page = await database.Pages.FindAsync([id], cancellationToken);

        if (page is null)
        {
            return TypedResults.NotFound();
        }

        database.Pages.Remove(page);
        await database.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Validates and normalises a create/update request: slug (lower-cased, pattern,
    /// length, uniqueness), title (trimmed, length) and body (length pre-check, sanitise,
    /// length re-check). The error is null when the request is valid.
    /// </summary>
    private static async Task<(ValidationProblem? Error, string Slug, string Title, string BodyHtml)> ValidateAsync(
        PageRequest request, HomonDbContext database, IPageHtmlSanitizer sanitizer,
        Guid? excludingId, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        var slug = request.Slug?.Trim().ToLowerInvariant() ?? string.Empty;
        var title = request.Title?.Trim() ?? string.Empty;
        var rawBodyHtml = request.BodyHtml ?? string.Empty;
        var bodyHtml = string.Empty;

        if (slug.Length is 0 or > Page.SlugMaxLength || !SlugRegex().IsMatch(slug))
        {
            errors["slug"] =
                [$"Slug is required, at most {Page.SlugMaxLength} characters, and lower-case kebab-case (e.g. \"how-to-connect\")."];
        }
        else
        {
            var duplicateExists = await database.Pages
                .Where(p => p.Slug == slug)
                .Where(p => excludingId == null || p.Id != excludingId)
                .AnyAsync(cancellationToken);

            if (duplicateExists)
            {
                errors["slug"] = ["A page with this slug already exists."];
            }
        }

        if (title.Length is 0 or > Page.TitleMaxLength)
        {
            errors["title"] = [$"Title is required and at most {Page.TitleMaxLength} characters."];
        }

        // Cheap pre-check on the raw string, before spending sanitising's cost on an absurd
        // payload — the real cap is the post-sanitise check below (sanitising only shrinks
        // or preserves length, so this is defence, not the rule).
        if (rawBodyHtml.Length > Page.BodyHtmlMaxLength * 2)
        {
            errors["bodyHtml"] = [$"Body is too long. The stored body may be at most {Page.BodyHtmlMaxLength} characters."];
        }
        else
        {
            bodyHtml = sanitizer.Sanitize(rawBodyHtml);

            if (bodyHtml.Length > Page.BodyHtmlMaxLength)
            {
                errors["bodyHtml"] = [$"Body is too long. The stored body may be at most {Page.BodyHtmlMaxLength} characters."];
            }
        }

        return (errors.Count == 0 ? null : TypedResults.ValidationProblem(errors), slug, title, bodyHtml);
    }

    /// <summary>
    /// Commits pending changes, turning a Postgres unique-violation (two concurrent creates
    /// racing the same slug past the check above) into the same <see cref="ValidationProblem"/>
    /// the check itself would have produced.
    /// </summary>
    private static async Task<ValidationProblem?> SaveOrConflictAsync(
        HomonDbContext database, CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["slug"] = ["A page with this slug already exists."],
            });
        }
    }

    // Matches Page.SlugPattern exactly — a source-generated regex is compiled once, not
    // built at every call.
    [GeneratedRegex(Page.SlugPattern)]
    private static partial Regex SlugRegex();

    private static PageResponse ToResponse(Page page) =>
        new(page.Id, page.Slug, page.Title, page.BodyHtml, page.IsPublished, page.CreatedAt, page.UpdatedAt);

    private static AdminPageSummaryResponse ToAdminSummary(Page page) =>
        new(page.Id, page.Slug, page.Title, page.IsPublished, page.UpdatedAt);

    /// <param name="Slug">
    /// Lower-case kebab-case, at most <see cref="Page.SlugMaxLength"/> characters, unique.
    /// Lower-cased server-side before validating.
    /// </param>
    /// <param name="Title">Required, at most <see cref="Page.TitleMaxLength"/> characters.</param>
    /// <param name="BodyHtml">
    /// Rich text as HTML. Sanitised server-side before storage — the response returns what
    /// was actually stored, which may differ from what was submitted.
    /// </param>
    /// <param name="IsPublished">Whether readers can see the page. Defaults to false when omitted.</param>
    internal sealed record PageRequest(string? Slug, string? Title, string? BodyHtml, bool IsPublished);

    /// <summary>What a reader's page list needs: enough to link to the page, nothing else.</summary>
    /// <param name="Slug">The page's slug.</param>
    /// <param name="Title">The page's title.</param>
    public sealed record PageSummaryResponse(string Slug, string Title);

    /// <summary>What the admin list needs: every page, published or not, with enough to link to the editor.</summary>
    /// <param name="Id">The page's id.</param>
    /// <param name="Slug">The page's slug.</param>
    /// <param name="Title">The page's title.</param>
    /// <param name="IsPublished">Whether readers can see the page.</param>
    /// <param name="UpdatedAt">When the page was last changed.</param>
    public sealed record AdminPageSummaryResponse(Guid Id, string Slug, string Title, bool IsPublished, DateTimeOffset UpdatedAt);

    /// <summary>A page in full — read, create and update responses all take this shape.</summary>
    /// <param name="Id">The page's id.</param>
    /// <param name="Slug">The page's slug.</param>
    /// <param name="Title">The page's title.</param>
    /// <param name="BodyHtml">The sanitised body, safe to render verbatim.</param>
    /// <param name="IsPublished">Whether readers can see the page.</param>
    /// <param name="CreatedAt">When the page was created.</param>
    /// <param name="UpdatedAt">When the page's fields were last changed.</param>
    public sealed record PageResponse(
        Guid Id, string Slug, string Title, string BodyHtml, bool IsPublished, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
}
