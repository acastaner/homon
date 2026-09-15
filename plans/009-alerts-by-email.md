# 009 — Alerts by email

> **Executor instructions**: Follow this plan step by step. Run every verification command
> and confirm the expected result before moving on. If anything in "STOP conditions" occurs,
> stop and report — do not improvise. When done, update this plan's row in `plans/README.md`
> unless a reviewer told you they maintain the index.
>
> **First step**: read "Contract assumed from plans 002, 003 and 008" and confirm each
> landed with that shape (map names if a plan diverged). **STOP if `IAlertEmailSender`,
> `EmailOptions`, or `BackupJobEvaluator.Evaluate` do not exist in the shape described** —
> this plan has nothing to attach to. Plan 002's scheduler and state machine were not fully
> written down when this plan was drafted (its own status line says so); Steps 3–4 below
> tell you how to find whatever landed and add the transition-publishing seam this plan
> needs if 002 did not already add one itself.
>
> **Drift check (run first)**:
> `git diff --stat f4e7261..HEAD -- src/Homon.Domain/Monitoring src/Homon.Domain/Alerts src/Homon.Domain/Backups/BackupJob.cs src/Homon.Infrastructure/Monitoring src/Homon.Infrastructure/Alerts src/Homon.Infrastructure/Email src/Homon.Infrastructure/InfrastructureServiceCollectionExtensions.cs src/Homon.Infrastructure/Persistence src/Homon.Api/Endpoints/AlertEndpoints.cs src/Homon.Api/Program.cs src/Homon.Web/src/pages/admin-home-page.tsx src/Homon.Web/src/lib docs/ARCHITECTURE.md docs/MODULES.md docs/deployment-runbook.md`
> 002–008 legitimately touch shared files (`Program.cs`,
> `InfrastructureServiceCollectionExtensions.cs`, the monitoring scheduler, `BackupJob.cs`,
> `admin-home-page.tsx`) — expected, not drift. Anchor each edit on the marker named in
> *Current state*, confirm it still exists verbatim, and make the smallest edit that adds
> this plan's line beside it; compare only the regions this plan names, not the whole file.

## Status

- **Priority**: P2
- **Effort**: L
- **Risk**: MED — the only `BackgroundService` in the codebase so far, and it hooks a
  scheduler this plan does not fully control the shape of
- **Depends on**: `plans/002-monitoring-core-groups-and-ping.md`,
  `plans/008-backup-reports-and-api-key-administration.md` (both must land first);
  `plans/003-http-probe.md` only for its written-down contract table, not its code
- **Category**: direction
- **Planned at**: commit `f4e7261`, 2026-09-15

## Context

`docs/MODULES.md` (verbatim): "**Alerts.** Email through Resend when a service goes
unstable or down; the key lives in `.env` beside the container." Row: "Email on
unstable/down via `IAlertEmailSender`, recipients from `Email:AlertRecipients`."

The email *transport* already exists (`docs/ARCHITECTURE.md` §3.7, Phase 0): a single seam,
two registered senders, and even the `.env`/`compose.prod.yaml` plumbing for
`Email:AlertRecipients` is already wired (see *Current state* — this is smaller than a
typical module plan because Phase 0 anticipated it). What is missing is everything that
decides *when* to call `IAlertEmailSender.SendAsync`: a way for the monitoring state machine
to announce a transition, a rule for which transitions are worth a mail, a debounce so a
flapping probe does not spam, and the Production-refuses-with-an-empty-list check
`docs/MODULES.md`'s "Alert recipients" constraint promises.

## Contract assumed from plans 002, 003 and 008

Not implemented here — only consumed, except where noted "(added here)".

| Assumed | Shape | Where |
| --- | --- | --- |
| `Probe` | `Guid Id`, `string Name`, … (003's table). | `Homon.Domain/Monitoring/Probe.cs` |
| `ProbeStatus` | An enum with members for up/unstable/down/unknown/paused (003's table says "up/unstable/down/unknown/paused + consecutive counters" without confirming exact member names — use 002's real names throughout). | `Homon.Domain/Monitoring/` |
| The scheduler | "One `BackgroundService` with a per-probe timer, or a channel of due work" — `docs/ARCHITECTURE.md` §4 says this was **not yet decided** when Phase 0 shipped. Whatever 002 landed, it is the one place that persists a `ProbeStatus` change. | `Homon.Infrastructure/Monitoring/` |
| `ProbeTransition` record + `IProbeTransitionPublisher` seam | **Added here if 002 did not add an equivalent.** See Decision 1. | `Homon.Domain/Monitoring/`, `Homon.Infrastructure/Monitoring/` (added here) |
| `IAlertEmailSender.SendAsync(EmailMessage, CancellationToken)` | Throws `EmailSendException` on transport failure; never propagates raw exceptions. | `Homon.Infrastructure/Email/IAlertEmailSender.cs:14-18` (read, confirmed) |
| `EmailOptions` | `ResendApiToken`, `FromAddress`, `FromName`, **`AlertRecipients` (`string[]`, already exists, defaults to `[]`)**, `IsResendConfigured`. | `Homon.Infrastructure/Email/EmailOptions.cs:6-38` (read, confirmed) |
| `FrontEndOptions.PublicBaseUrl` | Already exists, already doc-commented "used to build the links alert emails carry" — this plan is what makes that comment true. | `Homon.Api/Configuration/FrontEndOptions.cs:24-31` (read, confirmed) |
| `BackupJob`, `BackupRun`, `BackupJobEvaluator.Evaluate(job, lastRun, now)` → `BackupJobState {Unknown, Succeeded, Late, Failed}` | Pure, stateless (008's Decisions). No column remembers "last notified" — 008 deliberately left that to this plan. | `Homon.Domain/Backups/{BackupJob,BackupRun,BackupJobEvaluator}.cs` |
| `RecordingEmailSender`, `ApiDatabaseFactory`, `[DatabaseFact]` | Test doubles/fixtures (read, confirmed) — model every new test on these. | `tests/Homon.Api.Tests/` |

## Decisions

**1. Transition seam: `ProbeTransition` + a bounded `Channel<ProbeTransition>`, not a
synchronous call, not an outbox table.** 002's scheduler is not fully written down (its own
status line: "the monitoring core … still need[s] their own sections"), so this plan defines
the seam it needs and the executor adds it to whatever the scheduler turned out to be:

```csharp
// Homon.Domain/Monitoring/ProbeTransition.cs
public sealed record ProbeTransition(
    Guid ProbeId, string ProbeName, ProbeStatus From, ProbeStatus To,
    DateTimeOffset At, string? Detail);

// Homon.Infrastructure/Monitoring/IProbeTransitionPublisher.cs
public interface IProbeTransitionPublisher
{
    /// Non-blocking. Called from the scheduler's hot path — never awaits I/O.
    void Publish(ProbeTransition transition);
}

// Homon.Infrastructure/Monitoring/ChannelProbeTransitionPublisher.cs
public sealed class ChannelProbeTransitionPublisher : IProbeTransitionPublisher
{
    private readonly Channel<ProbeTransition> _channel = Channel.CreateBounded<ProbeTransition>(
        new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public ChannelReader<ProbeTransition> Reader => _channel.Reader;

    // TryWrite never blocks: DropOldest makes room instead of awaiting a reader.
    public void Publish(ProbeTransition transition) => _channel.Writer.TryWrite(transition);
}
```

The scheduler calls `Publish(...)` immediately after the write that persists the new
`ProbeStatus` (right after `SaveChangesAsync` — whichever the scheduler already does),
passing the old and new status the state machine just computed. `AlertDispatcher :
BackgroundService` (Decision 5) reads the channel and does the mailing — so a slow Resend
call (`ResendEmailSender.SendTimeout` is 10 s,
`src/Homon.Infrastructure/Email/ResendEmailSender.cs:23`) never delays the next poll.

*Rejected: a synchronous call from the scheduler straight to `IAlertEmailSender`.* Ties poll
cadence to Resend's latency and to `EmailSendException` handling inside the loop that must
keep polling every other probe. *Rejected: an outbox table.* Homon is a single-instance
home-server deployment (`docs/ARCHITECTURE.md` §1), not a scale-out service; losing whatever
is queued on a container restart is acceptable here — restarts are rare, and the next
transition re-announces the current state anyway. An outbox is a schema, a migration and a
poller for a problem this deployment does not have. If Homon ever runs more than one API
replica, revisit this.

**2. Which transitions mail.** `Homon.Domain/Alerts/AlertRules.cs` (pure, no dependencies —
new slot, see *Scope*):

```csharp
public static class AlertRules
{
    // Mailable: Up, Unstable and Down are "known" states; Unknown and Paused are not.
    // A transition only mails when BOTH ends are known — see rationale below.
    public static bool ShouldMail(ProbeStatus from, ProbeStatus to) =>
        IsKnown(from) && IsKnown(to) && from != to;

    private static bool IsKnown(ProbeStatus status) =>
        status is ProbeStatus.Up or ProbeStatus.Unstable or ProbeStatus.Down;

    public static bool IsWithinCooldown(DateTimeOffset lastAlertedAt, DateTimeOffset now, TimeSpan cooldown) =>
        now - lastAlertedAt <= cooldown;
}
```

So: Up↔Unstable↔Down mail in every direction, including recovery to `Up` (the brief does not
say either way; recommended and recorded as a *Default* — a family that got a "down" mail
wants the "it's back" one too). Anything touching Unknown or Paused never mails.

*Startup reasoning*: the first poll after boot takes every probe from `Unknown` to its real
state. Without the "both ends known" guard, every container restart would mail a `Down`
alert for anything still down from before the restart — the household already knows about
that outage. Cost: a service down at first-ever boot never gets an initial mail — acceptable
for a self-hoster who is, by definition, looking at the dashboard during setup.

**3. Debounce: a per-probe cooldown, not a coalescing window.** `AlertDispatcher` keeps an
in-memory `Dictionary<Guid, DateTimeOffset>` of the last time each probe was mailed. A
mailable transition inside `AlertDispatcher.CooldownWindow` (default 5 minutes, *Defaults*)
of that probe's last mail is dropped, not queued or merged — the next real transition
re-evaluates against the (now possibly expired) cooldown on its own. Up→Unstable→Down within
seconds sends one mail (the first), not three. *Rejected: a coalescing window that waits N
seconds before sending.* Adds latency to the first "the NAS is down" a household wants fast —
the entire point of an alert; a cooldown *after* sending gets flap-suppression without that
delay.

**4. `AlertDispatcher` hooks the scheduler; where exactly is a Step 3/4 task, not a
Decision.** Grep `src/Homon.Infrastructure/Monitoring/` for a `BackgroundService` subclass
(likely `ProbeScheduler` or similar — 002's real name). Confirm it computes old/new
`ProbeStatus` in or around the method that calls `SaveChangesAsync` on the status row; inject
`IProbeTransitionPublisher` there and call `Publish` right after that save. See Steps 3–4 and
the STOP conditions if none of this matches.

**5. Failure handling: log and move on, no retry.** `AlertDispatcher.HandleAsync` catches
`EmailSendException` around `SendAsync`, logs it with `[LoggerMessage]` at `Warning`, and
returns — the dispatcher keeps consuming the channel. *Rejected: one retry with backoff.*
`ResendEmailSender` already enforces its own 10 s timeout per attempt
(`ResendEmailSender.cs:23,44-45`); a retry would itself delay every alert queued behind it —
the head-of-line problem the channel exists to avoid. A transient outage self-heals the
ordinary way: the probe's next transition tries again.

**6. Recipients: validate format, and refuse Production + Resend + empty list.** Extend
`AddHomonEmail` in `InfrastructureServiceCollectionExtensions.cs:68-111` (the exact method
that already has the missing-token refusal at `:76-81`, *not* `Program.cs` — the task brief
that spawned this plan expected it in `Program.cs`; it is not there, and this plan corrects
that pointer) with two more chained calls on the same `AddOptions<EmailOptions>()`:

```csharp
.Validate(
    options => options.AlertRecipients.All(IsValidEmail),
    "Email:AlertRecipients contains an address that is not a valid email address.")
.Validate(
    options => !isProduction || !options.IsResendConfigured || options.AlertRecipients.Length > 0,
    "Email:AlertRecipients is empty. A Production host with Resend configured will not "
    + "silently drop every alert — set at least one recipient.")
```

`IsValidEmail` — a small private static helper using `System.Net.Mail.MailAddress`'s
constructor inside a `try/catch (FormatException)` (the pattern `[EmailAddress]` is itself
built on; no new package). Mirrors `A_production_host_refuses_to_start_without_a_token`
exactly (Test plan).

**7. Templates: plain text + a small HTML body, built in code — no templating engine.**
`Homon.Infrastructure/Alerts/AlertMessageBuilder.cs`, two static methods
(`ForProbeTransition`, `ForBackupJob`). Subject: `[Homon] {Name} is {word}` for probes
(`"down"`/`"unstable"`/`"up"`), `[Homon] Backup {Name} is late` / `[Homon] Backup {Name}
failed` for backups. Body: state, `Detail` (probe) or `Summary` (backup) when present, the
time, and a link to `FrontEndOptions.PublicBaseUrl` (already exists for exactly this — see
Contract table). HTML body interpolates every value through `System.Net.WebUtility.
HtmlEncode` — `Detail`/`Summary` originate from a probe response body or a script's free-text
field, not an admin, so they are untrusted input reaching an HTML mail client.

*Timezone*: **UTC only.** Confirmed by grep: no `TZ`, timezone or `TimeZoneInfo` setting
exists anywhere in `compose.prod.yaml`, `.env.example`, or `appsettings*.json`. Format with
`transition.At.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'",
CultureInfo.InvariantCulture)`. A `TZ`/`Display:TimeZone` setting is future work
(*Maintenance notes*).

**8. Backup alerts: in scope, but a separable final step, on the polling route (not the
report-endpoint hook).** 008's *Maintenance notes* offers two routes: (a) a synchronous hook
on `POST /backups/jobs/{key}/runs` when `succeeded: false`; (b) a low-frequency
`BackgroundService` comparing `BackupJobEvaluator.Evaluate` against a new
`BackupJob.LastNotifiedState` column, for "became late" (no event exists to hook — no report
ever arrives for a job that goes quiet).

This plan uses **route (b) for both Failed and Late**, not a mix. *Rejected*: (a) for Failed
+ (b) for Late — two mechanisms for one module's two trigger words is more surface than one
poller checked every 5 minutes, and Failed gaining ~5 minutes of latency is a fair trade for
a backup job (nobody is watching the dashboard the instant restic exits nonzero, unlike a
service outage). `BackupJob.LastNotifiedState` (`BackupJobState?`, nullable, default `null`)
is added here, exactly where 008's maintenance note said it would land. `BackupEndpoints.cs`
(008's file) is **not** touched — route (b) needs no endpoint change.

*Scope note*: a deliberate extension beyond the brief's literal wording ("Email through
Resend when **a service** goes unstable or down" — backups are not "a service" there). If it
turns out too large once under way, it is written as an independently droppable final step —
Steps 1–7, 9–10 stand alone (see Done criteria's two checklists).

**9. Admin visibility: `POST /api/v1/alerts/test`, in scope, small.** A self-hoster has no
other way to prove their Resend token works end to end. Administrator-only, no request body
(mirrors the sign-out endpoint's explicit `application/json` check,
`AuthenticationEndpoints.cs:149-161`, since a parameterless POST is otherwise
form-reachable), synchronous `SendAsync` call (an admin's deliberate click tolerates the 10 s
Resend timeout — this is not the polling hot path Decision 1 protects). 400 when
`AlertRecipients` is empty; 502 when Resend rejects the send.

## Defaults taken (change before implementation if wanted)

- Recovery (any known state → `Up`) mails. Flip `AlertRules.ShouldMail` to exclude `to ==
  ProbeStatus.Up` for a "only tell me about problems" default.
- `AlertDispatcher.CooldownWindow = TimeSpan.FromMinutes(5)`, per probe, in-memory (resets on
  restart — the same restart that already drops the channel's queue, Decision 1).
- `ChannelProbeTransitionPublisher`'s channel: capacity 256, `DropOldest`. 256 transitions
  between two dispatcher reads has never happened and never will on a home dashboard; the
  number exists only so the channel is bounded at all.
- `BackupAlertWatcher.PollInterval = TimeSpan.FromMinutes(5)`, matching the cooldown window
  for one round number across both mechanisms.
- Backup recovery (Late/Failed → Succeeded) does **not** mail — only `LastNotifiedState`
  resets to `null`, so the *next* Failed/Late fires fresh. Keeps the backup extension (already
  a scope addition, Decision 8) to exactly two trigger words, matching what 008's row said:
  "job lateness."
- `/alerts/test` sends a fixed, English, non-configurable body — no template selection.

## Current state

- **`.env.example` and `compose.prod.yaml` already carry every variable this plan needs** —
  Phase 0 scaffolded them ahead of this module: `.env.example:13-22` (`RESEND_API_TOKEN` +
  the `dotnet user-secrets` command for local dev), `.env.example:50-54`
  (`HOMON_FROM_ADDRESS`, `HOMON_ALERT_RECIPIENT_1`/`_2`), `compose.prod.yaml:98-104`
  (`Email__ResendApiToken`, `Email__FromAddress`, `Email__FromName`,
  `Email__AlertRecipients__0`/`__1` — the `__N` binding `docs/MODULES.md`'s "Alert
  recipients" constraint describes, already wired). Do **not** edit either file unless a
  name here turns out wrong — say so if it does.
- `src/Homon.Infrastructure/Email/EmailOptions.cs:6-38` — `AlertRecipients` already exists,
  defaults to `[]`, already doc-commented "the alerting module (plan 009) is where an empty
  list becomes a startup refusal in Production." This plan makes that comment true.
- `src/Homon.Infrastructure/InfrastructureServiceCollectionExtensions.cs:68-111` —
  `AddHomonEmail`, the method Decision 6 extends; `:76-81` is today's missing-token refusal,
  the pattern the two new `.Validate()` calls copy.
- `src/Homon.Api/Configuration/FrontEndOptions.cs:24-31` — `PublicBaseUrl`, already exists,
  already doc-commented for this exact use.
- `src/Homon.Api/Endpoints/AuthenticationEndpoints.cs:149-161` — the parameterless-POST CSRF
  check `/alerts/test` copies.
- `src/Homon.Api/Authentication/HomonPolicies.cs:17-32` — `Administrator` (`RequireRole`);
  no new policy needed.
- `src/Homon.Api/Program.cs:342-365` — the `AddRateLimiter` block, the IOptions-from-built-
  container pattern (CLAUDE.md: "Configuration read before `builder.Build()` misses test
  overrides") — not touched (no rate limit needed for an admin-only test endpoint), cited
  only as the exemplar the whole file already follows.
- `src/Homon.Api/Program.cs:476-479` — the commented module block. It does **not** list
  Alerts (cross-cutting per `docs/MODULES.md`'s table) — add `v1.MapAlertEndpoints();` on its
  own line immediately after that block, with a one-line comment ("cross-cutting — not one of
  the per-module rows above"), rather than editing the four lines other plans' drift checks
  anchor on.
- `src/Homon.Domain/Backups/README.md` and `docs/MODULES.md`'s 008 row (as shipped) —
  confirm `BackupJobEvaluator` landed exactly as 008 specified before Step 8.
- `src/Homon.Web/src/pages/admin-home-page.tsx:5-27` — the `<nav aria-label="Admin
  sections">` this plan does **not** add a row to (Alerts has no admin page — Decision 9's
  button lives on this page directly, below the `nav`).
- `tests/Homon.Api.Tests/EmailTransportTests.cs` — five existing tests, including
  `Alert_recipients_bind_as_a_list` (`:50-60`) and
  `A_production_host_refuses_to_start_without_a_token` (`:30-38`, the pattern Decision 6's
  new tests, and its `ConfiguredFactory` inner class, copy exactly).
- `tests/Homon.Api.Tests/RecordingEmailSender.cs`, `ApiDatabaseFactory.cs` — read in full;
  `ApiDatabaseFactory` already swaps in `RecordingEmailSender` as `IAlertEmailSender` and
  already sets `FrontEnd:PublicBaseUrl` (`:23-28`) — every `[DatabaseFact]` test here uses it
  unmodified.
- `docs/ARCHITECTURE.md` — ends at §3.12 as read. §3.13 (002), §3.14 (003), one of
  §3.14/§3.15 (007, self-conflicts with 003, says to renumber), and §3.16 (008, also "verify
  first") are reserved. Run `grep -n '^### 3\.' docs/ARCHITECTURE.md` first; by execution
  order the free slot is very likely **§3.17**, but verify — do not assume.
- `plans/README.md:17` — `| 009 | planned | Alerts by email |`.

## Commands you will need

| Purpose | Command | Expected on success |
| --- | --- | --- |
| Full gate | `./ci/run-ci.sh` | `PASS — web api e2e`, 0 skipped api tests |
| API only | `./ci/run-ci.sh api` | same, api suite |
| Web only | `./ci/run-ci.sh web` | `npm ci`, lint, build (=typecheck), vitest all pass |
| e2e only | `./ci/run-ci.sh e2e` | Playwright, two viewports |
| Release build (format gate) | `dotnet build Homon.sln --configuration Release` | 0 warnings/errors |
| New migration (Step 8 only) | `HOMON_DESIGNTIME_CONNECTION='Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x' dotnet dotnet-ef migrations add AddBackupAlertState --project src/Homon.Infrastructure --startup-project src/Homon.Api --output-dir Persistence/Migrations` | migration created |
| Apply locally | `dotnet run --project src/Homon.Api -- migrate` | `applied     : all of them.` |
| One xunit class | `dotnet test tests/Homon.Api.Tests --filter FullyQualifiedName~AlertDispatcherTests` | all pass |
| One Vitest file | `npm --prefix src/Homon.Web run test -- admin-home-page` | all pass |

## Scope

**In scope**:
- `src/Homon.Domain/Monitoring/ProbeTransition.cs` (create)
- `src/Homon.Domain/Alerts/README.md`, `AlertRules.cs` (create — new module slot;
  cross-cutting per `docs/MODULES.md`, but the mailing rule itself is pure and dependency-free
  like every other `Homon.Domain` type)
- `src/Homon.Infrastructure/Monitoring/IProbeTransitionPublisher.cs`,
  `ChannelProbeTransitionPublisher.cs` (create); the scheduler file 002 actually shipped
  (extend — inject the publisher, call `Publish` after the status save)
- `src/Homon.Infrastructure/Alerts/AlertDispatcher.cs`, `AlertMessageBuilder.cs` (create)
- `src/Homon.Infrastructure/Email/EmailOptions.cs` (extend — no new members, just what
  Decision 6 validates)
- `src/Homon.Infrastructure/InfrastructureServiceCollectionExtensions.cs` (extend —
  `AddHomonEmail`'s validators, a new private `AddHomonAlerts`)
- `src/Homon.Api/Endpoints/AlertEndpoints.cs` (create)
- `src/Homon.Api/Program.cs` (one new line, per *Current state*)
- `src/Homon.Web/src/lib/alerts.ts` (create)
- `src/Homon.Web/src/pages/admin-home-page.tsx` (extend)
- `docs/ARCHITECTURE.md` (new §3.17 or next free), `docs/MODULES.md` (009 row),
  `docs/deployment-runbook.md` (short addendum), `plans/README.md`
- Tests: `tests/Homon.Api.Tests/{AlertRulesTests,AlertMessageBuilderTests,
  AlertDispatcherTests,AlertEndpointTests}.cs` (create), `EmailTransportTests.cs` (extend),
  `src/Homon.Web/src/pages/admin-home-page.test.tsx` (extend)
- **Step 8 only** (separable): `src/Homon.Domain/Backups/BackupJob.cs` (extend —
  `LastNotifiedState`), `src/Homon.Infrastructure/Alerts/BackupAlertWatcher.cs` (create),
  `src/Homon.Infrastructure/Persistence/Configurations/BackupJobConfiguration.cs` (extend),
  `src/Homon.Infrastructure/Persistence/Migrations/*AddBackupAlertState*` (create),
  `tests/Homon.Api.Tests/BackupAlertWatcherTests.cs` (create)

**Out of scope**: `IAlertEmailSender`/`ResendEmailSender`/`LoggingEmailSender`/`EmailMessage`
(Phase 0, already built); `BackupEndpoints.cs` (008's file — Decision 8 needs no change to
it); a household-configurable timezone; per-probe alert opt-out; SMS/webhook transports;
`className`/styling (plan 012); `ApiKeyRules.cs`/`ApiKeyAuthenticationHandler.cs`/
`ApiKeyRefusalMiddleware.cs`/`HomonPolicies.cs` (untouched — no new policy needed).

## Steps

### Step 1: Confirm the plans 002/003/008 contract

Read `Probe.cs`, `ProbeStatus` (exact enum member names), the scheduler class, `BackupJob.cs`
and `BackupJobEvaluator.cs` as they actually landed. Note any name differing from the
contract table and use the real name throughout the rest of this plan.

**Verify**: the types exist and `dotnet build Homon.sln` succeeds. Missing any → STOP.

### Step 2: Domain — `ProbeTransition` and `AlertRules`

Create `ProbeTransition.cs` (Decision 1) and `Homon.Domain/Alerts/AlertRules.cs` (Decisions
2–3), and
`Homon.Domain/Alerts/README.md` (one paragraph: what this slot owns — the pure "which
transition mails, is it inside cooldown" rules; everything with a database or a network call
lives in `Homon.Infrastructure/Alerts/`).

**Verify**: `dotnet build src/Homon.Domain` → 0 errors/warnings.

### Step 3: Locate the scheduler and confirm the hook point

Grep `src/Homon.Infrastructure/Monitoring/*.cs` for a class deriving `BackgroundService`.
Read the method that, for one probe, computes the new `ProbeStatus` from the state machine
and saves it. Confirm the old status (before this poll) is available in the same scope —
either already loaded, or one query away. If neither is true, or no such class exists at
all, this is a STOP condition (see below) — 002 has not landed a state machine this plan can
attach to.

**Verify**: you can point at a specific method and line range where `Publish` will go.

### Step 4: Infrastructure — the transition publisher and the scheduler hook

Create `IProbeTransitionPublisher.cs` / `ChannelProbeTransitionPublisher.cs` per Decision 1.
Register `services.AddSingleton<ChannelProbeTransitionPublisher>();
services.AddSingleton<IProbeTransitionPublisher>(sp =>
sp.GetRequiredService<ChannelProbeTransitionPublisher>());` in a new private
`AddHomonAlerts(this IServiceCollection services)` in
`InfrastructureServiceCollectionExtensions.cs`, called from `AddHomonInfrastructure`
alongside `AddHomonDatabase`/`AddHomonEmail`/`AddAdministrator`. Inject
`IProbeTransitionPublisher` into the scheduler class found in Step 3; call `Publish(new
ProbeTransition(probe.Id, probe.Name, oldStatus, newStatus, DateTimeOffset.UtcNow,
result.Detail))` immediately after the status save.

**Verify**: `dotnet build Homon.sln --configuration Release` → 0 errors/warnings.

### Step 5: Infrastructure — `AlertDispatcher` and `AlertMessageBuilder`

Create `AlertMessageBuilder.cs` (Decision 7) and `AlertDispatcher.cs` (Decisions 1, 3, 5),
public classes taking `ChannelProbeTransitionPublisher`, `IAlertEmailSender`,
`IOptions<EmailOptions>`, `IOptions<FrontEndOptions>`, `TimeProvider`,
`ILogger<AlertDispatcher>`. `TimeProvider` is the BCL abstraction (`System.TimeProvider`,
.NET 8+, no NuGet package) — register `services.AddSingleton(TimeProvider.System);` in
`AddHomonAlerts` (008 decided *against* adding the `Microsoft.Extensions.TimeProvider.Testing`
package for one check; this plan needs deterministic time in its own tests, so it uses the
free BCL abstraction and a hand-written fake, not that package — see Test plan). Expose
`HandleAsync(ProbeTransition, CancellationToken)` as `public`, called both from `ExecuteAsync`
(reading `transitions.Reader.ReadAllAsync`) and directly by unit tests. Register
`services.AddHostedService<AlertDispatcher>();` in `AddHomonAlerts`.

**Verify**: `dotnet build Homon.sln --configuration Release` → 0 errors/warnings.

### Step 6: Infrastructure — recipient validation and the Production refusal

Extend `AddHomonEmail` in `InfrastructureServiceCollectionExtensions.cs:68-111` with the two
`.Validate()` calls in Decision 6, plus a private `static bool IsValidEmail(string address)`
helper.

**Verify**: `dotnet build src/Homon.Infrastructure` → 0.

### Step 7: API — `/alerts/test`

Create `AlertEndpoints.cs` per Decision 9. Add `v1.MapAlertEndpoints();` per *Current state*.

**Verify**: `dotnet build src/Homon.Api` → 0.

### Step 8: Backup alerts — separable, do last (Decision 8)

Add `BackupJobState? LastNotifiedState { get; set; }` to `BackupJob.cs`; extend
`BackupJobConfiguration.cs` with the column mapping (nullable, no default — matches
`BackupJobState`'s C# enum, stored as its underlying int unless 008's configuration already
picked a string conversion for the enum, in which case match that). Generate the migration
(command above); inspect it is a single additive `AddColumn`. Create
`BackupAlertWatcher.cs` (`BackgroundService`, Decision 8) using `IServiceScopeFactory` to
open a scope per tick (it needs `HomonDbContext`, which is scoped). Register
`services.AddHostedService<BackupAlertWatcher>();` in `AddHomonAlerts`.

**Verify**: `dotnet build Homon.sln --configuration Release` → 0; `dotnet run --project
src/Homon.Api -- migrate` → `applied: all of them.`

### Step 9: SPA — the test-alert button and wire types

Create `lib/alerts.ts`: a `useSendTestAlert()` mutation (`POST /api/v1/alerts/test`,
`application/json`, empty body `{}`), modelled on `lib/session.ts`'s mutation shape. Extend
`admin-home-page.tsx`: below the existing `<nav>`, a `<button>` "Send test alert" that calls
the mutation and shows its result in a `role="status"` (success: "Test alert sent.") or
`role="alert"` (failure: the problem's `detail`, e.g. "No alert recipients are configured.")
element. No `className`.

**Verify**: `npm --prefix src/Homon.Web run build` (typecheck) → 0 errors.

### Step 10: Docs

`docs/ARCHITECTURE.md`: new §3.17 (verify the free number first, per *Current state*) —
content: the channel seam and why (Decision 1), the cooldown rule (Decision 3), the
Unknown-guard startup reasoning (Decision 2), that backup alerts poll rather than hook
(Decision 8), UTC-only timestamps and why (Decision 7). Keep it to §3.7's length (~15
lines). `docs/MODULES.md`: extend the 009 row; add an "Added after the brief" paragraph
(002's pattern) for the cooldown default, the Unknown-guard default, and the backup-alert
scope extension. `docs/deployment-runbook.md`: a short paragraph — the alert variables are
already in `.env.example`; add one line pointing at `POST /api/v1/alerts/test` as the
end-to-end verification an administrator runs after first bring-up. `plans/README.md`:
status row.

**Verify**: `grep -c '^### 3\.' docs/ARCHITECTURE.md` → one more section than before.

## Test plan

**xunit — pure**
- `AlertRulesTests` (table-driven): `ShouldMail` true for Up↔Unstable↔Down in every
  direction where `from != to`; false for any pair touching `Unknown` or `Paused` on either
  side; false when `from == to`. `IsWithinCooldown`: `now - lastAlertedAt == cooldown` →
  `true` (assert the actual `<=`, per `BackupJobEvaluatorTests`' own convention); one tick
  past → `false`.
- `AlertMessageBuilderTests`: `ForProbeTransition` — subject is exactly `"[Homon] {name} is
  down"` (etc.) for each mailable `To`; text body contains the UTC-formatted timestamp and
  the `PublicBaseUrl`; HTML body HTML-encodes a `ProbeName`/`Detail` containing `<script>`
  (assert the raw tag does not appear, the encoded form does). Same shape for `ForBackupJob`.

**xunit — dispatcher, `RecordingEmailSender` + a hand-written `FixedTimeProvider : TimeProvider`**
- `AlertDispatcherTests.HandleAsync`: a `Down` transition with `From = Up` → one message in
  `RecordingEmailSender.Sent`; `From = Unknown, To = Down` → zero (startup guard); a second
  `Down`-adjacent transition for the same probe inside the cooldown → still one; advance the
  fixed clock past `CooldownWindow`, send a third mailable transition for that probe → two
  total; a sender that throws `EmailSendException` → `HandleAsync` does not throw, and a
  subsequent call for a different probe still sends normally (dispatcher keeps consuming).

**xunit — `[DatabaseFact]`/`ApiDatabaseFactory`**
- `EmailTransportTests` (extend): `Alert_recipients_must_be_valid_email_addresses` —
  `Email:AlertRecipients:0 = "not-an-address"` → `OptionsValidationException` containing "not
  a valid email address" (`ConfiguredFactory` pattern, `:74-88`).
  `A_production_host_with_resend_refuses_an_empty_recipient_list` — Production + a Resend
  token + empty `AlertRecipients` → `OptionsValidationException` containing
  "Email:AlertRecipients is empty" (mirrors `A_production_host_refuses_to_start_without_a_token`
  exactly).
- `AlertEndpointTests`: anonymous `POST /alerts/test` → 401; an API-key principal → 403
  (mirroring 008's load-bearing `ApiKeyEndpointTests` 403 tests); administrator with
  `AlertRecipients` configured (via `ApiDatabaseFactory`'s configuration override) → 200/202
  and `Emails.Sent` has exactly one message subject `"[Homon] Test alert"`; administrator
  with an empty `AlertRecipients` → 400 `ValidationProblem`.
- **End-to-end-in-process** (a probe transition → one recorded email): resolve
  `IProbeTransitionPublisher` from `ApiDatabaseFactory.Services`, call `Publish(new
  ProbeTransition(Guid.NewGuid(), "Test probe", ProbeStatus.Up, ProbeStatus.Down,
  DateTimeOffset.UtcNow, "connection failed"))` directly — this is the seam boundary, it does
  not require 002's real scheduler running — then poll (short `Task.Delay` loop, generous
  timeout) until `Emails.Sent.Count == 1`; assert the subject names the probe and says "down".
- **Step 8 only**, `BackupAlertWatcherTests`: seed a `BackupJob` (1 h interval, 0 grace) and a
  failed `BackupRun` via `HomonDbContext`; call `RunOnceAsync` directly (not the timer) →
  `Emails.Sent` has one Failed alert, `LastNotifiedState == Failed`; call again unchanged →
  still one; update the run to `Succeeded`, call again → no new mail, `LastNotifiedState`
  reset to `null`.

**Vitest**
- `admin-home-page.test.tsx` (extend): the "Send test alert" button exists; clicking it with
  `stubFetch` returning 200 shows a `role="status"` success message; returning 400 with a
  problem body shows its `detail` in a `role="alert"` element.

**Playwright**
- No new spec file. In `e2e/admin.spec.ts` (or wherever the admin-home page is already
  visited), assert the "Send test alert" button is present and clicking it (the e2e
  environment has no `Email:AlertRecipients` configured by default) surfaces the "no
  recipients configured" message — deterministic without adding e2e-only configuration. Run
  `expectTappable` on the button at both viewports.

## Done criteria

Steps 1–7, 9, 10 (the module, without backup alerts):
- [ ] `dotnet build Homon.sln --configuration Release` → 0 warnings/errors
- [ ] New xunit tests exist and pass: `AlertRulesTests`, `AlertMessageBuilderTests`,
      `AlertDispatcherTests`, `AlertEndpointTests`, the `EmailTransportTests` additions
- [ ] `npm --prefix src/Homon.Web run build` exits 0; new/extended Vitest tests pass
- [ ] `grep -n "MapAlertEndpoints" src/Homon.Api/Program.cs` shows it registered
- [ ] A `[DatabaseFact]` test proves a published `ProbeTransition` produces exactly one
      recorded email
- [ ] `docs/ARCHITECTURE.md` new §3.17 (or next free) added; `docs/MODULES.md` and
      `docs/deployment-runbook.md` updated
- [ ] `./ci/run-ci.sh` → `PASS — web api e2e`, 0 skipped api tests

Step 8 (backup alerts), additionally, if done:
- [ ] `dotnet run --project src/Homon.Api -- migrate` applies `AddBackupAlertState` cleanly
- [ ] `BackupAlertWatcherTests` exist and pass
- [ ] `git status` shows no file outside *Scope* touched (including Step 8's own list only if
      Step 8 was actually done)

- [ ] `plans/README.md`'s 009 row is `DONE` (or `DONE — Step 8 deferred`, if it was dropped)

## STOP conditions

- Plan 002 or 008 has not landed, or `IAlertEmailSender`/`EmailOptions`/
  `BackupJobEvaluator.Evaluate` do not exist in the shape this plan assumes.
- Step 3 finds no `BackgroundService`-derived scheduler under
  `src/Homon.Infrastructure/Monitoring/`, or the old/new `ProbeStatus` values are not both
  reachable at one hook point without a second database round trip per probe per poll.
- `docs/ARCHITECTURE.md`'s §3.13–§3.16 are already claimed by the time this executes and no
  number through §3.19 is free — pick the actual next free number; don't overwrite another
  plan's section.
- A step's verification fails twice after a reasonable fix attempt.
- Adding the scheduler hook (Step 4) would require touching anything in
  `src/Homon.Api/Endpoints/ProbeEndpoints.cs`, `StatusEndpoints.cs`, or
  `BackupEndpoints.cs` — those belong to 002/003/008; report what forced it rather than
  editing them.
- `.env.example`/`compose.prod.yaml`'s variable names (`RESEND_API_TOKEN`,
  `HOMON_FROM_ADDRESS`, `HOMON_ALERT_RECIPIENT_1`/`_2`, `Email__AlertRecipients__0`/`__1`) do
  not match `EmailOptions` as it actually landed — reconcile, and say so in your summary
  rather than silently renaming either side.

## Maintenance notes

- **The channel's queued alerts, and the dispatcher's cooldown state, do not survive a
  restart** (Decision 1) — both in-memory, both deliberate for a single-instance home
  deployment. If Homon is ever run with more than one API replica, this is the first thing to
  revisit: an outbox table keyed by `ProbeId`/`At` is the natural upgrade, and
  `IProbeTransitionPublisher`'s interface does not need to change to add it.
- **A household timezone setting**, if it lands, is additive: a `Display:TimeZone` option
  read by `AlertMessageBuilder` instead of the hard-coded UTC formatter.
- **005 (SNMP)** and any later probe kind must call the same `Publish` this plan wires into
  002's scheduler — nothing kind-specific in the transition seam.
- A reviewer should check: `AlertDispatcher.HandleAsync` never lets an `EmailSendException`
  escape; the Production+empty-recipients validator only fires when Resend is actually
  configured (an unconfigured Production host should still fail on the *existing*
  missing-token check, not a different message about recipients); the UTC-only timestamp
  claim stays true if anyone later adds a `TZ` environment variable without updating this
  code.
- Deferred: per-probe alert opt-out; a digest mode instead of one mail per transition; SMS or
  webhook transports; a configurable cooldown/poll interval (both are constants today).
