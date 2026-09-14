# Links — module slot

Not implemented yet. Plan 006.

**What it owns.** The bookmarks the dashboard shows beside the status cards: a URL, a
title, an optional description, and the order they appear in. Every link opens in a new
tab — that is a rule of the SPA, not a per-link option.

**Requirements.** Admin page to add, edit, delete and reorder links. Nothing about a link
is hard-coded; a fresh installation has none.

**Shape.** `Link` (Id, Title, Url, Description?, SortOrder, CreatedAt, UpdatedAt).
Optionally, later, a `ProbeId` so a link can sit on the same card as the probe that
watches the service it points at.

**Where the rest lands.** `Homon.Api/Endpoints/LinkEndpoints.cs`,
`Homon.Web/src/pages/admin-links-page.tsx`, a `links` section on the dashboard.
