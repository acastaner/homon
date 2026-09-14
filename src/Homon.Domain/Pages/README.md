# Pages — module slot

Not implemented yet. Plan 007.

**What it owns.** A handful of explanation pages the administrator writes for the family:
"how to connect to the NAS", "what to do when the internet is down". Each has a slug, a
title, and a body edited in a WYSIWYG editor and stored as sanitised HTML.

**Requirements.** Admin page to create, edit and delete pages; readers see them at
`/pages/{slug}`. Rendering must sanitise on the way *in* (server side) so the stored body
is safe to render verbatim.

**Shape.** `Page` (Id, Slug, Title, BodyHtml, IsPublished, CreatedAt, UpdatedAt).

**Where the rest lands.** `Homon.Api/Endpoints/PageEndpoints.cs`, a server-side HTML
sanitiser in `Homon.Infrastructure/Pages/`, `Homon.Web/src/pages/page-page.tsx` (already
a placeholder) and `admin-pages-page.tsx`. Editor candidate: TipTap (see `docs/MODULES.md`).
