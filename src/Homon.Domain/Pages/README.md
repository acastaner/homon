# Pages — module slot

Shipped. Plan 007.

**What it owns.** A handful of explanation pages the administrator writes for the family:
"how to connect to the NAS", "what to do when the internet is down". Each has a slug, a
title, and a body edited in a WYSIWYG editor (TipTap) and stored as sanitised HTML.

**Shape.** `Page` (Id, Slug, Title, BodyHtml, IsPublished, CreatedAt, UpdatedAt). `Slug` is
lower-case kebab-case, unique, at most 80 characters; `Title` at most 150; `BodyHtml` at
most 200,000 UTF-16 characters after sanitising.

**The sanitiser.** `Homon.Infrastructure/Pages/PageHtmlSanitizer.cs` — an allow-list
`HtmlSanitizer` (`Ganss.Xss`) wrapper. This, not the client-side editor, is the security
boundary: `BodyHtml` is rendered with `dangerouslySetInnerHTML` in `page-page.tsx`, so
nothing may write to this column without going through
`IPageHtmlSanitizer.Sanitize(...)` first. See `docs/ARCHITECTURE.md` §3.18.

**Endpoints** (`Homon.Api/Endpoints/PageEndpoints.cs`), under `/api/v1`:

| Route | Policy | What |
| --- | --- | --- |
| `GET /pages` | Reader | Published pages only, `{slug, title}`, ordered by title |
| `GET /admin/pages` | Administrator | Every page, published or draft, most recently updated first |
| `GET /pages/{slug}` | Reader | The full page; an administrator may preview a draft, nobody else can tell one exists |
| `POST /pages` | Administrator | Creates a page; the response is the sanitised body actually stored |
| `PUT /pages/{id}` | Administrator | Replaces slug, title, body, published state |
| `DELETE /pages/{id}` | Administrator | Deletes the page; renaming a slug leaves no redirect |

**The SPA.** `pages/page-page.tsx` (`/pages/{slug}`), `pages/admin-pages-page.tsx` (the admin
list), `pages/admin-page-editor-page.tsx` (`/admin/pages/new` and `/admin/pages/:id`, the
TipTap editor). The dashboard's Pages section (`dashboard-page.tsx`) is hidden entirely when
there are no published pages.

**Not in scope.** Image uploads (only absolute `https://` image embedding is sanitised
through), revision history, slug redirects on rename.
