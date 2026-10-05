# 007 — Pages and the WYSIWYG editor

> **Executor instructions**: Follow this plan step by step. Run every verification command
> and confirm the expected result before moving to the next step. If anything in "STOP
> conditions" occurs, stop and report — do not improvise. This plan does **not** ask you to
> update `plans/README.md`; the reviewer maintains that index for this run.
>
> **Drift check (run first)**: `git diff --stat f4e7261..HEAD -- src/Homon.Domain/Pages
> src/Homon.Infrastructure/Persistence src/Homon.Infrastructure/InfrastructureServiceCollectionExtensions.cs
> src/Homon.Api/Endpoints src/Homon.Api/Program.cs src/Homon.Web/src/pages/page-page.tsx
> src/Homon.Web/src/pages/admin-pages-page.tsx src/Homon.Web/src/pages/dashboard-page.tsx
> src/Homon.Web/src/pages/admin-home-page.tsx src/Homon.Web/e2e Directory.Packages.props
> src/Homon.Web/package.json docs/ARCHITECTURE.md docs/MODULES.md docs/design-brief.md`
> — several of these paths (`Program.cs`, `HomonDbContext.cs`, the migrations folder, `App.tsx`,
> `dashboard-page.tsx`, `admin-home-page.tsx`, `e2e/helpers.ts`, `Directory.Packages.props`,
> `package.json`/`package-lock.json`) are also touched by plans 002, 003 and 006, which this
> session runs first, in that order (004, 005, 008, 009 and 011 do **not** land before this
> plan runs — do not assume their shape). A diff there is *expected* — compare only the regions this
> plan itself edits (the commented `MapPageEndpoints()` line, the `Pages` route/lazy-import
> block, the Pages section of the dashboard, the `Page` model / `DbSet<Page>`) against the
> excerpts below; a change to an unrelated region (e.g. a new `ProbeEndpoints.cs` registration
> line) is not drift for this plan. On a mismatch in an in-scope region, treat it as a STOP
> condition.

## Status

- **Priority**: P2
- **Effort**: M
- **Risk**: MED (the sanitiser is the one security boundary this plan adds; get the allow-list wrong and the stored body is the vulnerability)
- **Depends on**: none (Pages is a self-contained module; it does not read probes, links or any other module's data)
- **Category**: direction
- **Planned at**: commit `f4e7261`, 2026-09-15
- **Reviewed**: 2026-09-15 (review-plan, execution order 013 → 002 → 003 → 006 → 007 → 010 →
  012)

## Context

`docs/MODULES.md`: "**Pages.** A light pages/articles feature with WYSIWYG editing for a few
explanation pages." The constraint on record: "**WYSIWYG.** Candidate: TipTap. Whatever is
chosen, sanitise on the server (the stored body is rendered verbatim) — an allow-list HTML
sanitiser in `Homon.Infrastructure/Pages/`." The slot README
(`src/Homon.Domain/Pages/README.md:13`) fixes the shape: `Page` (Id, Slug, Title, BodyHtml,
IsPublished, CreatedAt, UpdatedAt). The design brief's Page screen
(`docs/design-brief.md:49-53`) and Admin → Pages bullet (line 69: "slug, title, published
toggle, and a WYSIWYG editor") are the two screens this plan must satisfy; dashboard navigation
to a page is left to "the design's call" (line 53), decided below.

Because `BodyHtml` is rendered verbatim (`page-page.tsx` uses `dangerouslySetInnerHTML`), the
sanitiser is not an input-validation nicety — it is the entire defence against stored-XSS. An
administrator is the only writer today, but the module ships generically for self-hosters who
may add a second admin later (`docs/ARCHITECTURE.md:53-55`), and "trusted admin" is not a
reason to skip sanitising output another user's browser will execute.

## Decisions

- **Sanitiser: `HtmlSanitizer` (NuGet id `HtmlSanitizer`, namespace `Ganss.Xss`), MIT, version
  `9.2.1039`.** Verified read-only against the NuGet API
  (`.../htmlsanitizer/9.2.1039/htmlsanitizer.nuspec` → `<license type="expression">MIT</license>`,
  author Michael Ganss) — the package `docs/MODULES.md:77-78`'s constraint had in mind.
  *Rejected*: a bespoke allow-list walker over `AngleSharp` directly — reinvents a well-audited
  library's mXSS/malformed-attribute edge cases for no gain.
- **Editor: TipTap** (`@tiptap/react`, `@tiptap/pm`, `@tiptap/starter-kit`,
  `@tiptap/extension-link`, all `^3.31.3`, MIT — verified with `npm view <pkg> version license`).
  Compatible with the SPA's report-only CSP (`nginx.conf:37`): ProseMirror writes no `<script>`
  and calls no `eval`, so `script-src 'self'` is untouched; it does set inline `style="..."` on
  decoration nodes, which is exactly what `nginx.conf:32-36`'s own comment anticipated
  (`style-src 'self' 'unsafe-inline'` — "a WYSIWYG editor's rendered pages might [need it];
  revisit when the Pages module lands"). No nginx change needed. *Rejected*: Quill (weaker
  TypeScript story), a hand-built `contenteditable` (reinvents undo/paste-cleanup/lists — the
  parts a sanitiser needs fed *clean* markup, or the allow-lists drift).
- **Markdown storage — rejected.** WYSIWYG needs a render-time HTML conversion anyway (a second
  place to sanitise, or forget to). Storing sanitised HTML keeps one sanitise-on-write boundary.
- **Client-only sanitising (DOMPurify alone) — rejected.** `docs/MODULES.md:77-78`: the stored
  body is rendered verbatim to every future reader, not just the browser that wrote it.
- **Sanitising on output instead of on write — rejected.** Repeats a CPU cost on every read, and
  a future second render path (export, an alert email quoting a page) could forget to sanitise.
- **Reject vs. strip a disallowed element — strip, silently.** Create/update responses return
  the *sanitised* `bodyHtml`, so the SPA re-renders the editor from what was actually stored — a
  disallowed `<div>` from a paste loses the div, not the save. Rejecting would force the admin to
  hunt for the offending element; stripping degrades gracefully.
- **Images: absolute `https://` URLs only; no uploads.** The brief allows "maybe an image"
  (`docs/design-brief.md:52`) but this plan adds no file storage. A non-`https://` `src`
  (relative, `http://`, `data:`, `blob:`, `javascript:`) strips the element — a relative `src`
  can't be trusted without the app's own origin at sanitise time, and `data:`/`blob:` are classic
  XSS vectors. Upload support is a follow-up.
- **Link targets: forced, not admin-controlled.** Relative links open in the same tab; absolute
  links (`http:`, `https:`, `mailto:`) get `target="_blank" rel="noopener noreferrer"` forced
  during sanitising — `rel`/`target` are not on the allow-list, so TipTap cannot produce them and
  the sanitiser is the only place either is ever set (mirrors the Links rule, `docs/MODULES.md:32`).
- **Slug uniqueness via a plain unique index**, not the `NormalizedName` pattern
  `plans/002-monitoring-core-groups-and-ping.md:24` uses for `ProbeGroup.Name` — a slug is
  already canonical after validation (one regex, lower-case), unlike a free-text name. Race
  handling matches plan 002's: catch Postgres `23505`, turn it into the same validation problem.
- **Body size cap is a 400 validation problem, not a 413.** Minimal APIs bind the JSON body
  before a handler runs; enforcing a cap at the Kestrel level
  (`IHttpMaxRequestBodySizeFeature`) is fiddly to unit-test and duplicates nginx's
  `client_max_body_size 8m` (`nginx.conf:57`) at a coarser grain. A plain length check on
  `request.BodyHtml.Length` is one `if`, testable with `[DatabaseFact]`, and uses the same
  `ValidationProblem` shape as every other field. Cap: 200,000 UTF-16 chars (~200 KB), checked
  once on the raw string (cheap, before sanitising) and again on the sanitised result (a
  formality — sanitising only shrinks or preserves length).

## Defaults taken (change them before implementation if wanted)

- **No reserved slugs.** The admin editor keys its edit/create routes on the page's `Guid` id
  (`/admin/pages/:id`, `/admin/pages/new`), not the slug, so a slug never collides with an admin
  route — `/pages/{slug}` is the only reader route under that prefix.
- **Renaming a slug is allowed; there are no redirects.** A bookmark to the old slug 404s after
  a rename — the simplest default for "a light pages feature"; a redirect table is scope creep.
- **Where readers find pages**: a new `<section aria-labelledby="pages-heading">` on the
  dashboard, placed **after** the Links section (today at `dashboard-page.tsx:18-21`, before
  002 and 006 land — by the time this plan runs, 002 has restructured the file and 006 has
  filled in the section's content; locate it by `<section aria-labelledby="links-heading">`,
  not by the line number above, which will have moved), titled "Pages", **hidden
  entirely** when there are zero *published* pages — unlike Services/Links, which always show
  an empty-state sentence because they're core sections. Pages are optional; a fresh install
  showing "No pages yet" under a heading nobody asked for is more clutter than signal.
- **`GET /pages/{slug}` returns 200 to a signed-in Administrator viewing an unpublished page**
  (a preview at the real URL) and **404 to everyone else** for the same page — indistinguishable
  from a slug that doesn't exist, so a reader can't detect a draft exists.
- **Draft pages are listed at `GET /admin/pages`, not via a query flag on `GET /pages`.** Keeps
  the reader route's response shape and policy simple.
- **The editor toolbar covers**: Bold, Italic, Strikethrough, Code, Heading 2, Heading 3,
  Heading 4, Bullet list, Numbered list, Blockquote, Link, Horizontal rule, Undo, Redo — one
  real `<button>` each, matching the extension list and the sanitiser's allow-list exactly (see
  Maintenance notes). No image button — uploads are out of scope, and there is deliberately no
  raw-HTML escape hatch, so no image ships until upload support adds an "Insert image" action.
- **`e2e/helpers.ts`'s `READER_ROUTES` entry `/pages/welcome`** (line 6) is kept as-is; a
  published page with slug `welcome` is seeded once, in `e2e/auth.setup.ts`, right after the
  admin session is established (Step 8). The seed is idempotent — checks `GET /admin/pages`
  first — because the CI database is shared across every spec in a run.

## Current state

- `src/Homon.Domain/Pages/README.md` — the module slot; not implemented. Full text: `Page`
  (Id, Slug, Title, BodyHtml, IsPublished, CreatedAt, UpdatedAt); admin CRUD; readers at
  `/pages/{slug}`; sanitise on the way in.
- `src/Homon.Domain/Auth/ApiKey.cs` — Domain entity exemplar: `public sealed class`,
  `public const int` length limits as XML-documented fields, plain `{ get; set; }` properties,
  no EF attributes. Model `Page.cs` on it.
- `src/Homon.Infrastructure/Persistence/Configurations/ApiKeyConfiguration.cs` — the
  `IEntityTypeConfiguration<T>` exemplar: `ToTable`, `HasKey`, `HasMaxLength`+`IsRequired`, one
  `HasIndex(...).IsUnique()`. Model `PageConfiguration.cs` on it.
- `HomonDbContext.cs:22` — `public DbSet<ApiKey> ApiKeys => Set<ApiKey>();` is the only `DbSet`
  today; add `Pages` beside it. `OnModelCreating` (lines 24-28) already calls
  `ApplyConfigurationsFromAssembly(...)`, so a new configuration needs no other wiring.
- `InfrastructureServiceCollectionExtensions.cs` — `AddHomonInfrastructure` opens with plan
  013's `services.AddSingleton(TimeProvider.System);` (reuse it; do not register `TimeProvider`
  again) and then calls one private `AddXxx` extension method per module landed so far — by the
  time this plan runs that includes at least `AddHomonDatabase`/`AddHomonEmail`/
  `AddAdministrator` (Phase 0) and `AddHomonMonitoring` (002, extended by 003) — in sequence;
  add one more, `AddPages`, alongside whatever is already there.
- `Program.cs:476-479` — the commented module-registration block. **`v1.MapPageEndpoints();`
  shares one physical line (478) with two other calls**, not a line of its own:
  `//   v1.MapPageEndpoints();    v1.MapBackupEndpoints();   v1.MapApiKeyEndpoints();`.
  Uncommenting "the line" naively would also activate `MapBackupEndpoints`/`MapApiKeyEndpoints`,
  which do not exist as classes yet (008 hasn't landed) — that is a build break, not a style
  slip. Step 4 spells out the exact split.
- `Endpoints/AuthenticationEndpoints.cs` — endpoint-file exemplar: `internal static class
  XxxEndpoints` with `MapXxxEndpoints(this RouteGroupBuilder)`, `TypedResults`, a
  `ProblemHttpResult`-returning helper for a canned failure (`SignInFailure()`, lines 178-183).
  Its content-type check for a mutating bodyless endpoint (`SignOutAsync`, lines 149-165) is
  not needed for `DeletePage` — HTML forms cannot issue `DELETE` at all.
- `Authentication/HomonPolicies.cs` — `Reader`, `Administrator`, `ApiKey` policy constants;
  every endpoint group in this plan gates on one of these via `.RequireAuthorization(...)`.
- `Directory.Packages.props` — CPM; every `PackageReference` omits `Version`. Add one
  `PackageVersion` for `HtmlSanitizer`. Its transitive dependency `AngleSharp` needs **no**
  explicit entry — `CentralPackageTransitivePinningEnabled` (line 10) only requires one for
  packages a `.csproj` references directly.
- `App.tsx:16,44` — `AdminPagesPage` is already lazily imported and routed at `admin/pages`;
  `App.tsx:29` already routes `pages/:slug` to `PagePage` (eager, like `DashboardPage`). Add
  the editor route(s) (`admin/pages/new`, `admin/pages/:id`) as further lazy imports, kept out
  of the reader bundle either way.
- `pages/page-page.tsx` — 17-line placeholder (`<h1>{slug}</h1><p>Pages are not implemented
  yet.</p>`). Replace with the real fetch-and-render.
- `pages/admin-pages-page.tsx` — 12-line placeholder. Replace with a list-with-add/edit/delete
  page. By the time this plan runs, 002 (`admin-probe-groups-page.tsx`) and 006
  (`admin-links-page.tsx`) have already landed admin CRUD pages — read both before writing this
  one, and prefer matching their conventions (per-row actions named after the row, delete
  confirms inline, no unsaved state beyond the open form) where they still apply. Pages
  deliberately does **not** copy Links' "one shared form at the bottom of the list" layout: a
  TipTap editor needs far more room than a title/URL/description form, which is why Step 7
  gives the editor its own routes (`/admin/pages/new`, `/admin/pages/:id`) instead — keep it
  unstyled and semantic: an ordered list with real per-row actions, a create form, no unsaved
  state held beyond the form itself.
- `lib/api.ts` — `apiFetch<T>`, `ApiError`, `problemDetail()`. `lib/pages.ts` uses these,
  following `lib/meta.ts`'s `useQuery`/query-key pattern.
- `lib/use-document-title.ts` — `useDocumentTitle(pageTitle(...))`; keep using it.
- `src/test/fetch.ts` — `stubFetch(routes)`, the only fetch-stubbing helper (no MSW, per
  `CLAUDE.md`). All new Vitest files use this, not a real network call.
- `e2e/helpers.ts:4-8,11-17` — `READER_ROUTES`/`ADMIN_ROUTES` already list a `pages` entry; add
  `admin/pages/new` to `ADMIN_ROUTES` (Step 9) so the overflow check covers the editor.
- `nginx.conf:30-37` — the CSP; `style-src 'self' 'unsafe-inline'` already anticipates the
  editor (comment at lines 32-36). No change to this file in this plan.
- `tests/Homon.Api.Tests/ApiDatabaseFactory.cs`, `DatabaseFactAttribute.cs` — database-backed
  fixtures; `[DatabaseFact]` skips itself without `HOMON_TEST_CONNECTION`, and `ci/run-ci.sh
  api` fails on any skip. `PageEndpointTests` uses `ApiDatabaseFactory`, following
  `MetaEndpointTests.cs`'s `ConfiguredFactory` pattern for the `RequireSignInForReaders` case.
- No code in the repository catches `PostgresException`/`23505` yet as of this plan's writing
  (`grep -rn "PostgresException\|23505" src tests` is empty), but plan 002 (probe-group names,
  `plans/002-monitoring-core-groups-and-ping.md:78`) lands before this plan runs and is expected
  to add this handling for `ProbeGroup.Name`'s unique index — 006's `Link` has no unique
  constraint, so it introduces no such helper. Re-run the grep when you reach Step 4: if 002
  shipped a reusable helper, call it with the same name and shape rather than writing a second
  one. Only write it from scratch if the grep still comes back empty.

## Commands you will need

| Purpose | Command | Expected on success |
|---|---|---|
| Full gate | `./ci/run-ci.sh` | `PASS — web api e2e` |
| Web only | `./ci/run-ci.sh web` | exit 0 |
| API only | `./ci/run-ci.sh api` | exit 0, 0 skipped tests |
| e2e only | `./ci/run-ci.sh e2e` | exit 0 |
| Add the NuGet package version | edit `Directory.Packages.props` by hand (see Step 3) | — |
| Reference it from a project | `dotnet add src/Homon.Infrastructure/Homon.Infrastructure.csproj package HtmlSanitizer` | adds a `PackageReference` with no `Version` (CPM); if it writes one, delete the attribute |
| Build | `dotnet build Homon.sln -c Release` | exit 0 (the formatting gate — `IDE0055` is an error) |
| Create the migration | `HOMON_DESIGNTIME_CONNECTION='Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x' dotnet dotnet-ef migrations add AddPages --project src/Homon.Infrastructure --startup-project src/Homon.Api --output-dir Persistence/Migrations` | new `Migrations/<timestamp>_AddPages.cs` + updated `HomonDbContextModelSnapshot.cs` |
| Install a new npm dependency (updates the lockfile) | `cd src/Homon.Web && npm install @tiptap/react@^3.31.3 @tiptap/pm@^3.31.3 @tiptap/starter-kit@^3.31.3 @tiptap/extension-link@^3.31.3` | `package.json` and `package-lock.json` both change; run this **once**, by hand — the gate itself runs `npm ci`, which only *installs* from an existing lockfile and fails if the lockfile is stale, so `npm ci` is never how a dependency is added |
| .NET tests only | `dotnet test tests/Homon.Api.Tests/Homon.Api.Tests.csproj --filter "FullyQualifiedName~Page"` | all pass |
| Vitest only | `cd src/Homon.Web && npm test -- pages` | all pass |

## Scope

**In scope:**
- `src/Homon.Domain/Pages/Page.cs` (new)
- `src/Homon.Infrastructure/Pages/PageHtmlSanitizer.cs` (new), and its `IPageHtmlSanitizer` interface
- `src/Homon.Infrastructure/Persistence/Configurations/PageConfiguration.cs` (new)
- `src/Homon.Infrastructure/Persistence/HomonDbContext.cs` (add `DbSet<Page>`)
- `src/Homon.Infrastructure/InfrastructureServiceCollectionExtensions.cs` (add `AddPages`)
- `src/Homon.Infrastructure/Persistence/Migrations/*_AddPages.*` + snapshot (new migration)
- `src/Homon.Api/Endpoints/PageEndpoints.cs` (new)
- `src/Homon.Api/Program.cs` (uncomment `v1.MapPageEndpoints();` only)
- `src/Homon.Web/src/lib/pages.ts` (new)
- `src/Homon.Web/src/pages/page-page.tsx` (replace placeholder)
- `src/Homon.Web/src/pages/admin-pages-page.tsx` (replace placeholder)
- `src/Homon.Web/src/pages/admin-page-editor-page.tsx` (new — create/edit form + TipTap)
- `src/Homon.Web/src/pages/dashboard-page.tsx` (add the Pages section only)
- `src/Homon.Web/src/App.tsx` (add the editor route(s))
- `src/Homon.Web/e2e/helpers.ts` (`ADMIN_ROUTES` gains `admin/pages/new`)
- `src/Homon.Web/e2e/auth.setup.ts` (seed the `welcome` page)
- `src/Homon.Web/e2e/pages.spec.ts` (new)
- `tests/Homon.Api.Tests/PageHtmlSanitizerTests.cs`, `PageEndpointTests.cs` (new)
- `src/Homon.Web/src/pages/page-page.test.tsx`, `admin-page-editor-page.test.tsx` (new)
- `src/Homon.Web/src/App.test.tsx` (extend, only if Step 8's stub-route check finds it needs one)
- `Directory.Packages.props`, `src/Homon.Web/package.json`, `package-lock.json`
- `docs/ARCHITECTURE.md` (new section — see Step 10 for the actual number), `docs/MODULES.md`
  (007 row), `docs/design-brief.md` (Pages section note), `src/Homon.Domain/Pages/README.md`
  (fill in)

**Not in scope, and not edited by this plan's executor**: `plans/README.md` — the reviewer
maintains the index for this run (see the executor-instructions banner above); do not touch
it, including the 007 status row.

**Out of scope, and why:**
- **Image uploads.** Only remote `https://` image embedding is supported; a file-storage
  feature is a module of its own (upload endpoint, storage backend, size/type limits on binary
  data — a different risk surface from HTML sanitising). Record as a follow-up.
- **Revision history / page versioning.** Not in the brief or the slot README; "a light pages
  feature" does not ask for it.
- **Slug redirects on rename.** Decided against above; revisit only if an administrator asks.
- **CalDAV-style external content sources, search, or a table of contents.** Not asked for.
- **Enforcing the CSP** (report-only → enforced). `nginx.conf`'s CSP stays report-only; this
  plan only confirms TipTap is compatible with the policy as written, it does not flip it.
- **`Homon.Web/nginx.conf` changes.** None needed (see Decisions).
- **Any other module's endpoints, migrations, or SPA routes** (Probes, Links, Backups, etc.) —
  touch only the shared files named in "Current state" above, and only the lines this plan owns.

## Git workflow

- No new branch: commit on the worktree's current branch, in place. Do not create or switch
  branches, and do not merge or push — the reviewer running `./ci/run-ci.sh` afterwards owns
  that decision.
- Commit per logical step (or a small group of adjacent steps) rather than one giant commit at
  the end, so a STOP partway through still leaves a readable history. Match the repository's
  observed style (`git log --oneline`): `<Category>: <imperative summary> (plan 007)` — e.g.
  `Pages: add the domain model and the server-side HTML sanitiser (plan 007)`,
  `Pages: wire the reader route and the TipTap admin editor (plan 007)`.
- Do not touch `plans/README.md` at all — the reviewer maintains it for this run (see the
  executor-instructions banner above).

## Steps

### Step 1 — Domain: `Page.cs`

Create `src/Homon.Domain/Pages/Page.cs`:

```csharp
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
```

**Verify**: `dotnet build src/Homon.Domain/Homon.Domain.csproj` → exit 0.

### Step 2 — Infrastructure: the sanitiser

Add the package version to `Directory.Packages.props`, inside a new `ItemGroup Label="Pages"`,
beside the existing `Label="Email"` group:

```xml
<ItemGroup Label="Pages">
  <!--
    The security boundary for the Pages module: BodyHtml is rendered verbatim
    (dangerouslySetInnerHTML in page-page.tsx), so this allow-list sanitiser — not the TipTap
    editor client-side — is what makes that safe. MIT, mganss/HtmlSanitizer. Its extension
    list (client, admin-page-editor-page.tsx) and this sanitiser's allow-list
    (PageHtmlSanitizer.cs) must change together — see Maintenance notes.
  -->
  <PackageVersion Include="HtmlSanitizer" Version="9.2.1039" />
</ItemGroup>
```

Reference it from `Homon.Infrastructure.csproj` (`dotnet add ... package HtmlSanitizer`, then
confirm no `Version` attribute was written — CPM forbids one).

Create `src/Homon.Infrastructure/Pages/PageHtmlSanitizer.cs`:

```csharp
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
```

Register it in `InfrastructureServiceCollectionExtensions.cs`, following the `AddAdministrator`
pattern (a new private static method called from `AddHomonInfrastructure`):

```csharp
services.AddPages();
// ...
private static void AddPages(this IServiceCollection services) =>
    services.AddSingleton<IPageHtmlSanitizer, PageHtmlSanitizer>();
```

(Singleton: the sanitiser holds only immutable configuration set once in its constructor.)

**Verify**: `dotnet build Homon.sln -c Release` → exit 0.

### Step 3 — Persistence: `PageConfiguration.cs`, `DbSet<Page>`, migration

Create `src/Homon.Infrastructure/Persistence/Configurations/PageConfiguration.cs`, modelled on
`ApiKeyConfiguration.cs`:

```csharp
using Homon.Domain.Pages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homon.Infrastructure.Persistence.Configurations;

internal sealed class PageConfiguration : IEntityTypeConfiguration<Page>
{
    public void Configure(EntityTypeBuilder<Page> builder)
    {
        builder.ToTable("Pages");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Slug)
            .HasMaxLength(Page.SlugMaxLength)
            .IsRequired();

        // Already canonical after validation (lower-case kebab, one regex) — unlike
        // ProbeGroup.Name (plans/002), no separate NormalizedSlug column is needed.
        builder.HasIndex(p => p.Slug)
            .IsUnique();

        builder.Property(p => p.Title)
            .HasMaxLength(Page.TitleMaxLength)
            .IsRequired();

        // No HasMaxLength: Postgres' text and varchar(n) perform identically, and the
        // BodyHtmlMaxLength cap is an API-boundary rule (Page.cs), not a storage one.
        builder.Property(p => p.BodyHtml)
            .IsRequired();
    }
}
```

Add to `HomonDbContext.cs`, beside `ApiKeys`:

```csharp
public DbSet<Page> Pages => Set<Page>();
```

Create the migration with the exact command from `CLAUDE.md`:

```bash
HOMON_DESIGNTIME_CONNECTION='Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x' \
  dotnet dotnet-ef migrations add AddPages \
  --project src/Homon.Infrastructure --startup-project src/Homon.Api \
  --output-dir Persistence/Migrations
```

**Verify**: the migration file creates one `Pages` table with a unique index on `Slug`;
`dotnet build Homon.sln -c Release` → exit 0. Do **not** apply it here — `migrate` is a CLI verb
(`docs/ARCHITECTURE.md` §3.6), run by the test fixtures and by `ci/run-ci.sh`, never by hand
against a shared database.

### Step 4 — API: `PageEndpoints.cs`

Create `src/Homon.Api/Endpoints/PageEndpoints.cs`: `internal static class PageEndpoints` with
`MapPageEndpoints(this RouteGroupBuilder parent)`, guarded with
`ArgumentNullException.ThrowIfNull(parent);` as its first line (the public-entry-point
convention `CLAUDE.md` names, and the same guard `MetaEndpoints.MapMetaEndpoints` and
`AuthenticationEndpoints.MapAuthenticationEndpoints` already carry — match them). Route table:

| Route | Policy | Body → result |
|---|---|---|
| `GET /pages` | Reader | → `[{slug, title}]`, published only, ordered by `Title` |
| `GET /admin/pages` | Administrator | → `[{id, slug, title, isPublished, updatedAt}]`, all pages, ordered by `UpdatedAt` desc |
| `GET /pages/{slug}` | Reader | published → 200 `{id, slug, title, bodyHtml, isPublished, createdAt, updatedAt}`; unpublished and caller is Administrator → 200 (preview); unpublished and caller is not → 404; unknown slug → 404 |
| `POST /pages` | Administrator | `{slug, title, bodyHtml, isPublished}` → 201, `Location: /api/v1/pages/{slug}`, body is the stored (sanitised) page |
| `PUT /pages/{id:guid}` | Administrator | same body → 200, sanitised page; 404 if id unknown |
| `DELETE /pages/{id:guid}` | Administrator | → 204, or 404 |

Validation (`TypedResults.ValidationProblem`, matching plan 002's shape):
- **Slug**: lower-cased server-side before validating; must match `Page.SlugPattern`; length ≤
  `Page.SlugMaxLength`; empty after trim → invalid. Uniqueness: check first (excluding self on
  `PUT`), and catch a Postgres `23505` unique violation on save and turn it into the same
  problem (a race between two concurrent creates of the same slug) — model this on the intended
  shape from `plans/002-monitoring-core-groups-and-ping.md:78`. Plan 002 lands before this plan
  runs (this session's execution order is 002 → 003 → 006 → 007), so its helper — whatever it
  ended up named — should already exist; call it rather than writing a second one.
- **Title**: non-empty after trim, ≤ `Page.TitleMaxLength`.
- **BodyHtml**: raw length ≤ 2 × `Page.BodyHtmlMaxLength` (cheap pre-check, rejects an absurd
  payload before sanitising it) → sanitise with `IPageHtmlSanitizer` → sanitised length ≤
  `Page.BodyHtmlMaxLength` (the real cap; the pre-check is defence, not the rule).
- **IsPublished**: plain bool, defaults to `false` when omitted.

`GET /pages/{slug}` needs the caller's role without requiring it (`Reader` admits everyone when
`RequireSignInForReaders` is off) — inject `ClaimsPrincipal` and check
`user.IsInRole(HomonRoles.Administrator)` the same way `AuthenticationEndpoints.GetSession`
(lines 128-147) reads the principal, to decide whether to show an unpublished page.

Register in `Program.cs`. By the time this plan runs, 002 and 006 have already uncommented
their own calls (former neighbours on the same commented lines), but 008 has not, so the line
that reads `v1.MapPageEndpoints();` is still sharing one physical line with two calls that must
stay commented — confirm the live text with `grep -n "MapPageEndpoints" src/Homon.Api/Program.cs`
before editing, since 002/006 landing first may have shifted the line number. Split it rather
than uncommenting the whole line, e.g. from:

```csharp
//   v1.MapPageEndpoints();    v1.MapBackupEndpoints();   v1.MapApiKeyEndpoints();
```

to:

```csharp
v1.MapPageEndpoints();
//   v1.MapBackupEndpoints();   v1.MapApiKeyEndpoints();
```

Leave every other commented call alone — `MapBackupEndpoints`/`MapApiKeyEndpoints` (008) and
`MapWeatherEndpoints`/`MapCalendarEndpoints` (010/011) name classes that do not exist yet;
uncommenting one is a build break, not a style slip.

**Verify**: `dotnet build Homon.sln -c Release` → exit 0 (this alone catches most XML-doc/OpenAPI
comment omissions, since `EnforceCodeStyleInBuild` is on).

### Step 5 — SPA: `lib/pages.ts`

Create `src/Homon.Web/src/lib/pages.ts` following `lib/meta.ts`'s `useQuery` pattern and
`lib/api.ts`'s `apiFetch`:
- Types mirroring the wire shapes above (`PageSummary`, `AdminPageSummary`, `Page`).
- `usePublishedPages()` → `GET /pages`.
- `usePage(slug)` → `GET /pages/{slug}`, `enabled: !!slug`.
- `useAdminPages()` → `GET /admin/pages`.
- `useAdminPage(id)` → derived from `useAdminPages()` or a dedicated fetch — either is fine, no
  `GET /pages/{id}` exists, so the editor either fetches by slug (if known) or filters the admin
  list; simplest is to have the editor's "edit" link carry the page's `slug` in addition to
  `id` and reuse `usePage(slug)`, then submit `PUT /pages/{id}`.
- `useCreatePage()`, `useUpdatePage()`, `useDeletePage()` — mutations, each invalidating the
  published-pages query key and the admin-pages query key (and, on update, the single-page key)
  after success, matching the "every mutation invalidates" rule in
  `plans/002-monitoring-core-groups-and-ping.md:105`.

**Verify**: `cd src/Homon.Web && npx tsc -b` → exit 0.

### Step 6 — SPA: reader page

Replace `src/Homon.Web/src/pages/page-page.tsx`:

```tsx
import { useParams } from 'react-router'

import { problemDetail } from '@/lib/api'
import { usePage } from '@/lib/pages'
import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'

/** `/pages/:slug` — an administrator-written page. */
export function PagePage() {
  const { slug } = useParams()
  const { data: page, error } = usePage(slug)

  useDocumentTitle(pageTitle(page?.title ?? slug ?? 'Page'))

  if (error) {
    return (
      <>
        <h1>Page not found</h1>
        <p>{problemDetail(error) ?? 'This page does not exist.'}</p>
      </>
    )
  }

  if (!page) {
    return null
  }

  return (
    <>
      <h1>{page.title}</h1>
      {/*
        Safe only because the server sanitised bodyHtml before it was stored
        (Homon.Infrastructure/Pages/PageHtmlSanitizer.cs) — the client never sanitises, and
        must not be trusted to. Do not render any other field this way.
      */}
      <div dangerouslySetInnerHTML={{ __html: page.bodyHtml }} />
    </>
  )
}
```

**Verify**: `npm test -- page-page` (new `page-page.test.tsx`, Step 11) → passes.

### Step 7 — SPA: admin list + editor

Replace `admin-pages-page.tsx` with a list (via `useAdminPages()`): rows with "Edit {title}"
(→ `/admin/pages/{id}`) and "Delete {title}" (confirms inline, matching plan 002's "Delete
{name} … confirms inline" convention), a "Published"/"Draft" word per row, and a "New page"
link (→ `/admin/pages/new`).

Create `admin-page-editor-page.tsx` (new lazy route(s) in `App.tsx`, admin bundle only):
- Fields: **Slug** (auto-suggested from Title until the admin hand-edits it — track a
  `slugTouched` boolean, set on first manual edit), **Title**, **Published** (checkbox), and the
  TipTap **Body** editor.
- TipTap setup:
  ```tsx
  import { useEditor, EditorContent } from '@tiptap/react'
  import StarterKit from '@tiptap/starter-kit'
  import Link from '@tiptap/extension-link'

  const editor = useEditor({
    extensions: [
      StarterKit.configure({ heading: { levels: [2, 3, 4] } }),
      Link.configure({ openOnClick: false, autolink: false }),
    ],
    content: page?.bodyHtml ?? '',
    editorProps: { attributes: { role: 'textbox', 'aria-label': 'Body' } },
  })
  ```
  `StarterKit`'s default set (paragraph, bold, italic, strike, code, code block, blockquote,
  lists, horizontal rule, hard break, undo/redo) matches the sanitiser's tag allow-list
  one-for-one once headings are capped at 2–4; `Link` is added separately (not in StarterKit).
  No `Image` extension (no upload support — see Scope).
- Toolbar: one real `<button type="button">` per action listed in "Defaults taken", each
  calling the matching `editor.chain().focus().toggleBold().run()`-style command, with an
  `aria-label` matching the action name exactly. No `className` (plan 012 owns style).
- On submit: `useCreatePage()`/`useUpdatePage()` with `{ slug, title, bodyHtml:
  editor.getHTML(), isPublished }`; on success, `editor.commands.setContent(...)` the
  *response*'s sanitised `bodyHtml`, so a stripped element is visibly gone, not silently different.

**Verify**: `npm test -- admin-page-editor-page` (Step 11) → passes; toolbar buttons found by
`getByRole('button', { name: 'Bold' })` etc.

### Step 8 — SPA: dashboard Pages section, e2e seed

In `dashboard-page.tsx`, add (after the existing Links section) a section that renders only
when `usePublishedPages()` returns a non-empty list. By this point 006 has already imported
`Link` from `react-router` into this file for the Links section — reuse that import; do not add
a second one.

**Check `src/Homon.Web/src/App.test.tsx` before moving on.** It renders `<App />` at `/` with
`stubFetch(anonymous)`, and `stubFetch` throws on any path it wasn't told about
(`src/test/fetch.ts:19-21`). By the time this plan runs, 002 and 006 have each added their own
dashboard query (status/probes, links) and may already have extended this stub — if they have
not, or if this plan's `usePublishedPages()` call is the one that first breaks it, add
`'/api/v1/pages': { body: [] }` (or whatever `lib/pages.ts` actually calls the route) to the
`anonymous` object at the top of the file so the existing tests keep passing. Verify with
`npm --prefix src/Homon.Web run test -- App`.

```tsx
{pages && pages.length > 0 && (
  <section aria-labelledby="pages-heading">
    <h2 id="pages-heading">Pages</h2>
    <ul>
      {pages.map((p) => (
        <li key={p.slug}>
          <Link to={`/pages/${p.slug}`}>{p.title}</Link>
        </li>
      ))}
    </ul>
  </section>
)}
```

In `e2e/auth.setup.ts`, after the existing sign-in block, seed the `welcome` page
idempotently (using the page's own authenticated `request` context, which shares cookies with
the just-established browser session):

```ts
const existing = await page.request.get('/api/v1/admin/pages')
const already = (await existing.json()).some((p: { slug: string }) => p.slug === 'welcome')

if (!already) {
  await page.request.post('/api/v1/pages', {
    data: {
      slug: 'welcome',
      title: 'Welcome',
      bodyHtml: '<p>This is a seeded page for the end-to-end suite.</p>',
      isPublished: true,
    },
  })
}
```

(`page.request` vs. a separate `APIRequestContext` — either is fine as long as it carries the
storage state's cookie.)

**Verify**: `npx playwright test auth.setup` → the `welcome` page exists in the CI database
afterwards; re-running it does not create a duplicate.

### Step 9 — SPA: routes, `ADMIN_ROUTES`

Add `admin/pages/new` (and, if a fixed id is easy to seed for the overflow check, `admin/pages/:id` —
otherwise the `new` route alone is enough coverage) to `ADMIN_ROUTES` in `e2e/helpers.ts`, so
`layout.spec.ts`'s viewport sweep covers the editor.

**Verify**: `npx playwright test layout.spec` → passes at both viewports, including the new route.

### Step 10 — Docs

- `src/Homon.Domain/Pages/README.md`: replace "Not implemented yet. Plan 007." with the shipped
  shape, the sanitiser's location, and the endpoint table (no landed module README exists yet
  to copy — keep it terse).
- `docs/MODULES.md`: extend the 007 row's Summary cell only if it undersells what shipped; add
  an "Added after the brief" note only on a real deviation (plan 002's pattern,
  `plans/002-monitoring-core-groups-and-ping.md:129-131`), not speculatively.
- `docs/design-brief.md`: under "Page (`/pages/{slug}`)", record that dashboard navigation to
  pages is a "Pages" section after Links, hidden with zero published pages (resolves "the
  design's call" at line 53). Leave "Not drawn yet" (lines 272-278) as-is — this plan supplies
  semantics, plan 012 supplies pixels.
- `docs/ARCHITECTURE.md`: add a new numbered section. **Check the current highest `### 3.N`
  first** (`grep -n "^### 3\." docs/ARCHITECTURE.md`) — §3.12 at planning time. Plan 013 (runs
  first, before 002) reserves §3.13; plan 002 reserves §3.14–§3.16 (probe groups, scheduler
  shape, uptime); plan 003 reserves §3.17 (the secret protector) — all four land before this
  plan runs, so by the time this step executes the file likely already ends at §3.17 — use
  whatever number the grep actually reports as free, not a number hard-coded here. 006 adds no
  new section (see its plan's Decisions), so 007 is very likely §3.18, but verify rather than
  assume. Title: "Pages are sanitised on write, not on read — the stored body is trusted
  only because of that". Content: the sanitiser choice, the allow-list, the rejected
  alternatives (Markdown storage, client-only sanitising, sanitising on output) — see Decisions;
  keep it to §3.7/§3.11's length.
Do not edit `plans/README.md` — the reviewer maintains it for this run.

**Verify**: `git diff --stat -- docs/` shows only the intended files changed.

## Test plan

### xunit — `tests/Homon.Api.Tests/`

**`PageHtmlSanitizerTests.cs`** (pure, no database), table-driven (`[Theory]`), an explicit XSS
corpus:

| Input | Expected |
|---|---|
| `<script>alert(1)</script>` | stripped entirely |
| `<p onclick="alert(1)">hi</p>` | `<p>hi</p>` |
| `<a href="javascript:alert(1)">x</a>` | `href` stripped (scheme not allowed) |
| `<a href="JaVaScRiPt:alert(1)">x</a>` | same, case-insensitively |
| `<a href="&#106;avascript:alert(1)">x</a>` | same, entity-encoded |
| `<img src=x onerror=alert(1)>` | element removed (both: `onerror` not an allowed attribute, and `src` is not `https://`) |
| `<svg onload=alert(1)>` | stripped (tag not on the allow-list) |
| `<p style="background:url(javascript:alert(1))">x</p>` | `style` attribute stripped entirely (not on the allow-list; `AllowedCssProperties` is also cleared) |
| `<iframe src="https://evil.example"></iframe>` | stripped entirely, no child content kept |
| `<img src="data:text/html;base64,...">` | element removed (not `https://`) |
| `<a href="https://example.com">x</a>` | kept; gains `target="_blank" rel="noopener noreferrer"` |
| `<a href="/pages/other">x</a>` | kept; **no** `target`/`rel` added (relative) |
| `<img src="https://example.com/a.png" alt="a">` | kept, `alt` preserved |
| a legitimate `<h2>`/`<ul><li>`/`<blockquote>`/`<pre><code>` document | passes through unchanged |
| a disallowed `<h1>` | stripped (editor never produces one; sanitiser still must not trust that) |

**`PageEndpointTests.cs`** (`ApiDatabaseFactory`, `[DatabaseFact]`):
- create returns the sanitised body, not the raw submitted body, when the two differ.
- slug validation: empty, too long, invalid characters (uppercase, spaces, stray hyphens), and
  a duplicate → 400; a genuine race (two near-simultaneous creates of the same slug) → the
  second gets the same 400 shape via the caught `23505`, not a 500.
- `GET /pages` lists only published pages; `GET /admin/pages` lists all.
- `GET /pages/{slug}` on an unpublished page: 404 anonymous, 404 as an unauthenticated Reader,
  200 as Administrator.
- title too long, body over `BodyHtmlMaxLength` (post-sanitisation) → 400.
- `DELETE` an unknown id → 404; a known id → 204, then a subsequent `GET` 404s.
- auth matrix: anonymous write → 401; API-key write → 403 (`HomonPolicies.Administrator` never
  admits a key); anonymous `GET /pages` → 200; with `Auth:RequireSignInForReaders=true`
  (`ConfiguredFactory` pattern from `MetaEndpointTests.cs`), anonymous `GET /pages` → 401.

### Vitest

- **`page-page.test.tsx`**: renders the fetched title and body (via `stubFetch`); an unknown
  slug renders "Page not found" from the problem's `detail`.
- **`admin-page-editor-page.test.tsx`**: every toolbar button exists by name; the Body editor
  region has an accessible name "Body"; the slug field auto-fills from the title until touched.
- **`dashboard-page.test.tsx`** (extend whatever plan 002/006 added by the time this runs): a
  "Pages" region appears only when `usePublishedPages()` returns entries.

### Playwright — `e2e/pages.spec.ts` (new)

- Signed in as administrator: create a page through the UI (auto-suggested slug, title, a
  couple of toolbar actions), leave Published unchecked, save; assert it's **not** on the
  dashboard's Pages section, and anonymous visits to `/pages/{slug}` get "Page not found".
- Toggle Published, save; assert the dashboard's Pages section lists it, and `/pages/{slug}`
  shows the content anonymously (`test.use({ storageState: { cookies: [], origins: [] } })`,
  matching `admin.spec.ts`'s anonymous block).
- `expectNoHorizontalOverflow` and `expectNoOverlap` at both viewports on `/pages/{slug}`.
- Cleanup: delete the created page in `afterEach` via the UI (the database is shared across
  specs in a CI run).

**Verify**: `./ci/run-ci.sh` → `PASS — web api e2e`.

## Done criteria

- [ ] `dotnet build Homon.sln -c Release` exits 0 (the formatting/style gate).
- [ ] `./ci/run-ci.sh api` exits 0 with **zero** skipped tests, including the new
      `PageHtmlSanitizerTests` and `PageEndpointTests`.
- [ ] `./ci/run-ci.sh web` exits 0; new Vitest files pass.
- [ ] `./ci/run-ci.sh e2e` exits 0; `pages.spec.ts` and the extended `layout.spec.ts` pass at
      both viewports.
- [ ] `grep -rn "dangerouslySetInnerHTML" src/Homon.Web/src` returns exactly one match, in
      `page-page.tsx`, with the justification comment directly above it.
- [ ] `grep -n "MapPageEndpoints" src/Homon.Api/Program.cs` shows the call uncommented and every
      other module's call still commented.
- [ ] The XSS corpus table in Test plan is fully represented as `[Theory]` cases — no case
      silently dropped or merged.
- [ ] No files outside "Scope → In scope" are modified (`git status`) — `plans/README.md` is
      not one of them; the reviewer updates it after merging.
- [ ] `./ci/run-ci.sh` (all three) → `PASS — web api e2e`.

## STOP conditions

Stop and report back (do not improvise) if:
- TipTap's rendered DOM needs `'unsafe-inline'` on `script-src` or any `eval`-family API under
  the CSP as written — this plan's compatibility read is based on ProseMirror's documented
  behaviour, not a live run against the report-only header; a `script-src` console violation is
  a materially different finding than the `style-src` one already accounted for.
- The `HtmlSanitizer` package's licence has changed from MIT since this plan was written, or a
  newer stable version materially changes the `AllowedTags`/`AllowedAttributes`/
  `PostProcessNode` API shape the code above assumes.
- Plan 002's Postgres-23505-to-validation-problem helper (it has landed by the time this plan
  runs) turns out to be shaped so differently from the sketch in "Current state" that Step 4
  cannot call it directly — reconcile with the real helper before writing a second one; only
  stop if reconciling would mean redesigning 002's helper rather than adapting this plan's call
  to it.
- A step's verification fails twice after a reasonable fix attempt.
- The excerpts in "Current state" don't match the live code (the drift check already covers
  this — treat any mismatch found there the same way).

## Maintenance notes

- **The TipTap extension list and the sanitiser's allow-list move together.** Adding a TipTap
  extension without the matching tag/attribute in `PageHtmlSanitizer.cs` means the editor
  produces markup the server silently strips on save. Removing an item from the sanitiser
  without removing the extension is the dangerous direction: a control whose effect quietly
  vanishes on save. Change both in the same commit, and update `PageHtmlSanitizerTests.cs`.
- **Image uploads are the obvious next request.** When they land, `ForcePolicy`'s `IMG` case
  needs to allow the upload storage's own origin/path prefix, and the size cap conversation
  moves from `BodyHtml` character count to binary bytes stored server-side.
- **A reviewer should scrutinize**: the `ForcePolicy` post-process step (the one place this plan
  hand-writes sanitising logic beyond the library's own allow-list — a bug there is a security
  bug, not a cosmetic one), and the XSS corpus's coverage against the actual TipTap output once
  Step 7 lands (the corpus targets a *hostile* API caller, not the editor — the endpoint can't
  tell the two apart, and must not trust either).
