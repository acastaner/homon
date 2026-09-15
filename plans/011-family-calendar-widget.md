# 011 — Family calendar widget

> **Executor instructions**: Follow this plan step by step. Run every verification command
> and confirm the expected result before moving on. If anything in "STOP conditions" occurs,
> stop and report — do not improvise. When done, update this plan's row in `plans/README.md`
> unless a reviewer told you they maintain the index.
>
> **First step**: read "Contract assumed from plan 003" and confirm 003 landed with that
> shape. **STOP if `ISecretProtector`/`DataProtectionSecretProtector` does not exist in any
> form** — do not re-derive the protector here; a second implementation under a different
> purpose string would make a `dataprotection-keys` export cover only some of Homon's secrets.
>
> **Drift check (run first)**:
> `git diff --stat f4e7261..HEAD -- src/Homon.Domain/Calendar src/Homon.Infrastructure/Calendar src/Homon.Infrastructure/Persistence src/Homon.Api/Endpoints/CalendarEndpoints.cs src/Homon.Api/Program.cs src/Homon.Web/src/pages/dashboard-page.tsx src/Homon.Web/src/pages/admin-home-page.tsx src/Homon.Web/src/App.tsx src/Homon.Web/e2e/helpers.ts docs/ARCHITECTURE.md docs/MODULES.md docs/design-brief.md`
> Plans 002–010 land first and legitimately touch shared files — `Program.cs`'s endpoint
> list, `HomonDbContext.cs`, the migrations folder, `dashboard-page.tsx` (002 restructures
> it into grouped sections; 006/010 each add one). Seeing unrelated lines added by earlier
> plans is **not** a STOP condition; compare only the regions named in "Current state" /
> "Scope". If any named anchor changed shape rather than just gained neighbours, STOP.

## Status

- **Priority**: P2 · **Effort**: L
- **Risk**: MED — a new NuGet dependency doing real recurrence expansion (RRULE, EXDATE,
  RECURRENCE-ID, VTIMEZONE), and a second "the URL itself is the secret" shape for
  `ISecretProtector`. Lower than it would otherwise be: the cache (Decision 5) reuses plan
  010's `WeatherCache` shape rather than inventing a new one.
- **Depends on**: `plans/003-http-probe.md` (the secret protector — see below). Also reads
  `plans/010-weather-widget.md`'s cache design (Decision 5) — 010 executes first (numeric
  order) and is the precedent, not a hard blocking dependency: if 010 has not landed, build
  `CalendarCache` from Decision 5's description directly instead of reading `WeatherCache.cs`.
- **Category**: direction · **Planned at**: commit `f4e7261`, 2026-09-15

## Context

`docs/MODULES.md:18`: *"Several ICS sources merged, colour per source."*
`docs/MODULES.md:45` (verbatim brief): *"**Family calendar** widget with several sources in
one view."* `docs/MODULES.md:83-84`:

> **Calendar.** ICS URLs first (public or private links from Google/Apple/Nextcloud); CalDAV
> later. Credentials, when needed, are encrypted like probe secrets.

The slot README, `src/Homon.Domain/Calendar/README.md`, verbatim:

> **Shape.** `CalendarSource` (Id, Name, Colour, Kind, Url, Credentials?) and a server-side
> cache of fetched events. First phase reads public/private **ICS URLs**; CalDAV is a later
> kind.
>
> **Where the rest lands.** `Homon.Infrastructure/Calendar/` (fetchers behind an
> `ICalendarSourceReader`), `Homon.Api/Endpoints/CalendarEndpoints.cs`, a `calendar` widget
> on the dashboard. Credentials, when a source needs them, are encrypted the way probe
> secrets are.

`docs/ARCHITECTURE.md:176-180` (§4) still lists "the weather and calendar providers"
undecided — this plan settles and records the calendar half.

`docs/design-brief.md:43`: *"the coming days, several sources merged, one colour per
source."* Lines 158-166 fix the palette to exactly three validated tokens (`source-1` blue,
`source-2` amber, `source-3` violet): *"a fourth source needs a validated fourth colour,
never a generated hue; until then sources beyond three reuse the order and the legend's
text carries identity."* Lines 261-263: *"The section label with a legend at its right: 10px
swatch, 2px radius, source name in `muted`. Rows: the day in mono `muted` in a 76px column
(64px on phone), then events stacked, each a swatch, the title, and the time in mono
`muted`."*

The artboards (`docs/design/dashboard/Main.dc.html:219-230`, mirrored in
`BoardPhone.dc.html:188-199`) head the section **"This week"**, not "Calendar" — a legend of
source names with swatches, then day rows ("Mon 15" … "Thu 18"), each event a swatch, a
title, and either a 24-hour `HH:MM` or the literal word `"all day"`.
`BoardEmpty.dc.html:70-73` draws the empty state: "No calendars yet" / "The administrator
adds calendar feeds in the installation's configuration; the coming days appear here." —
written before the admin CRUD this plan builds existed; Decision 8 updates that sentence,
the way plan 006 updated `docs/design-brief.md:255`'s `rel` value once behaviour settled.

Nothing under `Homon.Domain/Calendar/`, `Homon.Infrastructure/Calendar/` or
`Homon.Api/Endpoints/CalendarEndpoints.cs` exists yet beyond the README above.

## Contract assumed from plan 003

Not implemented here — only consumed, and only this:

| Assumed | Shape | Where |
| --- | --- | --- |
| `ISecretProtector` | `Protect(string)`/`Unprotect(string)`. | `Homon.Infrastructure/Security/ISecretProtector.cs` |
| `DataProtectionSecretProtector` | Purpose `"Homon.Secrets.v1"` — the one purpose every probe/calendar secret uses. `AddSingleton<ISecretProtector, DataProtectionSecretProtector>()`. | `Homon.Infrastructure/Security/DataProtectionSecretProtector.cs` |
| Write-only wire semantics | A secret never round-trips: responses carry a boolean flag, never the value. On write: absent → keep; `""` → clear; non-empty → `Protect()` and replace. | plan 003, Decision 2 |
| Key-ring loss | `Unprotect()` throws `CryptographicException` when the key ring can't decrypt. Callers catch it and turn it into a typed failure, never crash a background loop or request. | plan 003, Decision 2 |

This plan reuses `ISecretProtector` for two things 003 doesn't do itself: the calendar
source's whole **URL** (Decision 6) and an *optional* Nextcloud-style basic-auth password
(Decision 7). Both still go through `Protect()`/`Unprotect()` under the existing purpose
string — use 003's real names if it diverged. **If 003 has not landed, STOP.**

## Decisions

**1. Colour is derived from `Position`, never stored.** The brief fixes exactly three
validated tokens and a fourth colour is "never a generated hue" (`docs/design-brief.md:162-166`)
— so `CalendarSource` carries no `Colour` column, despite the README naming one. A pure
function, `CalendarColour.SlotFor(int position) => (position % 3) + 1`, maps an admin-ordered
source to `1`/`2`/`3` (the `source-N` token plan 012 binds it to); a fourth-plus source
cycles back to slot 1, matching "sources beyond three reuse the order." Rejected: a stored
`ColourSlot` column — it would need renumbering on every reorder/delete, duplicating what
`Position` already carries, and could disagree with it. Step 1 updates the README's
`Colour` line.

**2. ICS parsing: `Ical.Net` 5.2.3.** MIT (verified against its nuspec:
`<license type="expression">MIT</license>`), the standard .NET RFC 5545 library — correct
RRULE/EXDATE/RECURRENCE-ID/VTIMEZONE handling is not worth hand-rolling, the same reasoning
`docs/MODULES.md` gives for the SMB/SNMP libraries. Its only dependency, `NodaTime` 3.2.2
(Apache-2.0, also nuspec-verified), must be pinned explicitly too —
`CentralPackageTransitivePinningEnabled` (`Directory.Packages.props:10`) requires every
transitive version pinned. Step 2 adds both, with a licence comment (the same courtesy
`docs/MODULES.md` gives `SMBLibrary`).

**3. Household time zone: `Calendar:TimeZone`, validated, default `"UTC"`.** "The coming
days" needs a definition of "today"; a new `CalendarOptions` (modelled on `EmailOptions.cs:6-38`)
exposes `string TimeZone { get; set; } = "UTC"`, bound `.ValidateOnStart()` with
`.Validate(o => TimeZoneInfo.TryFindSystemTimeZoneById(o.TimeZone, out _), "Calendar:TimeZone
is not a recognised time zone id.")` — the same cross-field `.Validate(...)` shape as
`AddHomonEmail` (`InfrastructureServiceCollectionExtensions.cs:76-80`). `UTC` needs no
tzdata anywhere; a real IANA id does — see Decision 9. Rejected: a per-source zone — a
VTIMEZONE inside each ICS source already carries that event's own zone; only "which day is
today" needs one household-wide answer.

**4. All-day events, cancellations, minimal fields.** `Ical.Net` exposes `IsAllDay`; an
all-day (or multi-day) event is emitted once per spanned day within the window, `allDay:
true`, matching `"all day"` (`Main.dc.html:229`). An event with `STATUS:CANCELLED`, or a
cancelled `RECURRENCE-ID` override, is dropped before merging. The wire event carries only
`sourceId`, `title`, `start`, `end`, `allDay` — the artboard's own row draws only a swatch, a
title, a time (`docs/design-brief.md:261-263`); `DESCRIPTION` is never read into the
response, even if a future plan adds `location` — an ICS description is exactly the private
detail a shared-screen dashboard must not print. Blank `SUMMARY` → `"Untitled event"`.

**5. Cache: `CalendarCache`, shaped exactly like plan 010's `WeatherCache` — pull-based,
`FreshFor`/`StaleTolerance`/single-flight, no background service.** Weather solved the same
problem (a server-side cache in front of a slow, rate-limited outside answer, read by an
anonymous dashboard) one plan earlier; reusing its shape rather than inventing a polling
`BackgroundService` here is both less new machinery and consistent terminology across the
two widgets. `CalendarCache` (singleton, no interface — data, not a swappable transport, the
same reasoning `WeatherCache` uses) holds one `CalendarSnapshot?` per source (`Ical.Net.Calendar
Calendar, DateTimeOffset FetchedAt, string? ETag, string? LastModified`), each source guarded
by its own `SemaphoreSlim(1,1)` for single-flight (a small `ConcurrentDictionary<Guid,
SemaphoreSlim>` of per-source locks — `WeatherCache` needs only one lock for its one
location; this cache needs one per source). Rules, identical in spirit to `WeatherCache`'s:
- **Fresh for 15 min** (`FreshFor`) — inside that window `GetSnapshotsAsync` never calls the reader.
- **Stale-while-error, up to 6 h** (`StaleTolerance`) — matching `WeatherCache`'s own values
  for consistency across both widgets: past `FreshFor` but the fetch fails, the last snapshot
  is still served (`stale: true` on that source) if younger than `StaleTolerance`; older than
  that, or no snapshot ever succeeded, the source is `unavailable: true`.
- **Single-flight per source**: concurrent callers past `FreshFor` for the same source share
  its semaphore; the first through calls `ICalendarSourceReader.FetchAsync` and the rest
  re-check the refreshed snapshot immediately after acquiring the lock, exactly like
  `WeatherCache` does, instead of each fetching independently.
- **Invalidated explicitly** by `POST /{id}/refresh` and by `PUT`/`DELETE
  /calendar/sources/{id}` when the URL or credential changes — a changed address must never
  keep serving the old one's events inside `FreshFor`.
- Stale sources within one `GET /calendar` are fetched concurrently (`Task.WhenAll`, one
  `Task` per source needing a refresh), not sequentially, so one request's total latency is
  bounded by the slowest single source, not the sum of all of them — a handful of family
  calendars fetched one after another at up to 15s each would otherwise make the dashboard
  itself feel like the thing that's down.

`GET /calendar` calls `CalendarCache.GetSnapshotsAsync(sources, ct)`, which fetches
whatever is stale (per the rules above), and then expands every returned snapshot into the
window through a pure static `CalendarWindowBuilder.BuildDays(sources-with-snapshots,
TimeProvider, TimeZoneInfo, days)` — expansion happens at read time, not at fetch time, so a
reader opening the dashboard just after midnight always sees *today's* window rather than
whatever "today" was when the snapshot was last fetched; a 7-day expansion from a raw
calendar is cheap, and this is what makes `CalendarWindowBuilder` itself testable with an
injected `TimeProvider` and no HTTP. Events within a day: all-day first, then start time,
then — for a stable tie-break — `(Source.Position, Title)`.

**6. Credentials: the URL is the secret.** Most ICS "private address" links (Google's
secret iCal address, a Nextcloud public-share link) embed a capability token in the URL —
the URL *is* the credential. `CalendarSource` stores `ProtectedUrl` (the whole URL,
`Protect()`-ed) and a separate plain `Host` (`scheme://host[:port]` only, derived once at
write time) so the admin list shows *which* source is configured without ever decrypting on
a read. The response carries `host`, never `url`. Unlike 003's optional secret, `url` is
*required* — write-only semantics differ slightly: **create** requires it; **update**
follows 003's absent/non-empty rule (absent → keep `ProtectedUrl`/`Host`; non-empty →
re-`Protect()` and re-derive `Host`); an explicit empty string on update is `400` (delete
the source instead of blanking it). `webcal://` normalises to `https://` at validation time,
before protecting, so every fetch already uses a scheme `HttpClient` understands.

**7. Optional basic auth for Nextcloud — included, not deferred.** `CalendarSource` gets
`CredentialType` (`None`/`Basic`), `CredentialUsername` (plain, mirrors
`HttpCredential.Username`), `CredentialProtectedSecret` (encrypted, same purpose string).
`credential.secret` follows 003's rule verbatim (absent keeps, `""` clears, non-empty
replaces; `type == "none"` always clears). Cheap to add now — a handful of nullable columns
— versus a later migration, and 003 already proved the pattern.

**8. Persistence: flat nullable columns, not an owned `.ToJson()` document.** Plan 003's
jsonb choice exists *because* `Probe` has four kinds with disjoint option sets. Here there is
one kind this phase (`Ics`; `CalDav` is a reserved future value, not a second shape today)
and five optional scalar credential fields — the "20+ mostly-null columns" problem 003
rejected doesn't apply at this size, and flat columns keep `CalendarSourceConfiguration` a
plain `IEntityTypeConfiguration<T>` like `ApiKeyConfiguration.cs`. Record this, citing 003's
Decision 1 and why this plan reaches the opposite conclusion at this shape.

**9. The fetch itself, and the two failure signals.** `IcsCalendarSourceReader.FetchAsync`
(called by `CalendarCache`, never by a background loop — Decision 5) sends
`If-None-Match`/`If-Modified-Since` from the previous snapshot when known, caps the body at
`MaxBodyBytes = 5 * 1024 * 1024` (mirrors `HttpProbeRunner.MaxBodyBytes`), and times out
after `FetchTimeout = TimeSpan.FromSeconds(15)` (longer than probes' 10s — Nextcloud/Google
aren't LAN-local), enforced with a linked `CancellationTokenSource`, matching
`ResendEmailSender.cs:44-45`. A 304 keeps the cached calendar, bumps only `FetchedAt`. On
every result — success, 304, or failure — `CalendarSource.LastFetchedAt`/`LastError`
persist to the row, so the admin list reflects reality even though the parsed calendar
itself is cache-only (Decision 5's "stays in memory only" — a restart costs at most one
`FreshFor` window of the calendar looking momentarily stale on first read, never a wrong
one; record the rejected alternative, persisting occurrences to the database, in
`CalendarSource.cs`, citing the same reasoning `Homon.Domain/Weather/README.md`'s cache
note implies for its sibling). `Unprotect()` throwing `CryptographicException` is caught at
the fetch site exactly like 003's runner: `LastError = "credentials unreadable — re-enter
them"`, never a crash.

`GET /calendar`'s `sources[]` carries two signals, per source, from `CalendarCache`:
`unavailable` (no successful fetch ever, or the last success is older than
`StaleTolerance`) and `stale` (a prior fetch succeeded, the latest attempt failed, but the
snapshot is still within `StaleTolerance` and is what's being shown).

**10. Endpoints.** `GET /calendar` (`Reader`) returns:

```json
{
  "sources": [{ "id": "...", "name": "Family", "colourSlot": 1, "stale": false, "unavailable": false }],
  "days": [{ "date": "2026-09-15", "events": [{ "sourceId": "...", "title": "Piano lesson", "start": "2026-09-15T17:00:00+02:00", "end": "2026-09-15T17:45:00+02:00", "allDay": false }] }],
  "fetchedAt": "2026-09-15T12:00:03Z"
}
```

`fetchedAt` is when *this response* was computed (per-source freshness is
`stale`/`unavailable`). `days` always has `CalendarEndpoints.WindowDays = 7` entries (today
+ six — "This week"), a constant this phase, the same call 003 made for its request
timeout. The handler itself is a thin `cache.GetSnapshotsAsync(sources, ct)` +
`CalendarWindowBuilder.BuildDays(...)` pair — identical in shape to `WeatherEndpoints`'
`GET ""` calling `cache.GetAsync(settings, ct)` —
so it may fetch live sources inline when they're stale (Decision 5); it never returns an
error for the whole response just because one source failed, unlike Weather's single-source
`503` — a bad Nextcloud login must not blank the other two calendars. Admin management is
`/calendar/sources`, `Administrator`-only: `GET`/`POST`/`PUT /{id:guid}`/`DELETE {id:guid}`
(DELETE requires `application/json` + `{}`, mirroring `SignOutAsync`), `PUT /order` (full id
list, model `Link.Reorder`), and `POST /{id:guid}/refresh` (`cache.Invalidate(id)` then one
fetch, updates cache + row, returns the refreshed row — avoids waiting up to `FreshFor`
after adding a source).

## Defaults taken

- `CalendarSource.Name` max length 100 (wider than `ProbeGroup.Name`'s 60 — no existing
  precedent fixes a number).
- `WindowDays = 7`; `CalendarCache.FreshFor = 15 min`, `StaleTolerance = 6 h` — matching
  `WeatherCache`'s own values (plan 010) for consistency across both widgets — constants,
  not exposed to the SPA or admin-configurable.
- Admin refresh endpoint included, not deferred.
- Migration name `AddCalendarSources`, additive only.
- Playwright does not stand up a live/stub ICS endpoint — see "Test plan".

## Current state

- `src/Homon.Api/Program.cs:219-226` — Data Protection block; unconditional provider
  registration per 003's Decision 3 (verify 003 landed that way).
- `src/Homon.Api/Program.cs:473-479` — endpoint list; line 479 already has the commented
  `v1.MapCalendarEndpoints();`.
- `src/Homon.Infrastructure/InfrastructureServiceCollectionExtensions.cs:28-41` —
  `AddHomonInfrastructure` composes `private static void AddXxx(...)` methods; add
  `AddCalendar` here, shaped like `AddHomonEmail` (lines 68-111).
- `src/Homon.Infrastructure/Email/ResendEmailSender.cs:14-60` and (once 003 lands)
  `HttpProbeRunner.cs` — the outbound-HTTP-with-timeout exemplars this reader follows.
- `plans/010-weather-widget.md`'s `WeatherCache` (`FreshFor`/`StaleTolerance`/single-flight,
  `services.TryAddSingleton(TimeProvider.System)`) — the exemplar `CalendarCache` copies
  (Decision 5). Once 010 lands, read the real `WeatherCache.cs`/`Homon.Infrastructure/Weather/`
  and reuse its `TimeProvider.System` registration rather than adding a second one.
- `.../Configurations/ApiKeyConfiguration.cs` — the flat `IEntityTypeConfiguration<T>` shape
  Decision 8 follows, not 003's owned-jsonb pattern.
- `.../Persistence/HomonDbContext.cs:19-29,9-12` — one `DbSet<T>` per aggregate; add
  `DbSet<CalendarSource> CalendarSources` and extend the class doc comment.
- `src/Homon.Domain/Links/Link.cs`'s `Reorder(...)` (plan 006) — the exemplar for
  `CalendarSource.Reorder`; if 006 hasn't landed, model `ProbeGroup.ReplaceMembers` instead
  (renumber `0..n-1`, never remove-and-re-add a tracked row).
- `src/Homon.Api/Endpoints/AuthenticationEndpoints.cs:13-19,149-165` — CSRF posture and the
  `SignOutAsync` content-type guard this plan's `DELETE` mirrors.
- `src/Homon.Api/Authentication/HomonPolicies.cs:17-26` — no new policy needed.
- `src/Homon.Web/src/lib/meta.ts` — the `lib/*.ts` shape `lib/calendar.ts` follows.
- `src/Homon.Web/src/test/fetch.ts:8-33` — `stubFetch`, the only network stub (no MSW).
- `src/Homon.Web/src/App.tsx:13-46` — lazy admin routes; **no calendar route exists yet**
  (unlike Links/Probes, pre-wired in Phase 0) — this plan adds the import, route, nav link
  and `e2e/helpers.ts` entry itself.
- `src/Homon.Web/src/pages/dashboard-page.tsx:1-24` — no calendar section yet; insert where
  `Main.dc.html:90,170,189,202,219` puts it (Services, Backups, Links, Weather, Calendar
  last) — check the actual order left by 002/006/010 before inserting.
- `src/Homon.Api/Dockerfile:40-44` — runtime stage; `mkdir -p /keys` runs before `USER
  $APP_UID` (line 44). Step 9 inserts `RUN apk add --no-cache tzdata` before that line.
- `Directory.Packages.props:1-52` — CPM; `Ical.Net`/`NodaTime` are new.
- `docs/ARCHITECTURE.md` ends at §3.12 today; §4 (lines 176-180) lists calendar providers
  undecided. **Grep the highest existing `### 3.N` and use the next free number** — do not
  assume a specific one.
- `plans/README.md:19` — `| 011 | planned | Family calendar widget |`.

## Commands you will need

| Purpose | Command | Expected on success |
| --- | --- | --- |
| Full gate | `./ci/run-ci.sh` | `PASS — web api e2e`, 0 skipped api tests |
| API only | `./ci/run-ci.sh api` | same, api suite |
| Web only | `./ci/run-ci.sh web` | `npm ci`, lint, build, vitest all pass |
| e2e only | `./ci/run-ci.sh e2e` | Playwright, two viewports |
| Release build (format gate) | `dotnet build Homon.sln --configuration Release` | 0 warnings/errors |
| New migration | `HOMON_DESIGNTIME_CONNECTION='Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x' dotnet dotnet-ef migrations add AddCalendarSources --project src/Homon.Infrastructure --startup-project src/Homon.Api --output-dir Persistence/Migrations` | migration files created |
| Apply locally | `dotnet run --project src/Homon.Api -- migrate` | `applied     : all of them.` |
| One xunit class | `dotnet test tests/Homon.Api.Tests --filter FullyQualifiedName~CalendarWindowBuilderTests` | all pass |
| One Vitest file | `npm --prefix src/Homon.Web run test -- dashboard-page` | all pass |
| Manual tzdata check | `docker build -f src/Homon.Api/Dockerfile -t homon-api-tz . && docker run --rm --entrypoint /bin/sh homon-api-tz -c "ls /usr/share/zoneinfo/Europe/Paris"` | file listed, not "No such file" |

## Scope

**In scope**:
- `src/Homon.Domain/Calendar/CalendarSource.cs`, `CalendarSourceKind.cs`,
  `CalendarCredentialType.cs`, `CalendarColour.cs`, `README.md` (create/edit)
- `src/Homon.Infrastructure/Calendar/ICalendarSourceReader.cs`, `IcsCalendarSourceReader.cs`,
  `CalendarSnapshot.cs`, `CalendarCache.cs`, `CalendarWindowBuilder.cs`, `CalendarOptions.cs`
  (create)
- `.../Persistence/Configurations/CalendarSourceConfiguration.cs` (create),
  `HomonDbContext.cs` (extend), the `AddCalendarSources` migration (generate)
- `InfrastructureServiceCollectionExtensions.cs` (extend)
- `src/Homon.Api/Endpoints/CalendarEndpoints.cs` (create); `Program.cs` (edit: map call;
  `TimeProvider.System` only if 010 hasn't already registered it); `Dockerfile` (edit:
  `tzdata`)
- `src/Homon.Web/src/lib/calendar.ts` (create); `dashboard-page.tsx` (extend, Calendar
  section only); `admin-calendar-page.tsx` (create); `App.tsx`, `admin-home-page.tsx`,
  `e2e/helpers.ts` (extend: new wiring, not already-done)
- `docs/ARCHITECTURE.md` (new §3.N), `docs/MODULES.md`, `docs/design-brief.md`
  (empty-state sentence only), `Directory.Packages.props`, `plans/README.md`
- Tests: `tests/Homon.Api.Tests/Fixtures/Calendar/*.ics` (5 files),
  `CalendarWindowBuilderTests.cs`, `IcsCalendarSourceReaderTests.cs`,
  `CalendarEndpointTests.cs`, `CalendarSourceOrderingTests.cs` (create);
  `dashboard-page.test.tsx` (extend), `admin-calendar-page.test.tsx` (create);
  `e2e/admin.spec.ts`, `layout.spec.ts` (extend if present)

**Out of scope**: CalDAV (reserved enum value only); per-event `location`; a configurable
window/interval exposed to the SPA; drag-and-drop reordering; alerting on
`stale`/`unavailable` (009's job); any `className`/styling, including the actual `source-N`
swatch colour (plan 012 — the legend is text-only this phase).

## Steps

### Step 1: Domain — `CalendarSource` and the slot README

Create `CalendarSourceKind.cs` (`enum { Ics }`, doc-commented that `CalDav` is reserved for
later, not added), `CalendarCredentialType.cs` (`enum { None, Basic }`), `CalendarColour.cs`
(`internal static class { internal static int SlotFor(int position) => (position % 3) + 1;
}`, Decision 1).

Create `CalendarSource.cs`, a mutable class modelled on `Auth/ApiKey.cs`'s shape and on
`Links/Link.cs`'s `Reorder` (or `ProbeGroup.ReplaceMembers` if 006 hasn't landed): `Guid Id`,
`string Name` (`const NameMaxLength = 100`), `int Position`, `CalendarSourceKind Kind`,
`string ProtectedUrl` (doc-commented: encrypted via `ISecretProtector`), `string Host`
(plain, display-only), `CalendarCredentialType CredentialType`, `string?
CredentialUsername`, `string? CredentialProtectedSecret`, `DateTimeOffset? LastFetchedAt`,
`string? LastError`, `DateTimeOffset CreatedAt`, `DateTimeOffset UpdatedAt`; a static
`Reorder(IReadOnlyList<CalendarSource>, IReadOnlyList<Guid>)` copying `Link.Reorder`'s exact
validation (throw `ArgumentException` unless an exact permutation; renumber `0..n-1`).

Update the README: replace `Colour`'s description per Decision 1; add the endpoint list
(`GET /calendar`; admin `GET/POST/PUT/DELETE /calendar/sources`, `PUT
/calendar/sources/order`, `POST /calendar/sources/{id}/refresh`); note CalDAV is reserved.

**Verify**: `dotnet build src/Homon.Domain -c Release` → 0 errors/warnings.

### Step 2: NuGet — `Ical.Net` and `NodaTime`

Add to `Directory.Packages.props`, a new `ItemGroup Label="Calendar"` after "Email":

```xml
<ItemGroup Label="Calendar">
  <!-- Ical.Net (MIT) does RFC 5545 recurrence expansion — not worth hand-rolling; see
       docs/MODULES.md's SMB/SNMP sections for the same reasoning. NodaTime (Apache-2.0) is
       its own dependency, pinned explicitly because CentralPackageTransitivePinningEnabled
       requires every transitive version pinned too. -->
  <PackageVersion Include="Ical.Net" Version="5.2.3" />
  <PackageVersion Include="NodaTime" Version="3.2.2" />
</ItemGroup>
```

Reference `Ical.Net` from `Homon.Infrastructure.csproj` (no version attribute — CPM supplies
it); `NodaTime` needs no direct reference. If `dotnet restore` resolves a different
`NodaTime` version, update the pin to match and say so in your summary.

**Verify**: `dotnet restore Homon.sln` → 0 errors; `dotnet build src/Homon.Infrastructure -c
Release` → 0 errors/warnings.

### Step 3: Infrastructure — options, reader, cache, window builder

`CalendarOptions.cs` modelled on `EmailOptions.cs`: `SectionName = "Calendar"`, `TimeZone =
"UTC"`. Register `AddCalendar` in `InfrastructureServiceCollectionExtensions.cs` (called
from `AddHomonInfrastructure`), shaped like `AddHomonEmail` (Decision 3's `.Validate(...)`).

`ICalendarSourceReader.cs` (`Task<CalendarFetchResult> FetchAsync(CalendarSource,
CalendarSnapshot? previous, CancellationToken)` — a new snapshot, a "not-modified, keep
previous" signal, or a typed failure with `Detail`) and `IcsCalendarSourceReader.cs`: named
`HttpClient`; `Unprotect()` the URL and, when `CredentialType == Basic`, the password, each
inside `try { } catch (CryptographicException)` → `Detail = "credentials unreadable —
re-enter them"`; send conditional headers from `previous`; cap the body at `MaxBodyBytes = 5
* 1024 * 1024`; enforce `FetchTimeout = TimeSpan.FromSeconds(15)` via a linked
`CancellationTokenSource`, modelled on `ResendEmailSender.cs:44-45`; parse with
`Ical.Net.Calendar.Load(stream)`, catching a parse failure into a typed `Detail` rather than
propagating.

`CalendarSnapshot.cs` (`record(Ical.Net.Calendar Calendar, DateTimeOffset FetchedAt, string?
ETag, string? LastModified)`); `CalendarCache.cs` per Decision 5: `FreshFor`/`StaleTolerance`
constants, a `ConcurrentDictionary<Guid, CalendarSnapshot>` plus a
`ConcurrentDictionary<Guid, SemaphoreSlim>` of per-source locks, `TimeProvider` injected
(reuse the registration from plan 010's `WeatherCache` if it landed first — see "Current
state" — else add `services.TryAddSingleton(TimeProvider.System)` here). Public surface:
`Task<IReadOnlyList<(CalendarSource, CalendarSnapshot?, bool Stale, bool Unavailable)>>
GetSnapshotsAsync(IReadOnlyList<CalendarSource> sources, CancellationToken)` — per source:
fresh snapshot within `FreshFor` → return as-is; otherwise acquire that source's semaphore,
re-check freshness immediately after (the single-flight re-check, exactly like
`WeatherCache`), and if still due, call `ICalendarSourceReader.FetchAsync`; fetches for
distinct sources run concurrently (`Task.WhenAll`). On success, replace the snapshot and
persist `LastFetchedAt`/clear `LastError` on the row; on failure, keep the old snapshot if
younger than `StaleTolerance` (`Stale = true`) else mark `Unavailable = true`, and persist
`LastError` either way. `Invalidate(Guid sourceId)` drops the snapshot outright (called by
`PUT`/`DELETE /calendar/sources/{id}` and `POST /{id}/refresh` — Decision 5).

`CalendarWindowBuilder.cs` (pure, static): `BuildDays(sources-with-snapshots, TimeProvider,
TimeZoneInfo, days)` → ordered `(DateOnly, events)` per Decision 4/5 (drop cancelled, expand
all-day spans onto every touched day, default blank titles, merge/sort per Decision 5) —
takes only the resolved snapshots `CalendarCache.GetSnapshotsAsync` already fetched; it does
no I/O and no locking itself, which is what makes it independently testable.

**Verify**: `dotnet build Homon.sln --configuration Release` → 0 errors/warnings.

### Step 4: Persistence — configuration, `DbContext`, migration

`CalendarSourceConfiguration.cs` modelled on `ApiKeyConfiguration.cs`: table
`"CalendarSources"`, `Name` required with its max length, `ProtectedUrl`/`Host` required,
credential fields optional, no unique index on `Position` (comment citing
`ProbeGroupConfiguration`'s non-deferrable-constraint reasoning). Include Decision 8's
rejected-alternative comment (003's owned-jsonb pattern, and why this differs).

Edit `HomonDbContext.cs`: `DbSet<CalendarSource> CalendarSources`; extend the class doc
comment (lines 9-12).

Generate: `HOMON_DESIGNTIME_CONNECTION='Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x'
dotnet dotnet-ef migrations add AddCalendarSources --project src/Homon.Infrastructure
--startup-project src/Homon.Api --output-dir Persistence/Migrations`.

**Verify**: the migration adds one `"CalendarSources"` table and touches nothing else;
`dotnet run --project src/Homon.Api -- migrate` → `applied: all of them.`; `dotnet build
Homon.sln -c Release` → 0 errors/warnings.

### Step 5: API — `CalendarEndpoints.cs`

`internal static class CalendarEndpoints`, `MapCalendarEndpoints(this RouteGroupBuilder)`,
modelled on `MetaEndpoints.cs` for style and `LinkEndpoints.cs` (once 006 lands, else
`AuthenticationEndpoints.cs`) for CSRF/`DELETE`:

| Route | Policy | Behaviour |
| --- | --- | --- |
| `GET /calendar` | Reader | `cache.GetSnapshotsAsync(sources, ct)` then `CalendarWindowBuilder.BuildDays(...)` — may fetch live sources inline when stale (Decision 5) |
| `GET /calendar/sources`, `GET /{id:guid}` | Administrator | Ordered `(Position, Id)` |
| `POST /calendar/sources` | Administrator | Validate; `Position` = max + 1; `201` |
| `PUT /{id:guid}` | Administrator | Validate; `404`/`200` |
| `DELETE /{id:guid}` | Administrator | `application/json` guard, mirrors `SignOutAsync`; `404`/`204` |
| `PUT /order` | Administrator | `{ sourceIds: Guid[] }` → `CalendarSource.Reorder`; `400` on non-permutation |
| `POST /{id:guid}/refresh` | Administrator | Inline fetch, updates cache + row, returns refreshed source; `404` |

`Validate(request, isCreate)`: `name` required, ≤ `NameMaxLength`; `url` required on create,
absent-keeps on update (Decision 6), and when present: normalise `webcal://` → `https://`,
`Uri.TryCreate` requiring `Scheme is "http" or "https"`, ≤ 2048 chars; `credential.type ==
"basic"` requires non-empty `username`; `credential.secret` follows 003's rule verbatim.

Wire types (XML-doc-commented): `CalendarSourceRequest(string? Name, string? Url,
CalendarCredentialRequest? Credential)`; `CalendarCredentialRequest(string Type, string?
Username, string? Secret)`; `ReorderCalendarSourcesRequest(Guid[] SourceIds)`;
`CalendarSourceResponse(Guid Id, string Name, string Kind, string Host, bool HasCredential,
DateTimeOffset? LastFetchedAt, string? LastError, DateTimeOffset CreatedAt, DateTimeOffset
UpdatedAt)` — no `url`, no protected secret, ever; the `GET /calendar` records per
Decision 10's JSON shape.

Edit `Program.cs:479`: replace the commented call with an active one; leave the rest of the
module list intact.

**Verify**: `dotnet build src/Homon.Api -c Release` → 0 errors; `grep -n
"ProtectedUrl\|CredentialProtectedSecret" src/Homon.Api/Endpoints/CalendarEndpoints.cs`
shows neither read into a response DTO.

### Step 6: xunit tests — fixtures, pure logic, reader, endpoints

`tests/Homon.Api.Tests/Fixtures/Calendar/`: `weekly-with-exdate-and-override.ics` (RRULE
weekly, one EXDATE, one RECURRENCE-ID override changing that instance's title),
`all-day-multi-day.ics` (a 3-day `VALUE=DATE` event), `dst-change.ics` (a `TZID` event whose
window crosses a real DST transition), `cancelled.ics` (`STATUS:CANCELLED`),
`malformed.ics` (truncated `VCALENDAR`).

`CalendarWindowBuilderTests.cs` (pure, model `ApiKeyRulesTests.cs`): load each fixture into
an `Ical.Net.Calendar`, wrap in a `CalendarSnapshot`, assert against an injected fake
`TimeProvider`: EXDATE instance absent, override title wins, multi-day all-day appears on
each spanned day, DST-crossing local time correct both sides, cancelled event never appears,
malformed input yields a caught typed failure (no unhandled exception), and cross-source
merge ordering.

`CalendarSourceOrderingTests.cs` (pure, model `LinkOrderingTests.cs`): permutation
renumbering; missing/extra/duplicate id → `ArgumentException`.

`IcsCalendarSourceReaderTests.cs`: reuse plan 003's `StubHttpMessageHandler` by name (do not
create a second one) scripting each fixture as a response. Cases: 200 → parsed entry; 304 →
previous entry unchanged; oversized body → capped failure, not an `OutOfMemoryException`; a
delayed response past an injected short timeout (never the real 15s) → timeout `Detail`;
`malformed.ics` → caught parse failure; a fake `ISecretProtector.Unprotect` throwing
`CryptographicException` for the URL, and separately the basic-auth secret → "credentials
unreadable — re-enter them", no exception escapes `FetchAsync`.

`CalendarEndpointTests.cs` (`ApiDatabaseFactory` + `[DatabaseFact]`, model
`AuthenticationEndpointTests.cs`): the URL never appears in any response body (assert on the
raw JSON string for every route); CRUD round-trip; `PUT` omitting `url` keeps `host`
unchanged; `PUT` with `url: ""` → `400`; `credential.secret` write-only rules exactly as
003's `ProbeEndpointTests`; zero-source `GET /calendar` shape (`sources: []`, 7 empty-event
days); full auth matrix — anonymous `GET /calendar` → `200`/`401` under
`RequireSignInForReaders` (`ConfiguredFactory` pattern, `MetaEndpointTests.cs:91-101`),
anonymous write → `401`, API-key write → `403` (`HomonPolicies.cs:22-26`).

**Verify**: `./ci/run-ci.sh api` → exit 0, 0 skipped, all new tests pass.

### Step 7: SPA data layer — `lib/calendar.ts`

Modelled on `lib/meta.ts`: `CalendarSourceSummary { id, name, colourSlot, stale,
unavailable }`, `CalendarEvent { sourceId, title, start, end, allDay }`, `CalendarDay {
date, events }`, `Calendar { sources, days, fetchedAt }`, `CALENDAR_QUERY_KEY`,
`useCalendar()`. Admin types (`CalendarSource`, `CalendarSourceFields`) and
`useCalendarSources`/`useCreateCalendarSource`/`useUpdateCalendarSource`/
`useDeleteCalendarSource`/`useReorderCalendarSources`/`useRefreshCalendarSource`, each
invalidating both query keys on success, following `lib/session.ts` (or `lib/links.ts`,
once 006 lands).

**Verify**: `npm run build` (in `src/Homon.Web`) → 0 errors.

### Step 8: SPA pages — dashboard section, admin page, wiring

`dashboard-page.tsx`: `<section aria-labelledby="calendar-heading"><h2
id="calendar-heading">This week</h2>…</section>` — "This week", matching `Main.dc.html:219`
literally (`docs/MODULES.md` calls the module "Calendar"; its heading is not — note this in
your summary). Position last, after Weather, per `Main.dc.html:90,170,189,202,219`. Legend:
a plain text list of `sources.map(s => s.name)` in order — no swatch yet (plan 012). Days:
an `<ol>` of `<li>` per day, `<time dateTime={day.date}>` showing "Mon 15" (no month), each
with a nested `<ul>` of events — title plus `<time dateTime={event.start}>` `HH:MM`, or the
literal text `"all day"`. Empty: "No calendars yet. An administrator adds them under Admin →
Calendar." with `<Link to="/admin/calendar">` — a deliberate departure from
`BoardEmpty.dc.html:72`'s "installation's configuration" wording (the real surface is CRUD);
update `docs/design-brief.md:261-263` with a short parenthetical recording it, as plan 006
does for its `rel="noopener noreferrer"` change.

`admin-calendar-page.tsx`: `<h1>Calendar</h1>`, modelled on `admin-links-page.tsx` (once 006
lands) or `admin-probes-page.tsx` otherwise: list with inline edit, up/down reorder, a
shared form. Fields: name, URL (`type="url"`, never pre-filled), a credential type selector
(None/Basic) with username/password and a "Replace credential" affordance like 003's, a
"Refresh now" button per row. One `<p>` of privacy help text: "Event titles are visible to
anyone who can reach this dashboard; readers are anonymous by default (see
docs/ARCHITECTURE.md §3.1)." No `className`.

Wire: lazy import + `/admin/calendar` route in `App.tsx` (model `AdminLinksPage`, lines
14-15,42-45); nav `<Link to="/admin/calendar">Calendar</Link>` in `admin-home-page.tsx`
(model lines 17-19,24-25); `{ path: '/admin/calendar', name: 'calendar' }` in
`e2e/helpers.ts`'s `ADMIN_ROUTES` (lines 11-17) — brings the overflow check with it.

**Verify**: `npm run build && npm run lint` (in `src/Homon.Web`) → 0 errors.

### Step 9: Docs, Dockerfile, package pin

`docs/ARCHITECTURE.md`: grep `### 3\.` for the highest number, add the next, titled
"Calendar sources: derived colour, URL-as-secret, cache of parsed calendars" — record
Decisions 1, 5, 6, 8. Update §4 (lines 176-180): drop "calendar providers" from undecided.

`docs/MODULES.md`'s 011 row: update only if its summary drifted (Decision 1 changes *how*
colour works, not the promise).

`src/Homon.Api/Dockerfile`: insert `RUN apk add --no-cache tzdata` in the runtime stage,
before `USER $APP_UID` (line 44) — Alpine ships no timezone database; without it
`TimeZoneInfo.FindSystemTimeZoneById` only resolves `UTC`. `ci/run-ci.sh` builds no image
(it uses `ci/compose.ci.yaml`, PostgreSQL only) so this can't be gate-verified — run the
manual tzdata check once by hand and record the result in your summary.

Update `plans/README.md`'s 011 row to `DONE` when finished.

**Verify**: `grep -n "^### 3\." docs/ARCHITECTURE.md | tail -1` shows the new section;
`grep -n "tzdata" src/Homon.Api/Dockerfile` shows the new line.

## Test plan

**xunit**: `CalendarWindowBuilderTests` (pure, injected `TimeProvider`, the five fixture
cases + merge ordering); `CalendarSourceOrderingTests` (pure); `IcsCalendarSourceReaderTests`
(stubbed handler, no live network); `CalendarEndpointTests` (`[DatabaseFact]`, URL-never-
leaks, CRUD, write-only rules, auth matrix). All per Step 6.

**Vitest**: extend `dashboard-page.test.tsx` — `stubFetch` a two-source, multi-day `GET
/calendar`; assert the legend lists both names, day rows render in order with `<time
dateTime>`, an all-day event reads "all day", a timed one reads `HH:MM`; a zero-source
response renders the empty state with a working `/admin/calendar` link. Create
`admin-calendar-page.test.tsx`: add-source form posts trimmed fields; URL never pre-filled
on edit; "Replace credential" only when `hasCredential`; reorder buttons send the correct
`PUT /calendar/sources/order` body.

**Playwright — deliberately no live network.** Standing up a real or stub ICS endpoint the
e2e stack could reach adds infrastructure this plan doesn't otherwise need, and recurrence
behaviour is already exhaustively covered by the xunit fixtures with zero network.
Playwright covers only what needs a real browser and session: extend `admin.spec.ts`'s
section loop to include "Calendar"; a new admin-CRUD-only test — create a source (URL can
point anywhere; it's never fetched in this test), confirm it lists with the right `host`,
delete it, confirm it's gone (no assertion on rendered events); confirm the dashboard's
empty-state sentence/link for an anonymous visitor with zero sources; `expectNoHorizontalOverflow`/
`expectTappable` on `/admin/calendar` at both viewports (automatic via `ADMIN_ROUTES`).

**Verification**: `./ci/run-ci.sh` → `PASS — web api e2e`, 0 skipped api tests.

## Done criteria

- [ ] `dotnet build Homon.sln --configuration Release` → 0 warnings/errors
- [ ] `dotnet run --project src/Homon.Api -- migrate` applies `AddCalendarSources` cleanly
- [ ] New xunit tests exist and pass: `CalendarWindowBuilderTests`,
      `CalendarSourceOrderingTests`, `IcsCalendarSourceReaderTests`, `CalendarEndpointTests`
- [ ] `npm --prefix src/Homon.Web run build` exits 0; new/extended Vitest tests pass
- [ ] `grep -rn "ProtectedUrl\|CredentialProtectedSecret" src/Homon.Api/Endpoints/CalendarEndpoints.cs`
      shows neither ever read into a response DTO
- [ ] `./ci/run-ci.sh` → `PASS — web api e2e`, 0 skipped api tests
- [ ] `docs/ARCHITECTURE.md` new §3.N; `docs/MODULES.md` and
      `src/Homon.Domain/Calendar/README.md` updated; `docs/design-brief.md`'s empty-state
      sentence updated with its departure note
- [ ] `src/Homon.Api/Dockerfile` has `apk add --no-cache tzdata`; manual tzdata check
      passes (recorded in your summary, not gate-verified)
- [ ] `plans/README.md`'s 011 row is `DONE`
- [ ] No files outside "Scope" modified (`git status`)

## STOP conditions

- Plan 003 has not landed, or `ISecretProtector`/`DataProtectionSecretProtector` doesn't
  exist under those names — do not build a second protector.
- `Program.cs:479`'s commented `v1.MapCalendarEndpoints();` is missing or reshaped.
- The generated migration touches anything beyond an additive `CalendarSources` table.
- `Ical.Net.Calendar.Load` diverges materially from RFC 5545 for any of the five fixture
  cases during development — STOP rather than special-case around it silently. (If a real
  private ICS URL is used by hand to obtain a sample, strip it to a local fixture and
  discard the URL — never commit it; see this plan's own "never reproduce secret values"
  constraint.)
- A step's verification fails twice after a reasonable fix attempt.
- `dashboard-page.tsx` has no stable insertion point matching `Main.dc.html`'s section order
  by the time this runs — reconcile with what 002/006/010 actually shipped before guessing,
  and report what you found.

## Maintenance notes

- **CalDAV** adds a second `CalendarSourceKind` and a second `ICalendarSourceReader`
  implementation dispatched by kind — the interface exists for this. It likely also needs
  per-source calendar selection (a CalDAV server hosts several calendars), a new field, not
  a reshape.
- **`CalendarCache` is a single process-wide singleton**, same as `WeatherCache` — fine for
  Homon's one-`api`-replica deployment (`docs/ARCHITECTURE.md` §3.6). A future
  multi-instance deployment would need a distributed cache for both widgets, not just this
  one.
- **Single-flight is per source, not global** (unlike `WeatherCache`'s one lock for its one
  location) — a reviewer adding a fourth widget with the same shape should default to
  per-key locking unless there's really only one key.
- **A reviewer should scrutinize**: that `url` never appears in any `CalendarEndpoints.cs`
  response path (worse than a leaked probe bearer token — it's the family's actual private
  calendar address); that `CryptographicException` is caught at every `Unprotect()` call
  site in `IcsCalendarSourceReader`, not just the first; that one source's fetch failure
  inside `CalendarCache.GetSnapshotsAsync`'s `Task.WhenAll` never faults the other sources'
  tasks or the response as a whole.
- **Deferred**: CalDAV; per-event `location`; a configurable refresh interval/window;
  alerting on `stale`/`unavailable` (009); the actual `source-N` swatch colour (plan 012).
