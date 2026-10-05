# 010 — Weather widget

> **Executor instructions**: Follow this plan step by step. Run every verification
> command and confirm the expected result before moving to the next step. If anything in
> "STOP conditions" occurs, stop and report — do not improvise. **Do not edit
> `plans/README.md`** — the reviewer dispatching this run maintains the index for this
> session.
>
> **Execution order for this run: 013 (API key scopes and expiry) → 002 (monitoring core,
> groups, ping) → 003 (HTTP probe) → 006 (Links) → 007 (Pages) → 010 (this plan) → 012 (design
> pass).** Plans 004, 005, 008, 009 and 011 do **not** land before this one runs — ignore any
> instinct to treat their shared-file edits as already present.
>
> **Drift check (run first)**: `git diff --stat f4e7261..HEAD -- src/Homon.Domain/Weather
> src/Homon.Infrastructure/Weather src/Homon.Infrastructure/Persistence
> src/Homon.Infrastructure/InfrastructureServiceCollectionExtensions.cs
> src/Homon.Infrastructure/Homon.Infrastructure.csproj Directory.Packages.props
> src/Homon.Api/Endpoints/WeatherEndpoints.cs src/Homon.Api/Program.cs
> src/Homon.Web/src/pages/dashboard-page.tsx src/Homon.Web/src/pages/admin-weather-page.tsx
> src/Homon.Web/src/App.tsx src/Homon.Web/src/App.test.tsx
> src/Homon.Web/src/pages/admin-home-page.tsx src/Homon.Web/e2e/helpers.ts
> src/Homon.Web/playwright.config.ts src/Homon.Web/src/lib docs/ARCHITECTURE.md
> tests/Homon.Api.Tests`. Plans 002, 003, 006 and 007 land first (numeric order, see above)
> and legitimately touch some of the same shared files — `Program.cs`'s endpoint-list
> comment, `HomonDbContext.cs`, the migrations folder, `dashboard-page.tsx` (002 restructures
> Services into probe-group sections; 006 fills in Links; 007 appends a Pages section — see
> its own conditional-render note in "Current state"), `admin-home-page.tsx`'s nav list,
> `e2e/helpers.ts`'s `ADMIN_ROUTES`, `App.test.tsx`'s fetch-stub map, and `package.json`.
> That drift is expected, not a STOP condition. What matters: a commented
> `v1.MapWeatherEndpoints();` still exists somewhere in `Program.cs`
> (grep for it, don't assume the line number); `dashboard-page.tsx` still ends with a flat
> sequence of top-level `<section aria-labelledby="…">` siblings you can append to (a
> conditionally-rendered Pages section from 007 is still a sibling, not a wrapper — see
> "Current state"); `admin-home-page.tsx` still renders `<nav aria-label="Admin sections"><ul>`
> of `<Link>` items. If any of those three no longer hold, STOP.

## Status

- **Priority**: P2
- **Effort**: M
- **Risk**: LOW — one outbound integration to a well-documented, keyless public API, no
  secrets to protect, no write traffic from readers.
- **Depends on**: none functionally — Weather is its own aggregate, no schema or runtime
  coupling to Monitoring/Links/Pages/Backups. It reuses the named-`HttpClient` pattern
  plan 003 (HTTP probe) is expected to establish in `Homon.Infrastructure`; since plans run
  in numeric order that pattern (and the `Microsoft.Extensions.Http` reference it needs)
  will usually already exist — Step 3 checks first and adds it itself if not.
- **Category**: direction
- **Planned at**: commit `f4e7261`, 2026-09-15
- **Reviewed**: 2026-09-15 (review-plan, execution order 013 → 002 → 003 → 006 → 007 → 010 →
  012)

## Context

`docs/MODULES.md` row 010: **"One location, cached provider answer."** Constraint:
*"Weather. Candidate: Open-Meteo, which needs no API key — a self-hoster should not have
to register anywhere to see the weather. Cache the answer server-side."* The slot README
(`src/Homon.Domain/Weather/README.md`) adds: one widget, current conditions and a short
forecast for the household's location, set once by the admin; no entities beyond a
`WeatherSettings` row (latitude, longitude, units) and a short-lived server-side cache, so
the SPA never talks to the provider; the rest lands in `Homon.Infrastructure/Weather/`
behind `IWeatherProvider` and `Homon.Api/Endpoints/WeatherEndpoints.cs`.

`docs/design-brief.md:257-259` already specifies the panel: a 40px muted condition icon,
temperature in mono 30px/500, a muted line of condition/wind/feels-like, then three
forecast rows (day, 20px icon, condition word, high/low). `docs/design/dashboard/
Main.dc.html:202` and `BoardPhone.dc.html:171` head it `Weather · Home` (place name
appended once set); `BoardEmpty.dc.html:66-67` shows the unconfigured state as a bare
`Weather` heading. This plan builds that structure with no `className` (plan 012 styles
it, `docs/ARCHITECTURE.md` §3.10).

**Confirmed against the live API** (the one permitted read-only request, neutral
coordinate, never a household's):

```
$ curl -s 'https://api.open-meteo.com/v1/forecast?latitude=0&longitude=0&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m,is_day&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=4'
{"current":{"temperature_2m":25.0,"apparent_temperature":26.8,"weather_code":0,
  "wind_speed_10m":20.9,"is_day":0},
 "daily":{"time":["2026-09-15","2026-09-16","2026-09-17","2026-09-18"],
  "weather_code":[51,51,51,51],
  "temperature_2m_max":[25.0,25.0,24.6,24.5],"temperature_2m_min":[24.3,24.3,24.2,23.9]}}
```

`daily[0]` is today (already covered by `current`); the three forecast rows are
`daily[1..3]`. `timezone=auto` resolves the location's own calendar day from lat/long at
the provider — the host's OS timezone is irrelevant, `compose.prod.yaml` sets no `TZ` for
`api` today, and nothing here needs one. The `api` container has no egress restriction in
`compose.prod.yaml` (only ports are limited), so outbound HTTPS to `api.open-meteo.com`
needs no infrastructure change — same posture Resend already relies on.

## Decisions

**Storage: a DB singleton row, not configuration.** The README says "the administrator
sets it once" — an admin-page action, not a redeploy. `WeatherSettings` holds **at most
one row**, at a fixed hard-coded id (`WeatherSettings.SingletonId`,
`00000000-0000-0000-0000-000000000001`): every read/write targets that constant, so there
is no ordering problem and no risk of a stray second row. *Rejected*: `Weather:Latitude` in
`.env`, matching `Administrator:Email` — that pattern exists for values a compromised
database must not yield, which coordinates are not, and it would need a restart to change,
unlike every other admin setting.

**Fields**: `Latitude`/`Longitude` (`double`, ±90/±180), `Place` (`string?`, ≤100,
trimmed, empty→`null`, mirrors Links' `Description`), `Units` (`WeatherUnits { Metric,
Imperial }`). `Place` is optional and generic — nothing here assumes a household.

**No geocoding search.** Open-Meteo has a free geocoding endpoint, but it means a second
integration and a search/disambiguation UI the brief doesn't ask for. Deferred: the admin
pastes coordinates; a follow-up can add place search without changing the wire contract.

**Provider seam**: `IWeatherProvider.GetForecastAsync(WeatherSettings, CancellationToken)`
in `Homon.Infrastructure/Weather/`, returning current + up to three forecast days already
in the settings' units — Open-Meteo accepts `temperature_unit=celsius|fahrenheit` and
`wind_speed_unit=kmh|mph` directly, so neither the API nor the SPA converts anything.
`OpenMeteoWeatherProvider` uses a named `HttpClient`, `RequestTimeout = 10s` enforced with
a linked `CancellationTokenSource.CancelAfter` (matches `ResendEmailSender.cs:23,44-45`,
not `HttpClient.Timeout` — the client is shared via the factory). It throws on any
failure; the cache decides what that means.

**WMO code → domain condition, in `Homon.Domain`** (pure, no dependencies, unit-testable
without infrastructure): `WeatherCondition { Clear, PartlyCloudy, Cloudy, Fog, Drizzle,
Rain, Snow, Thunderstorm, Unknown }`, mapped by `WmoWeatherCodeMap.Map(int)`:

| WMO code(s) | Condition | WMO code(s) | Condition |
| --- | --- | --- | --- |
| 0, 1 | `Clear` | 61,63,65,66,67,80,81,82 | `Rain` |
| 2 | `PartlyCloudy` | 71,73,75,77,85,86 | `Snow` |
| 3 | `Cloudy` | 95,96,99 | `Thunderstorm` |
| 45, 48 | `Fog` | anything else | `Unknown` |
| 51,53,55,56,57 | `Drizzle` | | |

The API serialises the enum as a camelCase string — the global
`JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` at `Program.cs:371-372` already
covers every enum. The SPA maps that string to a Lucide icon name and shows the same word
as text beside it (icon `aria-hidden`), per "status is not colour alone"
(`docs/design-brief.md:104`).

**Cache: a bespoke singleton, not raw `IMemoryCache`.** `WeatherCache` holds one
`WeatherSnapshot?` (last forecast + `FetchedAt`), guarded by a `SemaphoreSlim(1,1)` for
single-flight, reading the clock through an injected `TimeProvider`. Plan 013 already
registers `services.AddSingleton(TimeProvider.System);` as the first line inside
`AddHomonInfrastructure`, so `TimeProvider` resolves without this plan adding anything;
`AddWeather` still calls `services.TryAddSingleton(TimeProvider.System)` itself as a defensive,
idempotent no-op (it only registers if nothing already did), not because the registration is
actually missing. *Rejected*:
`IMemoryCache` alone has no single-flight primitive and no stale-while-error behaviour;
either would still need this same wrapper, so the extra abstraction buys nothing. Rules:
- **Fresh for 15 min** (`FreshFor`) — matches Open-Meteo's own update cadence; inside that
  window nothing calls the provider.
- **Stale-while-error, up to 6 h** (`StaleTolerance`): past `FreshFor` but the provider
  fails, the last snapshot is served with `stale: true` if younger than the tolerance;
  otherwise the cache reports unavailable.
- **Single-flight**: concurrent callers past `FreshFor` share one semaphore; the first
  through calls the provider, the rest re-check the refreshed snapshot instead of each
  calling Open-Meteo.
- **Invalidated explicitly** by `PUT`/`DELETE /weather/settings` — a changed location must
  never keep serving the old one's forecast, even inside `FreshFor`.
- **Invalidation must not lose to an in-flight refresh.** `Invalidate()` runs outside the
  semaphore (it is called synchronously from the endpoint handler, not awaited against a
  fetch in progress), so a refresh that started under the *old* settings can still be
  running when `PUT /weather/settings` calls `Invalidate()`. Without a guard, that refresh
  completes afterwards and overwrites the just-cleared snapshot with the old location's
  forecast — invalidation would appear to work and then silently un-happen. Guard it with a
  monotonic generation counter: `Invalidate()` does `Interlocked.Increment(ref
  _generation)` before clearing the snapshot; `GetAsync` captures `_generation` right before
  awaiting the provider and only commits the result to `_snapshot` if the counter is still
  the same value afterwards (a mismatch means an invalidation happened mid-fetch — discard
  the result rather than caching it, and still hand it back to *this* caller, since it is
  the true, current answer for the settings this call was given).
- *Rejected*: persisting forecasts to the database — nothing needs history, and a table
  only a cache reads from is upkeep with no benefit.

**The SPA is not meant to talk to Open-Meteo, and the code must simply never do it.**
`nginx.conf:37` serves `Content-Security-Policy-Report-Only`, not an enforced
`Content-Security-Policy` (the file's own comment above it says why: "nothing collects
reports yet ... revisit when the Pages module lands"). A report-only policy logs a
violation to the browser console; it does **not** block the request. So a stray
`fetch('https://api.open-meteo.com/...')` in the SPA would still reach the provider from
every reader's own browser/IP — the guarantee here is "the bundle contains no such call",
enforced by the Done-criteria grep (`grep -rn "api.open-meteo.com" src/Homon.Web/src`) and
by this plan giving the SPA no client for it, not by the browser refusing the request.
Privacy corollary: coordinates leave the server once, on a cache miss, never from a
reader's own browser/IP, *because nothing in the SPA is written to call the provider* — not
because a network boundary stops it. *Rejected*: the SPA calling Open-Meteo directly — the
brief asks for a server-side cache, and per-reader calls would multiply the provider's rate
limit by family size for nothing.

**Attribution.** Open-Meteo's data is CC BY 4.0. **Verified 2026-09-15** against
`https://open-meteo.com/en/licence` (the one permitted web request for this plan; the URL
redirects there from the American spelling `/en/license`) — its "Licence" section reads,
verbatim: *"You must include a link next to any location Open-Meteo data are displayed, for
example: `<a href="https://open-meteo.com/">Weather data by Open-Meteo.com</a>`."* Step 7's
markup matches that example exactly (it additionally opens the link in a new tab, which the
licence text neither requires nor forbids). No further check is needed before Step 7 ships.

**`Weather:Provider` switch (`OpenMeteo` default, `Fake` for e2e only), refused in
Production — this is how the gate avoids the live network.** Resolved exactly like
`IAlertEmailSender` (`InfrastructureServiceCollectionExtensions.cs:101-110`): both
providers are registered, and `IWeatherProvider` is resolved from `IOptions<WeatherOptions>`
at resolve time, not at registration, for the same test-override reason documented there.
`.Validate(o => o.Provider != Fake || !isProduction, …)` mirrors the existing Resend-token
Production refusal in the same file. `FakeWeatherProvider` returns a fixed, deterministic
forecast (clear, 18 °C, three days built from the injected `TimeProvider` so their weekday
labels are always relative to whenever the suite runs).

**Empty state**: "No weather location yet — an administrator sets it under Admin →
Weather" with a real link, per the brief's general rule (`docs/design-brief.md:265-268`).
Deliberately **not** `BoardEmpty.dc.html:67`'s wording ("sets one in the installation's
configuration") — that artboard predates the DB-row decision above, and
`docs/design-brief.md:120` says text wins over a disagreeing picture, so no edit is needed.

**`GET /weather` response shape**: `204` when unconfigured (mirrors `GetSession`'s
"anonymity is a fact" posture, `AuthenticationEndpoints.cs:132-135`, and lets
`lib/weather.ts` reuse `lib/session.ts`'s `?? null` idiom rather than a redundant
`configured: false` field); `200` with the forecast whenever the cache has any answer
(`stale: bool` on the body); `503` RFC 9457 problem when configured but nothing survives
`StaleTolerance` — the SPA already has `problemDetail()`/`ApiError.status` for this.

**Validation** on `PUT /weather/settings`: `latitude`/`longitude` required and range
checked; `place` optional, trimmed, ≤100; `units` required and must parse
(case-insensitively) to `WeatherUnits` — each a named field in the `ValidationProblem`,
matching `LinkEndpoints`' convention (`plans/006-links.md`).

## Defaults taken (change before implementation if wanted)

- `WeatherCache.FreshFor` = 15 min, `StaleTolerance` = 6 h — constants, not admin-configurable.
- `HttpClientName = "OpenMeteo"` → `https://api.open-meteo.com/`.
- Forecast window: today (`current`) + `daily[1..3]`, matching the artboards' three rows.
- Migration name `AddWeatherSettings`, table `WeatherSettings`.
- No delete-confirmation dialog beyond Links' inline pattern.
- `WeatherOptions.Provider` defaults `OpenMeteo`; only Playwright's `webServer` env sets
  `Weather__Provider=Fake`.

## Current state

| File | Role |
| --- | --- |
| `src/Homon.Domain/Weather/README.md` | Already describes this shape (see Context); Step 1 drops "Not implemented yet" and adds the endpoint list. |
| `src/Homon.Api/Program.cs` | `grep -n "MapWeatherEndpoints"` — locate the commented call (line 479 at `f4e7261`; may have moved). **`v1.MapWeatherEndpoints();` shares one physical comment line with `v1.MapCalendarEndpoints();`** (`//   v1.MapWeatherEndpoints(); v1.MapCalendarEndpoints();` at `f4e7261`), not a line of its own — 011 (Calendar) does not run this session, and `MapCalendarEndpoints` is not a class that exists yet, so uncommenting the whole line is a build break, the same trap plan 007 documents for its own shared line. Split it instead: uncomment only `v1.MapWeatherEndpoints();`, leave `v1.MapCalendarEndpoints();` commented on its own line. |
| `src/Homon.Api/Authentication/HomonPolicies.cs:17-26` | `Reader`/`Administrator`/`ApiKey` — no new policy. |
| `src/Homon.Infrastructure/Persistence/HomonDbContext.cs` | One `DbSet<T>` per aggregate (`ApiKeys` so far); add `DbSet<WeatherSettings>`; extend the class doc comment's module list. |
| `.../Configurations/ApiKeyConfiguration.cs` | Shape to model `WeatherSettingsConfiguration` on. |
| `src/Homon.Infrastructure/Email/ResendEmailSender.cs:14-60` | Outbound-HTTP-with-timeout exemplar `OpenMeteoWeatherProvider` follows, the way plan 003's `HttpProbeRunner` is expected to. |
| `.../InfrastructureServiceCollectionExtensions.cs:23-41,68-111` | `AddXxx(this IServiceCollection …)` composition; `AddHomonEmail`'s two-transports-resolved-from-options pattern (68-111) is the model for `AddWeather`. |
| `src/Homon.Infrastructure/Homon.Infrastructure.csproj` | Plain `Microsoft.NET.Sdk` — `AddHttpClient` needs an explicit `Microsoft.Extensions.Http` reference. **Check first**: `grep -n "Microsoft.Extensions.Http" Directory.Packages.props src/Homon.Infrastructure/Homon.Infrastructure.csproj` — reuse if plan 003 already added it. |
| `src/Homon.Api/Endpoints/AuthenticationEndpoints.cs:149-165` | `SignOutAsync`'s `Content-Type: application/json` guard — model `DELETE /weather/settings` on it verbatim. |
| `src/Homon.Api/Program.cs:371-372` | Global `JsonStringEnumConverter(CamelCase)` — no per-endpoint enum handling needed. |
| `src/Homon.Web/src/lib/session.ts:26-28` | The 204→`undefined`→`null` idiom `fetchWeather` reuses. |
| `src/Homon.Web/src/lib/api.ts` | `apiFetch<T>`, `ApiError`, `problemDetail` — `ApiError.status === 503` distinguishes "unavailable" from "unconfigured" (`null`). |
| `src/Homon.Web/src/test/fetch.ts` | `stubFetch(routes)` — the only fetch mock. |
| `src/Homon.Web/src/pages/dashboard-page.tsx` | Two `<section>` siblings at `f4e7261` (Services, Links). By the time this plan runs, 002 has replaced the Services placeholder with probe-group sections, 006 has filled in Links, and 007 has appended a `<section aria-labelledby="pages-heading">` that **renders only when there is at least one published page** (unlike every other section here, it is conditionally absent — see `plans/007-pages-and-wysiwyg-editor.md` Step 8). None of that changes this step: append the Weather `<section>` as the last sibling, after whatever 002/006/007 left, and don't reorder anything already there. 008 (Backups) does **not** run in this session — do not expect or look for a Backups section. |
| `src/Homon.Web/src/App.test.tsx` | Stubs `/api/v1/meta`+`/auth/session` and renders `<App/>` → `DashboardPage`. Once it calls `useWeather()`, the unstubbed `/api/v1/weather` fetch throws (`stubFetch` fails loudly on unmatched paths) — add `'/api/v1/weather': { status: 204 }` to that map (which earlier plans will already have grown). |
| `src/Homon.Web/src/App.tsx` | No `admin-weather-page.tsx` wiring exists (unlike Links, pre-wired in Phase 0) — add the lazy import and route yourself. |
| `.../pages/admin-home-page.tsx:12-27` | Add `<li><Link to="/admin/weather">Weather</Link></li>` to the nav list. |
| `src/Homon.Web/e2e/helpers.ts:11-17` | Add `{ path: '/admin/weather', name: 'weather' }` to `ADMIN_ROUTES`. |
| `src/Homon.Web/playwright.config.ts` | API `webServer`'s `env` block — add `Weather__Provider: 'Fake'` beside `Email__ResendApiToken: ''`. |
| `src/Homon.Web/package.json` | No `lucide-react` dependency yet (`components.json:13` configures `"iconLibrary": "lucide"` but nothing imports it) — Step 6 runs `npm install lucide-react`, committing the lockfile change (`npm ci` in the gate). Confirmed 2026-09-15 via `npm view lucide-react version peerDependencies`: current version supports React `^19.0.0` — no pin needed beyond what `npm install` resolves into the lockfile. |
| `tests/Homon.Api.Tests/ApiDatabaseFactory.cs`, `DatabaseFactAttribute.cs`, `TestDatabase.cs` | Database-backed fixture: a clone per class from a template migrated with everything landed — `AddWeatherSettings` is picked up automatically. |
| `tests/Homon.Api.Tests/MetaEndpointTests.cs:91-101` | `ConfiguredFactory : HomonApiFactory` — model the Reader-switch test on it. It extends `HomonApiFactory`, which stands up **no** database. `WeatherEndpointTests` needs both a database (for the settings row) and `Weather:Provider=Fake`, so write a *new* private `ConfiguredFactory : ApiDatabaseFactory` local to `WeatherEndpointTests.cs` — same override-`ConfigureWebHost`-and-add-in-memory-config shape as `MetaEndpointTests`' class, layered on `ApiDatabaseFactory`'s own `ConfigureWebHost` (its connection string and email-sender wiring; call `base.ConfigureWebHost(builder)` first). Do not try to reuse `MetaEndpointTests`' private nested class directly — it is private to that file and has no database. |
| `tests/Homon.Api.Tests/StubHttpMessageHandler.cs` | May exist already if plan 003 landed (`HttpProbeRunnerTests` needs the same seam) — `grep -l StubHttpMessageHandler tests/Homon.Api.Tests/*.cs` first; reuse or create. |
| `docs/ARCHITECTURE.md` | `grep -n '^### 3\.' docs/ARCHITECTURE.md` — ends at §3.12 at `f4e7261`; 013 adds one section (§3.13), 002 adds three (§3.14–§3.16), 003 adds one (§3.17), and 007 likely adds one more — all land before this plan runs. Use the next free integer, not a hard-coded one. |

## Commands you will need

| Purpose | Command | Expected on success |
| --- | --- | --- |
| Full gate | `./ci/run-ci.sh` | `PASS — web api e2e` |
| Web only | `./ci/run-ci.sh web` | exit 0 |
| API only | `./ci/run-ci.sh api` | exit 0, 0 skipped |
| E2E only | `./ci/run-ci.sh e2e` | exit 0 |
| Backend build (formatting gate) | `dotnet build Homon.sln -c Release` | exit 0, no warnings |
| Add the migration | `HOMON_DESIGNTIME_CONNECTION='Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x' dotnet dotnet-ef migrations add AddWeatherSettings --project src/Homon.Infrastructure --startup-project src/Homon.Api --output-dir Persistence/Migrations` | Migration files created |
| Frontend typecheck + build | `npm run build` (in `src/Homon.Web`) | exit 0 |
| Frontend lint | `npm run lint` (in `src/Homon.Web`) | exit 0 |
| Frontend unit tests | `npm test` (in `src/Homon.Web`) | all pass |
| Install lucide-react | `npm install lucide-react` (in `src/Homon.Web`) | lockfile updated, exit 0 |

## Scope

**In scope**: `src/Homon.Domain/Weather/{WeatherSettings,WeatherUnits,WeatherCondition,
WmoWeatherCodeMap}.cs` (new), `.../Weather/README.md` (edit); `src/Homon.Infrastructure/
Weather/{WeatherOptions,IWeatherProvider,WeatherForecast,OpenMeteoWeatherProvider,
FakeWeatherProvider,WeatherCache}.cs` (new); `.../Configurations/
WeatherSettingsConfiguration.cs` (new); `HomonDbContext.cs`,
`InfrastructureServiceCollectionExtensions.cs` (edit); the `AddWeatherSettings` migration
+ snapshot (generated); `Homon.Infrastructure.csproj`, `Directory.Packages.props` (edit,
conditional on Step 3); `src/Homon.Api/Endpoints/WeatherEndpoints.cs` (new); `Program.cs`
(edit: uncomment the map call); `src/Homon.Web/src/lib/weather.ts` (new);
`dashboard-page.tsx` (edit: Weather section only), `App.tsx` (edit: lazy import + route),
`admin-weather-page.tsx` (new), `admin-home-page.tsx` (edit: nav link), `App.test.tsx`
(edit: one stub entry); `e2e/helpers.ts` (edit: `ADMIN_ROUTES`), `playwright.config.ts`
(edit: one env line), `package.json`/`package-lock.json` (edit: `lucide-react`);
`docs/ARCHITECTURE.md` (new section); test files in Steps 5/8/9.

**Out of scope, with reasons**: `docs/MODULES.md` — the 010 row already matches; no
deviation to record. `docs/design-brief.md` — the Weather component text already matches
this plan's structure; no edit needed. `.env.example`/`compose.prod.yaml` — no new
required secret or `TZ` (see Context). Geocoding, admin-configurable TTL, historical
forecast storage — deferred, see Decisions. Calendar (011) — appended standalone, with no
reserved placeholder; 011 does not run in this session regardless. `plans/README.md` — the
reviewer dispatching this run maintains the index; do not edit it, including the 010 status
row.

## Git workflow

- No new branch: this plan runs in an isolated worktree the harness already prepared.
  Commit on that worktree's current branch as found; do not create, rename, or switch
  branches, and never merge.
- Commit per step or logical unit (e.g. one commit for Step 1's domain types, one for the
  Step 2 migration, one per SPA step) rather than one commit at the end — a reviewer
  bisecting a broken gate needs the granularity. Message style matches this repository's
  history (`git log --oneline`): `<Area>: <imperative summary> (plan 010)`, e.g. `Weather:
  add domain settings, WMO map and the slot README (plan 010)` or `Weather: wire the
  dashboard section and admin form (plan 010)`.
- Do not push and do not open a pull request — the reviewer takes the finished worktree from
  here and runs `./ci/run-ci.sh` before deciding whether to merge.

## Steps

### Step 1: Domain — `WeatherSettings`, the condition enum, the WMO map, the slot README

Create `WeatherUnits.cs` (`public enum WeatherUnits { Metric, Imperial }`) and
`WeatherCondition.cs` (`public enum WeatherCondition { Clear, PartlyCloudy, Cloudy, Fog,
Drizzle, Rain, Snow, Thunderstorm, Unknown }`, doc-commented as mirroring WMO codes).

Create `WmoWeatherCodeMap.cs`: `public static WeatherCondition Map(int wmoCode)`, a
`switch` expression implementing the Decisions table verbatim, doc-commented with that
table.

Create `WeatherSettings.cs` (mutable class, model `Homon.Domain/Auth/ApiKey.cs`):
`Id` (`Guid`, defaults to `SingletonId`), `Latitude`/`Longitude` (`double`), `Place`
(`string?`), `Units` (`WeatherUnits`), `CreatedAt`/`UpdatedAt`. Constants:
`PlaceMaxLength = 100`; `public static readonly Guid SingletonId =
Guid.Parse("00000000-0000-0000-0000-000000000001");`, doc-commented: "the table holds at
most one row, always at this id."

Update `Weather/README.md`: drop "Not implemented yet"; add the entity/endpoint list
(`WeatherSettings` singleton; `GET/PUT/DELETE /weather/settings`; `GET /weather`); one
sentence on privacy — coordinates leave the server once, to Open-Meteo only, never from a
reader's browser.

**Verify**: `dotnet build src/Homon.Domain/Homon.Domain.csproj -c Release` → exit 0.

### Step 2: Persistence — configuration, `DbContext`, migration

Create `WeatherSettingsConfiguration.cs` (model `ApiKeyConfiguration.cs`): table
`"WeatherSettings"`, key `Id`, `Latitude`/`Longitude`/`Units` required, `Place` with its
max length only (nullable), `CreatedAt`/`UpdatedAt` required. Comment: the singleton
invariant is enforced by `SingletonId` being the only value ever written (Step 1), not by
a database constraint.

Edit `HomonDbContext.cs`: `using Homon.Domain.Weather;`, `public DbSet<WeatherSettings>
WeatherSettings => Set<WeatherSettings>();`; append to the class doc comment's module list
(grep its current wording first — earlier plans will have already appended their own).

**Verify**: `dotnet build src/Homon.Infrastructure/Homon.Infrastructure.csproj -c Release` → exit 0.

Add the migration (command in "Commands you will need").

**Verify**: `ls src/Homon.Infrastructure/Persistence/Migrations/*AddWeatherSettings*`
lists a `.cs` and `.Designer.cs`; `HomonDbContextModelSnapshot.cs` gains a
`"WeatherSettings"` table; `dotnet build Homon.sln -c Release` → exit 0.

### Step 3: Infrastructure — packages, options, the provider seam, the cache

Check for `Microsoft.Extensions.Http` (see "Current state"); if absent, add
`<PackageVersion Include="Microsoft.Extensions.Http" Version="10.0.11" />` to
`Directory.Packages.props` and `<PackageReference Include="Microsoft.Extensions.Http" />`
to `Homon.Infrastructure.csproj`.

Create `WeatherOptions.cs` (model `Email/EmailOptions.cs`): `SectionName = "Weather"`;
`public enum WeatherProviderKind { OpenMeteo, Fake }`; `Provider` defaulting to
`OpenMeteo`, doc-commented that `Fake` exists only for the e2e gate.

Create `IWeatherProvider.cs`: `Task<WeatherForecast> GetForecastAsync(WeatherSettings,
CancellationToken)`. Create `WeatherForecast.cs`: `WeatherForecast(WeatherCurrent Current,
IReadOnlyList<WeatherForecastDay> Days)`; `WeatherCurrent(double Temperature, double
ApparentTemperature, double WindSpeed, WeatherCondition Condition, bool IsDay)`;
`WeatherForecastDay(DateOnly Date, WeatherCondition Condition, double High, double Low)`.

Create `OpenMeteoWeatherProvider.cs` (`internal const string HttpClientName =
"OpenMeteo"`): build the URL from the confirmed query parameters (Context), adding
`temperature_unit=fahrenheit&wind_speed_unit=mph` only when `Units == Imperial`; send
through the named client with a linked `CancellationTokenSource.CancelAfter(TimeSpan.
FromSeconds(10))` per `ResendEmailSender.cs`; deserialize with private DTOs matching the
confirmed shape; map codes via `WmoWeatherCodeMap.Map`; build `Days` from `daily[1..3]`.
Throw on any transport/parse failure — `WeatherCache` decides what it means.

Create `FakeWeatherProvider.cs` (constructor-injected `TimeProvider`): a fixed forecast —
`Clear`, 18 °C, 12 km/h wind, apparent 17 °C, `IsDay = true`, three days at `today+1..3`
from the clock, conditions `PartlyCloudy`/`Clear`/`Rain`, highs/lows near the artboard's
`19°/11°`, `21°/12°`, `16°/9°`. Doc-comment: only reachable via `Weather:Provider=Fake`,
refused outside Development/Testing by Step 3's registration — exists so the e2e gate
never reaches the live network.

Create `WeatherCache.cs` per Decisions: `FreshFor`/`StaleTolerance` constants, a
`SemaphoreSlim(1,1)`, injected `IWeatherProvider`/`TimeProvider`, one nullable
`WeatherSnapshot` (`record(WeatherForecast Forecast, DateTimeOffset FetchedAt)`), an `int
_generation` field, `Task<WeatherCacheResult> GetAsync(WeatherSettings, CancellationToken)`
returning `Forecast`/`FetchedAt`/`Stale`/`IsAvailable`. Re-check the snapshot immediately
after acquiring the semaphore (a concurrent caller may have already refreshed it) before
calling the provider. Before calling the provider, capture `var generationAtStart =
_generation;`; after it returns, only write `_snapshot = fetched` when `generationAtStart ==
_generation` still holds (see Decisions — a concurrent `Invalidate()` mid-fetch must not be
overwritten by the stale-settings result that was already in flight); either way, return the
fetched result to *this* caller. Catch every non-cancellation exception, log with
`[LoggerMessage]`, fall through to stale/unavailable — nothing from the provider escapes
`GetAsync`. Add:

```csharp
public void Invalidate()
{
    Interlocked.Increment(ref _generation);
    _snapshot = null;
}
```

Edit `InfrastructureServiceCollectionExtensions.cs`: add `AddWeather(this
IServiceCollection, IConfiguration, bool isProduction)`, called from
`AddHomonInfrastructure`. Bind `WeatherOptions` (`.ValidateOnStart()` +
`.Validate(o => o.Provider != Fake || !isProduction, "Weather:Provider=Fake refuses to
start in Production.")`); `services.TryAddSingleton(TimeProvider.System);`;
`services.AddHttpClient(OpenMeteoWeatherProvider.HttpClientName, c => c.BaseAddress = new
Uri("https://api.open-meteo.com/"));`; register both providers and resolve
`IWeatherProvider` from `IOptions<WeatherOptions>` at resolve time, exactly like
`AddHomonEmail`'s `IAlertEmailSender` (lines 101-110); `services.AddSingleton<WeatherCache>();`.

**Verify**: `dotnet build Homon.sln -c Release` → 0 errors/warnings.

### Step 4: API — `WeatherEndpoints.cs`

Create `WeatherEndpoints.cs`: `MapWeatherEndpoints(this RouteGroupBuilder parent)` mapping
`/weather`:

| Route | Policy | Behaviour |
| --- | --- | --- |
| `GET ""` | Reader | No settings row → `204`. Else `cache.GetAsync(settings, ct)`; unavailable → `Problem(title: "Weather unavailable", statusCode: 503)`; else `200` with `WeatherResponse`. |
| `GET "/settings"` | Administrator | No row → `204`; else `200` with `WeatherSettingsResponse`. |
| `PUT "/settings"` | Administrator | Validate; upsert the row at `SingletonId`; `cache.Invalidate()`; `200` with the saved value. |
| `DELETE "/settings"` | Administrator | `Content-Type: application/json` guard like `SignOutAsync`; delete if present (idempotent, `204` either way); `cache.Invalidate()`. |

`Validate(WeatherSettingsRequest)` → `Dictionary<string,string[]>?`: `latitude`/
`longitude` required, range-checked; `place` optional, trimmed, ≤100; `units` required,
`Enum.TryParse<WeatherUnits>(…, ignoreCase: true, …)` or fail.

Wire types (all XML-doc-commented — OpenAPI): `WeatherSettingsRequest(double? Latitude,
double? Longitude, string? Place, string? Units)`; `WeatherSettingsResponse(double
Latitude, double Longitude, string? Place, WeatherUnits Units)`; `WeatherResponse(string?
Place, WeatherUnits Units, WeatherCurrentResponse Current, WeatherForecastDayResponse[]
Forecast, DateTimeOffset FetchedAt, bool Stale)`; `WeatherCurrentResponse(double
Temperature, double ApparentTemperature, double WindSpeed, WeatherCondition Condition,
bool IsDay)`; `WeatherForecastDayResponse(DateOnly Date, WeatherCondition Condition,
double High, double Low)`.

Model the class layout on `MetaEndpoints.cs`; model the `DELETE` guard verbatim on
`AuthenticationEndpoints.cs:149-165`.

Edit `Program.cs`: confirm the live text with `grep -n "MapWeatherEndpoints"
src/Homon.Api/Program.cs` first — 007 landing before this plan may have shifted the line
number, and (per "Current state") `v1.MapWeatherEndpoints();` shares one physical comment line
with `v1.MapCalendarEndpoints();` (011, not run this session). Split the line rather than
uncommenting it whole, e.g. from:

```csharp
//   v1.MapWeatherEndpoints(); v1.MapCalendarEndpoints();
```

to:

```csharp
v1.MapWeatherEndpoints();
//   v1.MapCalendarEndpoints();
```

Leave every other commented call alone — `MapCalendarEndpoints` (011) and anything else still
commented name classes that do not exist yet; uncommenting one is a build break, not a style
slip.

**Verify**: `dotnet build src/Homon.Api/Homon.Api.csproj -c Release` → exit 0.

### Step 5: xunit tests

- `WmoWeatherCodeMapTests` (pure): one `[Theory]` case per table row plus one unmapped
  code → `Unknown`.
- `WeatherCacheTests` (pure): a small `TestTimeProvider : TimeProvider` (settable
  `GetUtcNow()`, no new package) and a `StubWeatherProvider` scripted per call, counting
  invocations. Cover: first call populates and calls once; a second call inside `FreshFor`
  doesn't call again; past `FreshFor` it does; a failure inside `StaleTolerance` returns
  the old forecast `Stale = true`; a failure past `StaleTolerance` (or no snapshot ever)
  → `IsAvailable = false`; two concurrent calls past `FreshFor` (`Task.WhenAll`) call the
  provider exactly once; `Invalidate()` forces a refresh even inside `FreshFor`; a scripted
  provider whose call is still in flight when `Invalidate()` runs (e.g. a `StubWeatherProvider`
  that awaits a `TaskCompletionSource` you control) must not have its eventual result land in
  `_snapshot` — assert the *next* `GetAsync` still triggers its own fresh call rather than
  reusing what the pre-invalidate fetch produced.
- `OpenMeteoWeatherProviderTests`, with `StubHttpMessageHandler.cs` (reuse or create, see
  "Current state"): the confirmed shape as a fixture → the right `WeatherForecast` (current
  + exactly three `Days` from `daily[1..3]`); request URL carries
  `temperature_unit=fahrenheit`/`wind_speed_unit=mph` only for `Imperial`; a response
  delayed past a short injected timeout (e.g. 50 ms — never the real 10 s) throws; a
  non-2xx or malformed body throws.
- `WeatherEndpointTests` (`ApiDatabaseFactory` + `[DatabaseFact]`, `Weather:Provider=Fake`
  via `ConfiguredFactory` so nothing touches the live network): `GET /weather` on empty →
  `204`; `PUT` then `GET` → `200` with the fake forecast; `PUT` validation (out-of-range
  lat/long, missing/unparsable `units`, 101-char `place`) → `400` naming the field; `PUT`
  twice never creates a second row (`WeatherSettings.CountAsync() == 1`); `DELETE` → `204`,
  idempotent, `415` without the content type; auth matrix (anonymous read never `401`
  unless `RequireSignInForReaders`; anonymous write `401`; API-key write `403`); a
  scripted `IWeatherProvider` failure with no prior snapshot → `GET /weather` → `503`.

**Verify**: `./ci/run-ci.sh api` → exit 0, 0 skipped, all new tests pass.

### Step 6: SPA data layer — `lib/weather.ts`, `lucide-react`

Run `npm install lucide-react`; confirm `package.json`/`package-lock.json` changed.

Create `lib/weather.ts` (model `lib/meta.ts` for the query, `lib/session.ts` for the
204→null idiom and mutations): `WeatherCondition` union type mirroring the C# enum's
camelCase values; `Weather`/`WeatherSettings`/`WeatherSettingsFields` interfaces mirroring
the wire types (`WeatherSettingsFields` holds form strings, like Links' `LinkFields`);
`WEATHER_QUERY_KEY`, `WEATHER_SETTINGS_QUERY_KEY`; `fetchWeather()` → `Weather | null`;
`useWeather()` (`refetchInterval: 15 * 60 * 1000`, matching `FreshFor` — shorter would only
re-read the same cached answer); `useWeatherSettings()`, `useSaveWeatherSettings()` (`PUT`),
`useDeleteWeatherSettings()` (`DELETE`, `body: '{}'`, matching Links' `useDeleteLink`), both
invalidating both query keys. Export `WEATHER_CONDITION_LABEL: Record<WeatherCondition,
string>` and `weatherConditionIcon(condition)` returning the matching `lucide-react` icon
(`Sun`/`CloudSun`/`Cloud`/`CloudFog`/`CloudDrizzle`/`CloudRain`/`CloudSnow`/
`CloudLightning`/`HelpCircle`) — both live here so tests can assert on label text without
rendering an SVG.

**Verify**: `npm run build` (in `src/Homon.Web`) → exit 0.

### Step 7: SPA pages — dashboard Weather section, the admin page, wiring, docs

Edit `dashboard-page.tsx`: append `<section aria-labelledby="weather-heading">` (don't
reorder existing sections). `const { data: weather, isError, error } = useWeather()`.
Heading: `Weather{weather?.place ? ` · ${weather.place}` : ''}` (matches
`Main.dc.html:202`; plain "Weather" when unconfigured, matching `BoardEmpty.dc.html:66`).
Body: `weather === null` → the empty-state sentence with a `<Link to="/admin/weather">`;
`isError` and `error instanceof ApiError && error.status === 503` → "Weather is
temporarily unavailable."; otherwise the icon (`aria-hidden`) + condition word + rounded
temperature with its unit symbol, the detail line (condition, wind, feels-like), then a
`<ul>` of forecast rows (weekday, icon, condition word, high/low). **Parse each `date`
string as local calendar components, not `new Date(dateString)`** — that constructs UTC
midnight and renders as the *previous* day in a negative-offset timezone. Below the
forecast, one line: "Weather data by <a href="https://open-meteo.com/" target="_blank"
rel="noopener noreferrer">Open-Meteo.com</a>" — wording confirmed against the licence page
(Decisions); no further check needed.

Create `admin-weather-page.tsx` (new): `<h1>Weather</h1>`, a form with labelled
`Latitude`/`Longitude`/`Place` (optional)/`Units` (`<select>`) inputs, a `role="alert"`
paragraph for `problemDetail(...)`, a "Save location" submit, and — only when settings
exist — a "Remove location" button (`useDeleteWeatherSettings().mutate()`). Model the form
on `sign-in-page.tsx:33-77`.

Edit `App.tsx`: add the `AdminWeatherPage` lazy import beside the others and `<Route
path="weather" element={<AdminWeatherPage />} />` inside the admin route.

Edit `admin-home-page.tsx`: add the `Weather` nav link. Edit `App.test.tsx`: add
`'/api/v1/weather': { status: 204 }` to the `anonymous` map. Edit `e2e/helpers.ts`: add
`{ path: '/admin/weather', name: 'weather' }` to `ADMIN_ROUTES`. Edit
`playwright.config.ts`: add `Weather__Provider: 'Fake',` to the API `webServer`'s `env`
(beside `Email__ResendApiToken: ''`, same reasoning — the gate must never call a live
external API).

Edit `docs/ARCHITECTURE.md`: add the next free `### 3.N` (grep first) — summarise:
coordinates never leave the server to a reader's own request, because nothing in the SPA
is written to call the provider — `nginx.conf`'s CSP is report-only and does not enforce
this, so it is a code property, not a network one (Decisions); the cache's
fresh/stale/single-flight rules and the generation-counter guard against an invalidation
racing an in-flight refresh; `Weather:Provider=Fake` exists only for the e2e gate and is
refused in Production. Rejected: the SPA calling the provider directly, persisting
forecasts, an admin-configurable TTL this phase.

**Verify**: `npm run build && npm run lint` (in `src/Homon.Web`) → exit 0.

### Step 8: Vitest

Extend/create `dashboard-page.test.tsx`: `204` → empty-state sentence and link; a fixture
forecast → temperature text, the condition word as visible text (not only inside the
`aria-hidden` icon), each row's high/low; `503` with a problem body → "Weather is
temporarily unavailable." renders.

Create `admin-weather-page.test.tsx`: submitting valid fields issues `PUT
/api/v1/weather/settings` with the parsed numeric body (inspect `stubFetch`'s `calls`,
matching plan 006's `admin-links-page.test.tsx` body-inspection pattern if it landed, or
write fresh otherwise); "Remove location" (shown only when settings exist) issues `DELETE`
with an empty JSON body.

**Verify**: `npm test` (in `src/Homon.Web`) → all pass.

### Step 9: Playwright

Create `e2e/weather.spec.ts`. The API already runs with `Weather__Provider=Fake` (Step
7), so every assertion is against `FakeWeatherProvider`'s fixed forecast — no live
network:
- `beforeEach` seeds settings via the authenticated API (`request.put(
  '/api/v1/weather/settings', { data: { latitude: 51.5, longitude: -0.12, place: 'Test
  location', units: 'metric' } })` — a neutral coordinate); `afterEach` deletes them
  (`request.delete(..., { data: {} })`) — the e2e database is shared, matching plan 002's
  `dashboard-groups.spec.ts` pattern (reuse its seeding helper if it landed first).
- Navigate to `/`; assert the heading reads "Weather · Test location", the fixed
  temperature is present, and three forecast rows render; run
  `expectNoHorizontalOverflow`/`expectTappable` on the section (both viewport projects run
  the same spec).
- A second test: fill and submit the `/admin/weather` form, reload, assert the values
  round-trip.
- A third test, in a `describe` that deletes settings first: assert the empty state and
  its link to `/admin/weather`.

**Verify**: `./ci/run-ci.sh e2e` → exit 0, `weather.spec.ts` at both viewports.

## Test plan

- **xunit, pure**: `WmoWeatherCodeMapTests`, `WeatherCacheTests`,
  `OpenMeteoWeatherProviderTests` — cases listed in Step 5.
- **xunit, database-backed**: `WeatherEndpointTests` — CRUD, validation, the singleton
  invariant, the `204`/`200`/`503` matrix, the auth matrix — cases in Step 5. Model:
  `AuthenticationEndpointTests.cs`, `MetaEndpointTests.cs:91-101`.
- **Vitest**: `dashboard-page.test.tsx`, `admin-weather-page.test.tsx` — cases in Step 8.
  Model: `sign-in-page.test.tsx`.
- **Playwright**: `e2e/weather.spec.ts` — cases in Step 9, against `Weather:Provider=Fake`
  so no live network anywhere in the gate.
- **Verification**: `./ci/run-ci.sh` → `PASS — web api e2e`, 0 skipped api tests.

## Done criteria

- [ ] `dotnet build Homon.sln -c Release` exits 0 with no warnings.
- [ ] `./ci/run-ci.sh api` exits 0, 0 skipped; all new Weather test classes pass.
- [ ] `./ci/run-ci.sh web` exits 0; the two new/extended Vitest files pass.
- [ ] `./ci/run-ci.sh e2e` exits 0; `weather.spec.ts` passes at both viewports.
- [ ] `grep -n "MapWeatherEndpoints" src/Homon.Api/Program.cs` shows an active call.
- [ ] `grep -rn "api.open-meteo.com" src/Homon.Web/src` returns no matches.
- [ ] Manually: `dotnet run --project src/Homon.Api -- migrate` applies
      `AddWeatherSettings` cleanly; setting a location at `/admin/weather` makes the
      dashboard render current conditions and three forecast rows within one reload;
      removing the location returns the empty state.
- [ ] `./ci/run-ci.sh` → `PASS — web api e2e`.
- [ ] `plans/README.md` unchanged (`git status` shows no edit to it — the reviewer updates
      the index after merging).

## STOP conditions

- No commented `v1.MapWeatherEndpoints();` anywhere in `Program.cs`'s module-registration
  block — someone already wired it, or the block changed shape.
- `dashboard-page.tsx` no longer ends with a flat sequence of top-level `<section
  aria-labelledby="…">` siblings (e.g. wrapped in layout logic expecting a fixed section
  set) — reconcile before writing Step 7.
- A migration named/matching `AddWeatherSettings` already exists — don't overwrite it, ask.
- `HomonDbContext.cs` already declares `DbSet<WeatherSettings>` — diff before assuming
  Step 2 is still needed.
- `Homon.Infrastructure.csproj` is no longer a plain `Microsoft.NET.Sdk` project — re-check
  whether `Microsoft.Extensions.Http` still needs an explicit reference.
- Any auth-matrix assumption above is wrong against actual `HomonPolicies` behaviour — a
  security contradiction, not a detail to paper over.
- A step's verification fails twice after a reasonable fix attempt.

## Maintenance notes

- **Open-Meteo's attribution wording** (Step 7) was verified live against
  `open-meteo.com/en/licence` on 2026-09-15 (see Decisions) — the markup matches the
  licence's own example. If a future change touches this line, re-check that page rather
  than trusting recall; licence pages drift without a version bump anyone notices.
- **`Weather:Provider=Fake`** exists solely so the gate never depends on a live third
  party. If Open-Meteo changes its response shape, `OpenMeteoWeatherProviderTests`'
  fixture is the one place to refresh (from a fresh `curl`); the gate would otherwise stay
  green on the fake provider even if the real integration had silently broken.
- **`WeatherCache` is a single process-wide singleton** — fine for Homon's one-`api`-replica
  deployment (§3.6). A future multi-instance deployment would need a distributed cache;
  not before then.
- **Geocoding** is the natural follow-up if pasting coordinates proves painful; it changes
  only how the admin form fills `latitude`/`longitude`, not `WeatherSettingsRequest`'s shape.
- **A reviewer should scrutinize**: that `WeatherCache.GetAsync`'s failure catch never lets
  an exception escape (a crash there 500s every dashboard load the first time Open-Meteo
  has a bad minute); the generation-counter guard in `WeatherCache` (Decisions, Step 3) —
  the fixed `SingletonId` primary key *does* stop a second row from ever existing (a
  concurrent second `PUT` racing an initial create gets a primary-key violation from
  Postgres, not a silent duplicate), but an endpoint that does a plain find-or-add without
  catching that violation would surface it to the caller as an unhandled 500 instead of a
  clean retry-as-update — acceptable for a single-admin settings form, but worth a look if
  this pattern is copied for a multi-writer resource later; and the local-date parsing in
  Step 7, an easy off-by-one to reintroduce.
