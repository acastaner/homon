# ARCHITECTURE.md — Homon

The decision record. Numbered so code comments can cite a section (`§3.4`); a section is
never renumbered, only superseded by a later one that says so.

## 1. What Homon is

A self-hosted home dashboard for one household, open-sourced so another household can run
it unchanged. Readers are the family, on phones, glancing; the administrator is one person
who configures probes, links and pages. It runs as containers on a home server, behind
whatever network access control the household already has.

## 2. Shape

- **One repository, four .NET projects, one SPA.** `Homon.Domain` (entities, no
  dependencies) → `Homon.Infrastructure` (EF Core, Identity stores, email, the
  administrator) → `Homon.Api` (the only host: minimal APIs, auth, CLI verbs) ←
  `Homon.Api.Tests`. `Homon.Web` is a Vite app beside them, not in the solution.
- **Modules, not layers, are the unit of work.** Each feature — Monitoring, Links, Pages,
  Backups, Weather, Calendar — lands as one plan that touches Domain, Infrastructure, Api and
  Web together. Phase 0 left a README per module under `src/Homon.Domain/`;
  `docs/MODULES.md` is the index and the record of the constraints already known.
- **Modelled on a sibling.** The repository transposes the conventions of the maintainer's
  earlier project (Chronolectum): the layout, the CPM/analyzer settings, the endpoint
  pattern, the test fixtures, the CI gate, the Docker topology and the comment culture.
  Where Homon departs from it, the departure is recorded below.

## 3. Decisions

### 3.1 Readers are anonymous; the network is the gate; one switch closes it

The brief: no authentication for readers, access enforced by trusted-network rules on the
reverse proxy — but the ability to require sign-in later. So every read endpoint carries
the `Reader` policy (`HomonPolicies.Reader`), whose handler admits everyone while
`Auth:RequireSignInForReaders` is false and only authenticated principals once it is true.
The option is read from the built container on every evaluation (`ReaderHandler` takes
`IOptionsMonitor<AuthOptions>`), so a test host's override is honoured and no endpoint knows
the switch exists. `GET /api/v1/meta` and `/health` stay anonymous either way: the SPA
needs the former to know which mode it is in.

*Rejected:* two route groups (open and closed) swapped at startup — every future endpoint
would have had to be registered in the right one, and the mistake is silent.

### 3.2 The administrator is configuration, not a row

`Administrator:Email` + `Administrator:PasswordHash` (Identity's PBKDF2, minted by the
`hash-password` verb). No database row: a dump of the database yields nothing that signs
in, and a leaked environment yields only a hash. The sign-in endpoint checks this account
*first* and verifies the hash unconditionally, so a wrong address and a wrong password take
the same time. The cookie's security-stamp validation is skipped for this one subject —
its id is not a Guid, and the lookup would throw.

Identity's user and role tables exist anyway (`HomonDbContext : IdentityDbContext<…>`), and
the sign-in path for a database-backed account is complete, so a second administrator later
is a feature rather than an auth rewrite. Roles, not a rank ladder: Homon has exactly one
privilege level to name.

**Both values are optional**, and that is a departure from the sibling. A self-hoster's
first `docker compose up` should show them the dashboard; `/meta` reports
`administratorConfigured: false`, the admin gate says why sign-in refuses everyone, and a
warning is logged at boot. Setting one without the other is refused at startup.

### 3.3 API keys are for scripts, and never administer

The backup scripts must report to the API, so there is a second way in:
`Authorization: Bearer hmn_<tokenId>_<secret>` (or `X-Api-Key`). The token id is a public
Crockford-base32 handle; the secret is 32 random bytes whose SHA-256 is stored — a
password hasher would only add latency to an automated call. A policy scheme forwards each
request to the key handler or the cookie handler by what it carries, so no endpoint knows
which it got.

A key's principal has no user id and no role: `HomonPolicies.ApiKey` admits it, `Reader`
admits it, `Administrator` never does. And a key that *fails* to authenticate fails the
whole request (`ApiKeyRefusalMiddleware`) rather than being demoted to anonymous — because
readers are anonymous by default, a revoked key would otherwise keep getting 200s from
every read while its reports silently 401.

`create-api-key --name …` was the only minter until plan 021 added `POST /api-keys` and
`POST /reporters`, which both mint through the same `ApiKeyIssuer`; the verb stays, because a
fresh install needs a key before anybody can sign in. A key also carries a scope (read or
read-write) and an optional expiry as of §3.13, and as of §3.25 a read-write key is what the
message gateway's ingestion endpoint requires.

### 3.4 One origin, one published port

The SPA calls the relative `/api/v1`; in development the Vite dev server proxies it, in
production the SPA container's nginx does (`src/Homon.Web/nginx.conf`, `location /api/`).
The session cookie is therefore first-party, CORS never engages, and a deployment publishes
exactly one port (`HOMON_HOST_PORT`, 8102 by default). The `api` and `postgres` services
publish nothing.

*Departure from the sibling*, whose host nginx does the split and whose containers bind
loopback. Homon's intended host has no host nginx — its front door is a WAF's virtual
server or the LAN itself — and a generic self-hoster expects one port to point their proxy
at. `HOMON_BIND_ADDRESS` exists for a host whose proxy is local.

### 3.5 PostgreSQL's data is a bind mount in production

`compose.prod.yaml` mounts `./data/postgres` rather than a named volume. The maintainer's
host backs up bind mounts under its docker-apps directory with restic and backs up no named
volume; a named volume would have been the one piece of state in the stack no backup
covered. The key ring (`dataprotection-keys`) stays a named volume and the runbook says how
to export it. On PostgreSQL 18 the mount is `/var/lib/postgresql`, not `…/data`.

### 3.6 Migrations are a verb, never a startup call

`migrate` is a CLI verb on the API host (`Program.cs`), and `compose.prod.yaml` runs it as a
one-shot `migrator` service from the same image before `api` starts. One image means the
migration code and the application code are always the same build; a verb means applying a
migration is a deliberate act with a lock and a failure mode, not something every replica
races for on boot. `dotnet run -- migrate` is also how a developer applies them.

### 3.7 Email is one seam with two transports

`IAlertEmailSender.SendAsync(EmailMessage)` — a single generic method, because alerts are
the only mail Homon sends and the alerting module owns the templates. `ResendEmailSender`
and `LoggingEmailSender` are both registered; which one resolves is decided from
`IOptions<EmailOptions>` at resolve time, so a test's configuration override wins. The one
configuration switch is `Email:Transport` (`Resend`, the default, or `Log`); a Production host
refuses `Log`, so it cannot silently log alerts instead of sending them, and the API test host
and the Playwright suite set `Log`, so the gate never mails anyone. The Resend key and the
sender come from the `AlertSettings` database row on every send, not from configuration
(plan 026, §3.31).

### 3.8 The gate runs locally and refuses skips

`./ci/run-ci.sh` runs `web`, `api` and `e2e` against a throwaway `postgres:18-alpine`
(`homon-ci`, port 55433) and fails the api suite if any test was skipped — because without
`HOMON_TEST_CONNECTION` the `[DatabaseFact]` tests skip and `dotnet test` still exits 0.
Each test class gets its own database cloned from a migrated template (`TestDatabase`), so
the collections run in parallel and share no data. GitHub's `build.yml` is the same three
jobs, and runs only when asked: `workflow_dispatch`, or `workflow_call` from `release.yml`,
which runs it on a `v*.*.*` tag and pushes no image unless it passes. There is no
`pull_request` trigger — Dependabot's pull requests fired it on every bump — so a pull
request is proved locally or by a dispatch against its branch.

`ci/guard-docker.py` is a Claude Code hook that blocks destructive docker commands outside
`homon-ci`, because the development database lives in a container another project owns.

### 3.9 Ports

Development 5300 (SPA) / 5301 (API); e2e 5310 / 5311; CI PostgreSQL 55433; production
8102. All chosen not to collide with the sibling's, so both stacks run on one machine.

### 3.10 The design pass styled Phase 0's blank sheet

Phase 0 shipped semantic HTML with landmarks and accessible names and not one `className`;
Tailwind v4 and the shadcn toolchain sat installed and unused. Plan 012 (`docs/design-
brief.md`) is what ended that: `@theme` tokens and self-hosted fonts, the theme bootstrap
and toggle, `StatusChip`/`Sparkline` and the panel/row/empty-state primitives, applied to
every surface a module plan had already built when 012 ran. The unit and Playwright suites
still query by role and name — that contract survived the pass unchanged, by construction:
no step was allowed to touch an accessible name, landmark role, or heading text, only add
`className`. A surface whose owning module has not landed yet (Backups, Calendar, the
SMB/SNMP probe fieldsets, the API-key reveal flow) stays an unstyled placeholder until that
module's own plan runs and reuses 012's primitives.

### 3.11 Security headers, problem details, request ids

Every response carries `X-Content-Type-Options`, `X-Frame-Options: DENY`,
`Referrer-Policy` and `X-Request-Id` (the bare TraceId, also the problem body's `traceId`
and the JSON log scope, so a report greps straight to its log line). Failures are RFC 9457
problems. Mutating endpoints require `application/json` — a cross-site form cannot produce
it — which is the CSRF posture, and why there is no antiforgery token. nginx adds a
report-only CSP on the SPA.

### 3.12 The look is a status board, dark by default

Chosen on 15 September 2026 from four directions drawn at phone and desktop width
(tinted tiles, status board, warm home, dark console; all kept in `docs/design/`). The
status board won because it reads like a table where a table is the truth — one row per
probe, uptime and last check in aligned columns — and still answers the family's
question at a glance: anything wrong tints its whole row and carries a glyph and a word.
The tinted tiles, closest to the reference screenshots, were rejected because orange and
red fight a saturated ground; the warm home because its sentence headline has to stay
honest from zero probes to forty; the dark console because its light variant has no
character.

Dark is served by default to every visitor, not only to those whose system asks for it.
That overrides the brief's original "respect `prefers-color-scheme`": a home dashboard is
often left open on a shared screen, and the maintainer wants one predictable look. The
warm-paper light scheme is a first-class alternative behind an explicit choice, stored
per browser. Following the system instead is a one-line change in the theme bootstrap,
and is the obvious thing to revisit if readers ask.

The written specification — tokens in both schemes, type ramp, component rules — is the
"Design guidelines" section of `docs/design-brief.md`. The artboards illustrate it; where
they disagree, the guidelines win.

### 3.13 API keys gain a scope and an optional expiry

Superseding part of §3.3, not replacing it: a key still never administers, and it is still
the mechanism a script authenticates with. A key minted from here on carries `ApiKeyScope`
(`Read` or `ReadWrite`, persisted as its name) and an optional `ExpiresAt`. Neither is
enforced by scope-specific policy yet — `HomonPolicies.AdministratorOrApiKey` (the policy
plan 002's probe-list read uses) admits a key of either scope equally, because nothing this
session needs to tell them apart. The distinction exists for a report endpoint that should refuse
a Read key; that endpoint is plan 021's `POST /messages`, gated on
`HomonPolicies.ApiKeyWrite` — see §3.25.

Expiry is enforced immediately, the same way revocation already is: the authentication
handler fails the whole request — never demotes it to anonymous — the moment `ExpiresAt` is
in the past, checked right after the revoked check and before the secret comparison.

`create-api-key` defaults a new key to `Read` (least privilege for an operator who forgot
the flag) and to no expiry. Every key that existed before this section landed (the runbook's
"clockmaster restic" key among them) became `ReadWrite` on migration, so a report endpoint
gated on `ReadWrite` later does not retroactively lock out an already-deployed key.

### 3.14 Probe groups are optional, many-to-many and independent of kind

The administrator wants cards arranged into named groups they create themselves — "Hosts"
for servers and network devices, "Services" for Jellyfin, Immich, Audiobookshelf checks,
say. Because monitoring did not exist before plan 002, groups ship in the same migration
as `Probe` itself, and the dashboard is grouped from its first commit rather than
retrofitted onto a flat list later.

A probe may belong to several groups (a NAS in both "Hosts" and "Storage") — the join is a
`ProbeGroupMembership` row, not a `GroupId` column on `Probe`, because a single column
would force a probe into exactly one group, or duplicate the row (and poll it twice) to
appear in two. A probe in no group appears in the dashboard's final, ungrouped section,
labelled "Services" when it is the only section shown and "Other" once at least one named
group exists — a group may also be named "Other"; the name is not reserved. Groups do not
nest and are not derived from `ProbeKind`: the administrator explicitly wants to mix kinds
inside one group. Deleting a group removes its memberships and leaves the probes alone;
deleting a probe removes it from every group it was in. An empty group — one with no
members at all — is left out of the dashboard's status payload; a group whose members are
all paused is not empty and still appears.

*Rejected*: free-text tags (no order, no heading identity to key a `<section id>` off).

### 3.15 The probe scheduler is a tick-based scan, not per-probe timers

Resolves this record's own open question, above. `ProbeScheduler` is one
`BackgroundService` that wakes on a fixed tick (`MonitoringOptions.TickInterval`, 5 s by
default), scans the database each tick for probes whose `PollInterval` has elapsed, and
dispatches each due probe's poll under a bounded-concurrency gate
(`MonitoringOptions.MaxConcurrentPolls`). A probe already mid-poll is never dispatched
again by an overlapping tick.

*Rejected*: a `System.Threading.Timer`/`PeriodicTimer` per probe. It needs explicit
lifecycle management on every create, edit, pause and delete — a second source of truth
for "what probes exist," running beside the database itself. The tick-scan design picks up
a create, edit, pause or delete for free: the next scan simply sees the new state. It is
also the more testable shape — one `TimeProvider` governs the whole scheduler, rather than
N independent timer objects each needing their own fake.

### 3.16 Uptime is a per-probe ratio; the aggregate averages probes, not observations

Also resolves an item from this record's open questions. Uptime, per probe, is
`successCount / totalCount` over `ProbeObservation` rows within the retained window (30
days by default), as a percentage rounded to two decimals; `null` (rendered `—`) when the
probe has no observations at all.

The dashboard's stat strip reports the *mean of each probe's own uptime percentage*, not a
single ratio pooled across every observation row. A probe polled every 15 seconds would
otherwise contribute roughly sixty times as many rows as one polled every 15 minutes, and
silently dominate the number a family reads as "is everything basically fine" — averaging
per-probe percentages weights every service equally. Probes with no observations are
excluded from the average, not counted as 0%; a probe belonging to two groups still counts
once.

*Rejected*: time-weighted uptime (integrating success/failure duration between consecutive
observations) — a probe's `PollInterval` can change mid-window and pausing leaves gaps
with no observations at all, and reconciling both into one duration-weighted figure is
real complexity for a number the brief only asks to be "computed over the retained
observation window."

### 3.17 Probe secrets share one encrypted-at-rest seam; per-kind options are one jsonb column each

Plan 003 (HTTP) is the first probe kind to carry options beyond `Host`, and the first to
carry a secret — the pattern set here is binding on 004 (SMB) and 005 (SNMP), which reuse
it by name rather than inventing their own.

**Per-kind options.** `Probe` gains one nullable owned-type property per kind (`HttpOptions`
for `Http`, `SmbOptions`/`SnmpOptions` when 004/005 land), each mapped with EF Core's
`OwnsOne(...).ToJson()` to its own `jsonb` column — additive to `AddMonitoring`'s migration,
never a reshape of it. *Rejected*: a separate table per kind (every probe-list read needs a
conditional join per kind, and a new kind becomes a migration touching the shared read
path); one kind-agnostic jsonb blob (loses compile-time field names, mixes every kind's
validation together); flat nullable scalar columns prefixed by kind (four kinds × ~6 fields
is 20+ mostly-null columns with no natural home for negation flags).

**Secrets.** `Homon.Infrastructure.Security.ISecretProtector`, backed by ASP.NET Data
Protection (`DataProtectionSecretProtector`), is the one seam every secret Homon ever
stores goes through — the HTTP bearer token or basic-auth password today, the Resend API key
(plan 026), the SMB password and calendar credentials later — under a single purpose string (`"Homon.Secrets.v1"`), not
one per kind, so a key-ring export/import covers every stored secret together. The key ring
is the `dataprotection-keys` volume in production (`docs/MODULES.md`); losing it means
every `Unprotect()` throws `CryptographicException`, which a runner catches and turns into
a failed observation ("credentials unreadable — re-enter them") — it must never reach the
scheduler as an unhandled exception.

**Wire semantics are write-only.** A probe response never carries a secret, only a derived
`hasSecret: bool`. On write, the credential's `secret` field is absent/null → keep the
stored value; `""` → clear it; non-empty → encrypt and replace. Setting the credential's
type to "none" clears any stored secret regardless of what else is sent. The admin form
never pre-fills a secret field; editing a probe with one already set shows that a secret
exists and offers a "Replace credential" affordance rather than an editable field seeded
from nothing.

**`IDataProtectionProvider` is not free everywhere `Homon.Api` runs.** ASP.NET Core's
`WebApplication.CreateBuilder` host registers Data Protection's defaults implicitly, but
`Program.cs`'s CLI verbs (`migrate`, `hash-password`, `create-api-key`) build a *different*,
plain `Host.CreateApplicationBuilder` host (`CommandHost`) that does not — so
`AddDataProtection()` is called unconditionally in both hosts, not only inside the
`DataProtection:KeyRingPath` branch that only ever governs *where* keys persist.

*Rejected*: a second `IDataProtectionProvider` purpose per kind — the whole point of one
key ring is that losing it is one incident, not N; skipping secret protection for
command-line verbs — `ISecretProtector` is registered unconditionally in
`AddHomonMonitoring`, so every host that resolves the DI container needs the provider
available, not only the one that serves HTTP traffic.

### 3.18 Pages are sanitised on write, not on read — the stored body is trusted only because of that

`Page.BodyHtml` is rendered with `dangerouslySetInnerHTML` verbatim
(`page-page.tsx`) to every reader who ever loads the page, so the sanitiser — not the
TipTap editor that produced the markup — is the module's whole defence against stored XSS.
`Homon.Infrastructure/Pages/PageHtmlSanitizer.cs` wraps `HtmlSanitizer` (`Ganss.Xss`, MIT)
with an allow-list that matches the editor's extension list tag for tag: `p`, `h2`–`h4`,
`strong`, `em`, `s`, `code`, `pre`, `blockquote`, `ul`/`ol`/`li`, `a`, `hr`, `br`, `img`, and
three attributes (`href`, `src`, `alt`) — no `style`, `class`, `id` or `on*` survive.
`PostProcessNode` then forces two rules the allow-list cannot express: an absolute link
(`http`/`https`/`mailto`) gains `target="_blank" rel="noopener noreferrer"`, never
admin-controlled since the editor has no `target`/`rel` on its own allow-list; and an
`<img>` is dropped unless its `src` is `https://` — no relative, `http://`, `data:` or
`blob:` sources, since this plan ships no image upload and a relative path cannot be
trusted without the app's own origin at sanitise time.

**Sanitise on write, once.** *Rejected*: sanitising on read — repeats the cost on every
request, and a second render path (an export, an alert email quoting a page) could forget
to call it; storing raw Markdown and converting to HTML at render time — still needs a
sanitise step somewhere, and now there are two places (convert, then sanitise) to keep in
sync instead of one; client-side sanitising alone (DOMPurify) — the stored body is read by
every future browser that loads the page, not only the one that wrote it, so the server is
the only place a guarantee can be made.

**A disallowed element's children are dropped with it** (`KeepChildNodes = false`), not
unwrapped into surrounding text — an `<iframe>` or `<script>` never survives as inert text
nobody meant to keep. The TipTap extension list and this allow-list move together: adding
an extension without adding its tag/attribute here means the editor produces markup the
server silently strips on save; removing an allow-list entry without removing the
extension is the dangerous direction, since a control's effect then quietly vanishes only
on save. `PageHtmlSanitizerTests.cs` carries the explicit XSS corpus this pairing is
checked against.

### 3.19 The weather location is a DB singleton row; the SPA never talks to the provider

`WeatherSettings` (`Homon.Domain/Weather/`) holds at most one row, always at the fixed id
`WeatherSettings.SingletonId` — the admin-page action the module's README asks for ("the
household sets it once"), not a redeploy. Open-Meteo (https://api.open-meteo.com) needs no
API key; `OpenMeteoWeatherProvider` asks it directly in the settings' own units
(`temperature_unit`/`wind_speed_unit`), so nothing downstream converts a value. Coordinates
leave the server once, on a cache miss, never from a reader's own browser — because nothing
in the SPA is written to call the provider. `nginx.conf`'s CSP is
`Content-Security-Policy-Report-Only`, which logs a violation but does not block a request,
so this is a code property (the SPA carries no client for Open-Meteo, verified by a
`grep -rn "api.open-meteo.com" src/Homon.Web/src` gate), not a network one.

**`WeatherCache`** is a bespoke singleton wrapper, not raw `IMemoryCache` — neither the
single-flight semaphore nor the stale-while-error fallback below comes free from that:

- **Fresh for 15 minutes**, matching Open-Meteo's own update cadence; inside that window
  nothing calls the provider.
- **Stale-while-error, up to 6 hours**: past fresh but a refresh fails, the last snapshot is
  served with `stale: true`; older than that (or nothing was ever fetched), `GET /weather`
  answers `503`.
- **Single-flight**: concurrent callers past the fresh window share one semaphore; the
  first through calls the provider, the rest re-check the (by then refreshed) snapshot
  instead of each calling Open-Meteo.
- **A monotonic generation counter guards `Invalidate()` against a refresh already in
  flight.** `PUT`/`DELETE /weather/settings` call `Invalidate()` synchronously, outside the
  semaphore, so a fetch started under the *old* settings can still be running when a new
  location is saved. Without a guard, that stale-settings fetch would complete afterwards
  and silently overwrite the just-cleared snapshot. `Invalidate()` bumps the counter before
  clearing the snapshot; a fetch only commits its result if the counter is unchanged since
  it started — a mismatch discards the result rather than caching it, but still returns it
  to the caller that started that fetch, since it is the true answer for the settings that
  call was given.
- One process-wide singleton — fine for Homon's one-`api`-replica deployment (§3.6). A
  future multi-instance deployment would need a distributed cache; not before then.

**`Weather:Provider=Fake`** (registered alongside the real provider, resolved from
`IOptions<WeatherOptions>` at first use — the same test-override-safe pattern as
`IAlertEmailSender`, §3.2) exists solely so the e2e gate, and any other automated run, never
reaches the live network; it is refused at startup in Production.

*Rejected*: an admin-configurable cache TTL this phase; persisting forecasts to the
database — nothing needs history, and a table only a cache reads from is upkeep with no
benefit; the SPA calling Open-Meteo directly — the module README asks for a server-side
cache, and per-reader calls would multiply the provider's rate limit by family size for
nothing; geocoding search — pasting coordinates is deferred to a follow-up, which would
change only how the admin form fills latitude/longitude, not the wire contract.

### 3.20 The dashboard's freshness is the browser's clock, and the banner says so

Plan 012 wired every design-brief component but one: the Banner's "refreshed 42 s ago"
timestamp, deferred because a plain `useStatus()` in `AppShell` would add a `/status` fetch
to every route, `/admin/sign-in` included. Plan 014 closed that gap on 2026-09-18 with a
*disabled* observer — `useQuery({ queryKey: STATUS_QUERY_KEY, queryFn: fetchStatus, enabled:
false })` inside `RefreshIndicator`. `enabled: false` suppresses only automatic fetching; the
observer still subscribes to the `['status']` cache entry and re-renders when
`DashboardPage`'s own `useStatus()` writes to it. On a route where nothing has populated that
entry — sign-in, the dashboard's first paint — `dataUpdatedAt` is `0` and the indicator
renders nothing rather than "refreshed 57 years ago".

The age is measured `dataUpdatedAt` (TanStack's browser-side receive timestamp) against
`Date.now()`, never against `Status.generatedAt` on the wire. Those are two different clocks;
`generatedAt` is stamped by the API server's `TimeProvider`, and a drifted household machine
would render a nonsensical "refreshed -3 s ago" against it. `generatedAt` stays on the wire
unconsumed — an honest answer to "when did the server compute this", and a future
server-push or multi-instance deployment would still want it.

The indicator renders once, always visible, not as the brief's banner/desktop ·
page-header/phone pair: jsdom applies no stylesheet, so a `hidden`/`sm:flex` split would
resolve both copies in the unit suite and break every `getByText`/`getByRole` strict-mode
query — the same reason §3.10's design pass already gives for "Signed in as …". The banner's
existing `flex-wrap`/`gap-y-2` wraps it onto a second line on a phone instead, and
`e2e/layout.spec.ts` covers the result at both viewport projects.

Returning to a backgrounded tab now refetches the dashboard's queries (status, weather, and
— on the dashboard only — links and pages) immediately rather than after up to 30 more
seconds of a frozen "Checked" column: `useStatus`, `useWeather`, and `DashboardPage`'s calls
to `useLinks`/`usePublishedPages` set `refetchOnWindowFocus: true`, overriding `main.tsx`'s
global `refetchOnWindowFocus: false` per query. That global default still protects every
admin form from refetching under an administrator's hands. The override is safe because
`main.tsx`'s global `staleTime: 30_000` still gates it — a focus event inside 30 s of the
last successful fetch finds the cache entry not yet stale and issues no request; only a
return after 30 s refetches. `useLinks`/`usePublishedPages` take their polling options from
the caller (an optional `{ refetchInterval?, refetchOnWindowFocus? }`, defaulting to `{}`)
rather than setting them internally, because `AdminLinksPage` shares `useLinks` and reorders
rows straight out of its `data` — a background refetch landing mid-reorder would shuffle the
list under the administrator's cursor.

The banner also gained a Refresh button, next to the timestamp, calling
`queryClient.invalidateQueries()` with no filter — refetching whatever the current route has
mounted, so it is correct on the dashboard and on every admin page without a hand-maintained
list of query keys.

The poll interval itself stays a hard-coded 30 seconds in `lib/status.ts`'s `useStatus`, as
it was before this plan — the maintainer chose "fixed, but honest" over a new configuration
surface on 2026-09-18. If a household ever needs it configurable, `GET /api/v1/meta`
reporting it from `MonitoringOptions` is the cheapest route, not a database singleton.

### 3.21 The session cookie's `Secure` attribute follows the request's real scheme, opt-in

Homon's first production deployment (`clockmaster`, 2026-09-18) exposed a defect an
always-`Secure` cookie hides until it is reached over plain HTTP: a LAN-first origin like
`http://homon.lan.acastaner.fr:8102` gets a `Set-Cookie: homon.sid=…; secure` on a
successful sign-in, and a browser silently discards a `Secure` cookie delivered over plain
HTTP — `localhost` is the only exemption. The sign-in endpoint returns `204`, the cookie
never lands, and the next request is anonymous; nothing is logged because nothing failed.

The fix is two changes, not one, because relaxing the cookie policy on its own would quietly
weaken the deployment that *does* terminate TLS at a WAF. `src/Homon.Web/nginx.conf` listens
on plain `:80` and previously set `X-Forwarded-Proto: $scheme` unconditionally — always
`http`, overwriting whatever a WAF in front had already set. nginx therefore now forwards
the client's *real* scheme through a new `map $http_x_forwarded_proto $homon_forwarded_proto`,
placed in the `http` context beside the existing `$homon_cache` map (the only context `map`
is legal in). The map is a strict allow-list — only the literal values `http` and `https`
pass through; anything else, including a comma-joined list from a double proxy, falls back
to `$scheme` — so an unexpected header value degrades to today's behaviour rather than
travelling on uninspected into `Request.Scheme`. This is deliberately not keyed on
`$remote_addr`: under rootless Docker the published port is SNATed, so a LAN client and the
WAF arrive at the web container from the same address, and there is nothing to discriminate
on. A client can therefore claim `https` over plain HTTP; the only effect is that its own
cookie is marked `Secure` and its own browser refuses to store it — self-inflicted and inert.
Stripping `Secure` on the WAF path is not reachable the other way, because the WAF sets the
header itself and replaces whatever the client sent. This all depends on
`ReverseProxy:KnownNetwork` (`compose.prod.yaml`, default `172.16.0.0/12`) matching the
network the web container reaches the api container from — that setting is what switches the
forwarded-headers middleware on at all, unchanged by this plan.

Only with that in place is it safe to let the cookie policy follow the request: a new flag,
`Auth:AllowPlainTextSessions`, default `false`, changes
`CookieAuthenticationOptions.Cookie.SecurePolicy` from unconditional
`CookieSecurePolicy.Always` to `CookieSecurePolicy.SameAsRequest` when set (Development
already used `SameAsRequest` unconditionally, and still does). Deliberately
`SameAsRequest`, not a blanket `CookieSecurePolicy.None` — with nginx
forwarding the real scheme, `SameAsRequest` is correct on *both* paths from one setting: a
WAF-fronted request is seen as HTTPS and still receives a `Secure` cookie, while a plain-HTTP
LAN request receives one the browser will actually store. `None` would give up the WAN
path's protection to fix the LAN's. The policy is registered in its own
`AddOptions<CookieAuthenticationOptions>(...).Configure<IOptions<AuthOptions>>(...)` call,
separate from the rest of the cookie configuration delegate, because it must resolve
`IOptions<AuthOptions>` from the built container rather than closing over
`builder.Configuration` — the standing rule recorded in `CLAUDE.md` and already followed by
the sign-in rate limiter.

The default is `false` on both ends (`AuthOptions.AllowPlainTextSessions` and
`compose.prod.yaml`'s `HOMON_ALLOW_PLAINTEXT_SESSIONS`), so a host that does nothing keeps
today's behaviour exactly — this is a bridge for a deployment shape the project already
documents for itself (`compose.prod.yaml`'s own header: "a reverse proxy … a WAF, nothing on
a LAN"), not a change to the default posture.

**Accepted residual risk**: with the flag on, the session cookie travels in clear on that
network, so anyone who can sniff the LAN segment can replay the session — accepted because
the alternative is no administration at all on that host, and retired by putting TLS on the
plain-HTTP origin, at which point the flag is set back to `false` and nothing else changes.

### 3.22 Collapsed dashboard sections live in this browser's local storage, and the heading is the control

Plan 018 lets a reader fold any dashboard section away — one probe group, Links, Pages,
Weather — and remembers the choice the next time that browser opens the dashboard. It was
first asked for as a cookie; it shipped as `localStorage` instead
(`src/Homon.Web/src/lib/collapsed-sections.ts`).

**Why not the cookie that was asked for.** A cookie here has exactly the failure mode §3.21
exists to fix: the SPA and the API are one origin in every deployment, and the production
host serves the LAN over plain HTTP. `Secure` is what every cookie checklist tells you to
add, and adding it here would make the feature pass every test and every HTTPS deployment
while remembering nothing on the one dashboard a household actually uses — a silent failure,
not a loud one, because the sign-in flow that motivated §3.21 has no equivalent for a display
preference nobody is watching fail. Past that trap, a cookie would also ride on every
`/status` poll (every 30 s) plus links, pages and weather, for a value no server-side code
reads — the API reads only its own `homon.sid`. `src/Homon.Web/src/lib/theme.ts` already
persists a per-browser display preference exactly this way, so this module is the same
mechanism, not a new one. The one thing a cookie buys — a value the *server* can read — is
worth nothing while Homon is a Vite SPA behind nginx serving a static bundle; if that
changes, this module is the only thing that has to move.

**The stored shape.** One key, `homon-collapsed-sections`, holding a JSON array of section
ids — `["group-hosts","links"]` — parsed defensively (`JSON.parse` in a `try`/`catch`,
anything that is not an array of non-empty strings discarded) because the read happens
synchronously inside a `useState` initialiser, where a throw would blank the entire
dashboard. A value written by an older version of this code, a newer one, or a curious reader
with dev tools open all have to degrade to "everything expanded" rather than to an exception.
Storing collapsed ids only, never expanded ones, means a first-ever visit, cleared site data,
a private window and a brand-new probe group all behave identically — expanded — without a
special case anywhere. Writing an empty set removes the key entirely rather than storing
`[]`, so "never used" and "used, then everything re-expanded" look the same in dev tools, and
the key's presence is itself a truthful signal the e2e suite asserts on directly. On write,
the list is also capped at 50 ids and pruned against the section ids currently known to the
page, so a deleted probe group's id cannot sit in storage forever.

**The markup.** The toggle is a `<button>` *inside* the section's `<h2>`, and the button's
text is the heading text and nothing else
(`src/Homon.Web/src/components/collapsible-section.tsx`). That arrangement is forced by four
assertions that were already green and had to stay green: `e2e/dashboard-groups.spec.ts`
matches every level-2 heading's `textContent` exactly against the section names, so no count,
suffix or `sr-only` span may live inside the `<h2>`; `dashboard-page.test.tsx` finds each
section by the accessible name its heading supplies through `aria-labelledby`;
`e2e/layout.spec.ts` needs the headings visible on the bare dashboard; and
`e2e/refresh.spec.ts` runs a 40px tap-target check over *every* `<button>` on `/`, which a
naive 12px-tall heading button would fail. The last of those is why the button carries
`min-h-10 min-w-10` even though it costs the design brief's "section label sits 10px above
its panel" — the tap-target floor is enforced by the gate, the 10px is enforced by nobody, so
the floor wins. §3.23 later added two more controls to that header row; they are siblings of the
`<h2>`, never children, and every constraint in this paragraph still holds unchanged.
Collapse itself is the `hidden` attribute on a wrapper `<div>` that always
exists (never a conditional render), because `aria-controls` has to point at a real element,
and that wrapper takes no Tailwind display utility — `flex`/`grid`/`block` all beat
`[hidden]`'s `display: none` and would leave a "collapsed" section still on screen.

**No bootstrap script, unlike the theme.** `public/theme-bootstrap.js` exists because the
theme has to be right before the very first paint or the page flashes the wrong scheme.
Sections do not have that problem: nothing can render one before its data
(`/status`, `/links`, …) has resolved anyway, and the stored ids are read synchronously in
the `useCollapsedSections` initialiser, which runs before the first paint that could show a
section at all. A second bootstrap script here would be solving a problem this feature does
not have.

**No server-side state.** This is a display preference, the same class of thing as the theme
toggle, not a household setting — nothing changes under `src/Homon.Api/`,
`src/Homon.Domain/` or `src/Homon.Infrastructure/`, and the API gains no new field or
endpoint. Moving it server-side later, so every browser in a household opened the same
arrangement, would be a different decision with its own migration, not an extension of this
one.

### 3.23 The dashboard's section order is this browser's too, and an empty section is not a section

Plan 019 extends §3.22 — it does not supersede it. Two changes to `/`, both SPA-only: an empty
ungrouped section stops rendering, and a reader can put the sections in whatever order they like,
remembered per browser (`src/Homon.Web/src/lib/section-order.ts`).

**An empty section is not a section.** `dashboardSections` used to append the ungrouped section
unconditionally, so a household that had put every probe in a group saw an "Other" heading above a
"No probes yet. An administrator adds them under Admin → Probes" panel that was simply false. The
rule is not new — the API already refuses to send a group with no members
(`StatusEndpoints.cs`'s `.Where(g => g.Members.Count > 0)`), so this makes the SPA agree with the
server rather than inventing a policy. The guard is `ungrouped.length > 0 || groups.length === 0`,
and it tests the **resolved** probes rather than the id count, because `resolve()` drops an id with
no matching probe and a stale id would otherwise keep the section alive. The second half of that
condition is load-bearing: with zero groups *and* zero probes the section is still returned,
labelled "Services", because that one **is** the bare install's onboarding hint and is the only
thing a fresh deployment has to look at. `e2e/layout.spec.ts` asserts it, and two whole-object
assertions in `lib/status.test.ts` pin it.

The label was left as "Other" (and "Services" when it is the only section) on the maintainer's
call, though the request called it "Others" — plan 002's Decision 7 chose it and
`e2e/dashboard-groups.spec.ts` matches heading text exactly, so renaming costs spec edits for no
functional gain.

**A second key, against plan 018's own advice.** 018's maintenance notes said that if the stored
value ever had to grow, the array should be widened into an object under the one key, and that a
second key must not be introduced. This is the growth it anticipated, and it took the second key
anyway: `homon-section-order`. Collapse and order are two preferences with two independent defaults
and, decisively, two independent "back to default" states, and one key would give them one
lifetime. `e2e/dashboard-collapse.spec.ts` asserts that `homon-collapsed-sections` is `null` once
every section is expanded again — a truthful signal that stops being true the moment a stored order
keeps the key alive, after which that `null` means "no collapse *and* no custom order" to everyone
who ever reads it. Two keys keep each signal honest and leave a landed feature's module and its six
tests untouched. The cost is one more `try`/`catch`, which is the same trade 018 itself made when it
declined to merge with `lib/theme.ts`.

**A sequence is not a set, in three places.** The two modules look like siblings and are not the
same shape, and each difference is a bug somebody will reintroduce by making them symmetrical:

- The 50-id cap is `slice(0, 50)` here, not `slice(-50)`. A set's newest entries are the
  interesting ones; an order's whole meaning is its front, and keeping the tail would forget where
  the top sections go while faithfully remembering the bottom ones.
- Deduping keeps the **first** occurrence, because the first position is the one the reader chose.
- "Back to default" is a comparison against the natural order (`isNaturalOrder`), not an emptiness
  check. An order can be a full list of every id and still be the natural one, so without the
  comparison, undoing your only move would leave the key in storage spelling the natural order out
  — exactly the "used, then reset, but it looks different in dev tools" state §3.22 argues against.

**A swap, not a relocation.** Moving a section exchanges it with the section **visible** next to it
— what a reader can see is what "up" means — and the write exchanges exactly those two ids in the
stored list, touching no others. The obvious alternative, lifting the id out and splicing it in
beside its neighbour, is not invertible: with `["links","pages","weather"]` stored and `pages`
absent from the page because nothing is published, moving `weather` up and straight back down
returns the visible order but leaves `pages` one position lower than it started. Every arrange
session would nudge the sections a reader cannot see. Only a swap is its own inverse, and only a
swap preserves an absent section's slot; `swapSectionIds` has a test named for each property.

**Hydration, pruning, and where a new section appears.** The stored value is pruned to ids that
still exist and extended with every known id it has not seen, so every known id appears exactly
once — which is what makes a swap total. A section whose id the stored order has never met keeps its
natural relative position and goes to the **end**, so a probe group created after a reader arranged
the dashboard turns up below Weather until it is moved. That is one stable sort with no special
case, it agrees with hydration, and the Reset order control answers it in one click; anchoring a new
group beside its natural neighbours is a fiddly rule for a case that already has an answer.

**The prune list is no longer derived from the rendered sections, and that is the sharp edge the two
changes create together.** `knownSectionIds` in `dashboard-page.tsx` is built from
`status.data.groups` with `'ungrouped'`, `'links'`, `'pages'` and `'weather'` as literals — not, as
it was, from `dashboardSections`'s output. Once an empty ungrouped section stops rendering, deriving
the list from what renders drops `'ungrouped'` out of it, and the next press of any other section's
control prunes that section's remembered collapse *and* its order slot. Nothing in the suite would
have caught it. A section id belongs in this list because it *can* exist, never because it is on
screen; that is also why `'pages'` stays in it while nothing is published.

**Arrange mode is not persisted, and is not a preference.** It is a mode a reader is in, and one key
per concern is the budget — a browser that reopened the dashboard mid-arrangement would greet
somebody who only wanted to know whether the NAS is up with two extra controls on every section. It
is plain React state, gone on reload, and `e2e/dashboard-arrange.spec.ts` asserts that.

**Up/down buttons, not drag-and-drop.** Every reorder surface in this app already works this way
(`admin-links-page.tsx`, `admin-probes-page.tsx`, `admin-probe-groups-page.tsx`), it needs no new
dependency, and it is the only pattern that clears the 40px tap-target floor and works with a
keyboard and a screen reader without a parallel implementation. The controls are typed props on
`CollapsibleSection` rather than a free-form `headerActions?: ReactNode` slot, because that
component's comment claims ownership of the floor and a slot would let a caller drop an undersized
button into the header row and turn `e2e/refresh.spec.ts` red from a different file. Their
accessible name comes from a stable `label` — "Move Weather up", never "Move Weather · Kitchen up"
— so a name the tests and a screen reader depend on cannot change when an administrator renames the
weather location. One page-level Reset order, not one per section: the action clears a single key,
and N buttons sharing that name is an immediate strict-mode ambiguity.

**Still no server-side state.** This is a display preference, the same class as the theme toggle,
and it now layers over the household-wide group order that `PUT /api/v1/probe-groups/order` already
sets: that remains the natural order every browser starts from, and this is one browser's view of
it. §3.22's closing paragraph already ruled that moving either preference server-side would be a
different decision with its own migration; the same sentence covers this one.

**One accepted limitation.** Because the API hides a group with no members, emptying a group removes
its id from the prune list and drops its arranged slot; refilling it later puts it back at the
bottom. The ungrouped section does not have this problem — it is a literal in the list. The
alternative, never pruning, is worse: a stale id at the head of the order would survive the cap and
evict a live one.

### 3.24 Severe-weather banners are derived from thresholds, not relayed from a provider

**Open-Meteo publishes no weather-warnings endpoint.** Confirmed against the live API on
2026-10-01. The banners at the top of `/weather` are therefore *Homon's own reading* of the
forecast against a table of thresholds, never a relayed official advisory — the vocabulary is
deliberately `Caution` and `Severe` rather than "warning", and no copy anywhere names an
authority. Anyone extending this must not "fix" it by trusting a field that does not exist.

The thresholds live in `Homon.Domain/Weather/WeatherWarningThresholds.cs`, hard-coded and
unit-aware, with `WeatherWarningEvaluator` applying them:

| Kind | Source | Caution (metric) | Severe (metric) | Caution (imperial) | Severe (imperial) |
| --- | --- | --- | --- | --- | --- |
| Wind | hourly gust | 60 km/h | 90 km/h | 38 mph | 56 mph |
| Thunderstorm | hourly WMO code | 95 | 96 or 99 (hail) | same | same |
| Snow | daily snowfall | 1 cm | 5 cm | 0.4 in | 2 in |
| Rain | daily precipitation | 20 mm | 40 mm | 0.8 in | 1.6 in |
| Heat | daily high | 32 °C | 38 °C | 90 °F | 100 °F |
| Cold | daily low | −10 °C | −18 °C | 14 °F | 0 °F |

Decisions inside that:

- **Hard-coded, not configurable.** Same trade as §3.19's rejected cache TTL: the module has
  one setting, and the alternative costs a migration, six admin fields and their validation to
  let a household retune numbers it sets once. The figures suit a temperate household, and a
  reader in Arizona will find 32 °C unremarkable — that is the accepted cost, and the clean
  follow-up is a `Weather:Warnings:*` configuration section, not a database column.
- **The imperial figures are rounded, not converted.** 90 °F is not 32 °C; it is the round
  number a Fahrenheit reader recognises as "hot", which is what a threshold is for.
- **Open-Meteo's own unit asymmetry is preserved.** `snowfall_sum` is centimetres while
  `precipitation_sum` is millimetres, so the snow thresholds are written in cm. The imperial
  request sends `precipitation_unit=inch`, which converts both.
- **At most three banners, one per kind, over the next 48 hours.** A six-hour gale is one
  banner, not six: runs of consecutive qualifying hours collapse, and a run never spans
  midnight (a warning carries one date, and "22:00 to 02:00" on the first day reads as a window
  in the wrong direction). The order is total — severity descending, then earliest start, then
  `WeatherWarningKind`'s declaration order — because the tests pin it. Surplus advisories are
  dropped silently; there is no "and 2 more" affordance.
- **Every variable Open-Meteo may not report is nullable, and a null trips nothing.** It
  answers `null` per hour or day for a variable a model has no value for, and a `double[]`
  throws on a null element.

**The window is sliced per request, not per fetch.** `WeatherCache.FreshFor` is 15 minutes
(§3.19), so a window chosen when the snapshot was built would open on an hour already past for
most of that snapshot's life. The provider therefore parses and caches *everything* it got —
eight days, 192 hourly rows, and Open-Meteo's `utc_offset_seconds` — and
`WeatherEndpoints.GetWeatherAsync` computes "the current hour at the location" from the
injected `TimeProvider` on every request. 48 hours are scanned for advisories; the 24 the
table can show cross the wire. `WeatherCache` itself is unchanged.

**Local clock times cross the wire as `"HH:mm"` strings.** Open-Meteo under `timezone=auto`
already answers in the location's zone; sending those as instants and formatting them in the
browser would re-read them in the *reader's* zone — the trap `formatForecastDay` documents for
calendar dates. So `WeatherHourResponse.Time`, `Sunrise` and `Sunset` are strings the SPA
prints verbatim. `utc_offset_seconds` is also why this module needs no tz database: a
household-wide IANA setting is plan 011's `Calendar:TimeZone` territory, and once that lands
there will be two notions of the household's zone to reconcile.

**One payload serves both surfaces.** The dashboard widget and `/weather` read the same
`WEATHER_QUERY_KEY`, so clicking the widget renders the page with no request and no spinner.
`WeatherResponse.Forecast` therefore grew from three days to seven and the widget slices to
three. Today is its own field rather than `Forecast[0]`: the provider used to drop `daily[0]`
because `current` covered it, and `current` cannot give a high and a low.

**The banners are a named list, not `role="alert"` apiece.** They are present on first paint,
and `role="alert"` is assertive — three of them would be announced over each other. This
application reserves `role="alert"` for errors and `role="status"` for the session check
(§3.3). Severity is carried by the word beside the glyph, with the tint as the echo, so it is
never colour alone. Both tables stay real tables and drop columns under `sm:` rather than
folding rows into two lines: plan 012 established that re-displaying `<tr>`/`<td>` as blocks
strips the implicit ARIA row/cell roles the e2e specs query by.

*Rejected*: a second endpoint for the page — one cache, one key, no second round-trip;
persisting hourly history, for §3.19's reason; admin-tunable thresholds this phase; a banner
navigation entry for `/weather` — the brief's two-item nav must stay one row on a phone, and
the widget is the way in, as the Pages section is for `/pages/{slug}`; remembering the
"show more" count in `localStorage` — how far down a table someone has read is a position
within one visit, not a preference like collapsed sections (§3.22) or section order (§3.23).

### 3.25 A reporter pushes, Homon ingests, and the monitor is a probe kind

Homon can watch things that answer when asked. It could not watch anything that only speaks when
it has something to say — a nightly restic run, a NAS array check, any script that knows its own
outcome. Plan 021 adds the ingestion point: `POST /api/v1/messages`, authenticated with an API
key, carrying what the reporter is called, what it did, the text that proves it, and **when it
intends to report again**. The pattern's names are worth knowing because the search terms are:
this is *push monitoring*, and the overdue half is a *dead man's switch*. It is not a message bus
— nothing here routes, fans out or subscribes.

Two entities, in `Homon.Domain/Messaging/`. A `Reporter` is registered by the administrator, who
never chooses its `Identifier` (Homon generates sixteen Crockford base32 characters) and never
sees its key twice. A `Message` is append-only; a correction is a second message. Everything the
reporter says about itself — name, description, status, category, recurrence — belongs to the
message, not to the reporter, so Homon needs no editing when a script changes what it calls
itself.

**The key decides who is reporting.** One `hmn_…` key is bound 1:1 to one reporter
(`Reporter.ApiKeyId`, unique index, `Restrict`), minted inside the same transaction that creates
it. An `identifier` in the body is optional and only ever checked for a match, so a stolen key
cannot file a report as somebody else. *Rejected*: auto-creating a reporter on first report. Plan
008's reasoning applies unchanged — a typo would spawn a phantom reporter nobody is watching,
which is worse than a 403.

**Ingestion is the first endpoint gated on an API key's scope.** `HomonPolicies.ApiKeyWrite`
requires the `homon:key-scope` claim to read `ReadWrite`, which is the consumer §3.13 said would
arrive with the Backups module. A `Read` key is refused with 403 — it authenticated, it simply may
not report — and a cookie session is refused too, because a browser must never be tricked into
filing a report.

**The monitor is a new `ProbeKind.Message`, not a second status model.** A message probe is a
`Probe` row, so probe groups, display order, pause, the observation history, the uptime ratio, the
dashboard's sections and its collapsed-section memory, and plan 009's alert transitions when they
land, all apply with no new machinery. Adding a member to `ProbeKind` needed no migration: §3.17's
convention stores it as its *name* in a column with no check constraint, so nothing was renumbered.

**`Probe.Host` holds the reporter's identifier.** A ping probe's host is the thing it listens to;
a message probe's is too. The API validates on write that it names a reporter that exists, and
refuses to delete a reporter while a probe names it — the `Restrict` this reference cannot have,
spelled out as a 400 that names the probe. *Rejected*: a nullable `Probe.ReporterId` with a real
foreign key, because §3.17 already rejected per-kind scalar columns on `Probe`; an owned
`MessageOptions` jsonb like `HttpProbeOptions`, because a message probe has no options at all —
the schedule is the reporter's and the tolerance is the reporter's.

**The scheduler polls it like everything else.** `MessageProbeRunner` performs no I/O beyond
reading the newest message. That a push probe is *polled* is the point rather than an oddity:
nothing arriving must be able to change a probe's state, and only a tick can notice that nothing
arrived. A dead man's switch needs a clock, not a webhook.

**`ProbeResult` gained an optional derived status, because the state machine cannot express
"late".** `ProbeStateMachine` is binary and right for a probe that flaps; a message probe's
authority is the reporter's own verdict, and a single `warning` message re-read on every tick would
otherwise accumulate a failure streak into `Down`. So a runner may supply the status directly and
the streak machine's answer is overridden; the counters still advance underneath it, so `Unpause`
and `ChangeFailureThreshold` have something to read, and for a message probe that re-derived
status is a placeholder the next poll corrects within one tick. **A derived `Unknown` means no
verdict at all** — no message has ever arrived, or the reporter said it cannot tell — and the
scheduler writes no `ProbeObservation` for it, because recording a failure there would drag a
never-reported reporter's uptime to 0.00% when §3.16 promises an em dash. *Rejected*: making
`ProbeResult.Succeeded` nullable with a separate "no judgement" path, which costs the same three
files but forces every future runner to answer a question only this kind has, and cannot express
the `Warning` row at all.

The derivation is one pure function over `(MessageSnapshot?, now)`, which is also the seam plan
009 reads. Overdue is checked first and beats the reported status, because a success from three
days ago is not evidence about today. `warning` reads `Unstable`; `none` — checked in, claiming
nothing — reads `Up` with the word "Reported", and counts as a success for uptime, because
checking in on time is the only claim a heartbeat makes.

### 3.26 The recurrence is the reporter's own promise, in one of two spellings

A reporter says when to expect the next message, either as an ISO 8601 duration (`PT25H`, `P1D`,
`P5Y`) or as an absolute instant. Never both — that is a 400. Homon stores one absolute
`NextExpectedAt`, computed from arrival, plus the raw declaration for the administrator to read
back.

Durations go through `System.Xml.XmlConvert.ToTimeSpan`, the framework's own parser, so Homon
ships no duration grammar of its own. The trade is its fixed-length rule: **a month is 30 days and
a year is 365**, exactly, so `P1M` is thirty days and not "the first of next month". That sentence
appears in the failure message and in `docs/message-reporting.md`, because it is the one surprise,
and a reporter that needs a real calendar sends the instant instead.

**There is no server-side grace period.** The reporter owns its own tolerance by declaring a
window wider than its period — "within 25 hours" for a nightly backup, which is exactly how the
requirement was phrased. *Rejected*: plan 008's `Grace` field, which would have Homon guessing a
number the reporter already knows.

**A reporter that declares nothing can never be overdue** — only its own `failure` or `warning`
can take it off green. That is a real foot-gun, so the administrator's page says so on the row
rather than leaving the cell blank: a reporter that silently forgot the field otherwise looks
monitored when it is not.

### 3.27 A message body is administrator-only unless its reporter says otherwise, and the newest one is never swept

A body is whatever a script piped into it. A restic log carries repository paths, hostnames and
snapshot ids — detail the phone-glancing reader §3.1 admits anonymously has no business seeing —
so `Reporter.BodyVisibility` defaults to `Administrator`, and `GET /reporters/{id}/messages` is
the only route in the application that returns a body in full. The administrator opts a reporter
in to `Reader` deliberately, one at a time, and even then only its *latest* body travels on
`GET /status`, capped at 2000 characters: that payload is polled every thirty seconds, and a
64 KiB command output on that interval is not a thing to put on a kitchen-counter dashboard.

The reader-facing `detail` line therefore carries status and timing words only — never the
reporter's name, description or body — and that is structural rather than a promise: the evaluator
is handed a `MessageSnapshot` which has no free text in it to leak.

A body over 64 KiB is **truncated, never rejected** (plan 008's decision, carried): a truncated
proof-of-run beats a failed report at 02:00, the tail is the half worth keeping because that is
where a shell command puts its summary and its errors, and Kestrel's 30 MB request limit stays the
real backstop. Truncation is measured in UTF-8 bytes and cuts on a character boundary, and the
marker it prepends is how a read knows it happened — there is no second column to disagree with
the text.

Messages are kept 32 days, swept hourly by a hosted service in the shape
`ProbeObservationRetentionService` already set (a kill switch, an options-bound window, a directly
callable sweep for the tests). Thirty-two rather than Monitoring's thirty: a calendar month plus
slack, so a monthly reporter's previous message outlives a 31-day month.

**The sweep never deletes a reporter's most recent message, however old it is.** This is
correctness, not tidiness. A reporter silent for 33 days is the one case the whole module exists
for, and deleting its last message would take `NextExpectedAt` with it — flipping the probe from a
loud red "overdue by 33 days" to a reassuring grey "no report received yet", exactly 32 days after
the thing died. *Rejected*: plan 008's prune-on-insert, which never runs at all for a reporter that
has stopped reporting.

### 3.28 An HTTP probe's latency is its time to first byte

An HTTP probe records how long `SendAsync` takes to return, and the 30-day sparkline plots it, as
ping's already did for round-trip time. The request is sent with `ResponseHeadersRead`, so
`SendAsync` completes once the response headers are in — strictly the last header byte rather than
the first, a difference not worth a custom `SocketsHttpHandler` callback. *Rejected*: instrumenting
the socket with `ConnectCallback` / `PlaintextStreamFilter` to catch the literal first byte; it is
far more code and would split the single named client that §3.17 and plan 003's Decision 4 rely on.
This supersedes plan 003's "Latency" note, which timed the whole request through the capped body
read — *rejected* here because that mixes server responsiveness with page size, and "is the service
getting slow to respond?" is the question a household has.

The body is still read and evaluated (`ExpectedBodyText` works as before) and the probe's own
`TimeoutSeconds` still bounds the whole request including the body; only the stopwatch stops
earlier. Redirects are inside the measurement, because the named client follows them within
`SendAsync` and a visitor waits for them too. Connection setup is sometimes inside it and sometimes
not: the handler is pooled and recycled, so some polls include DNS, TCP and TLS and others reuse a
warm connection, and the daily mean flattens that. There is deliberately no "fresh connection per
poll" setting.

Observations recorded before this change hold full-request time and are left to age out with the
30-day retention sweep, so for up to a month an HTTP sparkline mixes the two and usually shows a
step down. *Rejected*: a data migration nulling the old values — destructive, irreversible, and a
fix for a cosmetic effect that vanishes on its own.

`/status` and the dashboard gate the sparkline on an explicit allow-list of `Ping` and `Http`, on
both sides, not on "every kind with a latency": a message probe has none, and SMB and SNMP have not
decided what theirs would mean. When plans 004 and 005 land, each opts its kind in at both ends.
`ProbeObservation.LatencyMs` now means round-trip time for ping and time to first byte for HTTP, so
anything that compares latencies across probes must not treat them as one unit.

### 3.29 A probe has a page; its history is for readers, its configuration for administrators

Each probe has a page at `/probes/{id}`, reached from its name on the dashboard: state, 30-day
uptime, a latency graph for 24 hours, 7 days or 30 days, the 50 most recent polls, and — for an
administrator only — how the probe is configured (plan 023).

**Two sources, not one.** The history comes from a new Reader-gated `GET /api/v1/status/probes/{id}`
whose response has no host, path, URL or credential at all. The configuration block comes from the
existing `GET /api/v1/probes/{id}`, which stays administrator-only, and the SPA does not call it
unless the session is an administrator's, so a reader's browser never makes a request that would be
refused. *Rejected*: one endpoint whose shape varies with the caller — a nullable `configuration`
object that appears for administrators invites the next change to leak it, and duplicates
`ProbeResponse`. *Rejected*: showing readers the configuration, which would reverse plan 002's
Decision 8 (a probe's host is an internal hostname or LAN address, not fit for an anonymous
reader). `ProbeEndpoints` and its policies are unchanged. What the page does add for readers is each
recent poll's `Detail` string, the same kind of text `/status` already sends for the latest poll.

**One allow-list per side, shared by both uses.** Which kinds plot latency is still the §3.28
allow-list of `Ping` and `Http`, now written once on the server (`StatusEndpoints.PlotsLatency`,
used by the sparkline and by this endpoint) and once in the SPA (`plotsLatency` in `lib/status.ts`,
used by the dashboard and by the page). For any other kind every bucket's average is null while its
poll and failure counts are still filled in. Plans 004 and 005 opt a kind in by editing one line per
side.

**Three ranges, fixed bucket counts, every bucket present.**

| `range` | Window | Buckets | Bucket width |
| --- | --- | --- | --- |
| `24h` (default) | 24 h | 96 | 15 min |
| `7d` | 7 d | 168 | 1 h |
| `30d` | 30 d | 120 | 6 h |

Buckets are measured from `now - window`, as the sparkline's are, and the response holds all of
them, empty ones with `polls: 0`, so the client never infers a gap. A bucket's average is the mean
latency of its successful polls. An unknown `range` is a 400 keyed `range`; the page keeps the range
in the URL (`?range=7d`) so a range can be linked to. If `Monitoring:RetentionWindowDays` is below 30
the 30 d view simply shows empty early buckets; it is not clamped. The table is 50 polls whatever the
range, because tying it to the range could mean paging through 172,800 rows.

**Bucketed in memory.** The endpoint loads `(ObservedAt, Succeeded, LatencyMs)` for one probe and
window through the `(ProbeId, ObservedAt)` index and groups in C#, as `/status` does for every ping
and HTTP probe at once; at the 15-second minimum interval that is at most 172,800 small rows for the
30 d range. *Rejected*: translating the bucket arithmetic to SQL (`date_bin`) — unproven in this
Npgsql setup, and the cost is not yet a measured problem. It is the first thing to try if a
household reports a slow probe page.

The header's uptime is the 30-day figure computed exactly as `/status` computes it, so it matches
the dashboard row; the graph's caption carries the chosen range's own uptime, and both are labelled.

### 3.30 Admin pages share one set of primitives

Every admin page is built from the pieces the Probes page (plan 024) established, extracted in plan
025: the class strings in `components/admin-classes.ts`, `AdminPageHeader`, `AdminSection`,
`IconButton`/`MoveButtons`, `ConfirmStrip` and `KeyReveal`, with `Stamp`/`formatStamp` for times. A
new admin surface uses these rather than declaring its own class constants; the 40px floor on every
button is held by `e2e/layout.spec.ts`.

- **Summaries are computed in the browser.** The Admin home derives each section's one-line state
  (`lib/admin-summary.ts`) from the list responses the section pages already fetch, under the same
  cache keys. *Rejected*: a `/admin/summary` endpoint, which would restate seven derivations in C#
  for one page; reconsider when an eighth section arrives.
- **A watched reporter's Delete explains, it does not attempt.** The button stays enabled (a
  disabled one cannot say why, and `aria-disabled` makes Playwright refuse the click) and opens a
  neutral strip naming the probe. The page repeats the server's rule (`kind === 'message' && host
  === identifier`, `ReporterEndpoints.cs`); the server's 400 remains the backstop.
- **`formatStamp` follows the reader's locale and zone**, as `formatObservedAt` does; the year
  appears only when it is not the current one. Tests never pin its text.
- **Revoke stays on a reporter's paired key** on the API keys page. The page splits "Script keys"
  from "Reporter keys", but a styling pass does not remove a capability.

### 3.31 Alerts: the outage is probe state, delivery is an outbox, settings are a database row

Plan 026. Homon emails the household when a probe is declared Down and again when it is Up, saying
how long it was down.

- **The outage is probe state.** `Probe.DownSince` is set on entering Down (when none is open) and
  cleared on reaching Up, only inside `Probe.RecordObservation`, which returns a
  `ProbeOutageChange`. Down, Unstable, Down flapping sends no second mail; Unstable never mails; a
  message probe's Down, Unknown, Up recovers normally; `Pause`, `Unpause` and
  `ChangeFailureThreshold` do not touch it. Because status and `DownSince` are persisted, a
  restart sends nothing spurious. Downtime runs from when Homon declared Down to when it declared
  Up. *Rejected*: rebuilding the start from `ProbeObservations` (a message probe's `Succeeded` does
  not map to its status, a derived Unknown writes no row, rows are swept after 30 days); keeping it
  in memory (lost on restart).
- **Delivery is an outbox.** The scheduler adds an `AlertNotification` row in its existing single
  save, so the status change and the mail that announces it commit together. `AlertDispatcher`
  (a second `BackgroundService`) sends due `Pending` rows, retrying up to 5 times with 1, 2, 4 and
  8 minute backoff before `Failed`. Rows are written even when alerts are off and marked `Skipped`
  with the reason; finished rows older than 30 days are pruned at most hourly. *Rejected*: an
  in-memory channel, which a Resend outage or a restart would turn into silently lost mail and
  which the page could not show. Single-instance by design; two dispatchers would race, and
  `FOR UPDATE SKIP LOCKED` is the answer if that ever changes.
- **Settings are a singleton database row** (`AlertSettings`, the `WeatherSettings` pattern) and
  the Alerts admin page is the only source. The Resend key goes through `ISecretProtector` and is
  write-only on the wire (§3.17). `Email:Transport` (`Resend` | `Log`) is the one setting left in
  configuration; see §3.7.
- **Templates** are plain text plus a small HTML part built in code (`AlertMessageBuilder`), every
  interpolated value HTML-encoded, times in UTC (no time-zone setting yet), links built from
  `FrontEnd:PublicBaseUrl`.
- **A test send is an outbox row** (`POST /alerts/test` returns 202). It shows up in Recent alerts
  and turns Sent or Failed, which avoids a `role="status"` message the design brief reserves for the
  session check. It is sent even when alerts are off, never when they are not set up.
- **The Admin home stays client-side** (§3.30). Alerts is its eighth summary query, the threshold
  §3.30 named; one small GET is accepted.

## 4. Things this record does not yet decide

The calendar provider. It is a module plan's decision and will be recorded here when made.
