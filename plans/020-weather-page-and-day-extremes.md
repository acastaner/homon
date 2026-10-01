# Plan 020: Give the weather module today's extremes, an hour-by-hour page and derived severe-weather banners

> **Executor instructions**: Save this file as `plans/020-weather-page-and-day-extremes.md`
> in the repository as your first act, then follow it step by step. Run every verification
> command and confirm the expected result before moving to the next step. If anything in
> "STOP conditions" occurs, stop and report — do not improvise.
>
> **Drift check (run first)**:
> ```bash
> git diff --stat 9957b7b..HEAD -- src/Homon.Domain/Weather src/Homon.Infrastructure/Weather \
>   src/Homon.Api/Endpoints/WeatherEndpoints.cs src/Homon.Web/src/lib/weather.ts \
>   src/Homon.Web/src/pages/dashboard-page.tsx src/Homon.Web/src/App.tsx \
>   src/Homon.Web/e2e tests/Homon.Api.Tests
> ```
> Empty output is the expected state. If any of these changed, compare the "Current state"
> excerpts below against the live code before proceeding; on a mismatch, STOP.
>
> **Do not create a git worktree.** `CLAUDE.md` forbids it outright. Work in
> `/home/acastaner/Git/homon` on a branch and leave that branch checked out.

## Status

- **Priority**: P2
- **Effort**: L
- **Risk**: MED — it widens a wire contract two surfaces read, and adds the app's first
  "load more" affordance and its first derived-advisory logic.
- **Depends on**: none (010 and 012 are both DONE)
- **Category**: direction
- **Planned at**: commit `9957b7b`, 2026-10-01
- **Requested by**: the maintainer, verbatim — "First, I want the widget to include the
  min/max temperature for the current day (instead of just the current time). Second, I want
  that when clicking on the widget it takes us to a new, full Weather page" with a summary,
  "a table with the hour-by-hour weather condition … show the next 6 hours by default but
  allow a button to load the next 6 hours, up to next 24 hours", "a table with the day-by-day
  weather for the next 7 days", and "any dangerous weather forecast … needs to be a banner at
  the top of the page".

## Why this matters

The weather module answers "what is it like right now" and nothing else. The dashboard widget
shows the current temperature and three days of highs and lows, but not *today's* high and
low — so a reader on a cold morning sees `6°C` and cannot tell whether to take a coat for the
afternoon, which is the single most-asked question of a kitchen-counter dashboard. There is
also nowhere to go for more: `/api/v1/weather` already fetches a forecast, caches it for
15 minutes and throws most of it away, and the SPA has no weather route at all. This plan
spends the fetch we are already paying for: today's extremes in the widget, a full `/weather`
page behind it, and a banner for weather worth changing plans over.

The design is settled. Mockups were reviewed on 2026-10-01 and direction **B — "Now panel"**
was chosen: <https://claude.ai/artifact/UujdzH2z7ewuAFYNNMdzy9> (artboards `OptionB.dc.html`
desktop, `Phone.dc.html` at 412px, `Widget.dc.html` before/after). Where an artboard and this
plan disagree, **this plan wins** — the same rule `docs/design-brief.md` sets for itself.

## Decisions — implement these exactly

**D1. One payload, one cache, one query key.** `GET /api/v1/weather` grows; no second
endpoint and no second React Query key. The page reads the same `WEATHER_QUERY_KEY` the
dashboard already warmed, so clicking the widget renders the page with no fetch and no
spinner. The payload grows to roughly 4 KB of JSON for 24 hours + 8 days, which is nothing on
a LAN dashboard and is the whole reason the server caches it.

**D2. Open-Meteo has no warnings endpoint — the banners are *derived*, and must never claim
otherwise.** No copy anywhere may say "warning issued", name a met office, or imply an
official source. The vocabulary is `Caution` (orange, reusing the `unstable` token) and
`Severe` (red, reusing `down`), with the kind word beside it: "Severe · Gale-force wind". The
`docs/ARCHITECTURE.md` section this plan adds must state this limitation in the first
sentence, so nobody later "fixes" it by trusting a field that does not exist.

**D3. Thresholds are hard-coded, unit-aware, and in `Homon.Domain`.** No migration, no admin
fields, no configuration section. This matches §3.19's existing *Rejected* entry for an
admin-configurable cache TTL: the weather module deliberately has one setting
(`WeatherSettings`) and no knobs. The table, which goes in
`src/Homon.Domain/Weather/WeatherWarningThresholds.cs` as a documented static class:

| Kind | Source field | Caution (metric) | Severe (metric) | Caution (imperial) | Severe (imperial) |
| --- | --- | --- | --- | --- | --- |
| `Wind` | hourly `wind_gusts_10m` | ≥ 60 km/h | ≥ 90 km/h | ≥ 38 mph | ≥ 56 mph |
| `Thunderstorm` | hourly `weather_code` | code 95 | code 96 or 99 (hail) | same | same |
| `Snow` | daily `snowfall_sum` | ≥ 1 cm | ≥ 5 cm | ≥ 0.4 in | ≥ 2 in |
| `Rain` | daily `precipitation_sum` | ≥ 20 mm | ≥ 40 mm | ≥ 0.8 in | ≥ 1.6 in |
| `Heat` | daily `temperature_2m_max` | ≥ 32 °C | ≥ 38 °C | ≥ 90 °F | ≥ 100 °F |
| `Cold` | daily `temperature_2m_min` | ≤ −10 °C | ≤ −18 °C | ≤ 14 °F | ≤ 0 °F |

The imperial figures are the metric ones rounded to a round number a reader recognises, not
exact conversions — write that in the class's doc comment so nobody "corrects" 90 °F to
89.6 °F.

**D4. The warning window is the next 48 hours; at most three banners.** Ordering is fully
determined, because the tests pin it: `Severe` before `Caution`, then earliest start first,
then by kind in the D3 table's order. One warning per kind at its highest severity — a
six-hour gale is one banner, not six. If more than three survive, the first three win and the
rest are dropped silently (no "and 2 more" affordance).

**D5. 48 hours are scanned; 24 are sent.** The hourly table's ceiling is 24 (the maintainer's
"up to next 24 hours"), but a caution for tomorrow afternoon is worth seeing today, so
derivation reads a 48-hour slice while the wire carries 24.

**D6. Slice at *request* time, not fetch time.** `WeatherCache` holds a snapshot for up to
15 minutes, so a window computed when the snapshot was built would show a stale first row.
The provider therefore returns everything it parsed plus Open-Meteo's `utc_offset_seconds`,
and the endpoint computes "the current local hour" from the injected `TimeProvider` on every
request. `WeatherCache` itself is not touched by this plan.

**D7. Local clock times cross the wire as `"HH:mm"` strings, never as `DateTime`.** Open-Meteo
with `timezone=auto` already answers in the location's own zone. Sending those instants as
`DateTimeOffset` and formatting them in the browser re-interprets them in the *reader's*
timezone — exactly the bug `formatForecastDay`'s comment in `dashboard-page.tsx:95-102`
already warns about for dates. So `WeatherHourResponse.Time`, `Sunrise` and `Sunset` are
strings the SPA prints verbatim. Calendar dates stay `DateOnly` ("YYYY-MM-DD"), as they are
today.

**D8. Every new numeric from Open-Meteo is nullable.** `precipitation_probability`,
`wind_gusts_10m`, `precipitation_sum`, `snowfall_sum` and `wind_gusts_10m_max` can all come
back `null` for some models; `sunrise`/`sunset` are `null` above the Arctic circle, and Homon
is a generic product. Declare the deserialisation arrays with nullable element types
(`double?[]`, `int?[]`, `string?[]`) — a `double[]` throws on a null element — and render a
missing value as `—`, the same way uptime does. A null never trips a warning.

**D9. `WeatherForecast.Days` now starts with today.** The provider currently drops `daily[0]`
(`OpenMeteoWeatherProvider.cs:108-119`). It must stop: `Days[0]` is today and the list is 8
long. The *endpoint* splits it — `Today = Days[0]`, `Forecast = Days[1..7]`. Document "today
first" on the record, because the old behaviour was the opposite.

**D10. `WeatherResponse.Forecast` grows from 3 entries to 7; the widget slices to 3.** The
dashboard keeps exactly three forecast rows — `e2e/weather.spec.ts` asserts
`getByRole('listitem')` `toHaveCount(3)` and that assertion stays green and stays meaningful.
Use `weather.forecast.slice(0, 3)` with a comment saying why.

**D11. The widget's new high/low omits the unit symbol; the three forecast rows keep
theirs.** Today's extremes render as muted mono `20° / 12°` beside the current `18°C`, which
already carries the unit 14px away. The three rows below keep `17°C/11°C` byte-for-byte,
because `dashboard-page.test.tsx:147` and `e2e/weather.spec.ts` match those exact strings and
this plan has no business renaming them. On the page, the Today panel's big temperature
carries the unit and every table cell omits it.

**D12. The widget's link wraps the data, never the attribution.** One `<Link to="/weather">`
around the current-conditions block and the three forecast rows, with
`aria-label={place ? \`Weather for ${place}, full forecast\` : 'Weather, full forecast'}`. The
Open-Meteo credit — a real external `<a>` — stays *outside* that link: nested anchors are
invalid HTML and the attribution must stay independently reachable. The section `<h2>` is not
touched; `CollapsibleSection` requires its `textContent` to be the heading and nothing else.

**D13. The tables stay real tables and *drop columns* on phone.** Do not fold rows into two
lines. `plans/README.md`'s cross-plan follow-ups record why: "overriding `display` on
`<tr>`/`<td>` strips the implicit ARIA row/cell roles the e2e specs query by". Follow
`dashboard-page.tsx:169`'s pattern exactly — a `${PANEL} overflow-x-auto` wrapper (the
`.overflow-x-auto` class is load-bearing: `e2e/helpers.ts:45` skips its subtree when checking
horizontal overflow) around `<table className="w-full border-collapse text-left">`, with
`hidden sm:table-cell` on the columns phone drops:
- hourly keeps Time · Condition · Temp · Rain; `Feels like` and `Wind` are `sm:`-only;
- daily keeps Day · Condition · High / low; `Rain`, `Wind` and `Sunrise / sunset` are
  `sm:`-only.
Put `hidden sm:table-cell` on both the `<th>` and every matching `<td>`.

**D14. "Show 6 more hours" is the app's first progressive disclosure, and it is not
remembered.** Plain `useState(6)`, clamped to the hourly length; no `localStorage`, unlike
`lib/collapsed-sections.ts` and `lib/section-order.ts`, because a reading position within one
visit is not a preference. The button is a real `<button type="button">` at `h-12` in the
panel's last row (a 48px row, matching the table's row height), it disappears when everything
is shown, and its label is
`Show ${Math.min(6, remaining)} more hour${Math.min(6, remaining) === 1 ? '' : 's'} — ${visible} of ${total}`.

**D15. The banners are a named list, not `role="alert"`.** They are present on first paint,
and `role="alert"` is assertive — three of them would be announced on top of each other, and
the repo reserves `role="alert"` for errors (`role="status"` for the session check). Render
`<ul aria-label="Weather warnings">` of `<li>`s, each carrying its kind's Lucide icon
(`aria-hidden`), the severity word and the kind word as text, so severity is never colour
alone. Tint with `row-tint-unstable` / `row-tint-down` from `index.css`.

**D16. No migration, no new configuration, no admin UI.** `WeatherSettings`,
`WeatherSettingsConfiguration`, every migration and `admin-weather-page.tsx` are untouched.
If a step seems to need a migration, that is a STOP condition.

## Current state

### Backend

- `src/Homon.Infrastructure/Weather/OpenMeteoWeatherProvider.cs` — the only outbound call.
  `BuildRequestUri` at lines 75–94 and `ToForecast` at 96–122 are both replaced:

  ```csharp
  // OpenMeteoWeatherProvider.cs:84-86
  var query = $"v1/forecast?latitude={latitude}&longitude={longitude}"
      + "&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m,is_day"
      + "&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=4";
  ```
  ```csharp
  // OpenMeteoWeatherProvider.cs:108-119
  // daily[0] is today, already covered by `current` — the three forecast rows are
  // daily[1..3].
  var days = new List<WeatherForecastDay>();

  for (var i = 1; i < daily.Time.Length && days.Count < 3; i++)
  { /* ... */ }
  ```
  Imperial adds only `&temperature_unit=fahrenheit&wind_speed_unit=mph` (lines 88–91) —
  **`precipitation_unit` is missing**, and must be added now that precipitation is surfaced.
  The class takes only `IHttpClientFactory`; it gains a `TimeProvider`-free signature (D6 puts
  the clock in the endpoint), so its constructors stay as they are. The internal
  `(IHttpClientFactory, TimeSpan)` test seam at line 38 stays.

- `src/Homon.Infrastructure/Weather/WeatherForecast.cs` — 27 lines, three records
  (`WeatherForecast`, `WeatherCurrent`, `WeatherForecastDay`). `WeatherForecastDay` is
  `(DateOnly Date, WeatherCondition Condition, double High, double Low)`.

- `src/Homon.Infrastructure/Weather/WeatherCache.cs` — `FreshFor` 15 min, `StaleTolerance`
  6 h, single-flight, generation counter. **Do not change it.**

- `src/Homon.Infrastructure/Weather/FakeWeatherProvider.cs` — fixed 18 °C clear + three days
  (19/11, 21/12, 16/9). Reached only via `Weather:Provider=Fake`, which
  `playwright.config.ts:160-161` sets and `AddWeather` refuses in Production. Every e2e and
  `WeatherEndpointTests` assertion is against these numbers.

- `src/Homon.Api/Endpoints/WeatherEndpoints.cs` — `GetWeatherAsync` at 51–83 composes the
  response; the wire records are at 227–265. `GET ""` carries
  `.RequireAuthorization(HomonPolicies.Reader)`. `Program.cs:413` installs
  `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`, so every new enum crosses the wire
  camelCase (`"severe"`, `"partlyCloudy"`).

- `src/Homon.Domain/Weather/WmoWeatherCodeMap.cs` — pure `Map(int) → WeatherCondition`; the
  pattern every new Domain helper follows (static, dependency-free, one XML doc table).

### Frontend

- `src/Homon.Web/src/lib/weather.ts` — the only weather module: wire types mirroring the C#
  records (lines 17–73), `WEATHER_CONDITION_LABEL`, `WEATHER_CONDITION_ICON` /
  `weatherConditionIcon` (Lucide), `fetchWeather` (`?? null` for the 204), and `useWeather`
  with `refetchInterval: 15 * 60 * 1000` + `refetchOnWindowFocus: true`. `fetchedAt` and
  `stale` are already on the wire and rendered nowhere.

- `src/Homon.Web/src/pages/dashboard-page.tsx` — `weatherSectionBody()` at 213–293 and the
  section slot at 407–414. Four states in this order: `weather === null` → dashed empty state
  linking `/admin/weather`; `ApiError` 503 → "Weather is temporarily unavailable."; `!weather`
  → `null` (no skeleton); loaded. Helpers `unitSymbol`, `windUnit`, `formatForecastDay` at
  90–106; `const PANEL = 'rounded-md border border-line bg-surface'` at 109.

- `src/Homon.Web/src/App.tsx` — one flat `<Routes>` under `<Route element={<AppShell />}>`.
  Reader routes are statically imported (`DashboardPage`, `PagePage`, `SignInPage`); only the
  `admin` subtree is `lazy()`. The new page is a **reader** route, so import it statically.

- Conventions a new page must match (exemplar: `src/Homon.Web/src/pages/admin-pages-page.tsx`,
  read it before writing):
  - return a **bare fragment**, never a wrapping `<div>` — `app-shell.tsx`'s `<main>` is
    `flex flex-col gap-8` and the page's top-level elements are its flex children;
  - exactly one `<h1>` with
    `PAGE_H1 = 'border-b border-line-strong pb-4 text-[22px] font-semibold -tracking-[0.01em] sm:text-[26px]'`,
    copied into the new file (plan 012 Slice E sanctions this duplication: "extract it once a
    third instance exists");
  - sections get an accessible name from `aria-labelledby` on a `<h2 id>`; lists from
    `aria-label`;
  - `h-10` (40px) minimum on every interactive element — `e2e/helpers.ts`'s `expectTappable`
    measures every `<button>`;
  - `useDocumentTitle(pageTitle('Weather'))`;
  - named export, file `src/pages/weather-page.tsx`.

- `src/Homon.Web/src/test/fetch.ts` — `stubFetch(routes)` keyed on **full paths including
  `/api/v1`**; an undeclared path throws `Unexpected fetch: …`. It returns the call log, which
  `admin-weather-page.test.tsx` asserts against.

- Existing coverage that must stay green without edits beyond the ones this plan names:
  `dashboard-page.test.tsx:137` (empty state), `:147` (loaded — `/18°C/`, `/19°C\/11°C/`,
  `/16°C\/9°C/`, the Open-Meteo href), `:184` (503), `:283-400` (ordering, and that the move
  button is named for the section not the place); `e2e/weather.spec.ts` (three describes,
  seeding through `request.put`/`request.delete`); `e2e/layout.spec.ts` (iterates
  `READER_ROUTES`/`ADMIN_ROUTES`); `e2e/contrast.spec.ts` (axe colour-contrast, both schemes).

### Design tokens this plan may use

From `src/Homon.Web/src/index.css`'s single `@theme` block (and its `[data-theme='light']`
re-declaration): `bg surface line line-strong text muted up unstable down unknown
unstable-bg down-bg source-1..3`, fonts `--font-sans` / `--font-mono`, and the utility classes
`.mono` (mono + `tabular-nums`), `.row-tint-unstable`, `.row-tint-down`, `.row-paused`. No
spacing or radius tokens exist — use Tailwind's scale and arbitrary bracket type sizes, as
every existing page does. **Do not introduce a new colour.**

From `docs/design-brief.md`, the measurements this page must hit: section label `<h2>`
12px/600/0.12em/uppercase/muted sitting 10px above its panel; panels `surface` + 1px `line` +
6px radius, no shadows; table column header 11.5px/600/0.08em/uppercase; rows 12×16px padding
and 48px minimum height (12×14 and 60px on phone); weather icons 20px at stroke 1.75 in rows,
40px at 1.5 for current conditions; mono carries `tabular-nums`; 32px between sections on
desktop, 26px on phone; 16px phone side gutter.

## Commands you will need

| Purpose | Command | Expected on success |
| --- | --- | --- |
| Whole gate | `./ci/run-ci.sh` | `PASS — web api e2e` |
| Web suite | `./ci/run-ci.sh web` | `npm ci`, oxlint, `tsc -b && vite build`, vitest all pass |
| API suite | `./ci/run-ci.sh api` | Release build (the formatting gate), migrate, `dotnet test` — **0 skips** |
| e2e | `./ci/run-ci.sh e2e` | Playwright green at Pixel 7 and 1440×900 |
| SPA never calls the provider | `grep -rn "api.open-meteo.com" src/Homon.Web/src` | no matches (the §3.19 gate) |
| Lint only | `cd src/Homon.Web && npx oxlint` | silent |

`./ci/run-ci.sh api` fails on a skipped test — without `HOMON_TEST_CONNECTION` the
`[DatabaseFact]` tests skip and `dotnet test` still exits 0, which is why the script counts.
Per the "Homon CI on this machine" note: **run the suites one at a time**; memory kills and
`0x80131506` aborts on this machine are environmental — retry them rather than treating them
as failures.

## Scope

**In scope**

New:
- `src/Homon.Domain/Weather/WeatherWarning.cs` — `WeatherWarningKind`,
  `WeatherWarningSeverity`, and the `WeatherWarning` record.
- `src/Homon.Domain/Weather/WeatherWarningThresholds.cs` — D3's table.
- `src/Homon.Domain/Weather/WeatherWarningEvaluator.cs` — pure; days + hours + units → up to
  three warnings, D4's order.
- `src/Homon.Domain/Weather/WeatherHour.cs` — the hourly row.
- `src/Homon.Web/src/pages/weather-page.tsx`, `src/Homon.Web/src/pages/weather-page.test.tsx`.
- `tests/Homon.Api.Tests/WeatherWarningEvaluatorTests.cs`.
- `src/Homon.Web/e2e/weather-page.spec.ts`.

Changed:
- `src/Homon.Infrastructure/Weather/WeatherForecast.cs` — `Days` is 8 and today-first; days
  gain precipitation, snowfall, wind max/gust max, sunrise/sunset; the forecast gains `Hours`
  and `UtcOffsetSeconds`.
- `src/Homon.Infrastructure/Weather/OpenMeteoWeatherProvider.cs` — the query and the mapping.
- `src/Homon.Infrastructure/Weather/FakeWeatherProvider.cs` — today + 7 days + 48 hours, with
  one gusty hour so the banner has e2e coverage.
- `src/Homon.Api/Endpoints/WeatherEndpoints.cs` — the wire records and `GetWeatherAsync`
  (which gains `TimeProvider`).
- `src/Homon.Web/src/lib/weather.ts` — types, warning labels, formatters.
- `src/Homon.Web/src/pages/dashboard-page.tsx` — today's extremes and the link.
- `src/Homon.Web/src/App.tsx` — one route.
- `src/Homon.Web/e2e/helpers.ts` — `/weather` in `READER_ROUTES`.
- `src/Homon.Web/e2e/contrast.spec.ts` — `/weather` added, both schemes (the tinted banners
  are new text-on-tint pairs).
- `tests/Homon.Api.Tests/OpenMeteoWeatherProviderTests.cs`,
  `tests/Homon.Api.Tests/WeatherEndpointTests.cs`.
- `src/Homon.Web/src/pages/dashboard-page.test.tsx`, `src/Homon.Web/src/App.test.tsx` (its
  `/api/v1/weather: 204` stub still works; confirm, do not rewrite).
- `docs/ARCHITECTURE.md`, `docs/MODULES.md`, `plans/README.md`, `plans/020-*.md`.

**Out of scope — do NOT touch, even though they look related**

- `WeatherCache.cs` and `WeatherCacheTests.cs`. The snapshot's *shape* changes; its caching
  behaviour does not, and its nine facts must pass unedited. If one fails, that is a signal
  you changed the cache, not the forecast.
- `WeatherSettings.cs`, `WeatherSettingsConfiguration.cs`, anything under
  `Persistence/Migrations/`, the model snapshot. **No migration.**
- `admin-weather-page.tsx` and its test. No new settings (D3).
- `WmoWeatherCodeMap.cs` and its 30-row theory. The map is sufficient; warnings read raw WMO
  codes directly for thunderstorm severity (95 vs 96/99), which the condition enum flattens.
- `components/collapsible-section.tsx`, `lib/section-order.ts`,
  `lib/collapsed-sections.ts`. The page is not a dashboard section and is not arrangeable.
- `components/ui/*` (shadcn, installed and unused). Hand-styled class-name constants are the
  live convention; adopting shadcn here would set a precedent this plan has no mandate for.
- The banner navigation. `/weather` is reached from the widget, the way `/pages/{slug}` is
  reached from the Pages section — the brief's banner is a deliberate two-item nav and
  `docs/design-brief.md` requires it to stay one row on a phone.

## Git workflow

- Branch: `plan/020-weather-page`, cut from `main` without asking.
- Commit per step; message style from `git log`: `<Area>: <imperative summary> (plan 020)` —
  e.g. `Weather: derive severe-weather advisories from the forecast (plan 020)`.
- End every commit message with:
  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01WNKhm5KcWB875CxfuHQ2No
  ```
- **Leave the branch checked out when done and say so.** Do not merge, do not switch to
  `main`, do not `git stash`, do not push, do not open a PR. The maintainer reviews by running
  the app in this checkout on this branch.

## Steps

### Step 0: Confirm Open-Meteo's live response before writing any parser

`plans/README.md`'s "Verify at execution" list already flags Open-Meteo for this module. Run:

```bash
curl -s 'https://api.open-meteo.com/v1/forecast?latitude=51.5&longitude=-0.12&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m,is_day&hourly=temperature_2m,apparent_temperature,weather_code,wind_speed_10m,wind_gusts_10m,precipitation_probability&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_sum,snowfall_sum,wind_speed_10m_max,wind_gusts_10m_max,sunrise,sunset&timezone=auto&forecast_days=8' | head -c 3000
```

Confirm four things and write what you saw into the plan file's own body before continuing:
1. `utc_offset_seconds` is present at the top level;
2. `hourly.time[0]` is **today at 00:00 local** (the hourly block starts at midnight, not at
   the current hour) — this is what D6's request-time slice assumes;
3. `daily.time` has 8 entries, `daily.time[0]` being today;
4. `daily.sunrise[i]` looks like `"2026-10-01T07:10"` — a local, zone-less ISO string.

Then confirm the imperial variant converts precipitation:

```bash
curl -s 'https://api.open-meteo.com/v1/forecast?latitude=51.5&longitude=-0.12&daily=precipitation_sum,snowfall_sum&timezone=auto&forecast_days=2&temperature_unit=fahrenheit&wind_speed_unit=mph&precipitation_unit=inch' | head -c 600
```

**If (2) is false** — the hourly block starts at the current hour, or `forecast_hours` turns
out to be needed — STOP and report. The slice in step 3 is written against a
midnight-anchored array and silently returns the wrong rows otherwise.

**Verify**: both commands return HTTP 200 JSON and all four facts hold.

#### Observed on 2026-10-01 — all four facts hold

Captured from the live API (`latitude=51.5&longitude=-0.12`, `timezone=auto`,
`forecast_days=8`):

- `utc_offset_seconds` **is** a top-level integer — `3600`, with
  `timezone: "Europe/London"`, `timezone_abbreviation: "GMT+1"`.
- `hourly.time` has **192** entries (8 × 24) and starts at **`"2026-10-01T00:00"`** — today
  at midnight *local*, exactly the anchor D6's request-time slice assumes. Last entry
  `"2026-10-08T23:00"`.
- `daily.time` has **8** entries, `[0]` being today (`"2026-10-01"` → `"2026-10-08"`).
- `daily.sunrise[0]` is `"2026-10-01T07:01"` — a local, zone-less ISO string, so D7's
  `.Split('T')[1]` yields `"07:01"`.

Units, from the response's own `hourly_units` / `daily_units` blocks:

| Variable | Metric default | With `precipitation_unit=inch` |
| --- | --- | --- |
| `precipitation_sum` | `mm` | `inch` |
| `snowfall_sum` | **`cm`** | `inch` |
| `wind_speed_10m`, `wind_gusts_10m` | `km/h` | `mp/h` (with `wind_speed_unit=mph`) |
| `precipitation_probability` | `%` | `%` |

Two things this pins down:

1. **`snowfall_sum` is centimetres, not millimetres** — D3's snow thresholds (1 cm / 5 cm)
   are already in the right unit. Do not divide by ten.
2. **`precipitation_unit=inch` converts `snowfall_sum` as well as `precipitation_sum`**, so
   the single imperial parameter covers both of D3's accumulation rows. It is required: the
   imperial branch today sends only `temperature_unit` and `wind_speed_unit`, which would have
   left a US household reading millimetres.

`precipitation_probability` and `wind_gusts_10m` came back with **no** null elements in this
sample. D8's nullable element types stay anyway — a sample is not a contract, and the arrays
are documented as optional per model.

Polar `sunrise`/`sunset` could not be proven null: the API's date window is
`2026-06-30`–`2026-10-16`, so no polar-night date is reachable, and Svalbard (78.22 N) in
October still returns `"2026-10-01T07:32"` / `"2026-10-01T17:57"`. `Sunrise`/`Sunset` stay
`string?` defensively — Homon is a generic product and the cost of the null check is one `?.`.

### Step 1: Domain — the warning types, the thresholds and the evaluator

Four new files in `src/Homon.Domain/Weather/`, all dependency-free, file-scoped namespaces,
XML doc comments carrying the reasoning (the repo's rule: "a bare setting is a setting
somebody will simplify back").

`WeatherHour.cs`:

```csharp
namespace Homon.Domain.Weather;

/// <param name="Date">The hour's calendar date in the location's own timezone.</param>
/// <param name="Time">The hour on the location's own clock, "HH:mm". A string, not a
/// DateTimeOffset: Open-Meteo already answered in the location's zone, and re-parsing it in
/// the browser would re-interpret it in the reader's. See plan 020 D7.</param>
public sealed record WeatherHour(
    DateOnly Date,
    string Time,
    WeatherCondition Condition,
    int WmoCode,
    double Temperature,
    double ApparentTemperature,
    double WindSpeed,
    double? WindGusts,
    int? PrecipitationProbability);
```

`WeatherWarning.cs` — `WeatherWarningKind { Wind, Thunderstorm, Snow, Rain, Heat, Cold }` in
D3's order (the enum's declaration order *is* the tie-break in D4, so say so in a comment),
`WeatherWarningSeverity { Caution, Severe }`, and:

```csharp
/// <param name="Value">The figure that tripped the threshold, in the settings' own units, or
/// null for a kind with no figure (Thunderstorm).</param>
/// <param name="FromTime">Start on the location's clock, "HH:mm", or null for a whole-day
/// warning derived from a daily aggregate.</param>
public sealed record WeatherWarning(
    WeatherWarningKind Kind,
    WeatherWarningSeverity Severity,
    double? Value,
    DateOnly Date,
    string? FromTime,
    string? ToTime);
```

No sentence, no label, no English anywhere in this record — `lib/weather.ts` composes the
copy, the way `WEATHER_CONDITION_LABEL` already owns condition words.

`WeatherWarningThresholds.cs` — D3's table as a `static class` with one
`static (double Caution, double Severe) For(WeatherWarningKind, WeatherUnits)` plus the doc
table verbatim, including the "rounded, not converted" note.

`WeatherWarningEvaluator.cs`:

```csharp
public static IReadOnlyList<WeatherWarning> Evaluate(
    IReadOnlyList<WeatherDay> days,
    IReadOnlyList<WeatherHour> hours,
    WeatherUnits units)
```

- `hours` is the caller's already-sliced 48-hour window; `days` are the days whose `Date`
  appears in it. The evaluator does no slicing and reads no clock — it is pure.
- Wind: scan `hours` for `WindGusts >= threshold`; collapse consecutive qualifying hours into
  one warning spanning `FromTime` of the first to `ToTime` of the last; `Value` is the maximum
  gust in that run. Highest severity wins; one Wind warning total.
- Thunderstorm: scan `hours` for `WmoCode` 95/96/99; same run-collapsing; `Severe` if any hour
  is 96 or 99, else `Caution`; `Value` is null.
- Snow / Rain / Heat / Cold: per day from the daily aggregate; `FromTime` and `ToTime` null;
  one warning per kind at the day with the worst figure.
- A null figure (D8) never qualifies.
- Sort by `(Severity descending, FromTime ?? "00:00" ascending within Date ascending, Kind
  ascending)` and `Take(3)`.
- Zero warnings returns an empty list, never null.

**Verify**: `./ci/run-ci.sh api` → PASS (the new files compile under
`TreatWarningsAsErrors` and `IDE0055`-as-error; no test asserts them yet).

### Step 2: Domain — the warning evaluator's tests

New `tests/Homon.Api.Tests/WeatherWarningEvaluatorTests.cs`, modelled structurally on
`WmoWeatherCodeMapTests.cs` (theory-driven, no infrastructure) with explicit facts for the
behaviours D4 pins:

- one fact per kind × severity (12), metric;
- the imperial threshold for each kind (6) — a figure just over the imperial Caution bound
  must *not* trip under metric thresholds, proving the units argument is read;
- six consecutive gusty hours collapse to **one** warning with the run's start, end and
  maximum;
- a gust run broken by one calm hour produces one warning (the earlier, worse run) because
  only one Wind warning exists per evaluation;
- `Severe` sorts before `Caution` even when the caution starts earlier;
- five qualifying kinds yield exactly three warnings, and they are the three D4's order picks;
- a null gust / null precipitation sum / null snowfall trips nothing;
- empty `hours` and empty `days` each return an empty list;
- thunderstorm code 95 is `Caution`, 96 and 99 are `Severe`.

**Verify**: `./ci/run-ci.sh api` → PASS, 0 skips, and the run's test count is 32 higher than
before this step.

### Step 3: Infrastructure — widen the forecast and the provider

1. `WeatherForecast.cs`: rename `WeatherForecastDay` to **`WeatherDay`** (it is no longer only
   a *forecast* day — `Days[0]` is today) and move it to
   `src/Homon.Domain/Weather/WeatherDay.cs` beside `WeatherHour`, since the evaluator in
   `Homon.Domain` consumes it and Domain may not depend on Infrastructure. Its shape:

   ```csharp
   public sealed record WeatherDay(
       DateOnly Date,
       WeatherCondition Condition,
       double High,
       double Low,
       double? PrecipitationSum,
       double? SnowfallSum,
       double? WindSpeedMax,
       double? WindGustsMax,
       string? Sunrise,
       string? Sunset);
   ```

   `WeatherForecast` becomes:

   ```csharp
   /// <param name="Days">Eight days, <b>today first</b> — changed in plan 020; the provider
   /// used to drop today because `current` covered it, and now the page needs today's
   /// extremes. The endpoint splits Days[0] off as Today.</param>
   /// <param name="Hours">Every hourly row Open-Meteo returned, from today 00:00 local. The
   /// endpoint slices the window it needs per request (plan 020 D6), so a snapshot up to
   /// WeatherCache.FreshFor old still starts at the right hour.</param>
   public sealed record WeatherForecast(
       WeatherCurrent Current,
       IReadOnlyList<WeatherDay> Days,
       IReadOnlyList<WeatherHour> Hours,
       int UtcOffsetSeconds);
   ```

2. `OpenMeteoWeatherProvider.cs`: replace `BuildRequestUri`'s query with the step-0 URL
   (`forecast_days=8`, the six hourly variables, the nine daily variables) and add
   `&precipitation_unit=inch` to the imperial branch. Keep the existing comment's shape —
   explain *why* `forecast_days=8` (today + 7) and why nothing here converts a value.
   Replace `ToForecast` to map all 8 days and every hourly row, with
   `DateOnly.Parse(…, InvariantCulture)` for dates and `.Split('T')[1]` for the `"HH:mm"`
   times (D7) — guarding a null or malformed sunrise into `null` rather than throwing.
   Declare the deserialisation arrays with nullable element types per D8.

3. `FakeWeatherProvider.cs`: emit today + 7 days + 48 hours from `timeProvider`, with
   today's high 20 and low 12 (so no value collides with the current 18 that existing
   assertions match on), and **one deliberate `Severe` wind run** — three consecutive hours
   with `WindGusts: 94` — so the banner is reachable from e2e. Keep current conditions at
   18/17/12/Clear/day, byte-identical, because `dashboard-page.test.tsx` and
   `e2e/weather.spec.ts` match them.

**Verify**: `./ci/run-ci.sh api` → it will **fail to compile** `WeatherEndpoints.cs` and the
provider tests at this point; that is expected. Confirm the two Infrastructure files and the
Domain files compile by `dotnet build src/Homon.Infrastructure -c Release` → exit 0.

### Step 4: Infrastructure — the provider's tests

Rewrite `tests/Homon.Api.Tests/OpenMeteoWeatherProviderTests.cs`'s `SampleBody` (line 14) from
the body step 0 captured, trimmed to 8 days and 48 hours, and keep all six existing facts
working. Add:

- `Days` has 8 entries and `Days[0].Date` is the body's `daily.time[0]`;
- `Hours` has as many entries as `hourly.time`, `Hours[0].Time == "00:00"`;
- a null `precipitation_probability` element deserialises to `null`, not 0, and does not throw;
- `UtcOffsetSeconds` is read;
- the imperial query contains `precipitation_unit=inch` as well as the two existing params;
- `Sunrise` is `"HH:mm"`, and a null `sunrise` element yields `null`.

**Verify**: `./ci/run-ci.sh api` still fails only in `Homon.Api`; `dotnet test
tests/Homon.Api.Tests --filter OpenMeteo` is not runnable until step 5 compiles the API, so
confirm with `dotnet build tests/Homon.Api.Tests -c Release` after step 5 instead and note it.

### Step 5: API — the wire contract and the request-time slice

In `WeatherEndpoints.cs`:

```csharp
public sealed record WeatherResponse(
    string? Place,
    WeatherUnits Units,
    WeatherCurrentResponse Current,
    WeatherDayResponse Today,
    WeatherDayResponse[] Forecast,
    WeatherHourResponse[] Hourly,
    WeatherWarningResponse[] Warnings,
    DateTimeOffset FetchedAt,
    bool Stale);
```

`WeatherDayResponse` mirrors `WeatherDay`; `WeatherHourResponse` mirrors `WeatherHour` minus
`WmoCode` (an implementation detail the SPA has no use for — `Condition` is the public word);
`WeatherWarningResponse` mirrors `WeatherWarning`. Keep the existing
`WeatherForecastDayResponse` name **only if** you prefer continuity — but if you rename it to
`WeatherDayResponse`, update every reference and say so in the commit; do not leave both.

`GetWeatherAsync` gains a `TimeProvider timeProvider` parameter (minimal APIs resolve it from
the container, which `Program.cs` already registers) and, after the existing
`result.IsAvailable` guard:

```csharp
var forecast = result.Forecast!;

// Slice per request, not per fetch: WeatherCache.FreshFor is 15 minutes, so a window
// computed when the snapshot was built would open on an hour already past. Plan 020 D6.
var localNow = timeProvider.GetUtcNow().UtcDateTime
    .AddSeconds(forecast.UtcOffsetSeconds);
var fromHour = new DateTime(localNow.Year, localNow.Month, localNow.Day, localNow.Hour, 0, 0);

var window = forecast.Hours
    .Where(hour => hour.Date.ToDateTime(TimeOnly.Parse(hour.Time, CultureInfo.InvariantCulture)) >= fromHour)
    .Take(48)
    .ToArray();

// 48 hours are scanned for advisories; 24 cross the wire, which is the table's ceiling.
var hourly = window.Take(24).ToArray();
var warnedDates = window.Select(hour => hour.Date).Distinct().ToHashSet();
var warnings = WeatherWarningEvaluator.Evaluate(
    forecast.Days.Where(day => warnedDates.Contains(day.Date)).ToArray(), window, settings.Units);
```

`Today = forecast.Days[0]`, `Forecast = forecast.Days.Skip(1).Take(7)`. If `Days` is empty —
a provider that answered with nothing — return the existing 503 problem rather than indexing.

Then extend `WeatherEndpointTests.cs`: `GET /weather` against the Fake provider returns
`today` with high 20 / low 12, `forecast` with 7 entries, `hourly` with 24, and exactly one
`severe` `wind` warning. Keep every existing fact (the 204, the validation theory, the auth
matrix, the 503, `DELETE`'s 415) unedited.

**Verify**: `./ci/run-ci.sh api` → PASS, 0 skips. Then
`curl -s localhost:5301/api/v1/weather | python3 -m json.tool | head -40` against a locally
run API shows `today`, `hourly` and `warnings`.

### Step 6: SPA — the weather library

In `src/Homon.Web/src/lib/weather.ts`, mirroring each C# record with a doc comment naming it
(the file's existing convention), add `WeatherDay` (replacing `WeatherForecastDay`, or keeping
that name — match whatever step 5 chose), `WeatherHour`, `WeatherWarning`,
`WeatherWarningKind`, `WeatherWarningSeverity`, and extend `Weather` with `today`, `hourly`
and `warnings`. Then the copy, which lives here and only here:

```ts
export const WEATHER_WARNING_KIND_LABEL: Record<WeatherWarningKind, string> = {
  wind: 'Gale-force wind',
  thunderstorm: 'Thunderstorm',
  snow: 'Heavy snow',
  rain: 'Heavy rain',
  heat: 'Extreme heat',
  cold: 'Extreme cold',
}

export const WEATHER_WARNING_SEVERITY_WORD: Record<WeatherWarningSeverity, string> = {
  caution: 'Caution',
  severe: 'Severe',
}
```

plus `weatherWarningIcon(kind): LucideIcon` (`Wind`, `CloudLightning`, `Snowflake`,
`CloudRain`, `Thermometer`, `ThermometerSnowflake` — all already available from
`lucide-react`, which the file imports from today), and a pure
`warningSentence(warning, units): string` that composes e.g.
`"Gusts to 94 km/h expected between 14:00 and 21:00 today."` / `"28 mm forecast tomorrow."` —
with `today` / `tomorrow` / the weekday computed from the warning's `date` using
`formatForecastDay`'s local-components idiom (export it from this module rather than
duplicating it in two page files). `useWeather` is unchanged.

**Verify**: `cd src/Homon.Web && npx tsc -b` → exit 0; `npx oxlint` → silent.

### Step 7: SPA — the `/weather` page

New `src/Homon.Web/src/pages/weather-page.tsx`, named export `WeatherPage`, matching
`admin-pages-page.tsx`'s shape (bare fragment, copied `PAGE_H1` and `PANEL` constants,
`useDocumentTitle(pageTitle('Weather'))`). It consumes `useWeather()` and reproduces the four
states `weatherSectionBody` already defines, in the same order and with the same sentences —
204 → the dashed empty state linking `/admin/weather`; 503 → "Weather is temporarily
unavailable."; loading → `null`; loaded → the page.

Loaded layout, top to bottom (artboards `OptionB.dc.html` / `Phone.dc.html`):

1. `<h1 className={PAGE_H1}>Weather</h1>` with the place and the date as a sibling `<p>` in
   the same `flex flex-wrap items-baseline gap-4` wrapper — the place is household data and
   does **not** go in the `<h1>`, for the same reason `dashboard-page.tsx:410` keeps it out of
   the section label.
2. `<ul aria-label="Weather warnings">` of tinted `<li>` banners, per D15, rendered only when
   `warnings.length > 0`.
3. `<section aria-labelledby="weather-today">` — `<h2 id="weather-today">Today</h2>` and a
   `PANEL` holding the 40px condition icon, the current temperature in mono with its unit,
   today's `high° / low°` beside it in muted mono, the condition sentence, and a four-up stat
   grid (`Feels like`, `Wind`, `Rain today`, `Sunrise / sunset`) that is `grid-cols-2` on
   phone and `sm:grid-cols-4` with `border-l border-line` dividers from `sm:` up.
4. `<section aria-labelledby="weather-hours">` — `<h2>Next hours</h2>`, the hourly table per
   D13, and the D14 button as the panel's last row.
5. `<section aria-labelledby="weather-days">` — `<h2>Next 7 days</h2>`, the daily table per
   D13.
6. A muted `<p>` carrying `Forecast fetched HH:mm` from `weather.fetchedAt` (its first use
   anywhere) and the Open-Meteo attribution link, `target="_blank"`
   `rel="noopener noreferrer"`, with the exact href and text the dashboard uses today.
7. When `weather.stale` is true, add one muted sentence to that line saying the forecast could
   not be refreshed — `stale` is also unused today and this page is the right place for it.

Row tinting: an hourly row whose hour appears in a `severe` warning's span gets
`row-tint-down`, in a `caution` warning's span `row-tint-unstable`, and the tripping cell's
text takes `text-down` / `text-unstable` at `font-medium` — the same treatment
`dashboard-page.tsx` gives a down probe's detail. A daily row tints the same way from a
whole-day warning.

Then register it in `App.tsx` — `import { WeatherPage } from '@/pages/weather-page'` beside
`PagePage`, and `<Route path="weather" element={<WeatherPage />} />` after the `pages/:slug`
row — and add `'/weather'` to `READER_ROUTES` in `e2e/helpers.ts`.

**Verify**: `./ci/run-ci.sh web` → PASS. Then `npm run dev` and open
<http://localhost:5300/weather>: the page renders, "Show 6 more hours" steps 6 → 12 → 18 → 24
and then disappears, and nothing scrolls horizontally at 412px in the browser's device mode.

### Step 8: SPA — the dashboard widget

In `dashboard-page.tsx`'s `weatherSectionBody`, per D11 and D12:

- wrap the current-conditions block and the forecast `<ul>` in one
  `<RouterLink to="/weather" aria-label={…}>` with `block rounded-md p-4 hover:bg-bg` and no
  underline, replacing the `${PANEL} p-4` wrapper's padding (the panel keeps the border);
- leave the Open-Meteo `<p>` outside that link, inside the panel, with its own `px-4 pb-3.5`;
- add today's extremes beside the current temperature: `{Math.round(weather.today.high)}° /
  {Math.round(weather.today.low)}°` in `mono text-[17px] font-medium text-muted`, with a
  comment pointing at D11 for why the unit symbol is absent here and present below;
- add a 18px `ChevronRight` (`aria-hidden`) at the row's right end;
- change `weather.forecast.map` to `weather.forecast.slice(0, 3).map(…)` with the D10 comment.

Then extend `dashboard-page.test.tsx`'s loaded test (`:147`) — do not rewrite it — with the
`20° / 12°` text and `getByRole('link', { name: /full forecast/ })` →
`toHaveAttribute('href', '/weather')`, and widen its `/api/v1/weather` stub body to the new
shape. Its `/19°C\/11°C/` and `/16°C\/9°C/` assertions must still pass untouched.

**Verify**: `./ci/run-ci.sh web` → PASS. `grep -rn "api.open-meteo.com" src/Homon.Web/src` →
no matches.

### Step 9: e2e

New `src/Homon.Web/e2e/weather-page.spec.ts`, seeding and cleaning its own location through
`request.put` / `request.delete` in `beforeEach` / `afterEach` exactly as
`e2e/weather.spec.ts` does (workers: 1, shared DB, built bundle):

- from `/`, clicking the widget's `full forecast` link lands on `/weather` with
  `getByRole('heading', { level: 1, name: 'Weather' })` visible;
- the hourly table shows 6 body rows; after one click of the show-more button, 12; after four
  clicks, 24 and the button is gone;
- `getByRole('list', { name: 'Weather warnings' })` contains one item whose text matches
  `/Severe/` and `/Gale-force wind/` (the Fake provider's planted gust run);
- `getByText(/20° \/ 12°/)` is visible in the Today panel;
- `expectNoHorizontalOverflow(page)` at both projects;
- the 204 case: with the location deleted, `/weather` shows the "No weather location yet"
  empty state and its link to `/admin/weather`.

Add `/weather` to `e2e/contrast.spec.ts`'s route list so axe checks the tinted banners in both
schemes. Leave `e2e/weather.spec.ts` otherwise untouched — its three describes still cover the
widget and the admin round-trip.

**Verify**: `./ci/run-ci.sh e2e` → PASS at Pixel 7 and 1440×900, including the contrast rule.

### Step 10: Docs and the index

- `docs/ARCHITECTURE.md`: `grep -n '^### 3\.' docs/ARCHITECTURE.md | tail -3` to find the
  highest section number and take the next. Title it so the limitation leads, e.g. *"3.N
  Severe-weather banners are derived from thresholds, not relayed from a provider"*. Record:
  Open-Meteo has no warnings endpoint; the D3 table and why it is hard-coded rather than
  configurable (pointing at §3.19's existing Rejected list); 48 scanned / 24 sent; the
  request-time slice and why `WeatherCache` was not changed; the `"HH:mm"` wire decision and
  the timezone bug it avoids; `role="alert"` deliberately not used on the banners; and a
  *Rejected* list — a second endpoint for the page, persisting hourly history, admin-tunable
  thresholds, a nav entry for `/weather`, `localStorage` for the show-more count.
- `docs/MODULES.md`: append one sentence to the Weather paragraph under "Added after the
  brief" noting that 020 extended it to a full page with derived advisories.
- `plans/README.md`: add the `| 020 | planned | L | — | … |` row and, under "Verify at
  execution", replace the stale "Open-Meteo's exact attribution wording (010)" entry with
  what step 0 actually confirmed.

**Verify**: `./ci/run-ci.sh` → `PASS — web api e2e`.

## Test plan

| Suite | New coverage | Pattern to follow |
| --- | --- | --- |
| xunit | `WeatherWarningEvaluatorTests.cs` — ~32 facts (step 2's list) | `WmoWeatherCodeMapTests.cs` for the theory shape |
| xunit | `OpenMeteoWeatherProviderTests.cs` — 6 new facts on top of the existing 6 | its own `StubHttpMessageHandler` + `SampleBody` |
| xunit | `WeatherEndpointTests.cs` — today / 7 forecast / 24 hourly / one severe wind warning | its existing `ConfiguredFactory` describe |
| vitest | `weather-page.test.tsx` — loaded page, warnings list, show-more 6→12→24 then gone, 503, 204 empty state | `admin-weather-page.test.tsx` for `stubFetch` + the call log; `renderWithProviders` from `src/test/render.tsx` |
| vitest | `dashboard-page.test.tsx` — today's extremes, the `/weather` link | extend the existing `:147` test, do not replace it |
| Playwright | `weather-page.spec.ts` — step 9's list, both viewports | `e2e/weather.spec.ts`'s seeding |
| Playwright | `contrast.spec.ts` — `/weather` in both schemes | its existing route loop |

Every `stubFetch` route key is a **full path including `/api/v1`**, and an undeclared path
throws `Unexpected fetch: …` — the new page touches only `/api/v1/weather`, but
`renderWithProviders` mounts `AppShell`, so declare `/api/v1/auth/session` too if the test
throws on it.

## Done criteria

Machine-checkable. All must hold:

- [ ] `./ci/run-ci.sh` prints `PASS — web api e2e`
- [ ] `./ci/run-ci.sh api` reports **0 skipped** tests
- [ ] `grep -rn "api.open-meteo.com" src/Homon.Web/src` returns no matches
- [ ] `git diff --stat 9957b7b..HEAD -- src/Homon.Infrastructure/Weather/WeatherCache.cs tests/Homon.Api.Tests/WeatherCacheTests.cs src/Homon.Domain/Weather/WeatherSettings.cs src/Homon.Web/src/pages/admin-weather-page.tsx` is **empty**
- [ ] `git status --porcelain src/Homon.Infrastructure/Persistence/Migrations` is **empty** — no migration
- [ ] `cd src/Homon.Web && npx oxlint` is silent
- [ ] `grep -rn 'role="alert"' src/Homon.Web/src/pages/weather-page.tsx` returns no matches (D15)
- [ ] `grep -c 'hidden sm:table-cell' src/Homon.Web/src/pages/weather-page.tsx` is at least 10 (D13: two `<th>` + two `<td>` for the hourly table's 2 phone-hidden columns, three + three for the daily table's 3 — count both header and body cells)
- [ ] `/weather` appears in `src/Homon.Web/e2e/helpers.ts` and `src/Homon.Web/e2e/contrast.spec.ts`
- [ ] `docs/ARCHITECTURE.md` has a new `### 3.N` section whose first sentence names the
      no-warnings-endpoint limitation
- [ ] `plans/020-weather-page-and-day-extremes.md` exists, carries what step 0 observed, and
      `plans/README.md` has its row
- [ ] branch `plan/020-weather-page` is checked out, nothing merged, nothing pushed

## STOP conditions

Stop and report; do not improvise, if:

- Step 0's fact (2) is false — Open-Meteo's `hourly` block does not start at today 00:00
  local. The request-time slice is written against that anchor and would silently return the
  wrong hours.
- `utc_offset_seconds` is absent from the live response. Without it the server cannot know the
  location's current hour, and the alternative (shipping a tz database and an IANA setting) is
  plan 011's `Calendar:TimeZone` territory, not this plan's.
- Any of `WeatherCacheTests.cs`'s nine facts fails. That means the cache's behaviour changed,
  which this plan forbids.
- A step appears to need a migration, a new `WeatherSettings` column, or a new configuration
  section. All three are out of scope by D3 and D16.
- `e2e/weather.spec.ts`'s `toHaveCount(3)` fails. The widget must still show exactly three
  forecast rows (D10); a failure means the slice is missing, not that the test is wrong.
- A step's verification fails twice after a reasonable fix attempt.
- Any assertion in `dashboard-page.test.tsx` outside the `:147` loaded test needs editing.

## Maintenance notes

- **The thresholds will be argued with.** D3's figures suit a temperate household; a reader in
  Arizona will find 32 °C unremarkable. That is the trade this plan took to avoid a migration
  and six form fields, and `docs/ARCHITECTURE.md`'s Rejected list must say so. If it becomes a
  real complaint, the clean follow-up is a `Weather:Warnings:*` configuration section read
  through `IOptions<T>` — note that **configuration read before `builder.Build()` misses test
  overrides** (see the rate limiter in `Program.cs` and `ReaderHandler`).
- **The payload is now the module's widest contract.** Three surfaces read it: the widget, the
  page, and `WeatherEndpointTests`. A future plan adding a field should extend `WeatherDay`
  rather than adding a parallel array, and should check whether `FakeWeatherProvider` needs to
  grow with it — every e2e assertion in this module is against that fake's numbers.
- **Plan 011 (Calendar) was told to follow 010's `WeatherCache` shape.** This plan does not
  change the cache, so that guidance still stands. But 011 introduces `Calendar:TimeZone`, and
  once it lands there will be two notions of "the household's timezone" — Open-Meteo's
  `utc_offset_seconds` and the configured IANA id. The follow-up in `plans/README.md` about
  promoting a household-wide zone setting should then absorb this one.
- **What a reviewer should scrutinise**: that the hourly slice is computed from
  `TimeProvider` and not from `fetchedAt`; that no new text claims an official warning; that
  `hidden sm:table-cell` is on the `<td>`s as well as the `<th>`s (a missing one shifts every
  cell in the row one column left on a phone, and no test catches it — look at 412px by hand);
  and that the widget's `<Link>` does not enclose the Open-Meteo anchor.
- **Deliberately deferred**: a sparkline of the 24-hour temperature curve (the `Sparkline`
  primitive exists and its unused `size="inline"` variant would fit, but the maintainer asked
  for tables); precipitation *amount* per hour (only probability is shown); any "and 2 more"
  affordance when more than three warnings qualify; `/weather` in the banner navigation.
