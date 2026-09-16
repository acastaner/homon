# Links — module slot

Implemented. Plan 006.

**What it owns.** The bookmarks the dashboard shows beside the status cards: a URL, a
title, an optional description, and the order they appear in. Every link opens in a new
tab — that is a rule of the SPA, not a per-link option.

**Requirements.** Admin page to add, edit, delete and reorder links. Nothing about a link
is hard-coded; a fresh installation has none.

**Shape.** `Link` (Id, Title, Url, Description?, Position, CreatedAt, UpdatedAt).
`Position` is a sort key, not a dense index — the same convention plans 002 and 006 use
for every orderable list; reads order by `(Position, Id)`, and every write that rewrites
the list renumbers it. Optionally, later, a `ProbeId` so a link can sit on the same card
as the probe that watches the service it points at.

**Endpoints.** `GET /links`, `POST /links`, `PUT /links/{id}`, `DELETE /links/{id}`,
`PUT /links/order`.

**Where the rest lands.** `Homon.Api/Endpoints/LinkEndpoints.cs`,
`Homon.Web/src/pages/admin-links-page.tsx`, a `links` section on the dashboard.
