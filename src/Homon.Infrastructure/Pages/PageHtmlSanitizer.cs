using Ganss.Xss;

namespace Homon.Infrastructure.Pages;

/// <summary>Sanitises a page body before it is stored. The allow-list is the security boundary.</summary>
public interface IPageHtmlSanitizer
{
    /// <summary>Returns the sanitised HTML. Never throws on malicious input — it strips.</summary>
    string Sanitize(string html);
}

/// <summary>
/// Wraps <see cref="HtmlSanitizer"/> with the exact tag/attribute allow-list the TipTap editor
/// (admin-page-editor-page.tsx) is configured to produce. If the editor's extension list
/// changes, this allow-list must change with it — see plans/007-pages-and-wysiwyg-editor.md,
/// Maintenance notes.
/// </summary>
public sealed class PageHtmlSanitizer : IPageHtmlSanitizer
{
    private readonly HtmlSanitizer _sanitizer;

    public PageHtmlSanitizer()
    {
        _sanitizer = new HtmlSanitizer();

        _sanitizer.AllowedTags.Clear();
        _sanitizer.AllowedTags.UnionWith(
        [
            "p", "h2", "h3", "h4", "strong", "em", "s", "code", "pre",
            "blockquote", "ul", "ol", "li", "a", "hr", "br", "img",
        ]);

        // No style, no class, no id, no on*: HtmlSanitizer denies everything not listed here.
        _sanitizer.AllowedAttributes.Clear();
        _sanitizer.AllowedAttributes.UnionWith(["href", "src", "alt"]);

        _sanitizer.AllowedCssProperties.Clear();
        _sanitizer.AllowedSchemes.Clear();
        _sanitizer.AllowedSchemes.UnionWith(["http", "https", "mailto"]);

        // A disallowed element's children are dropped with it — an <iframe> or <script> never
        // gets "unwrapped" into surrounding text.
        _sanitizer.KeepChildNodes = false;

        _sanitizer.PostProcessNode += ForcePolicy;
    }

    // ArgumentNullException.ThrowIfNull on a public entry point (CLAUDE.md) — a null body
    // would otherwise reach HtmlSanitizer.Sanitize and throw its own, less specific exception.
    public string Sanitize(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        return _sanitizer.Sanitize(html);
    }

    // Runs after the allow-list pass. Two jobs: force target/rel on absolute links (never
    // admin-controlled — the editor has no rel/target attribute on its allow-list, so this is
    // the only place either is ever set), and enforce the stricter img src rule (https only —
    // AllowedSchemes above is a shared http/https/mailto list for every URL attribute, and img
    // needs to be narrower than href).
    private static void ForcePolicy(object? sender, PostProcessNodeEventArgs e)
    {
        if (e.Node is not AngleSharp.Dom.IElement element)
        {
            return;
        }

        switch (element.TagName)
        {
            case "A":
                var href = element.GetAttribute("href");
                var isAbsolute = href is not null &&
                    (href.Contains("://", StringComparison.Ordinal)
                        || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase));

                if (isAbsolute)
                {
                    element.SetAttribute("target", "_blank");
                    element.SetAttribute("rel", "noopener noreferrer");
                }
                break;

            case "IMG":
                var src = element.GetAttribute("src");
                if (src is null || !src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    element.Remove();
                }
                break;
        }
    }
}
