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

Until the Backups module ships its admin page, `create-api-key --name …` is the minter. A key
also carries a scope (read or read-write) and an optional expiry as of §3.13.

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
`IOptions<EmailOptions>` at resolve time, so a test's configuration override wins, and a
development machine never mails anyone. A Production host without a token refuses to start.

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

### 3.10 Phase 0 ships no style

The SPA renders semantic HTML with landmarks and accessible names and not one `className`.
Tailwind v4 and the shadcn toolchain are installed and unused. The design pass
(`docs/design-brief.md`, plan 012) starts from that blank sheet; the unit and Playwright
tests query by role and name, so they survive it — and must keep surviving it.

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
session needs to tell them apart. The distinction exists for the Backups module (008), whose
report endpoint is the first thing that should refuse a Read key.

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
Protection (`DataProtectionSecretProtector`), is the one seam every probe secret Homon ever
stores goes through — the HTTP bearer token or basic-auth password today, the SMB password
and calendar credentials later — under a single purpose string (`"Homon.Secrets.v1"`), not
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

## 4. Things this record does not yet decide

The calendar provider. It is a module plan's decision and will be recorded here when made.
