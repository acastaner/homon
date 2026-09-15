# 006 — Links

> **Executor instructions**: Follow this plan step by step. Run every verification
> command and confirm the expected result before moving to the next step. If anything in
> "STOP conditions" occurs, stop and report — do not improvise. The reviewer maintains
> `plans/README.md`; do not edit it.
>
> **Drift check (run first)**: `git diff --stat f4e7261..HEAD -- src/Homon.Domain/Links
> src/Homon.Api/Endpoints src/Homon.Api/Program.cs src/Homon.Infrastructure/Persistence
> src/Homon.Web/src/pages/dashboard-page.tsx src/Homon.Web/src/pages/admin-links-page.tsx
> src/Homon.Web/src/lib docs/design-brief.md tests/Homon.Api.Tests`. In this execution
> order, 002 and 003 land first and touch some of these same files — `Program.cs`'s
> endpoint-list comment, `HomonDbContext.cs`, the migrations folder, and `dashboard-page.tsx`
> most of all, which plan 002 restructures into grouped service sections followed by Links.
> Seeing unrelated lines added by 002 or 003 is **not** a STOP condition. What matters is whether
> three anchors are still shaped as quoted below: the commented `v1.MapLinkEndpoints();`
> in `Program.cs`, the `<section aria-labelledby="links-heading">` block in
> `dashboard-page.tsx` (the id must survive 002's restructure verbatim), and the
> placeholder text in `admin-links-page.tsx`. If any of the three changed shape rather
> than just gained neighbours, treat it as a STOP condition.

## Status

- **Priority**: P2
- **Effort**: M
- **Risk**: LOW
- **Depends on**: none functionally — Links is its own aggregate, no schema or runtime
  coupling to Monitoring. It reuses the `Position`/order wire convention plan 002
  introduces for probe groups (a stylistic dependency only); this session's execution
  order is 002 → 003 → 006, so that convention already exists by the time 006 runs.
- **Category**: direction
- **Planned at**: commit `f4e7261`, 2026-09-15
- **Reviewed**: 2026-09-15 (review-plan, execution order 013 → 002 → 003 → 006 → 007 → 010 →
  012)

## Context

`docs/MODULES.md`, verbatim: **"Links. URL, title, optional description; always open in a
new tab; admin CRUD."** Links are the household's bookmarks shown beside the status
cards — a NAS's web UI, the router, a shared recipe site. It is the simplest
unimplemented module: one entity, no external integration, no background work.
`src/Homon.Domain/Links/README.md` is the slot; nothing is implemented yet. The SPA
already has a placeholder page, a wired route, and an admin-nav link for Links (Phase 0
scaffolding built the shell before any module), so the SPA work here is narrower than it
looks: fill an existing gap, don't wire one up.

## Decisions

**`Position`, not `SortOrder`.** The slot README (`src/Homon.Domain/Links/README.md:12`)
names the shape with `SortOrder`. Plan 002 establishes the ordering convention this
codebase now follows for every orderable list (`plans/002-monitoring-core-groups-and-ping.md:36,74`):
*"`Position` is a sort key, not a dense index. Reads order by `(Position, Id)`, and every
write that rewrites a list renumbers it."* / *"The array's order is the wire format;
`Position` never leaves the server."* Links reuses that, rather than a second name for
the same idea. The brief never mandates a field name, only reordering behaviour
(`docs/design-brief.md:68`: "order (drag or up/down)"). Step 1 updates the README.

**No unique index on `Position`.** Same reasoning as `ProbeGroupConfiguration`: PostgreSQL
checks non-deferrable unique constraints row by row, so swapping two positions in one
transaction would fail partway through. Recorded as a code comment only — a restatement
of an already-recorded pattern, not a new `docs/ARCHITECTURE.md` section.

**URL validation: absolute `http`/`https` only.** The SPA renders `href` verbatim
(`docs/design-brief.md:255`). A stored `javascript:`/`data:` URL would execute in a
reader's tab on click — `target="_blank"` does not neutralise the scheme. The server
validates with `Uri.TryCreate(url, UriKind.Absolute, …)` and requires `Scheme is "http"
or "https"`, independent of the SPA's `type="url"` input. Rejected: a denylist of
dangerous schemes — an allow-list is the only kind a forgotten scheme can't slip past.

**A relative Homon URL (e.g. `/pages/foo`) is deferred, not rejected.** The brief
describes external bookmarks; a link that opens *within* the app is a different feature
with different rendering rules (it shouldn't force a new tab), which this plan's
unconditional "always new tab" rule would get wrong. Revisit only if a future plan wants
a dashboard link into a Homon Page.

**`rel="noopener noreferrer"`, not the brief's literal `rel="noopener"`
(`docs/design-brief.md:255`).** `noopener` alone stops the new tab reaching back via
`window.opener`; it does not stop the browser sending a `Referer` naming this dashboard
to whatever site the admin linked. A home dashboard's URL, and the sibling links beside
it (a NAS admin UI, a router config page), are not worth handing to a third party's
request logs. `noreferrer` is a strict superset of `noopener`'s protection in every
current browser and costs nothing. Step 6 updates `docs/design-brief.md:255` to match —
this plan is the settled decision the brief is meant to absorb, and disagreeing with a
since-superseded artboard line is exactly what that's for. Not promoted to
`docs/ARCHITECTURE.md`: a one-line hardening of an existing UI rule, not a cross-cutting
decision.

**Accessible "opens in a new tab" hint via `aria-label`, not visible text.** No CSS
classes exist yet (`docs/ARCHITECTURE.md` §3.10) to hide a visible hint from sighted
readers, so `aria-label={`${title} (opens in a new tab)`}` keeps the visible text as the
title (matching `docs/design/dashboard/Main.dc.html:191`) while giving assistive tech the
fact that matters. Plan 012 may swap this for a visually-hidden span; the accessible name
string must not change when it does.

**Field limits** (not specified anywhere else): `Title` ≤ 200, `Url` ≤ 2048 (the longest
length every major browser/proxy reliably accepts), `Description` ≤ 1000, optional —
trimmed, empty-after-trim stored as `null` so the SPA has one falsy case to check, not
two.

**`ProbeId` is deferred**, per the slot README's own "optionally, later." No current
requirement calls for a link attached to a service card; adding a nullable FK later is a
small, additive migration with nothing to design ahead of time.

## Defaults taken (change them before implementation if wanted)

- Admin page edits inline via one shared form at the bottom of the list ("Edit link" /
  "Cancel" toggle), not a modal or a per-row form — simplest shape satisfying "add, edit,
  delete, reorder" without a component the design pass has to undo.
- Dashboard's empty state links to `/admin/links` for real, upgrading the current plain
  text — matches `docs/design-brief.md:265-268`: "one sentence naming where an
  administrator fixes it, with a link when that place is an admin page."
- `DELETE /links/{id:guid}` requires `Content-Type: application/json` with an empty
  `{}` body, mirroring `AuthenticationEndpoints.SignOutAsync`
  (`src/Homon.Api/Endpoints/AuthenticationEndpoints.cs:149-165`) — a body-less mutating
  route has nothing else to make model binding demand the content type that closes the
  CSRF guard.

## Current state

| File | Role |
| --- | --- |
| `src/Homon.Domain/Links/README.md` | The module slot; says `SortOrder`. Updated in Step 1. |
| `src/Homon.Api/Program.cs:476-479` | Endpoint registration list; `v1.MapLinkEndpoints();` is commented out. |
| `src/Homon.Api/Authentication/HomonPolicies.cs:17-26` | `Reader`, `Administrator`, `ApiKey` — use these constants. |
| `src/Homon.Infrastructure/Persistence/HomonDbContext.cs:19-29` | `DbSet<ApiKey> ApiKeys` only so far; add `DbSet<Link> Links`. |
| `.../Configurations/ApiKeyConfiguration.cs` | The one existing `IEntityTypeConfiguration<T>` — model `LinkConfiguration` on it. |
| `.../Persistence/Migrations/` | One migration so far. By the time 006 runs, 002 and 003 will have added their own; add a new one, don't touch theirs. |
| `src/Homon.Api/Endpoints/MetaEndpoints.cs`, `AuthenticationEndpoints.cs` | The two existing endpoint classes: `internal static class XxxEndpoints`, `MapXxxEndpoints(this RouteGroupBuilder)`, `TypedResults`, XML doc comments on wire records. |
| `src/Homon.Web/src/App.tsx:15,43` | **Already wired**: `AdminLinksPage` lazily imported, routed at `/admin/links`. No change. |
| `.../pages/admin-home-page.tsx:18` | **Already wired**: admin nav already links to `/admin/links`. No change. |
| `src/Homon.Web/e2e/helpers.ts:11-17` | **Already wired**: `/admin/links` already in `ADMIN_ROUTES`, gets the overflow check. No change. |
| `.../pages/dashboard-page.tsx:18-21` | The placeholder this plan replaces. Anchor: `<section aria-labelledby="links-heading">` / `<h2 id="links-heading">Links</h2>`. Plan 002 relocates this section but must keep the id — see drift check. |
| `.../pages/admin-links-page.tsx` | "Not implemented yet" placeholder. Rewritten in Step 6. |
| `.../lib/meta.ts`, `session.ts` | Exemplars for `lib/links.ts`: query key constant, `useQuery`, `useMutation` + `queryClient.invalidateQueries`. |
| `.../lib/api.ts` | `apiFetch<T>`, `ApiError`, `problemDetail(error)` — reuse, don't reimplement. |
| `.../test/fetch.ts` | `stubFetch(routes)` — the only fetch mocking mechanism (no MSW). |
| `docs/design/dashboard/Main.dc.html:90,170,189,202` | Section order on the artboard: Services, Backups, **Links**, Weather, Calendar (same in `BoardPhone.dc.html`, `BoardEmpty.dc.html`). Styling is plan 012's job — nothing here is `className`. |
| `docs/design-brief.md:253-255` | The written Links component spec; line 255 gets the `noreferrer` edit. |
| `tests/Homon.Api.Tests/ApiDatabaseFactory.cs`, `DatabaseFactAttribute.cs`, `TestDatabase.cs` | Database-backed fixture: a real PostgreSQL clone per class, migrated from a template built off every landed migration — the `AddLinks` migration is picked up automatically. |
| `tests/Homon.Api.Tests/MetaEndpointTests.cs:91-101` | `ConfiguredFactory : HomonApiFactory` pattern for a config override with no real database — use for the `RequireSignInForReaders` test (the `Reader` policy denies before the handler runs). |
| `tests/Homon.Api.Tests/ApiKeyRulesTests.cs` | Shape for a pure, no-fixture test class — model `LinkOrderingTests` on it. |
| `tests/Homon.Api.Tests/TestClient.cs` | `TestClient.Create(factory)`, `SignInAsync` — the cookie-carrying `HttpClient` every endpoint test uses. |

Conventions to match: endpoints are `internal static class LinkEndpoints` with
`MapLinkEndpoints(this RouteGroupBuilder)` registered in `Program.cs`'s `v1` group (see
`MetaEndpoints.cs:14-24`); read endpoints carry `.RequireAuthorization(HomonPolicies.Reader)`,
writes carry `HomonPolicies.Administrator`; entities are mutable classes like `ApiKey`
(`src/Homon.Domain/Auth/ApiKey.cs:20-46`), not records; RFC 9457 problems come from
`AddProblemDetails`/`UseStatusCodePages` already wired in `Program.cs:377-380,422` —
`TypedResults.NotFound()`/`ValidationProblem(errors)` need no extra code; frontend files
are kebab-case, named exports, `@/` alias, no `className`, semantic HTML with
`<label htmlFor>` and `role="alert"` for mutation errors, matching `sign-in-page.tsx:33-77`.

## Commands you will need

| Purpose | Command | Expected on success |
| --- | --- | --- |
| Full gate | `./ci/run-ci.sh` | `PASS — web api e2e` |
| Web only | `./ci/run-ci.sh web` | exit 0 |
| API only (manages its own throwaway Postgres) | `./ci/run-ci.sh api` | exit 0, 0 skipped |
| E2E only | `./ci/run-ci.sh e2e` | exit 0 |
| Backend build (the formatting gate) | `dotnet build Homon.sln -c Release` | exit 0, no warnings |
| Add the migration | `HOMON_DESIGNTIME_CONNECTION='Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x' dotnet dotnet-ef migrations add AddLinks --project src/Homon.Infrastructure --startup-project src/Homon.Api --output-dir Persistence/Migrations` | Migration files created |
| Frontend typecheck + build | `npm run build` (in `src/Homon.Web`) | exit 0 (`tsc -b && vite build`) |
| Frontend lint | `npm run lint` (in `src/Homon.Web`) | exit 0 |
| Frontend unit tests | `npm test` (in `src/Homon.Web`) | all pass |
| Playwright | `./ci/run-ci.sh e2e` | all pass |

## Scope

**In scope**: `src/Homon.Domain/Links/Link.cs` (new), `.../Links/README.md` (edit),
`.../Configurations/LinkConfiguration.cs` (new), `.../HomonDbContext.cs` (edit),
the `AddLinks` migration + snapshot (generated), `src/Homon.Api/Endpoints/LinkEndpoints.cs`
(new), `Program.cs` (edit: uncomment the map call), `src/Homon.Web/src/lib/links.ts`
(new), `dashboard-page.tsx` (edit: Links section only), `admin-links-page.tsx` (rewrite),
`docs/design-brief.md` (edit: line 255's `rel` value only), plus new test files listed in
Steps 4/7/8.

**Out of scope, with reasons**: `App.tsx`, `admin-home-page.tsx`, `e2e/helpers.ts` —
already wire `/admin/links` (see "Current state"); touching them risks silently
reverting landed work. `docs/MODULES.md` — the 006 row already matches what this plan
builds; no deviation from the brief to record (unlike plan 002's probe groups, which the
brief never mentioned). `docs/ARCHITECTURE.md` — no new section; see Decisions.
`ProbeId` and anything probe-related — deferred. Drag-and-drop reordering — up/down
buttons need no pointer/drop-target handling and match plan 002's own choice for probe
groups; `PUT /links/order` already takes the full ordered list, so drag can be layered on
later without a protocol change.

## Git workflow

- You run in a fresh, isolated git worktree seeded from committed files only (no
  `node_modules`, no build output) — run `npm --prefix src/Homon.Web ci` and
  `dotnet restore Homon.sln` yourself before anything else needs them.
- Do NOT create or switch branches. Commit on the worktree's current branch as you go.
- Do NOT merge or push. The reviewer runs `./ci/run-ci.sh` against your commits and merges
  when it is green.
- Commit per step or logical unit. Message style, from `git log --oneline`: a category
  prefix, an imperative sentence, the plan number in parentheses — e.g. `Design: fix the
  Status board direction, dark by default (plan 012)`, `Scaffold Homon: solution,
  plumbing, SPA shell, gate, Docker, docs (plan 001)`. Use `Links:` as the prefix here,
  e.g. `Links: add the Link entity, endpoints and admin page (plan 006)`.
- Do not touch `plans/README.md` — the reviewer maintains the index for this run.

## Steps

### Step 1: Domain — `Link` entity and the slot README

Create `src/Homon.Domain/Links/Link.cs`: a mutable class with `Id`, `Title`, `Url`,
`Description` (nullable), `Position` (int), `CreatedAt`, `UpdatedAt`; constants
`TitleMaxLength = 200`, `UrlMaxLength = 2048`, `DescriptionMaxLength = 1000`; and a static
`Reorder(IReadOnlyList<Link> links, IReadOnlyList<Guid> orderedIds)` that throws
`ArgumentException` unless `orderedIds` is exactly a permutation of `links`' ids
(`orderedIds.Count == links.Count`, no duplicates, every id present), then assigns
`Position = 0..n-1` in `orderedIds`' order. Doc-comment it as mirroring the ProbeGroup
ordering convention (`plans/002-monitoring-core-groups-and-ping.md`). Model the class
shape on `src/Homon.Domain/Auth/ApiKey.cs`.

Update `src/Homon.Domain/Links/README.md`: change the shape line (line 12) from
`SortOrder` to `Position`, note it's a sort key not a dense index (per plans 002/006), add
the endpoint list (`GET/POST/PUT/DELETE /links`, `PUT /links/order`), and leave the
"optionally, later" `ProbeId` note as-is.

**Verify**: `dotnet build src/Homon.Domain/Homon.Domain.csproj -c Release` → exit 0.

### Step 2: Persistence — configuration, `DbContext`, migration

Create `src/Homon.Infrastructure/Persistence/Configurations/LinkConfiguration.cs`
modelled on `ApiKeyConfiguration.cs`: table `"Links"`, key `Id`, `Title`/`Url` required
with their max-length constants, `Description` with its max length only (nullable, not
required), `CreatedAt`/`UpdatedAt` required. Comment: no unique index on `Position`,
same reasoning as `ProbeGroupConfiguration`.

Edit `HomonDbContext.cs`: add `using Homon.Domain.Links;` and
`public DbSet<Link> Links => Set<Link>();` after `ApiKeys`. Update the class doc comment
(lines 9-11) from "the API keys, and — as each module lands — the probes, links, pages
and backup reports" to "the API keys, the household's links, and — as each module lands —
the probes, pages and backup reports."

**Verify**: `dotnet build src/Homon.Infrastructure/Homon.Infrastructure.csproj -c Release`
→ exit 0.

Add the migration (from the repository root, per `CLAUDE.md`):

```bash
HOMON_DESIGNTIME_CONNECTION='Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x' \
  dotnet dotnet-ef migrations add AddLinks \
  --project src/Homon.Infrastructure --startup-project src/Homon.Api \
  --output-dir Persistence/Migrations
```

**Verify**: `ls src/Homon.Infrastructure/Persistence/Migrations/*AddLinks*` lists a `.cs`
and a `.Designer.cs`; `HomonDbContextModelSnapshot.cs` now contains a `"Links"` table.
`dotnet build Homon.sln -c Release` → exit 0.

### Step 3: API — `LinkEndpoints.cs`

Create `src/Homon.Api/Endpoints/LinkEndpoints.cs`: `internal static class LinkEndpoints`
with `MapLinkEndpoints(this RouteGroupBuilder parent)` mapping a `/links` group:

| Route | Policy | Behaviour |
| --- | --- | --- |
| `GET ""` | Reader | `database.Links.OrderBy(l => l.Position).ThenBy(l => l.Id)` → `LinkResponse[]` (no `Position` field on the wire — order is the format) |
| `POST ""` | Administrator | Validate (below); `Position` = current max + 1 (0 if none); `201` with `Location: /api/v1/links/{id}` |
| `PUT "/{id:guid}"` | Administrator | Validate; `404` if missing; updates `Title`/`Url`/`Description`/`UpdatedAt`, `Position` untouched; `200` |
| `DELETE "/{id:guid}"` | Administrator | Explicit `Content-Type: application/json` check like `SignOutAsync` (no bound body to enforce it otherwise); `404` if missing; `204` |
| `PUT "/order"` | Administrator | Body `{ linkIds: Guid[] }`; load all links, call `Link.Reorder(links, request.LinkIds ?? [])`; catch `ArgumentException` → `ValidationProblem` on `linkIds`; else save, `204` |

`Validate(LinkRequest request)` returns `Dictionary<string, string[]>?` (null = valid),
used by `POST` and `PUT`:
- `title`: required after trim; ≤ `Link.TitleMaxLength`.
- `url`: required after trim; ≤ `Link.UrlMaxLength`; and
  `Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed.Scheme is "http" or
  "https"` — reject anything else (`javascript:`, `data:`, `ftp:`, relative).
- `description`: optional; when present after trim, ≤ `Link.DescriptionMaxLength`; store
  `null` when trimmed-empty (`NormalizeDescription`).

Wire types: `internal sealed record LinkRequest(string? Title, string? Url, string?
Description)`; `internal sealed record ReorderLinksRequest(Guid[] LinkIds)`; `public
sealed record LinkResponse(Guid Id, string Title, string Url, string? Description,
DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)` with XML doc comments (feeds
OpenAPI, per `GenerateDocumentationFile` in `Directory.Build.props`).

Model the class layout, `TypedResults` usage and `ArgumentNullException.ThrowIfNull`
guard on `MapLinkEndpoints`'s argument on `MetaEndpoints.cs`; model the `DELETE`
content-type guard verbatim on `AuthenticationEndpoints.cs:149-165`
(`SignOutAsync`'s check). Return `Task<IResult>` from handlers with more than one outcome
shape (`Ok`, `NotFound`, `ValidationProblem`, …) rather than a `Results<...>` union, to
match the simpler style `AuthenticationEndpoints.SignInAsync` already uses; keep
`GetLinksAsync` a concrete `Task<Ok<LinkResponse[]>>` like `MetaEndpoints.GetMeta`, since
it has exactly one outcome.

Edit `Program.cs:476-479`: replace the commented `v1.MapLinkEndpoints();` with an active
call, leaving the remaining commented module list intact.

**Verify**: `dotnet build src/Homon.Api/Homon.Api.csproj -c Release` → exit 0.

### Step 4: xunit tests

Create `tests/Homon.Api.Tests/LinkOrderingTests.cs` (pure, no fixture, model
`ApiKeyRulesTests.cs`): a full permutation renumbers `0..n-1` in the given order; a
missing id, an extra id, and a duplicate id each throw `ArgumentException`.

Create `tests/Homon.Api.Tests/LinkEndpointTests.cs` using `ApiDatabaseFactory` +
`[DatabaseFact]` (model `AuthenticationEndpointTests.cs`/`MetaEndpointTests.cs`):
- `GET` on a fresh database → `[]`.
- `POST` creates and appends at the end (assert relative order via a second `POST`,
  since `Position` isn't on the wire); returns `201` with `Location` and the body.
- `POST` validation: missing title, missing url, `url: "javascript:alert(1)"`,
  `url: "ftp://example.test"`, a 2049-char url, a 201-char title → each `400` with the
  failing field named in the `ValidationProblem` body.
- `PUT` edits fields, leaves order unchanged; unknown id → `404`.
- `DELETE` with `Content-Type: application/json` and body `{}` → `204`; without that
  content type → `415`; unknown id → `404`.
- `PUT /order` with a correct permutation reorders (a following `GET` reflects it);
  missing/extra/duplicate ids → `400`.
- Auth matrix: anonymous `GET` → `200`; anonymous write (any of `POST`/`PUT`/`DELETE`/
  `order`) → `401`; a signed-in API key on the same writes → `403`
  (`HomonPolicies.Administrator` never admits an API key — it is
  `RequireRole(HomonRoles.Administrator)`, `HomonPolicies.cs:19-20,34`, and an API key
  carries no role claim) — follow the bearer-key pattern in `ApiKeyAuthenticationTests.cs`.
- With `Auth:RequireSignInForReaders = true`, anonymous `GET` → `401`, using the
  `ConfiguredFactory : HomonApiFactory` pattern from `MetaEndpointTests.cs:91-101` (no
  real database touched — the `Reader` policy denies before the handler runs).

**Verify**: `./ci/run-ci.sh api` → exit 0, 0 skipped, all new tests pass.

### Step 5: SPA data layer — `lib/links.ts`

Create `src/Homon.Web/src/lib/links.ts`, modelled on `lib/meta.ts` (the query) and
`lib/session.ts` (mutations + invalidation): `interface Link { id, title, url,
description: string | null, createdAt, updatedAt }` (mirrors `LinkResponse`), `interface
LinkFields { title, url, description }` (form shape, always strings), `LINKS_QUERY_KEY =
['links'] as const`, `useLinks()` (`useQuery`), and `useCreateLink`/`useUpdateLink`/
`useDeleteLink`/`useReorderLinks` (`useMutation`, each invalidating `LINKS_QUERY_KEY` on
success via a shared `useInvalidateLinks()` helper). `useDeleteLink`'s `mutationFn` posts
`body: '{}'` so `apiFetch` sets `Content-Type: application/json` (it only sets that
header when `init.body` is present — `api.ts:44-46`), matching the endpoint's guard from
Step 3.

**Verify**: `npm run build` (in `src/Homon.Web`) → exit 0 (typechecks even with nothing
importing this file yet).

### Step 6: SPA pages — dashboard Links section and the admin page

Edit `dashboard-page.tsx`: replace lines 18-21 with real rendering, keeping
`<section aria-labelledby="links-heading"><h2 id="links-heading">Links</h2>` verbatim.
Read `const { data: links = [] } = useLinks()`. Empty: a paragraph "No links yet. An
administrator adds them under Admin → " with a real `<Link to="/admin/links">Links</Link>`
(react-router). Non-empty: a `<ul>` of `<li>`, each an `<a href={link.url}
target="_blank" rel="noopener noreferrer" aria-label={`${link.title} (opens in a new
tab)`}>{link.title}</a>` plus, when set, `<span>{link.description}</span>`. Check for a
naming clash before importing `Link` from `react-router` — plan 002's restructure may
already import identifiers named `Link`/`LinkResponse` in this file; alias if so.

Rewrite `admin-links-page.tsx`: `<h1>Links</h1>`; when empty, "No links yet."; otherwise
an `<ol aria-label="Links">` of `<li>` each showing the title as an `<a target="_blank"
rel="noopener noreferrer">`, the description when set, and four buttons per row: "Move
{title} up" / "Move {title} down" (disabled at the ends of the list; `onClick` swaps that
id with its neighbour in the current id array and calls `useReorderLinks().mutate(next)`
with the full array — never a partial update), "Edit {title}" (loads the row into local
form state), "Delete {title}" (`useDeleteLink().mutate(link.id)`). Below the list, one
`<form>` with labelled `Title`/`URL` (`type="url"`)/`Description` inputs, a `role="alert"`
paragraph showing `problemDetail(...)` from whichever mutation last errored, and a submit
button reading "Add link" or "Save changes" depending on whether a row is being edited (a
"Cancel" button appears only while editing). Model the form's structure — one `<p>` per
field, `<label htmlFor>`, a submit handler that calls `event.preventDefault()` — on
`sign-in-page.tsx:33-77`.

Edit `docs/design-brief.md:255`: change `rel="noopener"` to `rel="noopener noreferrer"`
and add a short parenthetical pointing at this plan for the reasoning (see Decisions).

**Verify**: `npm run build && npm run lint` (in `src/Homon.Web`) → exit 0.

### Step 7: Vitest

`stubFetch` (`src/Homon.Web/src/test/fetch.ts:8-33`) keys routes on the request path only —
not the method — so use the full API path exactly as `apiFetch` builds it
(`API_BASE_URL` + the path, i.e. `/api/v1/links`, `/api/v1/links/order`), matching
`sign-in-page.test.tsx`'s `/api/v1/auth/sign-in` key, not the bare `/links` shorthand used
elsewhere in this plan for readability. Because the key is path-only, a `GET /api/v1/links`
stub also answers any later `POST`/`PUT` to that same path in the same test — harmless for
the assertions below, which only inspect the request each mutation sent, never the response
it got back.

Extend or create `dashboard-page.test.tsx`: using `stubFetch` and `renderWithProviders`,
stub `/api/v1/links` with two links and assert both render as `<a>` with `target="_blank"`,
`rel="noopener noreferrer"`, and an accessible name containing "opens in a new tab".

Create `admin-links-page.test.tsx`: stub `/api/v1/links` with three links; click "Move
{title} down" on the first; assert a `PUT /api/v1/links/order` call was made with the
swapped id array as the JSON body. Find it with
`calls.find((call) => call.path === '/api/v1/links/order')` — model this on
`sign-in-page.test.tsx:25-33`'s `calls.find(...)` pattern, not `calls.at(-1)`: each
mutation's `onSuccess` invalidates the links query, which immediately refetches
`GET /api/v1/links` on the same path, so the *last* call in the array is not reliably the
mutation you just triggered. Parse the found call with
`JSON.parse(String(call?.init?.body)).linkIds`. Also: submitting the add-link form issues a
`POST /api/v1/links` call (found the same way) with the trimmed field values.

**Verify**: `npm test` (in `src/Homon.Web`) → all pass, including both new/extended files.

### Step 8: Playwright

Create `src/Homon.Web/e2e/links.spec.ts`: seed a link through the authenticated API
(`request.post('/api/v1/links', { data: {...} })`, using whatever storage-state mechanism
`admin.spec.ts`/`auth.setup.ts` already establish for the admin project), navigate to
`/`, and assert the rendered `<a>` for that link has `target="_blank"` and
`rel="noopener noreferrer"`. Clean up in `afterEach` with `request.delete('/api/v1/links/
{id}', { data: {} })` — the e2e database is shared across the whole run, matching plan
002's own `dashboard-groups.spec.ts` pattern (check whether that file landed first and
reuse its seeding helper instead of hand-rolling this, if so). Extend
`layout.spec.ts:28-34`'s section-heading assertion only if plan 002 hasn't already
covered the Links heading there.

**Verify**: `./ci/run-ci.sh e2e` → exit 0, including `links.spec.ts` at both viewports.

## Test plan

- **xunit, pure**: `LinkOrderingTests` — permutation renumbering, missing/extra/duplicate
  id rejection. Model: `ApiKeyRulesTests.cs`.
- **xunit, database-backed**: `LinkEndpointTests` — CRUD happy paths, validation (empty
  fields, dangerous/wrong schemes, over-length fields), 404s, order acceptance/rejection,
  the full auth matrix. Model: `AuthenticationEndpointTests.cs` for fixture/`TestClient`
  usage, `MetaEndpointTests.cs:91-101` for the no-database reader-switch case.
- **Vitest**: `dashboard-page.test.tsx` (link rendering, `target`/`rel`, accessible name)
  and `admin-links-page.test.tsx` (move buttons send the correct `PUT /links/order` body;
  the form posts trimmed fields). Model: `sign-in-page.test.tsx` for
  `renderWithProviders` + `stubFetch` + form-submission assertions.
- **Playwright**: `e2e/links.spec.ts` — seeds via the authenticated API, asserts rendered
  `target`/`rel`, cleans up in `afterEach`.
- **Verification**: `./ci/run-ci.sh` → `PASS — web api e2e`, 0 skipped api tests.

## Done criteria

- [ ] `dotnet build Homon.sln -c Release` exits 0 with no warnings.
- [ ] `./ci/run-ci.sh api` exits 0, 0 skipped; `LinkOrderingTests`/`LinkEndpointTests` pass.
- [ ] `./ci/run-ci.sh web` exits 0; the two new/extended Vitest files pass.
- [ ] `./ci/run-ci.sh e2e` exits 0; `links.spec.ts` passes at both viewports.
- [ ] `grep -n "MapLinkEndpoints" src/Homon.Api/Program.cs` shows an active call, not a comment.
- [ ] `grep -rn "SortOrder" src/Homon.Domain/Links/README.md` returns no matches.
- [ ] Manually: `dotnet run --project src/Homon.Api -- migrate` applies `AddLinks`
      cleanly against a fresh database; add/edit/reorder/delete a link at `/admin/links`;
      the dashboard's Links section reflects every change without a manual refresh.
- [ ] `./ci/run-ci.sh` → `PASS — web api e2e`.
- [ ] `git status` shows no changes outside the Scope section's files and new test files.

## STOP conditions

- `Program.cs` no longer has a commented `v1.MapLinkEndpoints();` in the shape quoted
  above — someone already wired it, or the block was restructured differently.
- `dashboard-page.tsx` no longer has a top-level `<section aria-labelledby=
  "links-heading">` — plan 002's restructure changed the contract this plan depends on.
- A migration named or matching `AddLinks` already exists — don't overwrite it, pick a
  different name and ask.
- `HomonDbContext.cs` already declares `DbSet<Link>` — diff before assuming Step 2 is
  still needed.
- Any auth-matrix assumption above turns out wrong against the actual `HomonPolicies`
  behaviour (e.g. `ApiKey` somehow admitting an Administrator-policy route) — that is a
  security contradiction, not a detail to paper over.
- A step's verification command fails twice after a reasonable fix attempt.

## Maintenance notes

- **`ProbeId`** will need a nullable FK to `Probe` and a migration once a later plan wants
  a link attached to a service card (`docs/MODULES.md:13`) — additive, no change to
  `GET /links`'s existing fields.
- **The admin page's shared-form-at-the-bottom pattern** is a reasonable phase-0 shape,
  not yet a component. If Pages or Backups (plans 007–008) want the same "list with
  inline edit and up/down reordering" shape, extract it once a third instance exists —
  not before, per "modules, not layers" (`docs/ARCHITECTURE.md` §2).
- **A reviewer should scrutinize**: the URL scheme allow-list (a gap there is stored
  XSS, since the SPA renders `href` verbatim into a real anchor), and that `DELETE`'s
  content-type check actually matches `SignOutAsync`'s — a silent drop reopens the CSRF
  gap it exists to close.
- **Plan 012** styles this module like every other; only the `aria-label` "opens in a new
  tab" hint may later move to a visually-hidden span — the accessible name it produces
  must not change when that happens.
