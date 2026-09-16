namespace Homon.Domain.Pages;

/// <summary>
/// An administrator-written explanation page: title and a body of prose, stored as HTML that
/// has already been through <c>IPageHtmlSanitizer</c>. The body is rendered verbatim by the
/// SPA (<c>page-page.tsx</c>), so nothing between the editor and this class may skip the
/// sanitiser — see <c>Homon.Infrastructure/Pages/PageHtmlSanitizer.cs</c>.
/// </summary>
public sealed class Page
{
    /// <summary>Longest slug the admin may set. Lower-case kebab; see <see cref="SlugPattern"/>.</summary>
    public const int SlugMaxLength = 80;

    /// <summary>Longest title.</summary>
    public const int TitleMaxLength = 150;

    /// <summary>
    /// Cap on the sanitised body, in UTF-16 characters (~200 KB). "A light pages feature" —
    /// not a document store; see plans/007-pages-and-wysiwyg-editor.md, Decisions.
    /// </summary>
    public const int BodyHtmlMaxLength = 200_000;

    /// <summary>Lower-case kebab-case: one or more runs of <c>[a-z0-9]</c> joined by single hyphens.</summary>
    public const string SlugPattern = "^[a-z0-9]+(-[a-z0-9]+)*$";

    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    /// <summary>Sanitised HTML. Never write anything here that has not been through <c>IPageHtmlSanitizer</c>.</summary>
    public string BodyHtml { get; set; } = string.Empty;

    public bool IsPublished { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
