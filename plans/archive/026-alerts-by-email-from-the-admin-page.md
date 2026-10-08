# Plan 026: Email the household when a probe goes down and when it is back up — set up on an Alerts admin page

> **Executor instructions**: Follow this plan step by step. Run every verification command and confirm the expected result before moving on. If anything in "STOP conditions" occurs, stop and report — do not improvise. When done, update this plan's row in `plans/README.md` unless a reviewer told you they maintain the index.
>
> **Where to work** (`CLAUDE.md`, hard rules): in `/home/acastaner/Git/homon` itself — **never a git worktree**. Create branch `plan/026-alerts-by-email` from `main`, commit on it, and **leave it checked out** when done. Do not merge, push, switch back to `main`, delete branches or `git stash`.
>
> **Drift check (run first)**: `git diff --stat 79dfe0a..HEAD -- src/Homon.Domain/Monitoring/Probe.cs src/Homon.Infrastructure/Monitoring/ProbeScheduler.cs src/Homon.Infrastructure/Email src/Homon.Infrastructure/InfrastructureServiceCollectionExtensions.cs src/Homon.Infrastructure/Persistence/HomonDbContext.cs src/Homon.Api/Program.cs src/Homon.Api/appsettings.json src/Homon.Api/appsettings.Development.json src/Homon.Web/src/App.tsx src/Homon.Web/src/pages/admin-home-page.tsx src/Homon.Web/src/lib/admin-summary.ts src/Homon.Web/playwright.config.ts src/Homon.Web/e2e tests/Homon.Api.Tests compose.prod.yaml .env.example docs`
> Shared files (`Program.cs`, `HomonDbContext.cs`, the model snapshot, `App.tsx`, `admin-home-page.tsx`, `e2e/helpers.ts`) may legitimately have moved; compare only the regions this plan edits against the excerpts below. A mismatch inside those regions is a STOP.

## Status

- **Priority**: P2 · **Effort**: L · **Risk**: MED — touches the scheduler's hot path, adds a migration and a second always-on `BackgroundService`, and removes production configuration keys.
- **Depends on**: nothing outstanding (builds on 002, 003, 021, 023, 025 — all DONE). **Supersedes** `plans/archive/009-alerts-by-email.md`.
- **Category**: direction · **Planned at**: commit `79dfe0a`, 2026-10-07

## Why this matters

Homon watches the household's services but tells nobody when one fails: you only find out by opening the dashboard. The email transport was built in Phase 0 and has never sent anything. Nothing calls `IAlertEmailSender`, and plan 009, which would have, assumed config-file settings and a rejected plan (008). After this plan:
- When a probe is declared **Down**, Homon emails a list of recipients.
- When the probe is declared **Up** again, it emails them again, saying **how long it was down**.
- The administrator enters the Resend API key, the sender and the recipients on an **Alerts** admin page that looks like the other admin pages.
- Every mail goes through a database outbox, so a Resend hiccup is retried rather than lost. The page lists what was sent and what failed.

## Decisions (load-bearing — do not re-litigate during execution)

**D1. The outage is probe state: `Probe.DownSince` (`DateTimeOffset?`), set and cleared only in `Probe.RecordObservation`.**
- When the derived status becomes `Down` and `DownSince` is null: set `DownSince = observedAt` and return `ProbeOutageChange.WentDown`.
- When the derived status becomes `Up` and `DownSince` is not null: return `ProbeOutageChange.Recovered` and clear `DownSince`.
- Every other case returns `None`.

Consequences, all intended:
- **Down → Unstable → Down flapping** sends no second mail. The probe is still in the same outage until it reaches `Up`.
- **Unstable never mails.** The maintainer asked for Down and Up only.
- A message probe going **Down → Unknown → Up** recovers normally.
- **`Pause`, `Unpause` and `ChangeFailureThreshold` do not touch `DownSince`.**
  - A threshold edit or unpause that flips the status is picked up as a recovery at the next real poll.
  - Downtime includes any paused period.
- **A restart sends nothing spurious.** The status and `DownSince` are persisted, so plan 009's "both ends known" guard is unnecessary.
- **Downtime is measured** from when Homon *declared* Down to when it *declared* Up. Each end lags reality by up to `FailureThreshold − 1` polls, so the lags roughly cancel.
- *Rejected*:
  - Rebuilding the outage start from `ProbeObservations`. A message probe's `Succeeded` does not map to its status, a derived Unknown writes no row, and rows are swept after 30 days.
  - Keeping it in memory. It would be lost on restart.

**D2. Outbox: an `AlertNotification` row is added in the scheduler's existing single `SaveChangesAsync`.** It is written atomically with the status change, so it is never lost and never sent twice. An `AlertDispatcher : BackgroundService` sends pending rows.
- **Retries**: up to 5 attempts, with backoff of 1, 2, 4 and 8 minutes. After that the row is `Failed`, with `LastError`.
- **Rows are always written, even when alerts are off.** The dispatcher marks them `Skipped`, with the reason, so the history shows what happened.
- **Retention**: rows older than 30 days that are not `Pending` are deleted by the dispatcher at most once per hour.
- *Rejected*: plan 009's in-memory channel. A Resend outage, or a restart between the save and the send, would silently lose the mail, and the GUI could not show delivery.

**D3. Settings live in a singleton `AlertSettings` row, the GUI is the only source, and the key is encrypted.**
- **Exemplar**: `WeatherSettings` (constant `SingletonId`, no database constraint).
- **The key**: stored through the existing `ISecretProtector` (purpose `Homon.Secrets.v1`), with the write-only wire rule of plan 003 / §3.17:
  - the response carries only `hasApiKey`;
  - in the request, `null` keeps the key, `""` clears it, and a non-empty value replaces it.
- **Config keys removed**: `Email:ResendApiToken`, `Email:FromAddress`, `Email:FromName`, `Email:AlertRecipients`, and the Production "no token" startup refusal. The `.env`/compose variables go too.

**D4. One configuration switch remains: `Email:Transport` = `Resend` (default) | `Log`.**
- It replaces the token-presence test as the way `IAlertEmailSender` is chosen.
- **Production refuses to start with `Log`.** This keeps the spirit of the old refusal: "a Production host will not silently log alerts".
- The Playwright suite and `HomonApiFactory` set `Log`. This is the new "the gate never mails anyone" guard, replacing `Email__ResendApiToken: ''`.
- **Deliberate change:** in Development the default is `Resend`. A developer who types a real key into their own dev database gets real mail. That is a deliberate act, not an accident.

**D5. `ResendEmailSender` reads the key and sender from the `AlertSettings` row on every send.**
- It builds the client with `ResendClient.Create(new ResendClientOptions { ApiToken = key }, httpClient)`. This is the sister project forgeLog's pattern; confirmed present in Resend 0.8.0.
- The `HttpClient` comes from a named client `"Resend"`.
- `services.AddResend(...)` and the `ResendClientOptions` binding are removed.
- **Unchanged**: `IAlertEmailSender` and `EmailMessage`, so `RecordingEmailSender` keeps working in tests. The caller still composes *what* is said, never *from whom*.

**D6. Templates**: plain text plus a small HTML part, built in code in `AlertMessageBuilder`.
- Every interpolated value goes through `WebUtility.HtmlEncode`.
- Times are in **UTC** (`yyyy-MM-dd HH:mm 'UTC'`, invariant culture). No zone setting exists yet; `Calendar:TimeZone` is plan 011's.
- Links use `FrontEnd:PublicBaseUrl` + `/probes/{id}`. That page exists (plan 023), and readers never see the host, so neither does the email.
- Subjects:
  - Down: `[Homon] {name} is down`
  - Up: `[Homon] {name} is back up`
  - Test: `[Homon] Test alert`
- Control characters (`char.IsControl`) are stripped from the name in subjects.

**D7. Test send = enqueue a `Test` row** (`POST /api/v1/alerts/test` → 202). The row appears in "Recent alerts" and turns Sent or Failed within seconds.
- This avoids a `role="status"` success message, which `docs/design-brief.md` reserves for the session check.
- A `Test` row is sent even when alerts are switched off, but never when they are not set up. That lets the admin prove the key before switching alerts on.

**D8. The Admin home stays client-side** (plan 025 D5). Adding Alerts makes the 8th summary query, the threshold 025's maintenance note named for reconsidering. One small GET is accepted, and the decision is recorded in §3.31.

## Current state (read from `79dfe0a`)

**Domain:**
- `src/Homon.Domain/Monitoring/Probe.cs:114-130`, `RecordObservation`. Today it returns `void`:
  ```csharp
  public void RecordObservation(bool succeeded, double? latencyMs, string? detail, DateTimeOffset observedAt, ProbeStatus? derivedStatus = null)
  {
      var (successes, failures, status) = ProbeStateMachine.Apply(ConsecutiveSuccessCount, ConsecutiveFailureCount, FailureThreshold, succeeded);
      ConsecutiveSuccessCount = successes;
      ConsecutiveFailureCount = failures;
      Status = derivedStatus ?? status;
      LastObservedAt = observedAt; LastLatencyMs = latencyMs; LastDetail = detail;
  }
  ```
- `Pause()` is at `:137`, `Unpause()` at `:150`, `ChangeFailureThreshold()` at `:167`. None of them change.
- `ProbeStatus` = `Unknown, Up, Unstable, Down, Paused`. Enums are stored as strings, max 20 (`ProbeConfiguration.cs:26-34`: `.HasConversion<string>().HasMaxLength(20)`). Follow that.
- `ProbeStateMachine.Derive` order: never polled → Unknown; failures ≥ threshold → Down; successes ≥ threshold → Up; otherwise Unstable. With the default threshold of 2, Up → Down always passes through Unstable.

**Scheduler** (`src/Homon.Infrastructure/Monitoring/ProbeScheduler.cs:131-155`):
```csharp
var observedAt = timeProvider.GetUtcNow();

// This is where plan 009's IProbeTransitionPublisher will read probe.Status before
// and after RecordObservation, right before the save.
probe.RecordObservation(result.Succeeded, result.LatencyMs, result.Detail, observedAt, result.DerivedStatus);
... if (result.DerivedStatus is not ProbeStatus.Unknown) { database.ProbeObservations.Add(...); }
await database.SaveChangesAsync(cancellationToken);
```
- It is the only save: one per probe per poll.
- Loop/`TickAsync`/`[LoggerMessage]` shape to copy for the dispatcher: `ProbeObservationRetentionService.cs` (the whole file, about 60 lines).
- Tests construct the scheduler by hand: `tests/Homon.Api.Tests/ProbeSchedulerTests.cs:219-224` (`BuildScheduler`, `SingleRunnerScopeFactory`, `StaticOptionsMonitor<>`, `FixedTimeProvider`).

**Email** (`src/Homon.Infrastructure/Email/`):
- `IAlertEmailSender.SendAsync(EmailMessage, CancellationToken)` throws `EmailSendException`.
- `EmailMessage(IReadOnlyList<string> To, string Subject, string TextBody, string? HtmlBody = null)`.
- `ResendEmailSender(IResend resend, IOptions<EmailOptions> options)`:
  - builds `From = $"{FromName} <{FromAddress}>"`;
  - applies a 10 s linked-CTS timeout (`SendTimeout`);
  - wraps every failure in `EmailSendException`.
- `LoggingEmailSender`: EventIds 1000 and 1100. Its message text is "Email not sent (no Resend token configured)".
- `EmailOptions`: `ResendApiToken`, `FromAddress`, `FromName`, `AlertRecipients`, `IsResendConfigured`.

**DI** (`src/Homon.Infrastructure/InfrastructureServiceCollectionExtensions.cs:84-127`, `AddHomonEmail(configuration, isProduction)`):
- binds `EmailOptions`;
- the `.Validate(o => o.IsResendConfigured || !isProduction, "Email:ResendApiToken is not configured…")`;
- `services.AddResend(_ => { })`;
- the `ResendClientOptions` binding;
- a scoped `IAlertEmailSender` factory that picks a sender at resolve time. The comment at `:99-106` explains why: test overrides land after registration. Keep that pattern and that comment's reasoning.

**Other infrastructure:**
- `ISecretProtector` (`src/Homon.Infrastructure/Security/ISecretProtector.cs`): `Protect`/`Unprotect`. `Unprotect` throws `CryptographicException` when the key ring is lost.
- The write-only rule as code, in `ProbeEndpoints.cs:561-573`:
  ```csharp
  private static string? ResolveProtectedSecret(ISecretProtector secretProtector, string? secret, string? existingProtectedSecret)
  { if (secret is null) return existingProtectedSecret; return secret.Length == 0 ? null : secretProtector.Protect(secret); }
  ```
- Settings exemplar: `src/Homon.Domain/Weather/WeatherSettings.cs` (`SingletonId = 00000000-0000-0000-0000-000000000001`) and `src/Homon.Api/Endpoints/WeatherEndpoints.cs`:
  - `MapWeatherEndpoints` (`:23-50`);
  - GET → `FindAsync([SingletonId])`;
  - PUT → `Validate` returns `Dictionary<string,string[]>` → `TypedResults.ValidationProblem`, then create-if-missing and stamp `UpdatedAt` from `TimeProvider`;
  - the bodiless-request 415 CSRF check (`:187-196`).
- `src/Homon.Api/Program.cs:206-207` binds `FrontEndOptions` (`PublicBaseUrl`, in **Homon.Api**, which Infrastructure cannot reference).
- `Program.cs:229` calls `AddHomonInfrastructure(builder.Configuration, builder.Environment.IsProduction())`.
- `Program.cs:540-545` holds the module endpoint list (`v1.MapWeatherEndpoints();` …). JSON enums use `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` (`Program.cs:432`), so `Down` serialises as `"down"`.
- `[LoggerMessage]` EventIds in use: 1, 1000, 1100, 2000-2002, 2100, 2200. Use **2300-2399** for alerts (grep `EventId = 23` first to confirm it is free).

**Configuration and test wiring that carries the old keys** (all must change; `grep -rln "ResendApiToken\|AlertRecipients\|RESEND_API_TOKEN\|HOMON_FROM_ADDRESS\|HOMON_ALERT_RECIPIENT\|re_test_token" --exclude-dir=node_modules --exclude-dir=bin --exclude-dir=obj --exclude-dir=archive .`):
- `.env.example`, `compose.prod.yaml:106-112`
- `ci/README.md:103-106`
- `docs/MODULES.md:16,50,124-126`
- `src/Homon.Api/appsettings.json:28-33`, `appsettings.Development.json:20-23`
- `src/Homon.Web/playwright.config.ts` (`Email__ResendApiToken: ''` with its "NOT OPTIONAL" comment)
- `tests/Homon.Api.Tests/{EmailTransportTests,HomonApiFactory,PlainTextSessionTests}.cs`
- `plans/README.md` and plan 009 itself: historical, leave them.

**SPA conventions (plan 025, `docs/ARCHITECTURE.md` §3.30): build new admin pages only from these primitives.**
- `components/admin-classes.ts`:
  - fields: `FIELD_LABEL`, `FIELD_INPUT`, `FIELDSET`, `LEGEND`;
  - buttons: `BUTTON_PRIMARY`, `BUTTON_SECONDARY`, `BUTTON_DANGER`;
  - layout: `ALERT`, `EMPTY_STATE`, `PANEL`, `COLUMN_HEAD`, `ROW`, `ROW_CELLS`, `CARD_FORM`.
- `components/admin-page-header.tsx`: `AdminPageHeader({title, description, count?, back?, children})`.
- `components/admin-section.tsx`: `AdminSection({id, heading, meta?})`, which renders `<section aria-labelledby>`.
- `icon-button.tsx`: `IconButton` (40px, `label` becomes the aria-label).
- `status-chip.tsx`: `StatusChip({state: 'up'|'unstable'|'down'|'unknown'|'paused', word?, glyph?})`.
- `stamp.tsx`: `Stamp({iso})`.
- `lib/admin-summary.ts`: `countOf`.

**The exemplar pages to read before Step 9:**
- **Settings form + current-value panel:** `pages/admin-weather-page.tsx`, read in full:
  - `AdminPageHeader` → `AdminSection` containing a `PANEL` single-row `<ul aria-label>` with a `StatusChip` → an always-visible `<form aria-labelledby className={CARD_FORM}>` with an `<h2 className="text-[15px] font-semibold">`;
  - `<p role="alert" className={ALERT}>{problemDetail(m.error) ?? '…'}</p>`;
  - a `BUTTON_PRIMARY` submit with a muted hint.
- **Write-only secret UI:** `pages/admin-probes-page.tsx:451-495,844-878`:
  - "A secret is already set. [Replace credential]" → a password input plus "Keep the current secret";
  - the field is sent only while the input is shown. Copy the behaviour, with "key" wording.
- **List rows with a remove button:** `pages/admin-probe-groups-page.tsx:276-405` (`<ol aria-label>` rows with `IconButton icon={Minus} tone="danger"`).
- **Data layer:** `lib/weather.ts:141,331-391` for query keys, `apiFetch`, invalidation, and mutations (DELETE sends `body: '{}'`).
- **Success feedback:** none. Success shows only as re-rendered data (design brief: `role="alert"` for errors, `role="status"` only for the session check).
- **Routes:** `App.tsx`:
  - a lazy import maps the named export, e.g. `const AdminWeatherPage = lazy(() => import('@/pages/admin-weather-page').then((m) => ({ default: m.AdminWeatherPage })))`;
  - `<Route path="weather" element={<AdminWeatherPage />} />` sits under `path="admin"`.
- **Admin home:** `pages/admin-home-page.tsx:57-118`. Each section is an `AdminItem {to,label,description,summary,downWord}` in the `monitoring`/`content`/`access` arrays, and `summariseX` lives in `lib/admin-summary.ts`.
- **Tests:**
  - unit: `src/test/fetch.ts` `stubFetch` throws on any undeclared path, so `admin-home-page.test.tsx`'s two stubs and `SECTION_NAMES` must gain the new GET;
  - e2e: `e2e/helpers.ts` `ADMIN_ROUTES` (feeds `layout.spec`), `e2e/contrast.spec.ts`'s admin list, and `e2e/weather.spec.ts` for how a spec seeds and cleans a singleton settings row in the shared database.

## Commands you will need

| Purpose | Command | Expected |
| --- | --- | --- |
| Release build (format gate) | `dotnet build Homon.sln --configuration Release` | 0 warnings, 0 errors |
| Migration | `HOMON_DESIGNTIME_CONNECTION='Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x' dotnet dotnet-ef migrations add AddAlerts --project src/Homon.Infrastructure --startup-project src/Homon.Api --output-dir Persistence/Migrations` | one migration created |
| Apply to dev DB | `dotnet run --project src/Homon.Api -- migrate` | `applied` |
| One xunit class | `dotnet test tests/Homon.Api.Tests --filter FullyQualifiedName~AlertDispatcherTests` | all pass |
| One Vitest file | `npm --prefix src/Homon.Web run test -- admin-alerts-page` | all pass |
| Gate, **one suite at a time** (this machine OOMs / CLR-aborts `0x80131506` on the combined run; retry such aborts) | `./ci/run-ci.sh web` then `./ci/run-ci.sh api` then `./ci/run-ci.sh e2e` | each `PASS`; api has 0 skipped |

Check each command's own exit code. Never pipe the gate into `tail`: that reports tail's exit code.

## Scope

**In scope (create):**
- **Domain:**
  - `src/Homon.Domain/Monitoring/ProbeOutageChange.cs`
  - `src/Homon.Domain/Alerts/{README.md, AlertSettings.cs, AlertNotification.cs, AlertKind.cs, AlertDeliveryState.cs}`
- **Infrastructure:**
  - `src/Homon.Infrastructure/Alerts/{AlertDispatcher.cs, AlertMessageBuilder.cs, AlertOptions.cs}`
  - `src/Homon.Infrastructure/Email/EmailTransport.cs`
  - `Persistence/Configurations/{AlertSettingsConfiguration.cs, AlertNotificationConfiguration.cs}`
  - `Persistence/Migrations/*_AddAlerts*`
- **Api:** `src/Homon.Api/Endpoints/AlertEndpoints.cs`
- **Web:**
  - `src/Homon.Web/src/lib/alerts.ts`
  - `src/Homon.Web/src/pages/admin-alerts-page.tsx` and its `.test.tsx`
  - `src/Homon.Web/e2e/alerts.spec.ts`
- **Tests:** `tests/Homon.Api.Tests/{ProbeOutageTests, AlertMessageBuilderTests, AlertDispatcherTests, AlertEndpointTests, ResendEmailSenderTests}.cs`

**In scope (edit):**
- **Domain and infrastructure code:**
  - `Probe.cs` (add `DownSince`; `RecordObservation` returns `ProbeOutageChange`)
  - `ProbeConfiguration.cs`
  - `ProbeScheduler.cs`
  - `HomonDbContext.cs` and the model snapshot (generated)
  - `Email/{EmailOptions, ResendEmailSender, LoggingEmailSender, IAlertEmailSender (doc comment only)}.cs`
  - `Security/ISecretProtector.cs` (doc comment only)
  - `InfrastructureServiceCollectionExtensions.cs`
- **Api:** `Program.cs`, `appsettings.json`, `appsettings.Development.json`
- **Web:**
  - `App.tsx`
  - `pages/admin-home-page.tsx` and its test
  - `lib/admin-summary.ts` and its test
  - `playwright.config.ts`
  - `e2e/{helpers.ts, contrast.spec.ts, layout.spec.ts}`
- **Tests:** `tests/Homon.Api.Tests/{EmailTransportTests, HomonApiFactory, PlainTextSessionTests, ProbeSchedulerTests, RecordingEmailSender}.cs`
- **Deployment and docs:**
  - `compose.prod.yaml`, `.env.example`
  - `ci/README.md`
  - `docs/{ARCHITECTURE.md, MODULES.md, deployment-runbook.md, design-brief.md (Admin list line only)}`
  - `plans/README.md` (your row)

**Out of scope:**
- **`ProbeStateMachine.cs`.** The outage rule sits on top of it and does not change it.
- **`Pause`/`Unpause`/`ChangeFailureThreshold`** (D1).
- **`StatusEndpoints.cs` and the dashboard.** Showing "down for 2 h" to readers is a follow-up.
- **Per-probe opt-out, a digest mode, other channels** (SMS/webhook/ntfy).
- **A time-zone setting.**
- **Upgrading the `Resend` package** (0.8.0; the sister projects are on 0.16/0.17). Separate change.
- **`components/ui/*` (shadcn).** Plan 025 deliberately does not use it.
- **`ProbeEndpoints.cs`, `WeatherEndpoints.cs`.** Read them; do not edit them.

## Steps

### Step 1 — Branch
`git switch -c plan/026-alerts-by-email` from `main`.
**Verify**: `git branch --show-current` → `plan/026-alerts-by-email`.

### Step 2 — Domain: the outage on the probe (D1)

**1. Create `ProbeOutageChange.cs`:** `public enum ProbeOutageChange { None, WentDown, Recovered }`, with a doc comment that points at D1's rules.

**2. In `Probe.cs`:**
- Add `public DateTimeOffset? DownSince { get; set; }`. Doc comment: "When Homon declared the current outage — set on entering Down, kept through Unstable/Unknown/Paused, cleared on reaching Up. Only RecordObservation changes it."
- Change `RecordObservation` to return `ProbeOutageChange`. After `Status = …`:
```csharp
if (Status == ProbeStatus.Down && DownSince is null) { DownSince = observedAt; return ProbeOutageChange.WentDown; }
if (Status == ProbeStatus.Up && DownSince is not null) { DownSince = null; return ProbeOutageChange.Recovered; }
return ProbeOutageChange.None;
```
- Update the method's XML doc (add `<returns>`).

**3. Callers that ignore the return value still compile.** Grep `RecordObservation(` to confirm none needs changing beyond the scheduler.

**Verify**: `dotnet build src/Homon.Domain --configuration Release` → 0 warnings and 0 errors.

### Step 3 — Domain: the Alerts slot

1. **`AlertKind`**: `{ Down, Up, Test }`.
2. **`AlertDeliveryState`**: `{ Pending, Sent, Failed, Skipped }`.
3. **`AlertSettings`**, modelled on `WeatherSettings`:

   | Member | Type and default |
   | --- | --- |
   | `SingletonId` | `Guid.Parse("00000000-0000-0000-0000-000000000001")`. Same literal as Weather; a different table, so no clash. |
   | `Id` | defaults to `SingletonId` |
   | `IsEnabled` | `bool` |
   | `ProtectedApiKey` | `string?` |
   | `FromAddress` | `string`, default `""` |
   | `FromName` | `string`, default `"Homon"` |
   | `Recipients` | `List<string>`, default `[]` |
   | `CreatedAt`, `UpdatedAt` | `DateTimeOffset` |

   Add `public const int MaxRecipients = 10;` and a plain computed `public bool IsReady => ProtectedApiKey is not null && FromAddress.Length > 0 && Recipients.Count > 0;`. Step 4 excludes it from the mapping with `builder.Ignore`, so add no attribute: the Domain project has no EF reference.
4. **`AlertNotification`**:

   | Member | Type and notes |
   | --- | --- |
   | `Id` | `long` |
   | `Kind` | `AlertKind` |
   | `ProbeId` | `Guid?` |
   | `ProbeName` | `string`. A snapshot, which survives probe deletion. |
   | `Detail` | `string?` (the probe's `LastDetail` at the event) |
   | `OccurredAt` | `DateTimeOffset` |
   | `DownSince` | `DateTimeOffset?` |
   | `State` | `AlertDeliveryState` |
   | `Attempts` | `int` |
   | `NextAttemptAt` | `DateTimeOffset` |
   | `SentAt` | `DateTimeOffset?` |
   | `LastError` | `string?` |

   Add a static factory `ForOutage(Probe probe, ProbeOutageChange change, DateTimeOffset? downSinceBefore, DateTimeOffset at)`:
   - `WentDown`: `Kind=Down`, `DownSince=probe.DownSince`.
   - `Recovered`: `Kind=Up`, `DownSince=downSinceBefore`.
   - Both: `State=Pending`, `NextAttemptAt=at`.
   - Any other `change`: throw `ArgumentOutOfRangeException`.
5. **`README.md`**, one paragraph: this slot owns the pure alert shapes; sending lives in `Homon.Infrastructure/Alerts/`.

**Verify**: `dotnet build src/Homon.Domain --configuration Release` → 0 warnings and 0 errors.

### Step 4 — Persistence and migration

1. **`ProbeConfiguration`**: `DownSince` is nullable, no default.
2. **`AlertSettingsConfiguration`**:
   - table `AlertSettings`;
   - `FromAddress` max 254, `FromName` max 100, `ProtectedApiKey` max 2000;
   - `Recipients` as a primitive collection, which Npgsql maps to `text[]`;
   - `builder.Ignore(s => s.IsReady)`.
3. **`AlertNotificationConfiguration`**:
   - table `AlertNotifications`;
   - `Kind` and `State` as `HasConversion<string>().HasMaxLength(20)`;
   - `ProbeName` max 200, `Detail` max 500, `LastError` max 500;
   - FK to `Probes` with `OnDelete(DeleteBehavior.SetNull)`;
   - index `(State, NextAttemptAt)`, index `OccurredAt`.
4. **`HomonDbContext`**: add `DbSet<AlertSettings> AlertSettings` and `DbSet<AlertNotification> AlertNotifications`, beside the existing sets.
5. **Generate `AddAlerts`** (command above). Inspect it. It must be exactly:
   - one `AddColumn` (`Probes.DownSince`);
   - two `CreateTable`;
   - the indexes and FK;
   - a `Recipients` column of type `text[]`.

**Verify**: Release build → 0 warnings and 0 errors; `dotnet run --project src/Homon.Api -- migrate` → applied. The dev database lives in another project's container: run only the `migrate` verb against it, nothing else.

### Step 5 — Scheduler writes the outbox row (D2)

In `ProbeScheduler.PollAndPersistAsync`, replace the plan-009 comment and the `RecordObservation` line with:
```csharp
// The outage rule lives on the probe (Probe.DownSince, plan 026's D1). An outage change is
// written as an AlertNotification in this same save, so the status change and the mail that
// announces it commit together — the outbox AlertDispatcher drains (D2).
var downSinceBefore = probe.DownSince;
var outage = probe.RecordObservation(result.Succeeded, result.LatencyMs, result.Detail, observedAt, result.DerivedStatus);
if (outage is not ProbeOutageChange.None)
{
    database.AlertNotifications.Add(AlertNotification.ForOutage(probe, outage, downSinceBefore, observedAt));
}
```
The single `SaveChangesAsync` at the end is unchanged.

**Verify**: Release build → 0 warnings and 0 errors.

### Step 6 — Email transport rework (D3, D4, D5)

**1. `EmailTransport.cs`:** `public enum EmailTransport { Resend, Log }`.

**2. `EmailOptions`:** reduce it to `SectionName = "Email"` and `public EmailTransport Transport { get; set; } = EmailTransport.Resend;`. Its doc comment explains:
- the key, sender and recipients moved to the `AlertSettings` row (plan 026);
- `Log` exists so the gate can never mail anyone.

**3. `ResendEmailSender`:**
- Constructor: `(HomonDbContext database, ISecretProtector secrets, IHttpClientFactory httpClients)`.
- Add `public const string HttpClientName = "Resend";`.
- `SendAsync`:
  1. Load `AlertSettings` with `AsNoTracking` by `SingletonId`.
  2. No row, or `ProtectedApiKey` null → throw `EmailSendException("No Resend API key is set.")`.
  3. `Unprotect` throws `CryptographicException` → throw `EmailSendException("The stored Resend API key can no longer be read (the data-protection key ring changed) — enter it again on the Alerts page.")`.
  4. `var resend = ResendClient.Create(new ResendClientOptions { ApiToken = token }, httpClients.CreateClient(HttpClientName));`
  5. Keep the existing `From` formatting (now from the row), the recipient loop, the 10 s timeout and the exception wrapping.
- **Never put the token in an exception message or a log.**

**4. `LoggingEmailSender`:**
- Message: `"Email not sent (Email:Transport is Log). To {Recipients}: {Subject}"`.
- Update the class doc ("registered when Email:Transport is Log").

**5. `AddHomonEmail`:**
- Bind `EmailOptions`, then add `.Validate(o => !isProduction || o.Transport == EmailTransport.Resend, "Email:Transport is Log. A Production host will not silently log alerts instead of sending them — remove the setting, or run a non-Production environment.")` and `.ValidateOnStart()`.
- Remove `AddResend` and the `ResendClientOptions` block.
- Add `services.AddHttpClient(ResendEmailSender.HttpClientName);`.
- Keep both scoped senders and the resolve-time factory, now choosing on `Transport == EmailTransport.Log`.
- Keep and adapt the `:99-106` comment.

**6. Doc comments to widen:**
- `ISecretProtector`'s doc comment: "probe credentials and the Resend API key".
- `IAlertEmailSender`'s remark "plan 009" → "plan 026".

**7. Appsettings:**
- `appsettings.json` Email section → `{ "Transport": "Resend" }`.
- `appsettings.Development.json`: remove the `ResendApiToken` entry. Keep a comment saying a key typed into the dev database's Alerts page sends real mail, and that setting `Email:Transport` to `Log` in user-secrets prevents it.

**Verify**: Release build → 0 warnings and 0 errors. `grep -rn "ResendApiToken\|AlertRecipients\|IsResendConfigured\|AddResend" src` → no matches.

### Step 7 — `AlertDispatcher` and `AlertMessageBuilder` (D2, D6, D7)

#### `AlertOptions`

Section `"Alerts"`:

| Option | Default |
| --- | --- |
| `DispatcherEnabled` | `true` |
| `DispatchInterval` | 5 s |
| `MaxAttempts` | 5 |
| `RetentionDays` | 30 |
| `BatchSize` | 20 |
| `PublicBaseUrl` | `string` |

`PublicBaseUrl` is filled from `FrontEndOptions` in `Program.cs` (Step 8). Infrastructure cannot see `FrontEndOptions`.

#### `AlertMessageBuilder` (public static)

**`Build(AlertNotification n, IReadOnlyList<string> recipients, string publicBaseUrl)`** returns `EmailMessage`. Text bodies (HTML is the same content in `<p>`s, every value `WebUtility.HtmlEncode`d, plus a link):

- **Down:**
  ```
  {name} is down.

  Since:  {DownSince UTC}
  Detail: {Detail}            ← line omitted when Detail is null/blank

  {base}/probes/{ProbeId}     ← omitted when ProbeId is null
  ```
- **Up:**
  ```
  {name} is back up after {FormatDuration(OccurredAt - DownSince)}.

  Down from: {DownSince UTC}
  Back up:   {OccurredAt UTC}

  {link}
  ```
- **Test:** "This is a test alert from Homon. If you can read it, alert email reaches you." plus `Sent to: {recipients joined}` and `{base}/admin/alerts`.

**`FormatDuration(TimeSpan)`** (public, for tests):

| Duration | Output |
| --- | --- |
| < 1 min | `"less than a minute"` |
| < 1 h | `"{m} min"` |
| < 1 day | `"{h} h {m} min"` (drop ` 0 min`) |
| otherwise | `"{d} d {h} h"` (drop ` 0 h`) |

Always invariant culture.

Strip control characters from the name in the subject. Trim `publicBaseUrl`'s trailing `/`.

#### `AlertDispatcher`

`public sealed partial class AlertDispatcher(IServiceScopeFactory scopeFactory, IOptionsMonitor<AlertOptions> options, TimeProvider timeProvider, ILogger<AlertDispatcher> logger) : BackgroundService`.

The `ExecuteAsync` loop is copied from `ProbeObservationRetentionService`: enabled check, catch-and-log, `Task.Delay(interval, timeProvider, ct)`.

`internal async Task<int> DispatchOnceAsync(CancellationToken ct)` handles one scope:
1. Read `now` and the settings row (may be null).
2. Load `due` = `Pending` rows with `NextAttemptAt <= now`, ordered by `OccurredAt`, then `Id`, taking `BatchSize`.
3. For each row, take the **first** branch that applies:
   1. `Kind != Test` and alerts off → `Skipped`, `LastError = "Alerts were switched off."`
   2. `settings?.IsReady != true` → `Skipped`, `LastError = "Alerts were not set up."`
   3. Otherwise `sender.SendAsync(AlertMessageBuilder.Build(row, settings.Recipients, opts.PublicBaseUrl), ct)`:
      - **Success** → `Sent`, `SentAt = now`.
      - **`catch (EmailSendException ex)`** → `Attempts++`. `LastError` = (`ex.InnerException?.Message ?? ex.Message`) truncated to 500. If `Attempts >= MaxAttempts` → `Failed`. Otherwise `NextAttemptAt = now + TimeSpan.FromMinutes(Math.Pow(2, Attempts - 1))`. Log it with `[LoggerMessage]` at Warning, EventId 23xx. Never log recipients' addresses or the key; the count is fine.
4. `SaveChangesAsync` after each row, so one bad row never re-sends the others.
5. Prune, at most once per hour (track `_lastPrunedAt` in a field): `ExecuteDeleteAsync` where `OccurredAt < now - RetentionDays` and `State != Pending`.
6. Return the number processed.

#### Registration

In a new private `AddHomonAlerts(this IServiceCollection, IConfiguration)`, called from `AddHomonInfrastructure` beside the others:
- `AddOptions<AlertOptions>().Bind(section).ValidateOnStart()`;
- `AddHostedService<AlertDispatcher>()`.

**Verify**: Release build → 0 warnings and 0 errors.

### Step 8 — API: `AlertEndpoints` and wiring

1. **`Program.cs`, next to the `FrontEndOptions` registration**:
   ```csharp
   builder.Services.AddOptions<AlertOptions>().Configure<IOptions<FrontEndOptions>>((alerts, frontEnd) => alerts.PublicBaseUrl = frontEnd.Value.PublicBaseUrl);
   ```
   Add a comment: this is resolved from the built container so test overrides win (CLAUDE.md, "Configuration read before builder.Build()").
2. **Endpoint mapping**: add `v1.MapAlertEndpoints();` after `v1.MapWeatherEndpoints();`, with the comment `// Alerts are cross-cutting (docs/MODULES.md), not one of the per-module rows — plan 026.`
3. **`AlertEndpoints.cs`** follows `WeatherEndpoints.cs`'s file shape: `internal static class`, `TypedResults`, `.WithName/.WithSummary`. Every route is `.RequireAuthorization(HomonPolicies.Administrator)`.

**Routes:**

- **`GET /alerts/settings`** → always 200. A missing row gives the defaults. Body: `AlertSettingsResponse(bool IsEnabled, bool HasApiKey, string FromAddress, string FromName, IReadOnlyList<string> Recipients, DateTimeOffset? UpdatedAt)`.
- **`PUT /alerts/settings`** takes `AlertSettingsRequest(bool IsEnabled, string? ApiKey, string FromAddress, string FromName, IReadOnlyList<string>? Recipients)`.
  1. Trim everything.
  2. Validate into `Dictionary<string,string[]>` with camelCase keys. Each failure is a 400 ValidationProblem:

     | Key | Rule |
     | --- | --- |
     | `apiKey` | ≤ 200 characters, no whitespace |
     | `fromAddress` | Empty is allowed only when `!IsEnabled`. Otherwise it must parse with `System.Net.Mail.MailAddress`, and the parsed `.Address` must equal the input (rejects `"Name <a@b>"` smuggling). ≤ 254. |
     | `fromName` | Required, ≤ 100, none of `\r \n < > "` (it is interpolated into the `From` header) |
     | `recipients` | ≤ 10, each a valid address by the same rule, no duplicates (case-insensitive) |
     | `isEnabled` | Enabling requires an effective key (the new non-empty one, or the stored one not being cleared), a `fromAddress` and at least one recipient. Message: "Add a Resend API key, a From address and at least one recipient before switching alerts on." |

  3. Apply the write-only rule with a local copy of `ResolveProtectedSecret` (or `null`/`""`/value inline).
  4. Create the row if missing, stamp `UpdatedAt`, save, return 200 with the response.
  5. **Never echo the key.**
- **`GET /alerts/deliveries`** → the 20 newest rows (`OccurredAt` desc, then `Id` desc) as `AlertDeliveryResponse(long Id, AlertKind Kind, Guid? ProbeId, string ProbeName, DateTimeOffset OccurredAt, DateTimeOffset? DownSince, AlertDeliveryState State, int Attempts, DateTimeOffset? SentAt, string? LastError)`.
- **`POST /alerts/test`**:
  1. Bodiless, so start with the 415 content-type check copied from `WeatherEndpoints.DeleteWeatherSettingsAsync`.
  2. Settings not `IsReady` → `TypedResults.Problem(statusCode: 400, title: "Alerts are not set up", detail: "Save a Resend API key, a From address and at least one recipient first.")`.
  3. A `Test` row is already `Pending` → 202 with that row (a double click sends one mail).
  4. Otherwise add `AlertNotification { Kind = Test, ProbeName = "Test alert", OccurredAt = now, NextAttemptAt = now, State = Pending }` → 202 `TypedResults.Accepted("/api/v1/alerts/deliveries", response)`.

**Verify**: Release build → 0 warnings and 0 errors. `curl -s localhost:5301/api/v1/openapi.json | grep -c '/api/v1/alerts'` → 4 path entries (or check the OpenAPI document via the test host).

### Step 9 — SPA: `lib/alerts.ts` and the Alerts page

**`lib/alerts.ts`** follows `lib/weather.ts`'s shape:
- `AlertSettings`, `AlertSettingsInput`, `AlertDelivery` types, each with a comment naming the C# record it mirrors;
- query keys: `ALERT_SETTINGS_QUERY_KEY = ['alerts','settings']`, `ALERT_DELIVERIES_QUERY_KEY = ['alerts','deliveries']`;
- `useAlertSettings()`;
- `useSaveAlertSettings()` (PUT; invalidates both keys);
- `useAlertDeliveries()` with `refetchInterval: (query) => query.state.data?.some((d) => d.state === 'pending') ? 2000 : false`;
- `useSendTestAlert()` (POST with `body: '{}'`; invalidates deliveries).

**`pages/admin-alerts-page.tsx`** exports `AdminAlertsPage` and calls `useDocumentTitle(pageTitle('Alerts', 'Admin'))`. Structure, top to bottom:

1. **`AdminPageHeader`**:
   - `title="Alerts"`;
   - `description="Homon emails the people below when a probe goes down, and again when it is back up — with how long it was down."`;
   - in its children: a `BUTTON_SECONDARY` **"Send test email"**. Below the header: `useSendTestAlert` errors as `<p role="alert" className={ALERT}>{problemDetail(error) ?? 'Could not queue a test email. Try again.'}</p>`.
2. **`AdminSection id="alerts-delivery" heading="Delivery"`**: a `PANEL` holding a one-row `<ul aria-label="Delivery">`, the same shape as weather's Location panel.
   - The chip:
     - `StatusChip state="up" word="On"` when enabled and ready;
     - `state="paused" word="Off"` when set up but off;
     - `state="unknown" word="Not set up"` otherwise.
   - A mono line: `{fromName} <{fromAddress}> · {countOf(n,'recipient')} · Resend key set` (or `no Resend key`).
3. **The form** — `<form aria-labelledby="alerts-form-heading" className={CARD_FORM}>` with h2 "Email settings". It has:
   - **The switch:** `<label className="flex items-center gap-2 text-[14px] text-text"><input type="checkbox" className="size-4 rounded border-line" /> Send alert emails</label>`, and the muted hint "When off, nothing is mailed; down and up events are still listed below as skipped."
   - **The Resend API key:** the write-only pattern from the probes page.
     - Key stored and not replacing: "A key is already set." plus a `BUTTON_SECONDARY` "Replace key".
     - Otherwise: `<label htmlFor="alerts-api-key">Resend API key</label>` with a `type="password"` input (`FIELD_INPUT mono`, `autoComplete="off"`), plus a "Keep the current key" button when one is stored, plus the hint "Leave empty and save to remove the key." when one is stored.
     - The `apiKey` property is sent only while the input is shown.
   - **The sender:** "From address" (`type="email"`, `required={enabled}`) and "From name" (`required`), in the weather page's auto-fit grid, with the hint "The address must be on a domain verified in Resend."
   - **The recipients:** `<fieldset className={FIELDSET}><legend className={LEGEND}>Recipients</legend>`.
     - A `<ul aria-label="Recipients">` styled like the probe-group member list. Each `li` has the address in mono and `IconButton icon={Minus} tone="danger" label={`Remove ${address}`}`.
     - `EMPTY_STATE` "No recipient yet. Add an address below." when empty.
     - An add row: a labelled "Add recipient" input (`type="email"`, id `alerts-new-recipient`; Enter adds instead of submitting) and a `BUTTON_SECONDARY` "Add" (`type="button"`). Trim the address and ignore a case-insensitive duplicate.
     - Hint: "Up to 10. Changes apply when you save." Disable nothing: at 10 the server's 400 explains the limit.
   - **The save:** errors as `<p role="alert" className={ALERT}>{problemDetail(saveSettings.error) ?? 'Could not save the alert settings. Try again.'}</p>`, then a `BUTTON_PRIMARY` "Save alert settings".
4. **`AdminSection id="alerts-history" heading="Recent alerts" meta={countOf(n,'alert')}`**: a `PANEL` with `COLUMN_HEAD` (Event · Probe · When · Delivery) and `ROW`/`ROW_CELLS` rows. The status words are always written, never colour alone.
   - **Event**: `StatusChip` — Down → `state="down"`, Up → `state="up" word="Back up"`, Test → `state="unknown" word="Test"`.
   - **Probe**: the name.
   - **When**: `<Stamp iso={occurredAt} />`, plus for Up the muted "after {duration}". Add a TS `formatDuration` mirroring C#'s in `lib/alerts.ts`, unit-tested.
   - **Delivery**: `Sent` + Stamp, or `Pending (attempt n)`, or `Failed — {lastError}`, or `Skipped — {lastError}`.
   - When empty: `EMPTY_STATE` "Nothing has been mailed yet. Down and up events appear here, and so does a test email."
   - Rows fold below `lg` per plan 025 D16 (copy the weather/probes `lg:grid` column pattern).

**`App.tsx`**: add a lazy `AdminAlertsPage` and `<Route path="alerts" element={<AdminAlertsPage />} />` after `weather`.

**Admin home**:
- Add to the `monitoring` array, after Reporters:
  ```ts
  { to: '/admin/alerts', label: 'Alerts', description: 'Who is emailed when a probe goes down or comes back.', summary: alerts.data ? summariseAlerts(alerts.data) : null, downWord: 'down' }
  ```
  with `const alerts = useAlertSettings()`.
- `lib/admin-summary.ts` gains `summariseAlerts(settings)`:
  - not ready (no key, or no from address, or no recipients) → `{ text: 'Not set up' }`;
  - not enabled → `{ text: \`Off · ${countOf(n,'recipient')}\` }`;
  - otherwise → `{ text: \`On · ${countOf(n,'recipient')}\` }`.

**Verify**: `./ci/run-ci.sh web` → PASS (lint, typecheck, vitest).

### Step 10 — Test wiring for the removed config

- **`HomonApiFactory`**: replace `["Email:ResendApiToken"] = null` with `["Email:Transport"] = "Log"`, and add `["Alerts:DispatcherEnabled"] = "false"` (same reasoning as the scheduler keys; tests drive `DispatchOnceAsync` by hand).
- **`PlainTextSessionTests.cs:80`**: delete the `["Email:ResendApiToken"] = "re_test_token"` line. Production no longer needs a token, and its default transport `Resend` passes the new validator.
- **`playwright.config.ts`**: replace `Email__ResendApiToken: ''` and its comment with `Email__Transport: 'Log'` and a rewritten "NOT OPTIONAL" comment. The point now: a Resend key saved in the e2e database would otherwise really send. Keep the `LoggingEmailSender=Debug` command-line flag. The e2e run leaves `Alerts:DispatcherEnabled` on, so the test-email spec sees a row turn `Sent`.
- **`RecordingEmailSender`**: add `public Exception? ThrowOnSend { get; set; }`. When it is set, throw it instead of recording.

**Verify**: `grep -rn "ResendApiToken\|re_test_token" tests src/Homon.Web/playwright.config.ts` → no matches.

### Step 11 — Deployment files and docs

1. **`compose.prod.yaml`**: delete the five `Email__…` lines and their comment. Put in their place:
   ```yaml
   # Alert email (Resend key, sender, recipients) is set on the Alerts admin page and stored in the database (plan 026). Nothing to configure here.
   ```
2. **`.env.example`**: delete the `RESEND_API_TOKEN` block and the `HOMON_FROM_ADDRESS`/`HOMON_ALERT_RECIPIENT_*` block, leaving a one-line pointer to `/admin/alerts`.
3. **`ci/README.md:103-106`**: the guard is now `Email__Transport: 'Log'`.
4. **`docs/ARCHITECTURE.md`**:
   - Rewrite §3.7: the transport is chosen by `Email:Transport`, Production refuses `Log`, and the key and sender come from the `AlertSettings` row on each send.
   - Widen §3.17's secret list to include the Resend key.
   - Add **§3.31 "Alerts: the outage is probe state, delivery is an outbox, settings are a database row"** — `grep -n '^### 3\.' docs/ARCHITECTURE.md` first and take the next free number. It covers D1–D8 in about 25 lines, including the rejected alternatives and the 8th-summary-query decision.
5. **`docs/MODULES.md`**: rewrite the 009 row as 026 ("Email on Down and back-Up via an outbox; settings on /admin/alerts"), and the "Alerts" and "Alert recipients" paragraphs.
6. **`docs/deployment-runbook.md`**:
   - After upgrading, open `/admin/alerts`: enter the Resend key, a From address on a Resend-verified domain, and the recipients, save, then "Send test email".
   - The data-protection key ring now also protects the Resend key. Losing it means re-entering the key; the page's delivery rows show "can no longer be read".
   - The old `.env` variables are now ignored and may be deleted.
   - `deploy.sh` does not copy `compose.prod.yaml`. The old copy still works (it passes variables nobody reads) but still demands them, so copy the new one across.
7. **`docs/design-brief.md`**: add "Alerts" to the Admin section's page list.

**Verify**: `grep -rn "RESEND_API_TOKEN\|HOMON_ALERT_RECIPIENT\|HOMON_FROM_ADDRESS" compose.prod.yaml .env.example docs ci` → no matches. `grep -c '^### 3\.' docs/ARCHITECTURE.md` → one more than before.

### Step 12 — Full gate, suite by suite
`./ci/run-ci.sh web`, then `./ci/run-ci.sh api`, then `./ci/run-ci.sh e2e`. Each must print PASS, and the api suite must report 0 skipped. Then update `plans/README.md`'s 026 row, and leave the branch checked out.

## Test plan

**xunit, pure** (pattern: `ProbeStateMachineTests.cs`, table-driven):
- **`ProbeOutageTests`** (threshold 2):
  - Two failures from Up → the second returns `WentDown`, and `DownSince == observedAt` of the second.
  - A third failure → `None`, and `DownSince` is unchanged.
  - One success (Unstable) → `None`, and `DownSince` is kept.
  - A second success (Up) → `Recovered`, and `DownSince` is null.
  - Threshold 1: the first-ever poll failing → `WentDown`.
  - Derived status (message-probe style), with the threshold irrelevant: `Down` → `WentDown`; `Unknown` → `None` with `DownSince` kept; `Up` → `Recovered`.
  - `Pause()` then `Unpause()` while down leaves `DownSince` untouched.
- **`AlertMessageBuilderTests`**:
  - The three exact subjects.
  - A control character in a name is stripped from the subject.
  - The Down text contains the `Since:` UTC stamp and `https://homon.test/probes/{id}`, and omits the `Detail:` line when Detail is null.
  - The Up text contains `back up after 1 h 12 min` for a 72-minute outage.
  - `FormatDuration` table: 30 s, 5 min, 60 min, 61 min, 25 h, 48 h.
  - HTML-encoding: a name `<script>x</script>` does not appear raw in `HtmlBody`; `&lt;script&gt;` does.

**xunit, `[DatabaseFact]`** (pattern: `WeatherEndpointTests.cs` for endpoints, `ProbeSchedulerTests.cs` for hand-built services):
- **`ProbeSchedulerTests`** (extend; give `BuildScheduler` nothing new — the outbox is just another entity):
  - A threshold-1 probe with a failing fake runner → after `TickAsync`, exactly one `AlertNotification` (`Kind=Down`, `State=Pending`, `ProbeName` = the probe's name).
  - Then a succeeding runner (advance the `FixedTimeProvider` past the poll interval) → a second row, `Kind=Up`, `DownSince` = the first poll's time.
- **`AlertDispatcherTests`** (build `AlertDispatcher` by hand with the factory's `IServiceScopeFactory`, a `StaticOptionsMonitor<AlertOptions>` with `PublicBaseUrl = "https://homon.test"`, and a `FixedTimeProvider`; seed `AlertSettings` with `ProtectedApiKey = "protected:x"`, a from address and 2 recipients; call `Emails.Clear()` in each test):
  - Pending Down, enabled → `Sent`, `SentAt` set, and `factory.Emails.Sent` holds 1 message to both recipients.
  - Disabled → `Skipped` "switched off" and 0 sent.
  - Disabled but `Test` → `Sent`.
  - No recipients → `Skipped` "not set up".
  - `ThrowOnSend = new EmailSendException("boom")` → `Attempts 1`, still `Pending`, `NextAttemptAt = now + 1 min`, `LastError "boom"`.
  - A row already at `Attempts 4` failing → `Failed`.
  - A row whose `NextAttemptAt` is in the future is untouched.
  - A 31-day-old `Sent` row is pruned; a 31-day-old `Pending` row is not.
- **`AlertEndpointTests`**:
  - A fresh database GET → 200 with `hasApiKey: false`, `fromName: "Homon"`, and `recipients: []`.
  - Storing a key:
    - PUT with key `"re_plain"` → the response JSON does not contain `re_plain` and `hasApiKey` is true;
    - the database row's `ProtectedApiKey` is not `re_plain`;
    - PUT with `apiKey: null` keeps the key; PUT with `""` clears it.
  - Each 400 case: `isEnabled` with no key; a bad recipient; a duplicate recipient; 11 recipients; `fromName` containing `\n`; `fromAddress` `"X <a@b.c>"`.
  - Auth: anonymous → 401, and an API-key principal → 403 (grep the tests for the existing API-key-forbidden pattern and copy it).
  - `POST /alerts/test`:
    - not set up → 400;
    - set up → 202 and one `Pending` `Test` row, and a second POST still gives one row;
    - `text/plain` → 415.
  - `GET /alerts/deliveries` is newest first and capped at 20.
- **`ResendEmailSenderTests`** (resolve `ResendEmailSender` from a scope and do not call the network):
  - no settings row → `EmailSendException` "No Resend API key";
  - a row whose key cannot be unprotected (store garbage in `ProtectedApiKey`) → `EmailSendException` containing "can no longer be read".
- **`EmailTransportTests`** (rewrite):
  - By default `ResendEmailSender` is resolved: a factory overriding `Email:Transport` to `Resend`, since `HomonApiFactory` now sets `Log`.
  - With `Log`, `LoggingEmailSender` is resolved.
  - Production + `Log` → throws "Email:Transport is Log".
  - Keep `Administrator_options_must_come_as_a_pair`.
  - Delete the token and recipients tests.

**Vitest** (pattern: `admin-weather-page.test.tsx`, `stubFetch`):
- **`admin-alerts-page.test.tsx`**:
  - The defaults render "Not set up".
  - A stored key shows "A key is already set." with no password input; "Replace key" reveals an empty input.
  - Adding two recipients, removing one, then Save sends a PUT whose body equals the exact JSON, with no `apiKey` property when not replacing.
  - A 400 problem shows its detail in `role="alert"`.
  - Deliveries render "Back up", "Failed — …" and "Skipped — …".
  - The empty history shows the empty-state text.
  - "Send test email" sends a POST, and a 400 shows its detail.
- **`admin-summary.test.ts`**: `summariseAlerts`'s three cases.
- **`lib/alerts` `formatDuration`**: the same table as C#.
- **`admin-home-page.test.tsx`**: "Alerts" is in `SECTION_NAMES`, and both stubs declare `/api/v1/alerts/settings`.

**Playwright**:
- **`e2e/alerts.spec.ts`**: seed and clean up through `request` the way `weather.spec.ts` does, including however it avoids the two viewport projects racing on one singleton row.
  - Fill the form with a fake key `re_e2e_not_real`, a From address, and two recipients (remove one), switch alerts on, and Save. The Delivery panel then reads "On".
  - Click "Send test email". Within 15 s the "Recent alerts" region shows a "Test" row reading "Sent" (`Email:Transport=Log` makes the send succeed without mailing).
  - In `afterEach`, PUT the defaults back with `apiKey: ""`.
- Add `{ path: '/admin/alerts', name: 'alerts' }` to `ADMIN_ROUTES`, add the page to `contrast.spec.ts`'s admin list, and add its buttons to `layout.spec.ts`'s tappable list.

## Done criteria

- [ ] `git branch --show-current` → `plan/026-alerts-by-email`. No worktree exists (`git worktree list` shows one entry).
- [ ] `dotnet build Homon.sln --configuration Release` → 0 warnings, 0 errors.
- [ ] `./ci/run-ci.sh web` → PASS; `./ci/run-ci.sh api` → PASS with 0 skipped; `./ci/run-ci.sh e2e` → PASS. Each run separately, and each exit code checked.
- [ ] The new test classes exist and pass: `ProbeOutageTests`, `AlertMessageBuilderTests`, `AlertDispatcherTests`, `AlertEndpointTests`, `ResendEmailSenderTests`, and `admin-alerts-page.test.tsx`.
- [ ] `grep -rn "ResendApiToken\|AlertRecipients\|IsResendConfigured\|RESEND_API_TOKEN\|HOMON_ALERT_RECIPIENT\|re_test_token" src tests compose.prod.yaml .env.example ci docs` → no matches.
- [ ] `grep -n "MapAlertEndpoints" src/Homon.Api/Program.cs` → one line. `grep -n 'path="alerts"' src/Homon.Web/src/App.tsx` → one line.
- [ ] The migration `*_AddAlerts.cs` exists, and `migrate` applies it to the dev database.
- [ ] `git status` / `git diff --stat main` shows only files in Scope.
- [ ] The `plans/README.md` 026 row reads `DONE (date, sha)` or `IN PROGRESS — awaiting manual review`, per the reviewer's instruction.

## STOP conditions

- `ResendClient.Create(ResendClientOptions, HttpClient)` does not exist in the referenced Resend 0.8.0, or does not send through the given client (it needs the API base address set by the library). Report it; do not upgrade the package on your own.
- Npgsql will not map `List<string>` to `text[]` with this EF/Npgsql version (10.0.3) without a value converter. Report it rather than switching to a comma-joined string.
- The `ProbeScheduler.cs:131-155` or `Probe.cs:114-130` excerpts no longer match.
- Any test in `ProbeStateMachineTests`, `ProbeSchedulerTests` (the existing cases) or the plan-025 admin specs needs its assertions changed to pass. The change must be additive.
- `weather.spec.ts` turns out to have no way to keep the two viewport projects off the same singleton row, and the alerts spec flakes because of it.
- A step's verification fails twice after a reasonable fix attempt.

## Maintenance notes

- **Any new `IProbeRunner` gets alerts for free.** The outage rule lives in `Probe.RecordObservation` and the outbox row is written by the scheduler; nothing is kind-specific.
- **Follow-up: the dashboard can show "down for 2 h".** `DownSince` is now persisted.
- **Review focus:**
  - the key never appears in a response, a log line, an exception message, or `LastError`;
  - `fromName` cannot inject header content;
  - `DispatchOnceAsync` saves per row;
  - the Production `Log` refusal exists;
  - the e2e guard comment is intact.
- **Multi-replica**: two dispatchers would race on `Pending` rows. Homon is single-instance (§1). If that changes, claim rows with `FOR UPDATE SKIP LOCKED`.
- **Deferred:**
  - per-probe opt-out;
  - a digest when many probes fail together (today: one mail per probe, and Resend's rate limit is absorbed by the retries);
  - a time-zone setting (011);
  - Unstable mails;
  - upgrading the Resend package;
  - ntfy/webhook channels behind the same outbox.
