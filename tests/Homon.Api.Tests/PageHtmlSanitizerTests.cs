using Homon.Infrastructure.Pages;

namespace Homon.Api.Tests;

/// <summary>
/// The XSS corpus for <see cref="PageHtmlSanitizer"/> — the sanitiser is the whole defence
/// against stored XSS (<c>BodyHtml</c> is rendered verbatim by the SPA), so every case here
/// asserts the exact stripped-or-kept output, not just that
/// <see cref="IPageHtmlSanitizer.Sanitize"/> returned. See
/// plans/007-pages-and-wysiwyg-editor.md, Test plan.
/// </summary>
public class PageHtmlSanitizerTests
{
    private readonly PageHtmlSanitizer _sanitizer = new();

    [Theory]
    [InlineData(
        "<script>alert(1)</script>",
        "",
        "a <script> is stripped entirely, including its text content")]
    [InlineData(
        "<p onclick=\"alert(1)\">hi</p>",
        "<p>hi</p>",
        "an inline event handler is stripped; the element and its text are kept")]
    [InlineData(
        "<a href=\"javascript:alert(1)\">x</a>",
        "<a>x</a>",
        "a javascript: href is stripped (the scheme is not on the allow-list)")]
    [InlineData(
        "<a href=\"JaVaScRiPt:alert(1)\">x</a>",
        "<a>x</a>",
        "a javascript: href is stripped case-insensitively")]
    [InlineData(
        "<a href=\"&#106;avascript:alert(1)\">x</a>",
        "<a>x</a>",
        "an entity-encoded javascript: href is stripped")]
    [InlineData(
        "<img src=x onerror=alert(1)>",
        "",
        "an <img> is removed entirely: onerror is not an allowed attribute, and src is not https://")]
    [InlineData(
        "<svg onload=alert(1)></svg>",
        "",
        "an <svg> is stripped entirely — the tag is not on the allow-list")]
    [InlineData(
        "<p style=\"background:url(javascript:alert(1))\">x</p>",
        "<p>x</p>",
        "a style attribute is stripped entirely — it is not on the allow-list")]
    [InlineData(
        "<iframe src=\"https://evil.example\">trapped text</iframe>",
        "",
        "an <iframe> is stripped entirely, with no child content kept")]
    [InlineData(
        "<img src=\"data:text/html;base64,PHNjcmlwdD4=\">",
        "",
        "an <img> with a data: src is removed")]
    [InlineData(
        "<a href=\"https://example.com\">x</a>",
        "<a href=\"https://example.com\" target=\"_blank\" rel=\"noopener noreferrer\">x</a>",
        "an absolute link is kept and gains target=_blank rel=noopener noreferrer")]
    [InlineData(
        "<a href=\"/pages/other\">x</a>",
        "<a href=\"/pages/other\">x</a>",
        "a relative link is kept without target or rel")]
    [InlineData(
        "<img src=\"https://example.com/a.png\" alt=\"a\">",
        "<img src=\"https://example.com/a.png\" alt=\"a\">",
        "an https:// image is kept with its alt text preserved")]
    [InlineData(
        "<h2>Heading</h2><ul><li>One</li><li>Two</li></ul><blockquote><p>Quoted</p></blockquote><pre><code>plain(code)</code></pre>",
        "<h2>Heading</h2><ul><li>One</li><li>Two</li></ul><blockquote><p>Quoted</p></blockquote><pre><code>plain(code)</code></pre>",
        "a legitimate document of allow-listed tags passes through unchanged")]
    [InlineData(
        "<h1>Title</h1>",
        "",
        "a disallowed <h1> is stripped entirely, including its text — the editor never produces one, but the sanitiser must not trust that")]
    public void Sanitize_matches_the_XSS_corpus(string input, string expected, string because)
    {
        var result = _sanitizer.Sanitize(input);

        Assert.True(
            expected == result,
            $"{because}\n  input:    {input}\n  expected: {expected}\n  actual:   {result}");
    }
}
