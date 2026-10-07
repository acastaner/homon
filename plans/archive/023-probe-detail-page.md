# Plan 023: Every probe has its own page — name, uptime, a large latency graph, recent polls, and (for administrators) its configuration

> **Executor instructions**: Follow this plan step by step. Run every verification command and
> confirm the expected result before moving on. If a STOP condition occurs, stop and report — do
> not improvise. The reviewer maintains `plans/README.md`'s status row unless told otherwise.
>
> **Drift check (run first)**:
> `git diff --stat fa5b2bd..HEAD -- src/Homon.Api/Endpoints/StatusEndpoints.cs src/Homon.Api/Endpoints/ProbeEndpoints.cs src/Homon.Web/src/App.tsx src/Homon.Web/src/lib/status.ts src/Homon.Web/src/lib/probes.ts src/Homon.Web/src/pages/dashboard-page.tsx src/Homon.Web/src/pages/dashboard-page.test.tsx src/Homon.Web/src/pages/admin-probes-page.tsx src/Homon.Web/e2e/helpers.ts docs/ARCHITECTURE.md docs/MODULES.md docs/design-brief.md`
> Empty output → proceed. Otherwise compare the "Current state" excerpts against the live code;
> any mismatch in a region this plan edits is a STOP condition.

## Status

- **Priority**: P3 — a feature the maintainer asked for, 2026-10-06.
- **Effort**: L
- **Risk**: LOW–MED. It adds one read endpoint and one route and needs no migration. The risk is
  privacy, not correctness: a probe's host must stay out of anything an anonymous reader can
  fetch (D1).
- **Depends on**: none (builds on 002, 003, 012, 021, 022 — all DONE)
- **Category**: direction (feature)
- **Planned at**: commit `fa5b2bd`, 2026-10-06
- **Requested by**: the maintainer, 2026-10-06. The three choices behind D1, D4 and D5 are theirs:
  configuration for administrators only, a graph plus a table, and a 24 h / 7 d / 30 d switch.

## Why this matters

The dashboard row is a glance: a chip, a 88×22px sparkline of 30 daily means, and "2 min ago".
When "Jellyfin" goes unstable there is nowhere to see *when* it started failing, whether latency
crept up first, or what the last few polls said. A probe page answers that with finer buckets,
real axes, and the raw polls. It also gives an administrator one place to read how a probe is
configured without opening its edit form.

## Decisions — implement these exactly

**D1. Two sources: a reader read-model and the existing admin read.** The history comes from a
**new** `GET /api/v1/status/probes/{id}`, gated `HomonPolicies.Reader` like `/status`. Its response
deliberately has **no host, path, URL, credential or any other configuration field**. The
configuration block comes from the **existing** `GET /api/v1/probes/{id}`
(`HomonPolicies.AdministratorOrApiKey`). The SPA calls it only when `useSession()` says
`kind === 'administrator'`. *Rejected*: one endpoint whose shape varies with the caller. A
nullable `configuration` object that appears for admins invites the next change to leak it, and it
duplicates `ProbeResponse`. *Rejected*: exposing configuration to readers. That reverses plan 002's
Decision 8 (`ProbeEndpoints.cs` remarks: Host is "an internal hostname or LAN IP not fit for an
anonymous reader").

**D2. The existing `Detail` strings are not a new exposure.** Each recent poll carries its
`ProbeObservation.Detail` (e.g. `HTTP 503`, a timeout line). `/status` already sends the latest one
to readers as `detail`. The page shows the same kind of string for older polls and adds no new
category of data.

**D3. Latency averages use the same kind allow-list as the sparkline, from one place per side.**
Ping plots RTT and HTTP plots TTFB (`docs/ARCHITECTURE.md` §3.28, plan 022 D6). Every other kind
gets `averageLatencyMs: null` in every bucket, while its poll and failure counts are still filled
in. On the server, extract `private static bool PlotsLatency(ProbeKind kind) => kind is
ProbeKind.Ping or ProbeKind.Http;` in `StatusEndpoints` and use it in **both**
`BuildSparklinesAsync` and the new endpoint. In the SPA, export `plotsLatency(kind)` from
`lib/status.ts` and use it in **both** `dashboard-page.tsx` and the new page. Plans 004/005 then
still opt in by editing one line per side.

**D4. A graph and a table.** The graph shows bucketed mean latency for the chosen range. The
table shows the **50 most recent polls, newest first, regardless of range**, under the heading
"Recent polls". *Rejected*: tying the table to the range, which would mean paging through up to
172,800 rows for 30 days at the 15 s minimum interval.

**D5. Three ranges, fixed bucket counts, dense buckets.**

| `range` | Window | Buckets | Bucket width |
| --- | --- | --- | --- |
| `24h` (default) | 24 h | 96 | 15 min |
| `7d` | 7 d | 168 | 1 h |
| `30d` | 30 d | 120 | 6 h |

The window ends at `TimeProvider.GetUtcNow()`, and buckets are measured from `windowStart = now −
window`, the same anchoring `BuildSparklinesAsync` uses. The response holds **every** bucket, empty
ones included (`polls: 0`), so the client never has to infer gaps. A bucket's `averageLatencyMs` is
the mean `LatencyMs` of its *successful* observations with a non-null latency, or null. `failures`
counts unsuccessful observations. An unknown `range` is a 400 validation problem keyed `range`; an
absent one means `24h`. The range lives in the page URL as `?range=7d`, so a range can be linked
to. An invalid value in the URL falls back to `24h` on the client. *Rejected*: one fixed 30-day
view, and a free-form `from`/`to`, both against the maintainer's choice of the switch. *Note*: if
`Monitoring:RetentionWindowDays` is configured below 30, the 30 d view simply shows empty early
buckets. Do not clamp it.

**D6. Bucket in memory, as `/status` already does.** Load `(ObservedAt, Succeeded, LatencyMs)` for
the one probe and window with `AsNoTracking()` through the existing `(ProbeId, ObservedAt)` index,
then group in C#. At worst that is 172,800 small rows for one probe, the same order of magnitude
`/status` already pulls for every ping and HTTP probe at once. *Rejected*: translating the bucket
arithmetic to SQL (`date_bin`). It is unproven in this codebase's Npgsql setup, and the cost is not
yet a measured problem.

**D7. Two uptimes, both labelled.** The header shows the **30-day** uptime, computed exactly as
`/status` computes it (`ProbeUptimeCalculator.Calculate` over `RetentionWindowDays`), so it matches
the dashboard row. The graph caption shows **this range's** uptime (`rangeUptimePercent`, from the
buckets' polls and failures). Both are null when there are no polls.

**D8. The chart is hand-drawn SVG, not a library.** The repo has no chart dependency
(`src/Homon.Web/package.json`), and `Sparkline` is hand-drawn. The plot `<svg>` uses
`preserveAspectRatio="none"` so it stretches to the panel width, and every stroke carries
`vector-effect="non-scaling-stroke"`. **No text and no circles inside the SVG**, because both would
distort. Axis labels are HTML around it. The y axis starts at **0** and ends at a "nice" ceiling,
which is honest for a chart this size, unlike the sparkline's min–max fit. The line breaks at
buckets with no average. A lone bucket between gaps draws as a short horizontal tick. A bucket with
failures gets a bar along the bottom: `--color-down` when every poll failed, `--color-unstable`
when only some did. The line stroke is `var(--color-muted)`, 2px, matching the sparkline
(`docs/design-brief.md`: muted is the "sparkline stroke" token). No hover tooltip in this plan
(deferred — see Maintenance notes). The table is the accessible form of the data, and the `<svg>`
is `role="img"` with a summary `aria-label`.

**D9. The dashboard's probe name becomes a link to `/probes/{id}`.** The link text is exactly the
probe name, so every existing `getByText(name)`, `getByRole('row', { name: /…/ })` and the
`closest('tr')` helper keep working. It uses the repo's existing text-link classes. Nothing else in
the row changes.

**D10. Message probes get the page too, minus the graph.** Their header shows the chip word the
dashboard shows (`Overdue` / `Succeeded` / …) and the reader-visible message body, when the server
sends one, unclamped because it is capped at 2000 characters server-side. The "Latency" section
and the table's Latency column are absent (D3). The server fills `message` with the same
`BuildMessageSummariesAsync` that `/status` uses.

**D11. Unknown probe id → 404 → a "no longer exists" panel.** Not the dashboard catch-all: a
deleted probe's bookmark should say so. The detail query must not retry a 404, because TanStack's
default of 3 retries would leave the reader on a blank page for about 7 s.

## Current state

### API — `src/Homon.Api/Endpoints/StatusEndpoints.cs`

- `MapStatusEndpoints` maps one route:
  ```csharp
  parent.MapGet("/status", GetStatusAsync)
      .RequireAuthorization(HomonPolicies.Reader)
      .WithName("GetStatus")
      .WithSummary("The dashboard's read model: totals, every probe, groups.");
  ```
- `GetStatusAsync(HomonDbContext database, IOptionsMonitor<MonitoringOptions> options, TimeProvider timeProvider, CancellationToken cancellationToken)`
  computes `windowStart = now - TimeSpan.FromDays(monitoring.RetentionWindowDays)` and uptime
  through `ProbeUptimeCalculator.Calculate(success, total)` (returns `double?`, two decimals).
- `BuildMessageSummariesAsync(HomonDbContext database, List<Probe> probes, DateTimeOffset now, CancellationToken)`
  → `Dictionary<Guid, ProbeMessageResponse>` — reusable with a one-element list.
- `BuildSparklinesAsync` filters with `.Where(p => p.Kind is ProbeKind.Ping or ProbeKind.Http)`.
  That is the line D3 replaces with `PlotsLatency(p.Kind)`. Its doc comment names the allow-list;
  keep that comment and add "shared with the probe page through `PlotsLatency`".
- Records at the bottom: `StatusResponse`, `StatusTotals`, `ProbeStatusResponse`,
  `ProbeMessageResponse(MessageStatus Status, bool Overdue, string? Body)`, `ProbeGroupSummary`.
- Endpoint registration is `v1.MapStatusEndpoints();` in `src/Homon.Api/Program.cs:537`. The new
  route goes inside `MapStatusEndpoints`, so `Program.cs` does not change.

### Domain facts

- `ProbeObservation { long Id; Guid ProbeId; DateTimeOffset ObservedAt; bool Succeeded; double? LatencyMs; string? Detail; }`
  with index `(ProbeId, ObservedAt)` (`ProbeObservationConfiguration.cs:32`).
- `Probe` carries `Name`, `Kind`, `Status`, `LastDetail`, `LastObservedAt`, `Host` (**must not
  appear** in the new response), `PollInterval`, `FailureThreshold`, `IsPaused`, `HttpOptions`.
- `ProbeEndpoints.GetProbeAsync` (`GET /probes/{id}`, `AdministratorOrApiKey`) returns
  `ProbeResponse(Id, Name, Host, Kind, PollIntervalSeconds, FailureThreshold, IsPaused, Position,
  Status, LastDetail, LastCheckedAt, GroupIds, Http)` where `Http` is
  `HttpProbeOptionsResponse(Method, Path, UseHttps, IgnoreCertificateErrors, TimeoutSeconds,
  ExpectedStatusCode, ExpectedStatusCodeNegate, ExpectedBodyText, ExpectedBodyTextNegate,
  Credential: HttpCredentialResponse(Type, Username, HasSecret))`. The secret is never on the wire.
  **No change to this file.**

### SPA

- `src/Homon.Web/src/App.tsx` — reader routes are statically imported: `index` → `DashboardPage`,
  `pages/:slug` → `PagePage`, `weather` → `WeatherPage`; `path="*"` → `DashboardPage`.
- `src/Homon.Web/src/pages/dashboard-page.tsx`:
  - `:192` `<td className="px-4 py-3 text-[15px] font-semibold">{probe.name}</td>`
  - `:210` `{probe.kind === 'ping' || probe.kind === 'http' ? <Sparkline samples={probe.sparkline} state={probe.state} /> : null}`
  - `:83-93` private `detailClassName(state)`; `:227-233` private `messageChipWord(probe)`.
  - Link style used across the app: `text-text underline decoration-line-strong underline-offset-[3px] hover:decoration-text`.
- `src/Homon.Web/src/lib/status.ts` — React-free types and helpers: `ProbeKind`, `ProbeState`,
  `StatusProbe`, `StatusProbeMessage`, `STATUS_QUERY_KEY = ['status']`, `useStatus()` (30 s
  `refetchInterval`, `refetchOnWindowFocus: true`), `formatCheckedAt(lastCheckedAt, now)`.
- `src/Homon.Web/src/lib/probes.ts` — `Probe`, `HttpProbeOptionsResponse`, `fetchProbes`,
  `useProbes()`. Has no single-probe hook yet.
- `src/Homon.Web/src/lib/probe-groups.ts` — `useProbeGroups()` (`GET /probe-groups`, Reader policy).
- `src/Homon.Web/src/lib/session.ts` — `useSession()` → `Session | null`, `kind: 'administrator' | 'user' | 'apiKey'`.
- `src/Homon.Web/src/lib/format-uptime.ts` — `formatUptime(value)` → `"98.32%"` or `"—"`.
- `src/Homon.Web/src/lib/api.ts` — `apiFetch<T>(path)` prefixes `/api/v1`; throws `ApiError` with `.status`.
- `src/Homon.Web/src/pages/admin-probes-page.tsx:23-29` — private `KIND_LABELS: Record<ProbeKind, string>`
  (`'Ping (ICMP)'`, `'HTTP/HTTPS'`, `'SMB/CIFS'`, `'SNMP'`, `'Message (a reporter pushes to us)'`).
- `src/Homon.Web/src/pages/weather-page.tsx` is **the exemplar for a reader page**: copied visual
  constants at the top (`PAGE_H1`, `PANEL`, `SECTION_LABEL`, `TABLE_HEAD`, `TH`, `TD`, with a
  comment saying they are copied on purpose), `useDocumentTitle(pageTitle(...))`, `<h2
  className={SECTION_LABEL}>` section labels, tables inside `` `${PANEL} overflow-x-auto` `` with
  an `aria-label`, and empty states as `` `${PANEL} border-dashed border-line-strong px-4 py-3.5` ``
  panels.
- `src/Homon.Web/src/components/sparkline.tsx` — the drawing style to match. **Do not modify it.**
- `src/Homon.Web/src/test/fetch.ts` — `stubFetch(routes)` keys on the **full string passed to
  fetch, query string included**: stub `'/api/v1/status/probes/p1?range=24h'`, not the bare path.
  Unmatched paths throw. Use `{ status: 204 }` for an anonymous `/api/v1/auth/session`.
- `src/Homon.Web/src/test/render.tsx` — `renderWithProviders(ui, { initialEntries })`. A page that
  reads a route param is rendered inside `<Routes><Route path="/probes/:id" element={<ProbePage />} /></Routes>`
  (see `pages/page-page.test.tsx:30`).

### Tests and e2e

- `tests/Homon.Api.Tests/StatusEndpointTests.cs` — the pattern: `[DatabaseFact]`,
  `IClassFixture<ApiDatabaseFactory>`, `TestClient.Create(factory)`, `client.SignInAsync()`, seeding
  `ProbeObservation` rows through a scope's `HomonDbContext`, a private `CreateProbeAsync`, and a
  private `ConfiguredFactory : HomonApiFactory` forcing `Auth:RequireSignInForReaders=true` (lines
  266-279).
- `src/Homon.Web/e2e/dashboard-groups.spec.ts` — seeding probes through `request.post('/api/v1/probes')`
  and **pausing them immediately** (the scheduler is live in e2e), with cleanup in `afterEach`. The
  default storage state is the administrator. `e2e/pages.spec.ts:63` shows an anonymous context:
  `browser.newContext({ baseURL, storageState: { cookies: [], origins: [] } })`.
- `e2e/helpers.ts` — `expectNoHorizontalOverflow(page)`, `expectTappable(page, selector)` (40px floor).
- `e2e/contrast.spec.ts` — axe `color-contrast` via `new AxeBuilder({ page }).withRules(['color-contrast']).analyze()`.

### Docs

- `docs/ARCHITECTURE.md` — the last decision is `### 3.28 An HTTP probe's latency is its time to
  first byte` (line ~874), followed by `## 4. Things this record does not yet decide`. The new
  decision is §3.29. Confirm with `grep -n '^### 3\.' docs/ARCHITECTURE.md | tail -1`, and take
  the next number if it has moved.
- `docs/MODULES.md` — the Monitoring entry (around line 27) describes the ping RTT and HTTP TTFB
  sparkline.
- `docs/design-brief.md:266` — the `**Sparkline.**` component rule. The new chart gets its own
  short rule next to it.

**Conventions** (from `CLAUDE.md`): comments carry the reasoning and name the rejected alternative.
C# is built with `TreatWarningsAsErrors`, and `IDE0055` is an error, so the Release build is the
formatting gate. Code uses file-scoped namespaces, `ArgumentNullException.ThrowIfNull` on public
entry points, `TypedResults`, and RFC 9457 problems. TypeScript files are kebab-case with named
exports (`App.tsx` is the only default) and use the `@/` alias. Tests stub `fetch` with
`src/test/fetch.ts`; there is no MSW. Unit and e2e tests query by role and accessible name. Keep
every heading and table name in this plan **exactly** as written, because the tests below depend
on them.

## Commands you will need

Run the suites **one at a time**. The combined `./ci/run-ci.sh` can abort on this machine with
`Internal CLR error (0x80131506)`, which is environmental; retry once. Never pipe the gate into
`tail`, because you would see tail's exit code instead of the gate's.

| Purpose | Command | Expected on success |
| --- | --- | --- |
| Web (npm ci, lint, build = typecheck, vitest) | `./ci/run-ci.sh web; echo "exit=$?"` | `exit=0` |
| API (Release build, migrate, tests, 0 skips) | `./ci/run-ci.sh api; echo "exit=$?"` | `exit=0`, 0 skipped |
| e2e (Playwright, two viewports) | `./ci/run-ci.sh e2e; echo "exit=$?"` | `exit=0` |

## Suggested executor toolkit

- `dotnet-webapi` skill when writing the endpoint (TypedResults / validation-problem shape).
- `vercel-react-best-practices` when writing the page's hooks.

## Scope

**In scope** (the only files you modify or create):
- `src/Homon.Api/Endpoints/StatusEndpoints.cs`
- `tests/Homon.Api.Tests/ProbeDetailEndpointTests.cs` (create)
- `src/Homon.Web/src/lib/status.ts` — add `plotsLatency`, `PROBE_KIND_LABEL`, `messageChipWord`
- `src/Homon.Web/src/lib/probes.ts` — add `useProbe`
- `src/Homon.Web/src/lib/probe-detail.ts` + `probe-detail.test.ts` (create)
- `src/Homon.Web/src/components/latency-chart.tsx` + `latency-chart.test.tsx` (create)
- `src/Homon.Web/src/pages/probe-page.tsx` + `probe-page.test.tsx` (create)
- `src/Homon.Web/src/App.tsx` — one route
- `src/Homon.Web/src/pages/dashboard-page.tsx` — name link, `plotsLatency`, import `messageChipWord`
- `src/Homon.Web/src/pages/dashboard-page.test.tsx` — one new case
- `src/Homon.Web/src/pages/admin-probes-page.tsx` — replace local `KIND_LABELS` with `PROBE_KIND_LABEL` (no other change)
- `src/Homon.Web/e2e/probe-page.spec.ts` (create)
- `docs/ARCHITECTURE.md`, `docs/MODULES.md`, `docs/design-brief.md`
- `plans/README.md` (this plan's row only, if told to)

**Out of scope** (do NOT touch):
- `src/Homon.Api/Endpoints/ProbeEndpoints.cs` and its policy (D1). The admin read is used as it is.
- `HomonPolicies`, `ReaderHandler`, anything under `Authentication/`.
- `ProbeObservation`, `Probe`, migrations, `MonitoringOptions`. No storage change.
- `components/sparkline.tsx`, `components/status-chip.tsx`. Reuse them, do not alter them.
- `components/refresh-indicator.tsx`. Its Refresh button already calls `invalidateQueries()`
  with no key, which refetches the new query too.
- `e2e/helpers.ts` `READER_ROUTES`. A probe page needs a seeded id, so it gets its own spec instead.
- Every other existing e2e spec, especially `layout`, `contrast`, `dashboard-groups`,
  `dashboard-collapse`, `refresh` and `reporters`. They must pass **unchanged**.
- `src/components/ui/*` (the unused shadcn primitives).
- `plans/archive/**`.

## Git workflow

- Work in this checkout, `/home/acastaner/Git/homon`. **Never create a git worktree**, including
  the Agent tool's `isolation: "worktree"`.
- Branch: `plan/023-probe-detail-page`, from `main`. Leave it checked out when you are done. Do
  not switch back to `main`, merge, push, stash or delete branches.
- One commit per step, in the repo's style, e.g.
  `Status: a reader read-model for one probe's history (plan 023)`,
  `Web: the probe page (plan 023)`.

## Steps

### Step 1: The reader endpoint `GET /api/v1/status/probes/{id}`

In `StatusEndpoints.cs`:

1. Add `private static bool PlotsLatency(ProbeKind kind) => kind is ProbeKind.Ping or ProbeKind.Http;`
   with a doc comment explaining that it is the one server-side allow-list (plan 022 D6, plan 023
   D3). Change `BuildSparklinesAsync`'s filter to `.Where(p => PlotsLatency(p.Kind))`.
2. Add the ranges as a private static table:
   ```csharp
   /// <summary>The probe page's three ranges (plan 023, D5): window, bucket count. Width = window / count.</summary>
   private static readonly Dictionary<string, (TimeSpan Window, int Buckets)> ProbeHistoryRanges =
       new(StringComparer.Ordinal)
       {
           ["24h"] = (TimeSpan.FromHours(24), 96),
           ["7d"] = (TimeSpan.FromDays(7), 168),
           ["30d"] = (TimeSpan.FromDays(30), 120),
       };

   private const string DefaultProbeHistoryRange = "24h";

   /// <summary>How many polls the probe page's table lists, newest first (plan 023, D4).</summary>
   private const int RecentObservationLimit = 50;
   ```
3. Map the route in `MapStatusEndpoints`, after `/status`:
   ```csharp
   parent.MapGet("/status/probes/{id:guid}", GetProbeHistoryAsync)
       .RequireAuthorization(HomonPolicies.Reader)
       .WithName("GetProbeHistory")
       .WithSummary("One probe's page: state, uptime, bucketed latency for a range, recent polls. Never its configuration.");
   ```
4. Implement `GetProbeHistoryAsync(Guid id, string? range, HomonDbContext database, IOptionsMonitor<MonitoringOptions> options, TimeProvider timeProvider, CancellationToken cancellationToken)`
   returning `Task<Results<Ok<ProbeHistoryResponse>, NotFound, ValidationProblem>>`:
   - `range ??= DefaultProbeHistoryRange`. If it is not a key of `ProbeHistoryRanges` →
     `TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["range"] = ["Range must be '24h', '7d' or '30d'."] })`.
   - `var probe = await database.Probes.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);`
     null → `TypedResults.NotFound()`.
   - `now = timeProvider.GetUtcNow()`. 30-day uptime (D7): count `Total` and `Success` of this
     probe's observations with `ObservedAt >= now - TimeSpan.FromDays(monitoring.RetentionWindowDays)`
     and pass them to `ProbeUptimeCalculator.Calculate`.
   - Range rows (D6): `windowStart = now - window`;
     `database.ProbeObservations.AsNoTracking().Where(o => o.ProbeId == id && o.ObservedAt >= windowStart).Select(o => new { o.ObservedAt, o.Succeeded, o.LatencyMs }).ToListAsync(...)`.
   - Build `bucketCount` buckets. The index is
     `Math.Clamp((int)((o.ObservedAt - windowStart) / bucketWidth), 0, bucketCount - 1)`, exactly as
     in `BuildSparklinesAsync`. For each index `i`: `Start = windowStart + bucketWidth * i`,
     `Polls`, `Failures = count(!Succeeded)`, and `AverageLatencyMs = PlotsLatency(probe.Kind) && any
     successful row with LatencyMs != null ? mean of those : null`.
   - `rangeUptimePercent = ProbeUptimeCalculator.Calculate(totalPolls - totalFailures, totalPolls)`.
   - Recent polls: `database.ProbeObservations.AsNoTracking().Where(o => o.ProbeId == id).OrderByDescending(o => o.ObservedAt).ThenByDescending(o => o.Id).Take(RecentObservationLimit)`,
     projected to `ProbeObservationResponse`. Set `LatencyMs` to null when `!PlotsLatency(probe.Kind)`.
   - `message = (await BuildMessageSummariesAsync(database, [probe], now, cancellationToken)).GetValueOrDefault(probe.Id)`.
     It takes a `List<Probe>`; a collection expression satisfies it.
5. Add the records next to the others, each with a doc comment. `ProbeHistoryResponse`'s comment
   must say why it has **no** `Host` (D1):
   ```csharp
   public sealed record ProbeHistoryResponse(
       Guid Id, string Name, ProbeKind Kind, ProbeStatus State, string? Detail,
       DateTimeOffset? LastCheckedAt, double? UptimePercent,
       string Range, DateTimeOffset WindowStart, int BucketSeconds, double? RangeUptimePercent,
       LatencyBucketResponse[] Latency, ProbeObservationResponse[] RecentObservations,
       ProbeMessageResponse? Message);

   public sealed record LatencyBucketResponse(DateTimeOffset Start, double? AverageLatencyMs, int Polls, int Failures);

   public sealed record ProbeObservationResponse(DateTimeOffset ObservedAt, bool Succeeded, double? LatencyMs, string? Detail);
   ```
   `BucketSeconds = (int)bucketWidth.TotalSeconds`. `Detail` / `LastCheckedAt` come from
   `probe.LastDetail` / `probe.LastObservedAt`, as in `ProbeStatusResponse`.
6. Update the class `<summary>` to mention the second route.

**Test first.** Create `tests/Homon.Api.Tests/ProbeDetailEndpointTests.cs`, modelled on
`StatusEndpointTests` (same usings, `IClassFixture<ApiDatabaseFactory>`, its own private
`CreateProbeAsync` and `ConfiguredFactory`). Name the class `ProbeDetailEndpointTests`. Cases
(`[DatabaseFact]` unless noted):

1. `An_unknown_probe_is_404` — `GET /api/v1/status/probes/{Guid.NewGuid()}` → 404.
2. `An_unknown_range_is_a_validation_problem_naming_range` — `?range=1y` → 400, and the body's
   `errors` has a `range` key.
3. `The_default_range_is_24h_in_96_buckets_of_15_minutes` — no `range` → `range == "24h"`,
   `bucketSeconds == 900`, `latency` length 96.
4. `Seven_and_thirty_days_have_168_and_120_buckets` — `7d` → 168 / 3600; `30d` → 120 / 21600.
5. `A_bucket_averages_successful_latency_and_counts_every_poll` — a ping probe with three
   observations at `now − 35/36/37 min`: success 10 ms, success 20 ms, failure. Those offsets
   sit inside `[now−45m, now−30m)` even with the server's clock a few ms later; do not use exact
   15-minute multiples. Assert exactly one bucket has `polls == 3`, its `failures == 1` and
   `averageLatencyMs == 15.0`, and the sum of all `polls` is 3.
6. `Range_uptime_and_thirty_day_uptime_use_their_own_windows` — add a failure at `now − 3 days`
   to case 5's seed (fresh probe). `24h` → `rangeUptimePercent == 66.67`, `uptimePercent == 50.0`.
7. `Recent_polls_are_the_newest_fifty_newest_first` — 55 observations at `now − 1..55 min` with
   distinct latencies → `recentObservations` length 50, the first is the `−1 min` one, and the
   list is strictly descending by `observedAt`.
8. `The_response_never_carries_the_host` — create a probe with host `do-not-leak.test`, read the
   response as a string, and assert it does not contain `do-not-leak.test` and that no property
   is named `host`.
9. `An_anonymous_reader_can_read_it` — `TestClient.Create(factory)` **without** `SignInAsync`;
   the probe is created with a signed-in client first → 200.
10. `[Fact]` `It_requires_sign_in_when_readers_must` — `ConfiguredFactory`, anonymous,
    `GET /api/v1/status/probes/{Guid.NewGuid()}` → 401. Mirror `StatusEndpointTests`' equivalent;
    no database is needed.

**Verify**: `./ci/run-ci.sh api; echo "exit=$?"` → `exit=0`, 0 skipped, all ten new tests
passed, and every existing `StatusEndpointTests` case (the sparkline ones especially) still passes.

### Step 2: SPA plumbing — `lib/status.ts`, `lib/probes.ts`, `lib/probe-detail.ts`

1. `lib/status.ts`: add and export
   - `plotsLatency(kind: ProbeKind): boolean` → `kind === 'ping' || kind === 'http'`, with a comment
     naming the server twin `StatusEndpoints.PlotsLatency` (D3).
   - `PROBE_KIND_LABEL: Record<ProbeKind, string>`, moved verbatim from `admin-probes-page.tsx`'s
     `KIND_LABELS`. Delete the local copy there and import this one; no other edit to that file.
   - `messageChipWord(probe: Pick<StatusProbe, 'kind' | 'message'>): string | undefined`, moved
     verbatim with its doc comment from `dashboard-page.tsx`, which now imports it. Add
     `import { MESSAGE_STATUS_WORD } from '@/lib/reporters'` as a value import. `status.ts`
     already imports a type from that module.
2. `lib/probes.ts`: add
   ```ts
   export function useProbe(id: string, options: { enabled: boolean }) {
     return useQuery({ queryKey: [...PROBES_QUERY_KEY, id], queryFn: () => apiFetch<Probe>(`/probes/${id}`), enabled: options.enabled })
   }
   ```
   Add a comment that this is administrator-only (D1) and that `enabled` is how the page avoids a
   401/403 for readers. The key nests under `PROBES_QUERY_KEY`, so the existing probe mutations'
   invalidation reaches it.
3. Create `lib/probe-detail.ts`:
   - Types mirroring Step 1's records: `ProbeHistory`, `LatencyBucket`, `ProbeObservationRow`.
     `kind: ProbeKind`, `state: ProbeState`, `message: StatusProbeMessage | null`.
   - `export const PROBE_RANGES = ['24h', '7d', '30d'] as const; export type ProbeRange = (typeof PROBE_RANGES)[number]`.
   - `PROBE_RANGE_LABEL: Record<ProbeRange, string>` = `{ '24h': '24 hours', '7d': '7 days', '30d': '30 days' }`.
     These are the button names.
   - `parseProbeRange(value: string | null): ProbeRange` returns the value if it is one of the
     ranges, otherwise `'24h'`.
   - `useProbeHistory(id: string, range: ProbeRange)`: `useQuery` with
     `queryKey: [...STATUS_QUERY_KEY, 'probe', id, range]`,
     `queryFn: () => apiFetch<ProbeHistory>(`/status/probes/${id}?range=${range}`)`,
     `refetchInterval: 30_000`, `refetchOnWindowFocus: true`, `placeholderData: keepPreviousData`
     (no flash when switching range), and
     `retry: (failureCount, error) => !(error instanceof ApiError && error.status === 404) && failureCount < 3` (D11).
     Comment that nesting under `['status']` means every probe mutation's existing
     `invalidateQueries({ queryKey: STATUS_QUERY_KEY })` refreshes it as well. `RefreshIndicator`
     observes the exact key `['status']`, so this nested key does not disturb it.
   - `formatLatency(ms: number): string` returns `"0.8 ms"` below 10 (one decimal), `"12 ms"` from
     10 to 999 (rounded), and `"1.23 s"` at 1000 or more (two decimals).
   - `formatDuration(seconds: number): string` returns `"15 s"`, `"1 min"`, `"1 min 30 s"`,
     `"1 h"`, `"6 h"`, `"1 day"`, `"2 days"`. Use whole units only: days if divisible by 86400,
     else hours if divisible by 3600, else minutes plus remaining seconds. It is used for poll
     intervals **and** bucket widths.
   - `niceCeiling(value: number): number` returns 1 when `value <= 0`. Otherwise it is the
     smallest of `[1, 2, 2.5, 5, 10] × 10^floor(log10(value))` that is ≥ value.
   - `formatObservedAt(iso: string): string` →
     `new Date(iso).toLocaleString(undefined, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })`.
     It is the reader's own locale and time zone, so do **not** assert its exact text in tests.

**Test** — `lib/probe-detail.test.ts` (model on `lib/format-uptime.test.ts`): the `formatLatency`
cases `0.84→"0.8 ms"`, `12.4→"12 ms"`, `999.4→"999 ms"`, `1234→"1.23 s"`; the `formatDuration`
cases above plus `90→"1 min 30 s"`, `900→"15 min"`, `21600→"6 h"`; `niceCeiling`
`0→1, 7→10, 12→20, 23→25, 41→50, 100→100, 0.3→0.5`; and `parseProbeRange` `null→'24h'`,
`'7d'→'7d'`, `'1y'→'24h'`.

**Verify**: `./ci/run-ci.sh web; echo "exit=$?"` → `exit=0`. The admin probes page tests still
pass unchanged, which proves the `PROBE_KIND_LABEL` move was verbatim.

### Step 3: `components/latency-chart.tsx`

`export function LatencyChart({ buckets, bucketSeconds, rangeLabel }: { buckets: LatencyBucket[]; bucketSeconds: number; rangeLabel: string })`.
`rangeLabel` is e.g. `'24 hours'`. Follow D8 exactly:

- `values` = the non-null `averageLatencyMs`. If there are none, render
  `<p className="text-[13.5px] text-muted">No successful polls in the last {rangeLabel}.</p>`
  inside a dashed empty-state panel, and stop.
- `yMax = niceCeiling(Math.max(...values))`; `n = buckets.length`. The SVG is
  `viewBox={`0 0 ${n} 100`}` with `preserveAspectRatio="none"`, `className="block h-48 w-full sm:h-56"`,
  `role="img"`, and `aria-label` = `` `Latency over the last ${rangeLabel}: lowest ${formatLatency(min)}, highest ${formatLatency(max)}, latest ${formatLatency(last)}` ``,
  plus `` `, ${failed} failed polls` `` when the failures sum is > 0.
- Bucket `i`'s x centre is `i + 0.5`, and `y(v) = 100 - (v / yMax) * 100`.
- Three horizontal gridlines at y = 0, 50 and 100: `<line … stroke="var(--color-line)" strokeWidth={1} vectorEffect="non-scaling-stroke" />`.
- The line is one `<path>` whose `d` starts a new `M` at every run of consecutive non-null
  buckets. A run of length 1 draws `M i+0.2 y L i+0.8 y`. Attributes: `fill="none"`,
  `stroke="var(--color-muted)"`, `strokeWidth={2}`, `strokeLinejoin="round"`,
  `strokeLinecap="round"`, `vectorEffect="non-scaling-stroke"`, `data-testid="latency-line"`.
- For each bucket with `failures > 0`: `<rect x={i + 0.1} y={94} width={0.8} height={6} data-failures="" fill={failures === polls ? 'var(--color-down)' : 'var(--color-unstable)'} />`.
- Layout: a `<figure>` holding a two-column grid (`grid grid-cols-[auto_1fr] gap-x-3`). The left
  column has the y labels top and bottom (`formatLatency(yMax)` and `0 ms`), `mono text-[12px]
  text-muted`, `flex flex-col justify-between`. The right column has the SVG and, below it, a
  `flex justify-between` row of `mono text-[12px] text-muted` x labels: the window start,
  formatted `HH:mm` for 24 h and as `toLocaleDateString(undefined, { month: 'short', day:
  'numeric' })` otherwise, and `Now`. A `<figcaption className="text-[12.5px] text-muted">` reads
  `` `Mean latency per ${formatDuration(bucketSeconds)}. Bars along the bottom mark failed polls.` ``
- A comment block explains: why `non-scaling-stroke` and no in-SVG text (D8), why y starts at 0,
  and that this is **not** `Sparkline` grown up. Sparkline fits min–max for a 22px glance, and
  this chart answers "how slow, in ms".

**Test** — `components/latency-chart.test.tsx` (model on `components/sparkline.test.tsx`):
- all-null buckets → the "No successful polls in the last 24 hours." text, and no `role="img"`;
- `[10, 20, null, 30, 40]` averages → `getByRole('img')` name contains `lowest 10 ms`,
  `highest 40 ms` and `latest 40 ms`, and `latency-line`'s `d` has exactly 2 `M` commands;
- a lone value between nulls → its segment is a tick (the `d` contains one `M` and one `L` for it);
- buckets `{polls:2,failures:2}` and `{polls:3,failures:1}` → two `rect[data-failures]`, with
  fills `var(--color-down)` and `var(--color-unstable)` respectively, and the aria-label ends
  with `, 3 failed polls`.

**Verify**: `./ci/run-ci.sh web; echo "exit=$?"` → `exit=0`.

### Step 4: `pages/probe-page.tsx` and the route

Create `export function ProbePage()`. Copy the visual constants from `weather-page.tsx`
(`PAGE_H1` is not needed because the h1 shares a row; `PANEL`, `SECTION_LABEL`, `TABLE_HEAD`,
`TH`, `TD`), with the same "copied rather than imported" comment. Copy `detailClassName` from
`dashboard-page.tsx` with a comment naming its source. Structure, top to bottom:

1. `const { id = '' } = useParams()`, `const [searchParams, setSearchParams] = useSearchParams()`,
   and `const range = parseProbeRange(searchParams.get('range'))`. Then `useProbeHistory(id,
   range)`, `useSession()`, and
   `const isAdministrator = session.data?.kind === 'administrator'`.
   `useDocumentTitle(pageTitle(history.data?.name ?? 'Probe'))`.
2. **404** (`error instanceof ApiError && error.status === 404`): `<h1>Probe not found</h1>`, then
   a panel reading `This probe no longer exists.` with a RouterLink `Back to the dashboard` → `/`.
   Other errors: a panel `This probe could not be loaded.`. Loading with no data: render nothing
   (`null`), as `WeatherPage` does.
3. **Back link**: `<RouterLink to="/" className="inline-flex h-10 items-center gap-1 text-[13.5px] text-muted hover:text-text">`
   containing `<ChevronLeft aria-hidden="true" …/>` and `Dashboard`. Its accessible name is
   "Dashboard". The 40px height is for phones.
4. **Header** (`border-b border-line-strong pb-4`, `flex flex-col gap-2`):
   - a row with the `<h1 className="text-[22px] font-semibold -tracking-[0.01em] sm:text-[26px]">{name}</h1>`
     and `<StatusChip state={state} word={messageChipWord(history)} />`;
   - `<p className="mono text-[13px] text-muted sm:text-sm">{formatUptime(uptimePercent)} uptime, 30 days · checked {formatCheckedAt(lastCheckedAt, new Date())}</p>`.
     Note that `formatCheckedAt` returns `Never` or `N min ago`. Keep this as **flat text**, for
     the reason `dashboard-page.tsx:488-494` gives;
   - when `detail` is not null: `<p className={`text-[14px] ${detailClassName(state)}`}>{detail}</p>`;
   - when `message?.body` is not null: `<p className="text-[14px] text-muted break-words whitespace-pre-wrap">{message.body}</p>` (D10).
5. **Latency** (only when `plotsLatency(kind)`): `<section aria-labelledby="probe-latency-heading" className="flex flex-col gap-3">`
   with `<h2 id="probe-latency-heading" className={SECTION_LABEL}>Latency</h2>`, then
   - a range switch: `<div role="group" aria-label="Range" className="flex gap-2">`. For each
     `PROBE_RANGES` entry, render a `<button type="button" aria-pressed={r === range}
     onClick={() => setSearchParams(r === '24h' ? {} : { range: r })}>{PROBE_RANGE_LABEL[r]}</button>`
     styled like the dashboard's `HEADER_BUTTON`: `h-10` (the 40px floor is enforced by e2e) and
     the `text-muted border-line` pair. The pressed state is `border-line-strong text-text`.
     `aria-pressed` is right **here**, unlike the dashboard's Arrange button, because the names
     are stable and only the pressed state changes. Say so in a comment;
   - `<div className={`${PANEL} p-4`}><LatencyChart buckets={latency} bucketSeconds={bucketSeconds} rangeLabel={PROBE_RANGE_LABEL[range]} /></div>`;
   - `<p className="mono text-[13px] text-muted">{formatUptime(rangeUptimePercent)} uptime over the last {PROBE_RANGE_LABEL[range]} · {totalPolls} polls</p>`.
6. **Recent polls**: `<section aria-labelledby="probe-polls-heading">` with `<h2 … >Recent polls</h2>`.
   If there are no rows, show the empty panel `No polls yet.` Otherwise show
   `<div className={`${PANEL} overflow-x-auto`}><table aria-label="Recent polls" className="w-full border-collapse text-left">`
   with columns `When` · `Result` · `Latency` (only when `plotsLatency(kind)`) · `Detail`:
   - When: `formatObservedAt`, `mono text-[13px] text-muted whitespace-nowrap`;
   - Result: `OK` (`text-muted`) or `Failed` (`font-medium text-down`, the pair the dashboard
     already uses, so `contrast.spec` has nothing new to police);
   - Latency: `mono text-right`, `formatLatency(ms)` or `—`;
   - Detail: `text-[14px] text-muted`, `detail ?? ''`.
7. **Configuration** (only when `isAdministrator`): `useProbe(id, { enabled: isAdministrator })`
   and `useProbeGroups()` (enable it on `isAdministrator` too, via a wrapper or by calling it
   unconditionally; it is a Reader endpoint, so either is safe). Render
   `<section aria-labelledby="probe-configuration-heading">` with `<h2 …>Configuration</h2>` and a
   `<dl className={`${PANEL} grid grid-cols-[max-content_1fr] gap-x-6 gap-y-2 px-4 py-3.5 text-[14px]`}>`.
   `<dt className="text-muted">` / `<dd className="break-words">` pairs, in this order. Show a row
   only where its condition holds:
   | dt | dd | when |
   | --- | --- | --- |
   | Kind | `PROBE_KIND_LABEL[kind]` | always |
   | Host / **Reporter** (label switches for `message`) | `mono` `host` | always |
   | URL | `mono` `` `${useHttps ? 'https' : 'http'}://${host}/${path}` `` | http |
   | Method | `GET` / `HEAD` | http |
   | Poll interval | `` `Every ${formatDuration(pollIntervalSeconds)}` `` | always |
   | Failure threshold | `1 poll` / `` `${n} consecutive polls` `` | always |
   | Timeout | `` `${timeoutSeconds} s` `` | http |
   | Expected status | `Any 2xx` (null) · `` `${code}` `` · `` `Anything but ${code}` `` (negate) | http |
   | Expected body | `` `Contains "${text}"` `` / `` `Does not contain "${text}"` `` | http, text set |
   | TLS certificate | `Not validated` / `Validated` | http **and** `useHttps` |
   | Credential | `None` · `Bearer token (stored)` / `Bearer token (not set)` · `` `Basic, as ${username}` `` | http |
   | Groups | group names joined `, `, or `None` | always |
   | Paused | `Yes` / `No` | always |

   Below the `<dl>`: `<RouterLink to="/admin/probes">Edit under Admin → Probes</RouterLink>` with the
   text-link classes. **Never render `credential.hasSecret` as anything but the words above, and
   never anything that could be a secret.** The API cannot send one anyway.
8. `App.tsx`: `import { ProbePage } from '@/pages/probe-page'` with the static reader imports.
   Add `<Route path="probes/:id" element={<ProbePage />} />` after the `weather` route, with a
   one-line comment: a reader route, statically imported; reached from a dashboard row's name.

**Test** — `pages/probe-page.test.tsx` (model on `pages/weather-page.test.tsx` and
`pages/page-page.test.tsx`). Write one `history(overrides)` fixture builder for a ping probe
`p1` named `Internet`: 96 buckets with a few non-null averages and one failing bucket, three
`recentObservations` (two OK and one `Failed` with detail `Timed out`), `uptimePercent: 99.5`.
Render inside `<Routes><Route path="/probes/:id" element={<ProbePage />} /></Routes>` with
`initialEntries: ['/probes/p1']`. Cases:
1. **anonymous reader** (`'/api/v1/auth/session': { status: 204 }`): the h1 `Internet`, the text
   `99.50% uptime, 30 days` (regex), `getByRole('img', { name: /Latency over the last 24 hours/ })`,
   and the `Recent polls` table has 3 body rows, one containing `Failed` and `Timed out`. There is
   **no** `Configuration` heading, **and** the stubbed-fetch call list contains no
   `/api/v1/probes/p1` request (D1).
2. **administrator** (session body `{ kind: 'administrator', name: 'Admin' }`, plus stubs for
   `/api/v1/probes/p1` and `/api/v1/probe-groups`): an http probe `Jellyfin`, host
   `jellyfin.test`, path `health`, `useHttps: true`, interval 60, threshold 2, bearer
   `hasSecret: true`, in group `Media`. Assert the `Configuration` heading and the texts
   `https://jellyfin.test/health`, `Every 1 min`, `2 consecutive polls`, `Bearer token (stored)`,
   `Media`, `Validated`.
3. **range switch**: click `getByRole('button', { name: '7 days' })`. A fetch to
   `/api/v1/status/probes/p1?range=7d` is made (stub both URLs), and that button has
   `aria-pressed="true"` while `24 hours` has `"false"`.
4. **message probe** (`kind: 'message'`, all averages null, `message: { status: 'failed', overdue: true, body: 'disk full' }`):
   no `Latency` heading, no `Latency` column header, the chip word `Overdue`, and the text `disk full`.
5. **404** (`status: 404` with a problem body): `Probe not found` and a link `Back to the dashboard`
   with `href="/"`.

**Verify**: `./ci/run-ci.sh web; echo "exit=$?"` → `exit=0`, lint clean, vitest count up by the
new cases.

### Step 5: The dashboard links to it

In `dashboard-page.tsx`:
- `:192` → `<td className="px-4 py-3 text-[15px] font-semibold"><RouterLink to={`/probes/${probe.id}`} className="text-text underline decoration-line-strong underline-offset-[3px] hover:decoration-text">{probe.name}</RouterLink></td>`.
  Add a comment: the link text is exactly the name, so the unit and e2e row/name queries are
  unaffected (D9), and the colour pair is the one Links already uses, so contrast is unaffected.
- `:210` → `plotsLatency(probe.kind) ? <Sparkline … /> : null`.
- Delete the local `messageChipWord` and import it from `@/lib/status`.

**Test** — add to `dashboard-page.test.tsx`
`it("links each probe's name to its own page", …)`, reusing an existing status fixture: `getByRole('link', { name: 'Router' })` (or whatever
name that fixture uses) has `href` `/probes/<its id>`.

**Verify**: `./ci/run-ci.sh web; echo "exit=$?"` → `exit=0`. **Every pre-existing
`dashboard-page.test.tsx` case passes unchanged.** If one fails, that is a STOP condition, not
something to edit.

### Step 6: e2e — `e2e/probe-page.spec.ts`

Seed in `beforeEach`, as `dashboard-groups.spec.ts` does, **paused immediately**:
- a ping probe `Detail ping` (host `detail-ping.invalid`, interval 3600, threshold 2);
- an http probe `Detail http` (host `detail-http.invalid`, `http: { method: 'get', path: 'health', useHttps: true }`).
Delete both in `afterEach`. Tests:
1. `the dashboard name is a link to the probe page`: on `/`, click
   `getByRole('link', { name: 'Detail ping' })` → URL matches `/\/probes\/[0-9a-f-]+$/` and the h1
   `Detail ping` is visible.
2. `an administrator sees the configuration`: on `/probes/<http id>`, the `Configuration` heading
   and `https://detail-http.invalid/health` are visible.
3. `an anonymous reader sees no configuration and no host`: use an anonymous context (copy
   `pages.spec.ts:58-63`, including its comment on why `baseURL` must be passed). The h1 is
   visible, `getByRole('heading', { name: 'Configuration' })` has count 0, and
   `page.getByText('detail-http.invalid')` has count 0.
4. `the range switch changes the URL`: click `7 days` → URL ends with `?range=7d` and the button
   has `aria-pressed="true"`.
5. `it fits and its controls are tappable`: `expectNoHorizontalOverflow(page)` and
   `expectTappable(page, 'main button')` on the ping probe's page.
6. `it has no color-contrast violations`: axe `color-contrast` on the http probe's page in the
   dark scheme. The probes are paused, so there is no latency data and the chart shows its empty
   state. That is expected, because e2e has no way to seed observations.

**Verify**: `./ci/run-ci.sh e2e; echo "exit=$?"` → `exit=0` at both viewport projects, with every
pre-existing spec unchanged and passing.

### Step 7: Record the decision and update the docs

- `docs/ARCHITECTURE.md`: add `### 3.29 A probe has a page; its history is for readers, its
  configuration for administrators` before `## 4.`. Write it as prose in that file's style. Cover
  D1 (two sources, and the rejected caller-dependent shape), D3 (the shared allow-list), D5 (the
  range table) and D6 (in-memory bucketing and its rejected SQL alternative). Note that
  `ProbeEndpoints`' policy is unchanged.
- `docs/MODULES.md`, Monitoring entry: one sentence. Each probe has a page (`/probes/{id}`, plan
  023, §3.29) with 24 h / 7 d / 30 d latency and its recent polls.
- `docs/design-brief.md`: after the `**Sparkline.**` rule, add a `**Latency chart.**` rule. It is
  the probe page's large chart: y from 0, muted 2px line, failure bars in down/unstable, axis
  labels in HTML mono 12px muted, and no text inside the SVG.

**Verify**: `grep -n '^### 3.29' docs/ARCHITECTURE.md` → one line;
`grep -n 'Latency chart' docs/design-brief.md` → one line.

### Step 8: Full gate

Run `web`, `api` and `e2e` one at a time (see Commands). All three print `exit=0`, and api
reports 0 skipped.

## Done criteria

- [ ] `./ci/run-ci.sh web`, `api` and `e2e`, run one at a time, each print `exit=0`; api reports 0 skipped
- [ ] The ten `ProbeDetailEndpointTests` cases pass, including `The_response_never_carries_the_host`
- [ ] `grep -n "ProbeKind.Ping or ProbeKind.Http" src/Homon.Api/Endpoints/StatusEndpoints.cs` → exactly one hit, inside `PlotsLatency`
- [ ] `grep -rn "kind === 'ping' || " src/Homon.Web/src --include=*.tsx --include=*.ts` → only `lib/status.ts`
- [ ] `grep -n "KIND_LABELS" src/Homon.Web/src/pages/admin-probes-page.tsx` → no output
- [ ] `grep -n "Host\b" src/Homon.Api/Endpoints/StatusEndpoints.cs` shows no `Host` member in `ProbeHistoryResponse` (only the pre-existing `p.Host` uses in `BuildMessageSummariesAsync`)
- [ ] `git diff --stat main...HEAD` lists only in-scope files;
      `git diff main...HEAD -- '*Migrations*' src/Homon.Api/Endpoints/ProbeEndpoints.cs src/Homon.Web/e2e/helpers.ts` is empty
- [ ] Every e2e spec other than `probe-page.spec.ts` is byte-identical to `main`:
      `git diff --stat main...HEAD -- src/Homon.Web/e2e | grep -v probe-page` → no output
- [ ] Branch `plan/023-probe-detail-page` is checked out in `/home/acastaner/Git/homon`

## STOP conditions

- The drift check shows changes and the excerpts above no longer match.
- Any step seems to need a migration, a change to `ProbeEndpoints.cs`, a policy change, or
  `Host` in a Reader-gated response.
- `The_response_never_carries_the_host` fails for any reason other than a typo in the test.
- A pre-existing `dashboard-page.test.tsx`, `admin-probes-page.test.tsx` or e2e spec fails after
  Step 5 or Step 2. The name link and the label move are meant to be invisible to them.
- `e2e/contrast.spec.ts` or the new spec's axe check reports a violation on a pair this plan
  introduced. Report the pair; do not invent a token.
- A suite fails twice in a way the steps don't explain. One retry for the `0x80131506` CLR abort
  is expected and doesn't count.

## Maintenance notes

- **Two allow-lists, one per side** (D3): `StatusEndpoints.PlotsLatency` and
  `lib/status.ts plotsLatency`. Plans 004 (SMB) and 005 (SNMP) opt a kind in by changing both.
  Changing only one gives an empty chart or a payload nobody draws.
- **Configuration stays admin-only by construction.** Nothing on the reader route returns
  `Host`. If a future change wants readers to see, say, the poll interval, add that field to
  `ProbeHistoryResponse` deliberately and record it in §3.29. Do not relax `GET /probes/{id}`.
- **Performance** (D6): the 30 d range loads every observation of one probe for 30 days, up to
  172,800 rows at a 15 s interval. If a household reports a slow probe page, move the bucketing
  into SQL first. `date_bin(bucketWidth, "ObservedAt", windowStart)` is the obvious candidate,
  and the `(ProbeId, ObservedAt)` index already serves it.
- **Deferred, deliberately**: a hover tooltip or crosshair on the chart (the table carries the
  exact values); min/max bands per bucket; paging the polls table beyond 50; a link from the admin
  probe list to this page; an "Edit" deep link that opens the specific probe's form (the admin
  page has no per-probe route today).
- **Reviewer**: read the anonymous-reader unit test and e2e test 3 first. They are the privacy
  contract. Then check that the dashboard diff is exactly the name cell, the `plotsLatency`
  line, and the `messageChipWord` import.
- The first month after 022 deployed still shows the TTFB step-down on HTTP history (plan 022
  D5). The larger chart makes it more visible, but it is not a regression.

---

## Revision 1 — the chart, after the maintainer's browser check (2026-10-06)

**What happened.** The first execution passed every gate and was approved, and then the maintainer
opened a real probe page. They found it bad. Their "Audiobookshelf" probe had 3 polls in the last
24 hours (a dev scheduler records only while the dev API runs). D8 as first written turned that
into three tiny dashes floating in a 0–500 ms frame for values around 250 ms:

- each lone bucket was a 0.6-bucket-wide tick;
- the line broke at every bucket with no polls, so thin data never became a line;
- `niceCeiling`'s 1/2/2.5/5 steps sent 260 ms up to 500, wasting half the height;
- there were only two y labels and two x labels;
- `preserveAspectRatio="none"` ruled out dots and in-SVG text.

The gates could not see any of this, because e2e has no observations to draw. It is a defect in the
plan, not in the execution. **D8 is superseded by D8′ below. Everything else in this plan stands.**

**D8′. The chart is drawn in real pixels and reads well with three points or ninety-six.**

1. **Pixel geometry, not a stretched viewBox.** The chart measures its own width with a
   `ResizeObserver`, through a small local hook in `latency-chart.tsx`, and draws at that width ×
   a fixed height. Use `FALLBACK_WIDTH = 720` until measured, and always where `ResizeObserver` is
   undefined, as in jsdom. No `preserveAspectRatio="none"` and no `vector-effect`. Constants:
   `HEIGHT = 224`, padding `{ top: 12, right: 12, bottom: 28, left: 52 }`;
   `x(i) = left + ((i + 0.5) / n) * plotW`, `y(v) = top + plotH - (v / yMax) * plotH`.
2. **Three kinds of bucket.** *data* (`averageLatencyMs !== null`), *empty* (`polls === 0`: no
   one looked), and *down* (`polls > 0`, no average: every poll failed).
   - Consecutive *data* buckets form a **solid run**: one `<path data-testid="latency-line">`
     with one `M` per run of length ≥ 2, `stroke="var(--color-text)"`, 2px, round joins and caps.
   - Two *data* buckets separated **only** by *empty* buckets are joined by a **bridge**: one
     `<path data-testid="latency-bridge">` with one `M` per bridge, `stroke="var(--color-muted)"`,
     1.5px, `strokeDasharray="4 4"`. "Nobody polled" is not "it was down", but a dashed line says
     the stretch is interpolated.
   - A *down* bucket breaks the line and is never bridged. The failure bar marks it.
3. **Area.** Under each solid run of length ≥ 2, draw a closed path down to the baseline:
   `data-testid="latency-area"`, `fill="var(--color-muted)"`, `fillOpacity={0.15}`.
4. **Dots.** When there are ≤ 48 *data* buckets, every one gets
   `<circle data-testid="latency-dot" r={3} fill="var(--color-text)">`, so three samples are
   three visible points. The latest *data* bucket always gets `r={4}`, filled with the probe's
   status colour, using the `DOT_COLOR` mapping `sparkline.tsx` uses (copy it, with a comment;
   `muted` fallback). That means the chart takes a new `state: ProbeState` prop, and
   `probe-page.tsx` passes `history.state`. Above 48 *data* buckets, only the latest dot is drawn.
5. **Failure bars.** For each bucket with `failures > 0`, draw a `rect[data-failures]`: centred on
   `x(i)`, `width = Math.max(plotW / n - 2, 2)`, 6px tall at the bottom of the plot, with the same
   fill rule as before (`down` when all polls failed, `unstable` when some did).
6. **Y axis.** `yMax = niceCeiling(highest * 1.1)`, and `niceCeiling`'s steps become
   `[1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10]`, so 260 ms gives a 300 ms ceiling, not 500. Draw
   gridlines at 0, `yMax / 2` and `yMax` (`stroke="var(--color-line)"`, 1px), each labelled
   **inside the SVG** with `<text>`: `x = left - 8`, `textAnchor="end"`, `className="mono"`,
   `fontSize={11}`, `fill="var(--color-muted)"`, and `formatLatency`.
7. **X axis.** Five evenly spaced tick labels across the window, or three when `plotW < 360`,
   placed at the bottom inside the SVG with the same text style. The window is
   `n * bucketSeconds`, ending now. Format clock time (`HH:mm`, 24-hour) when
   `bucketSeconds < 3600`, otherwise `toLocaleDateString(undefined, { month: 'short', day: 'numeric' })`.
   The last label reads `Now`. Anchors are `start` for the first, `end` for the last and `middle`
   otherwise.
8. **Hover and touch.** On `onPointerMove` over the SVG, set
   `index = clamp(floor((clientX - rect.left - left) / plotW * n), 0, n - 1)`, and clear it on
   `onPointerLeave`. When there is an index, draw a vertical guide at `x(index)`
   (`stroke="var(--color-line-strong)"`) and an HTML tooltip above the SVG. The chart's wrapper
   is `relative`, and the tooltip is `pointer-events-none absolute top-0`, horizontally clamped
   inside the chart, `aria-hidden="true"`, `data-testid="latency-tooltip"`, styled
   `rounded-md border border-line bg-bg px-2 py-1 text-[12px]`. It holds:
   - the bucket's time span, e.g. `14:30–14:45`, or `6 Oct 12:00–18:00` for wider buckets;
   - `Mean 182 ms` or `No successful polls`;
   - `4 polls`, plus `, 1 failed` when there were failures.

   The table remains the accessible form of the data.
9. **Unchanged:** the `role="img"` and its `aria-label` format, the empty-state sentence (a bare
   `<p>` since revision round 1), and the `<figure>` / `<figcaption>`. The caption becomes:
   `Mean latency per {bucket}. A dashed line spans time with no polls; bars along the bottom mark failed polls.`
   Every colour is an existing token, and no in-chart text uses a pair that `contrast.spec`
   has not already passed (`muted` on `surface`).

**Tests for D8′.** Rewrite the latency-chart cases that pinned the old path shape;
`components/latency-chart.test.tsx` is in scope. Name the bucket arrays as *data* / *empty* /
*down* with helpers.
- empty-state text (unchanged);
- the aria-label (unchanged);
- `data, data, empty, data, data`: `latency-line` has 2 `M`s and `latency-bridge` has 1;
- `data, data, down, data, data`: `latency-bridge` is absent or has no `M`, and there is one
  `rect[data-failures]`;
- three *data* buckets separated by *empty* ones: 3 `latency-dot`s, 2 bridge `M`s, and no
  `latency-line` `M`;
- 60 consecutive *data* buckets: exactly 1 `latency-dot`;
- hover: `fireEvent.pointerMove(svg, { clientX })`, aimed at a known bucket. jsdom's rect is all
  zeros and the width is the 720 fallback, so `clientX = 52 + plotW * (i + 0.5) / n`. Assert
  that `latency-tooltip` contains that bucket's `Mean … ms` and `… polls`;
- `lib/probe-detail.test.ts` `niceCeiling`: `0→1, 7→8, 12→15, 23→25, 41→50, 100→100, 286→300, 0.3→0.3`.

**Docs.** Rewrite `docs/design-brief.md`'s `**Latency chart.**` rule to match D8′.

## Revision 2 — two findings from rendering D8′ (2026-10-06, a third round on the maintainer's word)

The reviewer bundled the real `LatencyChart` into a scratch harness and screenshotted it in
Chromium with realistic data. Five cases rendered as D8′ intends: 3 sparse polls, a dense 24 h
with a spike and an outage, 7 d of dev sessions with long gaps, and two phone-width charts. Two
findings remained:

1. **Defect: the chart can stay at the 720px fallback for good.** `useMeasuredWidth` runs its
   effect once on mount (`[]` deps), but the `ref`'d wrapper is rendered only once there is data.
   A chart that mounts in its empty state (a new probe, or a range with no polls) and receives
   data on a later refetch never observes its element. In the harness it stayed `720/720` inside
   a 1666px panel. **Fix**: a callback ref (`useCallback((element) => …)`) that attaches and
   detaches the `ResizeObserver` whenever the element itself appears or goes away, instead of an
   effect keyed on nothing. Keep the `FALLBACK_WIDTH`, zero-width and no-`ResizeObserver`
   guards. **Test**: in `latency-chart.test.tsx`, stub a minimal `ResizeObserver` with
   `vi.stubGlobal` whose `observe` calls back with `contentRect.width = 1000`. Render the chart
   empty, `rerender` it with data, and assert the `<svg>`'s `width` attribute is `1000`, not `720`.
2. **Nit: the tooltip covers what it describes.** It is pinned `top-0` and centred on the
   pointer, so hovering a spike hides the spike. **Fix**: anchor it beside the guide line, its
   left edge at `x(hovered) + 8`, flipping to `x(hovered) - 8 - tooltipWidth` when it would
   cross the chart's right edge, still `top-0` and still clamped inside the chart. The existing
   tooltip test keeps passing.

## Revision 3 — section order (2026-10-07, the maintainer's word)

**Configuration** goes **above** **Recent polls**. The page order is now: header → Latency →
Configuration (administrators only) → Recent polls. This supersedes the order in Step 4 (items 5–7).
Nothing else changes: the conditions, the headings, and who sees what are exactly as before.
