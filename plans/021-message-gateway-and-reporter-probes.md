# Plan 021: A message gateway — reporters push, Homon ingests, and a silent reporter goes red

> **Executor instructions**: Save this file as
> `plans/021-message-gateway-and-reporter-probes.md` in the repository as your first act,
> then follow it step by step. Run every verification command and confirm the expected
> result before moving to the next step. If anything in "STOP conditions" occurs, stop and
> report — do not improvise.
>
> **Do not create a git worktree.** `CLAUDE.md` forbids it outright. Work in
> `/home/acastaner/Git/homon` on branch `plan/021-message-gateway` and leave that branch
> checked out for review.

## Context

Homon can watch things that answer when asked — ping, HTTP, and (later) SMB and SNMP. It
cannot watch anything that only speaks when it has something to say. The restic backups on
`clockmaster`, `acastaner-vps` and `remontoire` are exactly that: a nightly script that
knows whether it succeeded, holds the output that proves it, and has nowhere to put either.
A dashboard that cannot tell you last night's backup ran is missing the one fact the brief
asked it for.

This plan builds the ingestion point. A reporter — a shell script, a cron job, an agent —
`POST`s one JSON document to `/api/v1/messages` with an API key: what it is called, what it
did, the text that proves it, and **when it intends to report again**. Homon stores it,
derives a status, and shows it on the dashboard beside the ping and HTTP probes. If the
reporter said "again within 25 hours" and nothing arrives, the row goes red on its own. All
the judgement lives on the reporter's side; Homon is an ingestion point and a clock.

The pattern has a name worth knowing, because the search terms are useful later: this is
**push monitoring**, and the overdue half is a **dead man's switch** (Healthchecks.io and
Cronitor are the reference implementations). "Message bus" is the one thing it is *not* —
nothing here routes, fans out or subscribes.

**This plan supersedes plan 008.** 008 built the same machinery narrowed to backups
(`BackupJob` + `BackupRun`, an expected interval and a grace, a key-authenticated report
endpoint, lateness computed at read time). A restic backup is now just
`category: backup`, and 008's genuinely load-bearing decisions are carried over here by
name rather than rediscovered.

## Status

- **Priority**: P1 — it is the oldest unmet requirement in the brief, and the three hosts
  are already running the backups it is meant to prove.
- **Effort**: XL
- **Risk**: MED-HIGH — it adds the app's first inbound write from an unattended client, the
  first `ProbeKind` added after `AddMonitoring` shipped, the first endpoint gated on an API
  key's *scope*, and a retention sweep whose predicate is load-bearing for correctness.
- **Depends on**: 002 (the probe entity, the scheduler, the state machine, observations),
  012 (the design primitives), 013 (`ApiKeyScope`, `TimeProvider`). All three are DONE.
- **Supersedes**: 008 (`REJECTED — superseded by 021`).
- **Category**: direction
- **Requested by**: the maintainer. Verbatim: "I want Homon to be able to receive simple
  messages over its API and display them… the restic backup scripts running on clockmaster,
  acastaner-vps and remontoire should be able to self-report to Homon", with the fields
  `identifier`, `name`, `description`, `message`, `recurrence`, `status`, `category`; "if
  the reporter says 'I will report again tomorrow' and the message is not received within
  the timeframe (with some tolerance), then it can raise an alert"; "the logic is on the
  consumer side… homon is only an ingestion point"; "I want to be able to, as an admin,
  create a new type of probe that will be based on a given message"; "Messages will be held
  for 32 days then deleted."

## Decisions — implement these exactly

These were settled with the maintainer before the plan was written. The executor implements
them; it does not re-open them. Where an artboard, plan 008, or this plan's own prose
disagree, **this section wins**.

**D1. Two things, not one: a `Reporter` is registered by the admin, a `Message` is pushed by
the reporter.** Module slot `src/Homon.Domain/Messaging/`. The admin creates a reporter and
Homon generates its `Identifier` (a 16-character Crockford-base32 handle, reusing
`ApiKeyRules`' alphabet so the two handles look like siblings) — the maintainer's
"randomly-generated-data". Everything else on the wire (`name`, `description`, `message`,
`recurrence`, `status`, `category`) belongs to the *message*, because the reporter owns that
vocabulary and Homon must never need editing when a script changes what it calls itself.

**D2. The monitor is a new `ProbeKind.Message`, not a second status model.** The maintainer
asked for "a new type of probe", and taking that literally is also the cheapest correct
answer: a message probe is a `Probe` row, so probe groups, display order, pause, the
observation history, the uptime ratio, the dashboard section machinery, the collapsed-section
preferences and plan 009's future alert transitions all apply with no new code. Adding a
member to `ProbeKind` is additive because §3.17's convention stores it as a *string* — no
value is renumbered. Update `ProbeKind`'s doc comment, which currently claims all four
members exist from day one and the enum will not grow.

**D3. A message probe carries no new columns: `Probe.Host` holds the reporter's
identifier.** A ping probe's `Host` is the thing it listens to; a message probe's is too.
The API validates on write that the host names an existing reporter (400 otherwise), and the
SPA renders that one field as a `Reporter` `<select>` whose option values are identifiers.
*Rejected*: a nullable `Probe.ReporterId` column with a real foreign key — §3.17 already
rejected per-kind scalar columns on `Probe`, and it would be null for four kinds out of five.
*Rejected*: an owned `MessageOptions` jsonb like `HttpProbeOptions` — a message probe has no
options at all; the recurrence lives on the message, and the tolerance belongs to the
reporter (D6). *Consequence*: the reference is a string, not a constraint, so
`DELETE /reporters/{id}` is **refused with a 400 naming the probe** while any probe's `Host`
equals its identifier. That is 008's `Restrict`-on-`ApiKey` reasoning — make the assumption
loud rather than leaving a probe pointing at nothing.

**D4. The scheduler polls message probes like everything else, through a
`MessageProbeRunner : IProbeRunner`.** It performs no I/O beyond a database read: it loads
the newest `Message` for the probe's reporter and derives an outcome. Push arrival is *not*
what moves a probe — the clock is, which is the whole point of a dead man's switch: nothing
arriving must still change the state, and only a tick can notice that.

**D5. `ProbeResult` gains an optional derived status, because the streak machine cannot
express "late".** `ProbeStateMachine` is binary (N consecutive failures → down, N
consecutive successes → up, between → unstable), which is right for a probe that flaps and
wrong for one whose authority is the reporter's own verdict: a `Warning` message re-read on
every tick would accumulate consecutive failures and silently become `Down`. The smallest
honest accommodation, additive and invisible to the existing runners:

```csharp
// src/Homon.Infrastructure/Monitoring/ProbeResult.cs
public sealed record ProbeResult(bool Succeeded, double? LatencyMs, string? Detail,
    ProbeStatus? DerivedStatus = null);

// src/Homon.Domain/Monitoring/Probe.cs — RecordObservation gains one optional parameter
public void RecordObservation(bool succeeded, double? latencyMs, string? detail,
    DateTimeOffset observedAt, ProbeStatus? derivedStatus = null)
// … streak counters are applied exactly as today, then:  Status = derivedStatus ?? status;
```

The counters keep updating so `Unpause`/`ChangeFailureThreshold`'s `Derive` still has
something to read; a message probe unpaused therefore shows its streak-derived status for at
most one tick before the next poll corrects it. Document that in the method's doc comment.
`ProbeScheduler` passes `result.DerivedStatus` through; the ping and HTTP runners are not
touched (they construct `ProbeResult` with three arguments and keep the streak behaviour).
`LatencyMs` is always `null` for a message probe — there is no round trip to time — which is
already how the SPA guards the sparkline (`probe.kind === 'ping'`).

**D6. The derivation table, which the tests pin.** Pure, in
`src/Homon.Domain/Messaging/MessageProbeEvaluator.cs`, over `(Message? latest, DateTimeOffset
now)`:

| Situation | `Succeeded` | `DerivedStatus` | Chip |
| --- | --- | --- | --- |
| no message at all | `false` | `Unknown` | grey, "Unknown" |
| `NextExpectedAt` is set and `now >` it | `false` | `Down` | red, "Overdue" |
| `MessageStatus.Failure` | `false` | `Down` | red, "Failed" |
| `MessageStatus.Warning` | `false` | `Unstable` | orange, "Warning" |
| `MessageStatus.Success` | `true` | `Up` | green, "Succeeded" |
| `MessageStatus.None` | `true` | `Up` | green, "Reported" |
| `MessageStatus.Unknown` | `true` | `Unknown` | grey, "Unknown" |

Overdue is checked **first** and wins over the reported status, because a successful report
from three days ago is not evidence about today. `None` and `Unknown` count as `Succeeded`
for the uptime ratio: the reporter checked in on time, which is the only claim either status
makes. **There is no server-side grace period** — the reporter owns its own tolerance by
declaring a window wider than its period, which is exactly what the maintainer's "within 25
hours" for a daily backup does. *Rejected*: a `Grace` field like 008's, which would have
Homon guessing a number the reporter already knows.

**D7. The chip word comes from the message status; the colour comes from the probe state.**
`StatusChip`'s `word` prop exists for precisely this (its doc comment anticipates it for
Succeeded/Late/Failed), so no new component and no new colour token. The words are the
right-hand column of D6's table.

**D8. Recurrence: an ISO 8601 duration *or* an absolute instant, never both, stored as one
absolute deadline plus the raw declaration.** `recurrence` takes `PT25H`, `P1D`, `P5Y` and
is parsed with `System.Xml.XmlConvert.ToTimeSpan`; `expectNextBy` takes an instant. Both
present → 400. Homon computes `NextExpectedAt = ReceivedAt + duration` (or takes the instant
verbatim) and also stores the declaration string for display, so the admin page can show
what the reporter actually said. Bounds: a duration must be positive and at most 10 years; an
instant must be in the future and at most 10 years out. **Omitted means the reporter can
never be overdue** — only `Failure`/`Warning` can take it off green — and the admin page
must say so on the row, or a reporter that silently forgot the field looks monitored when it
is not. `XmlConvert`'s calendar rule (a month is 30 days, a year is 365) must be stated in
both the 400's message and `docs/message-reporting.md`, with "send `expectNextBy` if you need
calendar-exact". *Rejected*: a bare seconds integer (unreadable in the admin UI);
duration-only (cannot say "next Monday 03:00"); instant-only (every shell script would need
`date -d` arithmetic).

**D9. `MessageStatus` is a closed enum of five, all shipping today; `Category` is an open
slug.** `MessageStatus { Success, Warning, Failure, Unknown, None }`, persisted as its name
per §3.17's convention and `ApiKeyConfiguration`'s precedent. The UI must colour a status and
plan 009 must act on it, so it cannot be free text; all five exist from day one so no stored
string is ever renumbered or back-filled. `Category` is the opposite: a required slug
(`^[a-z0-9]+(-[a-z0-9]+)*$`, ≤40 characters, defaulting to `other`), so a new category —
`nas-array`, `certificates`, `disk-space` — needs no migration and no deploy. The SPA maps
known slugs to a lucide icon through one lookup object with a documented fallback for
anything unknown; an unknown category renders its slug as a plain label and is never an
error.

**D10. Bodies are truncated at 64 KiB, never rejected** (008's decision, carried over
verbatim, including the `"[truncated, showing the last 64 KiB]\n"` marker prepended to the
kept tail). A truncated proof-of-run beats a failed report at 02:00. Kestrel's 30 MB body
limit stays the real backstop.

**D11. A body is administrator-only unless the admin says otherwise, per reporter.**
`Reporter.BodyVisibility` (`Administrator` | `Reader`), default `Administrator`, because a
restic log carries host paths and hostnames that the phone-glancing reader has no business
seeing (§3.3's reasoning, and 008's). The admin message list is the only surface that ever
returns a full body. When visibility is `Reader`, the *latest* body also appears on
`GET /status` **capped at 2000 characters** — the status payload is polled every 30 seconds
and a 64 KiB blob on that interval is not acceptable; a reader-visible message is meant to be
a short line like "NAS array healthy, 0 errors" anyway.

**D12. The detail string a reader sees contains no reporter free text.** `Probe.LastDetail`
is on the reader-facing payload, so it carries status and timing words only — never the
reported name, description or body — or D11 would be decorative. Exact strings, which the
tests pin: `"message: no report received yet"`, `"message: success, reported 2026-10-01
04:30Z"`, `"message: overdue — none since 2026-09-30 04:28Z, expected by 2026-10-01 05:28Z"`.
The row's label is the probe's own admin-chosen name.

**D13. The key decides who is reporting; the identifier in the body is only ever checked for
a match.** One `hmn_…` key is bound 1:1 to one reporter (`Reporter.ApiKeyId`, unique index,
`Restrict`), minted in the same transaction that creates the reporter and revealed exactly
once. The ingestion endpoint resolves the reporter *from the key*, so a leaked key cannot
impersonate another reporter. An `identifier` that does not match the key's reporter → 403
(the caller is asserting an identity it does not own), not 404. A key with no reporter bound
→ 403. Rotation mints a new key, repoints `Reporter.ApiKeyId` and soft-revokes the old one,
which stays listed for the audit trail `ApiKey` already promises.

**D14. Ingestion requires scope `ReadWrite`.** This is the consumer §3.13 and
`HomonPolicies` both deferred to 008: a new `HomonPolicies.ApiKeyWrite` via
`RequireAssertion` over the `homon:key-scope` claim, because `AdministratorOrApiKey`
deliberately does not discriminate scope and `ApiKey` admits a `Read` key. A cookie session
is refused on ingestion (a browser must never file a report) and an API key is refused on
every administrative route — both directions get an explicit test.

**D15. Retention is a 32-day sweep that never deletes a reporter's most recent message.**
A hosted service copying `ProbeObservationRetentionService` *exactly* (hourly tick,
`ExecuteDeleteAsync`, an options-bound window and interval, a kill switch the test factory
sets false, an `internal SweepAsync` tests call directly instead of waiting on a timer).
The "keep the latest" exception is load-bearing, not tidiness: a reporter that has been
silent for 33 days is the one case the whole feature exists for, and deleting its last
message would take `NextExpectedAt` with it — flipping the probe from red back to a
reassuring grey "Unknown" exactly when it matters most. *Rejected*: 008's prune-on-insert,
which never runs for a reporter that has stopped reporting.

**D16. No idempotency key, no client-supplied timestamps.** `ReceivedAt` is server time from
`TimeProvider`; a replayed report is simply a second report. 008's five-minute
clock-drift rule is not needed because nothing on the wire is a client clock reading. Both
are additive later and are recorded in "Maintenance notes".

## The wire contract

### Ingestion — `POST /api/v1/messages`, policy `ApiKeyWrite`, rate-limited

```http
POST /api/v1/messages
Authorization: Bearer hmn_7Q2KX9VBMTR4_…
Content-Type: application/json

{
  "identifier": "9H4KQ2VBMTR4WXYZ",      // optional; must match the key's reporter or 403
  "name": "clockmaster backup",           // required, ≤100
  "description": "Daily restic backup",   // optional, ≤280
  "message": "repository 1a2b3c…\n…",     // optional, truncated to the last 64 KiB
  "recurrence": "PT25H",                  // optional; or expectNextBy, never both
  "expectNextBy": null,                   // optional absolute instant
  "status": "success",                    // optional, default "none"
  "category": "backup"                    // optional slug, default "other"
}
```

`201 Created`, `Location: /api/v1/messages/{id}`, body
`{ "id": 1234, "receivedAt": "…", "nextExpectedAt": "…" | null }` — the deadline is echoed so
a script can log what Homon understood, which is the cheapest way to catch a wrong
`recurrence` on the first run rather than the first outage.

The validation matrix, with the exact `errors` keys (camelCase wire names, one full sentence
each, every bound quoting its domain constant):

| Key | Rule |
| --- | --- |
| `identifier` | present and not the key's reporter → **403**, not a validation error |
| `name` | required, trimmed, 1…`Message.NameMaxLength` |
| `description` | ≤`Message.DescriptionMaxLength` |
| `recurrence` | parses as an ISO 8601 duration; positive; ≤10 years; absent if `expectNextBy` is present |
| `expectNextBy` | in the future; ≤10 years out; absent if `recurrence` is present |
| `status` | one of the five `MessageStatus` names (camelCase on the wire) |
| `category` | matches the slug pattern, ≤`Message.CategoryMaxLength` |

Note what is deliberately *not* there: `message` can never fail validation (D10), and an
unknown `category` can never fail (D9).

### Administration — policy `Administrator` throughout

| Route | Behaviour |
| --- | --- |
| `GET /reporters` | every reporter with its key summary, its latest message's status/`receivedAt`/`nextExpectedAt`/declaration, its message count, and whether a probe watches it — **no bodies** |
| `POST /reporters` | `{ name, description?, bodyVisibility? }` → creates the reporter, generates the identifier, mints its `ReadWrite` key in the same transaction; 201 `{ id, identifier, token }` — `token` appears nowhere else, ever |
| `PUT /reporters/{id:guid}` | `{ name, description?, bodyVisibility }` — the admin's own label and the visibility switch; never the identifier |
| `POST /reporters/{id:guid}/key` | rotates: mints a new key, repoints `ApiKeyId`, soft-revokes the old; 200 `{ token }` |
| `DELETE /reporters/{id:guid}` | 204; **400 naming the probe** if a probe's `Host` is its identifier (D3); cascades its messages; revokes its key |
| `GET /reporters/{id:guid}/messages` | newest first, `?take=` ≤100 default 50 — **the only route that returns a full body** |
| `GET /api-keys` | 008's slice A: id, name, tokenId, scope, createdAt, lastUsedAt, expiresAt, revokedAt, whether expired (computed, not stored), and the reporter it is bound to if any; revoked keys included, newest first |
| `POST /api-keys` | `{ name, scope?, expiresAt? }` → 201 with the one-time `token` |
| `DELETE /api-keys/{id:guid}` | soft-revoke via `ExecuteUpdateAsync`; 204, idempotent; 404 unknown; **400 if it is a reporter's bound key** — rotate or delete the reporter instead, so a reporter is never left holding a dead credential silently |

### The reader-facing payload — `GET /status`, policy `Reader`

Probe rows of `kind: "message"` gain one optional object; every other kind is untouched, so
no existing SPA code path changes:

```ts
message?: {
  status: 'success' | 'warning' | 'failure' | 'unknown' | 'none'
  category: string
  receivedAt: string
  nextExpectedAt: string | null
  overdue: boolean
  body: string | null        // present only when BodyVisibility is Reader; ≤2000 chars (D11)
}
```

## Scope

**In.** The `Messaging` domain module; one migration; the ingestion endpoint; the reporter
admin endpoints; the `/api-keys` endpoints (008's slice A); `ProbeKind.Message` plus the
runner and the `ProbeResult`/`RecordObservation` accommodation (D5); the retention sweep;
`/admin/reporters`; the real `/admin/api-keys` page; the `Reporter` select in the probe form;
the message object in the status payload and the chip word on the dashboard; xunit, vitest and
Playwright coverage; `docs/message-reporting.md`; the ARCHITECTURE sections; the 008
supersession paperwork.

**Out.** Email alerting (plan 009 owns it; this plan's contribution is `MessageProbeEvaluator`
as the pure seam 009 needs, and a one-line note in 009 re-pointing its backup step at it). A
reader-facing message-history page. Charts or trends over message history. Any change to how
ping or HTTP probes behave. Scoping a key to several reporters. An inbound webhook format
other than Homon's own JSON.

## Current state — verified at commit `c3cb8ee`, 2026-10-01

Confirm each of these before building on it; on a mismatch, STOP.

```csharp
// src/Homon.Infrastructure/Monitoring/ProbeResult.cs — three members today
public sealed record ProbeResult(bool Succeeded, double? LatencyMs, string? Detail);

// src/Homon.Infrastructure/Monitoring/IProbeRunner.cs
public interface IProbeRunner
{
    ProbeKind Kind { get; }
    Task<ProbeResult> RunAsync(Probe probe, CancellationToken cancellationToken);
}

// src/Homon.Infrastructure/Monitoring/ProbeScheduler.cs:131-145 — the whole write
var observedAt = timeProvider.GetUtcNow();
// This is where plan 009's IProbeTransitionPublisher will read probe.Status before
// and after RecordObservation, right before the save.
probe.RecordObservation(result.Succeeded, result.LatencyMs, result.Detail, observedAt);
database.ProbeObservations.Add(new ProbeObservation { … });
await database.SaveChangesAsync(cancellationToken);

// src/Homon.Domain/Monitoring/Probe.cs:94-105 — RecordObservation in full
// src/Homon.Domain/Monitoring/ProbeStateMachine.cs — Apply/Derive, binary, pure
// src/Homon.Domain/Monitoring/Probe.cs — Host is required, HostMaxLength = 255
//   (a 16-character identifier fits with room to spare); LastDetail has no length cap
//   configured in ProbeConfiguration, so it is unbounded text.
// src/Homon.Domain/Auth/ApiKey.cs — Guid Id, TokenIdLength = 12, Scope defaults to Read
```

Relevant facts established by the explorers, each of which the executor should spot-check:

- `HomonDbContext` discovers configurations by assembly scan
  (`ApplyConfigurationsFromAssembly`), so a new `IEntityTypeConfiguration<T>` file needs no
  registration — only a `DbSet` property.
- Enums are persisted **as strings** with an explicit `HasMaxLength`
  (`ProbeConfiguration.cs:28`); `ApiKeyConfiguration` shows the
  `.HasDefaultValue(…).ValueGeneratedNever()` trap worth copying for a defaulted enum column.
- `ProbeObservationRetentionService` is the sweep template: hourly `Task.Delay` on
  `TimeProvider`, `IServiceScopeFactory.CreateAsyncScope()` per sweep,
  `IOptionsMonitor<MonitoringOptions>`, a `RetentionEnabled` kill switch, an
  `internal async Task<int> SweepAsync(...)` for tests, and a try/catch that swallows
  `OperationCanceledException` on shutdown only. `HomonApiFactory` sets
  `Monitoring:SchedulerEnabled` and `Monitoring:RetentionEnabled` to `false` for every test
  host; the new options need the same treatment.
- `HomonPolicies` has no scope-discriminating policy yet and says so, deferring it to 008.
- `Program.cs:383` is the `AddRateLimiter` block with exactly one named policy; the comment
  there explains why options must be resolved from `httpContext.RequestServices` inside the
  partition factory rather than captured before `builder.Build()`.
- `Program.cs`'s module mapping block has the commented placeholders
  `//   v1.MapBackupEndpoints();   v1.MapApiKeyEndpoints();` — this plan replaces that line.
- `src/Homon.Web/src/pages/admin-api-keys-page.tsx` is 17 lines of placeholder whose text
  names plan 008; `admin-probes-page.tsx:318-326` is the `Host` text input this plan swaps for
  a select when the kind is `message`; `admin-probes-page.tsx:388` carries the
  "004 (SMB) and 005 (SNMP) add a sibling fieldset here" extension-point comment to follow.
- The admin section list is duplicated in four files that must stay in sync: `App.tsx`,
  `admin-home-page.tsx`, `e2e/helpers.ts` (`ADMIN_ROUTES`) and `e2e/admin.spec.ts`'s
  hard-coded nav loop.
- `docs/ARCHITECTURE.md` has 24 `### 3.N` sections, the highest being §3.24. Re-grep at
  execution time; take the next numbers.

## Commands you will need

```bash
# the gate, one suite at a time — the combined run aborts on this machine (0x80131506)
./ci/run-ci.sh web
./ci/run-ci.sh api
./ci/run-ci.sh e2e

# a single xunit class while iterating
dotnet test tests/Homon.Api.Tests --filter FullyQualifiedName~MessageEndpointTests

# the migration
HOMON_DESIGNTIME_CONNECTION='Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x' \
  dotnet dotnet-ef migrations add AddMessaging \
  --project src/Homon.Infrastructure --startup-project src/Homon.Api \
  --output-dir Persistence/Migrations

# the SPA alone
npm --prefix src/Homon.Web test
npm --prefix src/Homon.Web run build
```

## Git workflow

```bash
git switch -c plan/021-message-gateway
```

Commit per step, in the voice `git log` already uses (`Messaging: …`, `Monitoring: …`,
`Docs: …`, each citing `(plan 021)`). **Leave the branch checked out when you are done** and
say so. Do not merge, do not switch back to `main`, do not stash. The maintainer reviews by
running the app in this checkout on this branch.

## Steps

### Step 1 — Domain: the Messaging module

New files under `src/Homon.Domain/Messaging/`:

- `Reporter.cs` — `Id`, `Identifier` (≤`IdentifierMaxLength` 32, unique), `Name` (≤100),
  `Description?` (≤280), `BodyVisibility`, `ApiKeyId`, `CreatedAt`, `Messages`. Consts for
  every limit, as `Probe.cs` does. A `static string NewIdentifier()`? **No** — generation
  belongs with the other handle generator; expose
  `ApiKeyRules`-style generation from Infrastructure/Api and keep the entity dumb, or put a
  pure `IdentifierGenerator` here if it needs no crypto beyond
  `RandomNumberGenerator.GetInt32` (it does not — mirror `ApiKeyRules.TokenId`'s alphabet and
  method exactly, and say in a comment that the two are deliberately the same shape).
- `Message.cs` — `long Id` (append-only child, as `ProbeObservation` is, with the same comment
  about why a Guid would hurt here), `ReporterId`, `ReceivedAt`, `Name`, `Description?`,
  `Body?`, `Status`, `Category`, `RecurrenceDeclaration?`, `NextExpectedAt?`,
  `ReportedByKeyId`. Consts: `NameMaxLength = 100`, `DescriptionMaxLength = 280`,
  `CategoryMaxLength = 40`, `DeclarationMaxLength = 40`, `BodyMaxBytes = 65_536`,
  `DefaultCategory = "other"`.
- `MessageStatus.cs`, `MessageBodyVisibility.cs` — enums, every member XML-documented.
- `MessageCategory.cs` — the slug pattern as a `GeneratedRegex` plus `IsValid`/`Normalize`
  (lower-case, trim).
- `MessageRecurrence.cs` — the parser: `TryParse(string? recurrence, DateTimeOffset?
  expectNextBy, DateTimeOffset receivedAt, out DateTimeOffset? nextExpectedAt, out string?
  declaration, out string? error)`. Owns the `XmlConvert.ToTimeSpan` call, the 10-year bound,
  the both-present refusal and every error sentence. No I/O, no clock beyond the parameter.
- `MessageBody.cs` — `Truncate(string? body)` returning the last 64 KiB with D10's marker.
  Measure in **bytes** (UTF-8), not chars, and cut on a character boundary.
- `MessageProbeEvaluator.cs` — D6's table as a pure
  `Evaluate(Message? latest, DateTimeOffset now)` returning
  `(bool Succeeded, ProbeStatus Status, string Detail)`. This is the seam plan 009 needs;
  say so in the doc comment, naming 009.
- `README.md` — the module README every slot has: what it owns, the entities, where the rest
  lands, known constraints.

**Verify**: `dotnet build src/Homon.Domain -c Release` → 0 warnings (warnings are errors).

### Step 2 — Domain tests

`tests/Homon.Api.Tests/MessageRecurrenceTests.cs`, `MessageProbeEvaluatorTests.cs`,
`MessageBodyTests.cs`, `MessageCategoryTests.cs` — plain `[Fact]`/`[Theory]`, no database,
in the `ProbeStateMachineTests` idiom. The evaluator test is a `[Theory]` whose cases are
D6's table row for row, plus the two interaction cases that matter: overdue beats a
`Success` body, and a missing `NextExpectedAt` never goes overdue however old the message is.

**Verify**: `dotnet test tests/Homon.Api.Tests --filter FullyQualifiedName~Message` → all
pass, 0 skips.

### Step 3 — Monitoring: the probe kind and the derived-status accommodation

`ProbeKind` gains `Message` (and its doc comment is corrected — D2). `ProbeResult` gains
`ProbeStatus? DerivedStatus = null`. `Probe.RecordObservation` gains the optional parameter and
the doc comment D5 requires. `ProbeScheduler` passes it through. Nothing else in Monitoring
changes.

Add to `ProbeStateMachineTests` (or a new `ProbeDerivedStatusTests`): a derived status
overrides what the counters imply, and the counters still advance underneath it.

**Verify**: `dotnet build -c Release` clean, and
`dotnet test tests/Homon.Api.Tests --filter FullyQualifiedName~ProbeStateMachine` green —
proving the three existing runners are untouched by the signature change.

### Step 4 — Persistence: configurations and one migration

`ReporterConfiguration.cs`, `MessageConfiguration.cs` in
`src/Homon.Infrastructure/Persistence/Configurations/`; two `DbSet`s on `HomonDbContext`.

- `Reporters`: unique index on `Identifier`; unique index on `ApiKeyId` (this is what makes it
  1:1); `HasOne<ApiKey>().WithMany().HasForeignKey(r => r.ApiKeyId).OnDelete(Restrict)` —
  a key that a reporter holds is never deleted, only revoked; enums as strings with
  `HasMaxLength(20)`.
- `Messages`: FK to `Reporter` `Cascade` (deleting a reporter takes its history); FK to
  `ApiKey` `Restrict` (008's reasoning, verbatim: `ApiKey` rows are never deleted, and
  `Restrict` makes that assumption loud if it is ever wrong); composite index on
  `(ReporterId, ReceivedAt DESC)` — it serves the three queries this module makes: the latest
  message per reporter (the runner, every tick), the admin history page, and the retention
  sweep's cutoff scan; `Body` as unbounded `text`, every other string with its domain
  constant's max length.

Then generate `AddMessaging` with the command above and **read the generated file** before
committing it: it must contain two `CreateTable`s and no `AlterColumn` against `Probes` (if it
does, D3 has been violated somewhere).

**Verify**: `./ci/run-ci.sh api` → the `migrate` step applies cleanly and `dotnet test` is
green with 0 skips.

### Step 5 — Infrastructure: the runner, the options, the sweep

`src/Homon.Infrastructure/Messaging/`:

- `MessagingOptions.cs` — `SectionName = "Messaging"`, `RetentionWindowDays = 32`,
  `RetentionSweepInterval = 1h`, `RetentionEnabled = true`, bound with
  `AddOptions<T>().Bind(...).ValidateOnStart()` in a new `private static void AddMessaging(...)`
  in `InfrastructureServiceCollectionExtensions.cs`, beside `AddHomonMonitoring`.
- `MessageProbeRunner.cs` — `Kind => ProbeKind.Message`; loads the newest `Message` for the
  reporter whose `Identifier` equals `probe.Host` (one indexed query,
  `AsNoTracking().OrderByDescending(m => m.ReceivedAt).FirstOrDefaultAsync`); calls
  `MessageProbeEvaluator.Evaluate`; returns `new ProbeResult(succeeded, null, detail, status)`.
  A probe whose host matches no reporter returns `Unknown` with the detail
  `"message: no reporter with this identifier"` — the state D3's delete guard is meant to
  prevent, kept honest rather than throwing.
- `MessageRetentionService.cs` — `ProbeObservationRetentionService` copied line for line, with
  D15's predicate: delete messages older than the cutoff **except** each reporter's newest.
  Express it as `Messages.Where(m => m.ReceivedAt < cutoff && m.Id != database.Messages
  .Where(x => x.ReporterId == m.ReporterId).Max(x => x.Id))` and confirm EF Core 10 translates
  it to one `DELETE … WHERE … AND "Id" <> (SELECT MAX…)`; if it does not translate, fall back
  to one query collecting the per-reporter max ids followed by a single
  `ExecuteDeleteAsync` excluding them, and say in a comment which one you ended up with and
  why. Register both the runner (`AddScoped<IProbeRunner, MessageProbeRunner>`) and the
  service (`AddHostedService`) beside the Monitoring pair.

**Verify**: `dotnet build -c Release` clean; the app still starts
(`dotnet run --project src/Homon.Api` → `/health` returns healthy, then stop it).

### Step 6 — Infrastructure tests

`MessageProbeRunnerTests.cs` and `MessageRetentionServiceTests.cs`, both `[DatabaseFact]`
against `ApiDatabaseFactory`, calling `SweepAsync` directly. The retention test is the
load-bearing one: seed a reporter with three messages, two of them 40 days old, sweep, and
assert **two** rows survive — the recent one and the old-but-newest-for-its-reporter one —
then assert that a reporter whose only message is 40 days old still has it.

**Verify**: `./ci/run-ci.sh api` → green, 0 skips.

### Step 7 — API: policies, rate limit, endpoints

1. `HomonPolicies`: add `ApiKeyWrite` (D14) via `RequireAssertion` over
   `HomonClaimTypes.AuthenticationKind` **and** `HomonClaimTypes.ApiKeyScope ==
   nameof(ApiKeyScope.ReadWrite)`; delete the comment deferring it to 008.
2. `Program.cs:383`'s `AddRateLimiter`: a second named policy
   `MessageEndpoints.ReportThrottlePolicy` — fixed window, 30 permits / 5 minutes, partitioned
   on the `homon:key-id` claim (not the IP: a script's IP is its server's, shared with
   everything else there), with options resolved from `httpContext.RequestServices` for the
   reason the existing comment gives. Add a `MessageThrottleOptions` only if you need the
   figures configurable; otherwise hard-code them and say why in a comment.
3. `src/Homon.Api/Endpoints/MessageEndpoints.cs` — the ingestion route and
   `GET /messages/{id}` (Administrator, for the `Location` header to resolve).
4. `src/Homon.Api/Endpoints/ReporterEndpoints.cs` — the six reporter routes.
5. `src/Homon.Api/Endpoints/ApiKeyEndpoints.cs` — 008's slice A, plus the
   bound-key refusal (D13).
6. `ProbeEndpoints`: `message` joins the creatable kinds; `ValidateKind`'s reserved
   `smb`/`snmp` sentence stays; host validation branches by kind — for `message` the host must
   name an existing reporter (`errors["host"] = ["No reporter has this identifier."]`).
7. `Program.cs`: replace the commented placeholder line with
   `v1.MapMessageEndpoints();`, `v1.MapReporterEndpoints();`, `v1.MapApiKeyEndpoints();`.
8. `StatusEndpoints`: one extra grouped query for the newest message per message-probe
   reporter, mapped into the optional `message` object (D11's 2000-character cap applied
   here, and `body` set to `null` unless the reporter's visibility is `Reader`). No N+1 —
   follow the file's existing dictionary pattern.

Every DTO is a nested record at the bottom of its endpoint class; every route carries
`.WithName(...)` and `.WithSummary("One sentence.")`; `TypedResults` unions throughout;
`TypedResults.ValidationProblem` for 400 and `TypedResults.Problem(...)` for the 403s.

**Verify**: `./ci/run-ci.sh api`, then a manual round trip against a running API:
mint a reporter, `POST` a message with its token, create a message probe, and watch
`/api/v1/status` turn the row green and then red after the deadline passes (set
`recurrence: "PT1M"` to see it inside two minutes).

### Step 8 — API tests

`MessageEndpointTests.cs`, `ReporterEndpointTests.cs`, `ApiKeyEndpointTests.cs`, in the
`ProbeEndpointTests` idiom: `IClassFixture<ApiDatabaseFactory>`, `TestClient.Create(factory)`,
`SignInAsync()`, `JsonElement` assertions, a `[DatabaseTheory]` validation matrix asserting
`problem.errors` has the expected key. The tests that pin decisions, each named for what it
protects:

- a `Read`-scope key is refused with 403 on ingestion; a `ReadWrite` key succeeds; a cookie
  administrator is refused 403 (D14, both directions).
- an `identifier` that is not the key's reporter → 403, and the reporter's message count does
  not change.
- a 200 KiB body is accepted, stored at ≤64 KiB + marker length, and the marker is the first
  line (D10).
- `recurrence` and `expectNextBy` together → 400 on both keys; `P1D` yields
  `nextExpectedAt == receivedAt + 24h`; omitting both yields `null`.
- `GET /status` with an `Administrator`-visibility reporter contains neither the body text nor
  the reported name anywhere in the raw response string (D11, D12) — the
  `Assert.DoesNotContain(…, rawJson)` idiom `ProbeEndpointTests` already uses for secrets;
  with a `Reader`-visibility reporter the body is present and capped at 2000 characters.
- `DELETE /reporters/{id}` is refused 400 while a probe watches it, and succeeds once the
  probe is deleted (D3).
- the two auth matrices every resource in this repo carries: anonymous 401 / administrator /
  key on each read, and anonymous 401 + any API key 403 on every administrative write.

**Verify**: `./ci/run-ci.sh api` → green, 0 skips.

### Step 9 — SPA

New: `src/lib/reporters.ts`, `src/lib/api-keys.ts` (types mirroring the endpoints field for
field with the `/** Mirrors … */` comments the other lib modules carry, query keys, hooks
invalidating both their own key and `STATUS_QUERY_KEY` where a change can move a probe);
`src/pages/admin-reporters-page.tsx`; a real `src/pages/admin-api-keys-page.tsx`.

Edits: `App.tsx` (lazy import + route); `admin-home-page.tsx` (a `Reporters` nav entry before
`API keys`); `admin-probes-page.tsx` (the `Reporter` select replacing the `Host` input when
`kind === 'message'`, following the extension-point comment at :388 — label `Reporter`, id
`probe-host`, so the field name on the wire is unchanged); `src/lib/status.ts` (the
`message` object on `StatusProbe`, and a `messageStatusWord` map giving D6's words);
`dashboard-page.tsx` (pass `word` to `StatusChip` for message rows; render a reader-visible
body in a `<details>` whose summary is `Show message` inside the existing row grid).

Reuse the page-level class-name constants exactly as they are declared in
`admin-probes-page.tsx` — copy them into the new pages as the other admin pages do; do not
invent a shared module for them in this plan. Accessibility, non-negotiable because Playwright
runs in strict mode over N rows:

- `<h1>Reporters</h1>` in `PAGE_H1`; `<ol aria-label="Reporters">` of `<li>`.
- every row control's accessible name embeds the record's name: `Edit {name}`,
  `Rotate key for {name}`, `Delete {name}`, then `Confirm delete {name}` /
  `Cancel delete {name}` behind a `confirmingDeleteId`.
- the form is `<form aria-labelledby="reporter-form-heading">`; fields are
  `<p className="flex flex-col gap-1">` + `<label htmlFor>` + control; the visibility control
  is a `<select>` labelled `Message visibility` with options `Administrators only` / `Everyone
  on the network`.
- the one-time token block is `role="alert"` with the sentence
  `This key will not be shown again. Store it now.` and a `Copy key` button using
  `navigator.clipboard.writeText` (008's shape).
- every button is `h-10`, including disabled ones — `expectTappable` runs over all of them.

**Verify**: `./ci/run-ci.sh web` → lint, build and vitest green.

### Step 10 — SPA tests

`admin-reporters-page.test.tsx`, `admin-api-keys-page.test.tsx`, and additions to
`admin-probes-page.test.tsx` (choosing kind `message` renders a `Reporter` combobox and no
`Host` textbox, and submits `host` set to the chosen identifier) and `status.test.ts` (the
word map, and that a row with no `message` object still renders). `stubFetch` fixtures must
declare every endpoint the component touches, or the stub throws by design.

**Verify**: `npm --prefix src/Homon.Web test` → green.

### Step 11 — e2e

`e2e/helpers.ts`: `/admin/reporters` joins `ADMIN_ROUTES`. `e2e/admin.spec.ts`: `Reporters`
joins the nav loop. New `e2e/reporters.spec.ts`, running in both viewports, doing the whole
round trip through the real API with `page.request`: create a reporter through the UI, read the
revealed token, `POST /api/v1/messages` with it (`status: "success"`, `recurrence: "PT25H"`),
create a message probe on the probes page selecting that reporter, and assert the dashboard row
shows the `Succeeded` chip. Clean up both the probe and the reporter in `afterEach` — the
suite shares one database.

**Verify**: `./ci/run-ci.sh e2e` → both viewports green.

### Step 12 — Docs and the paperwork

1. `docs/ARCHITECTURE.md` — re-grep `^### 3\.` and append, before `## 4.`: a section on
   **push monitoring as a probe kind** (D2, D3, D4, D5, with the rejected alternatives), one on
   **the recurrence contract** (D8, including `XmlConvert`'s calendar rule and why the reporter
   owns the tolerance), and one on **message retention and body visibility** (D11, D15,
   including why the newest message is never swept). Each must state what it supersedes in
   008's absence; none may renumber an existing section.
2. `docs/message-reporting.md` — the runbook a script author reads: the wire contract, every
   field, the status and category vocabularies, and a `curl` example reading the key from a
   file (`Authorization: Bearer $(cat /etc/homon/clockmaster-restic.key)` — never inline, as
   008 insisted), plus a complete restic wrapper snippet that reports `failure` on a non-zero
   exit and passes the log on stdin.
3. `docs/MODULES.md` — the 008 row becomes the 021 row pointing at `Domain/Messaging/`; the
   brief's **Backups** paragraph gains a note that it is satisfied by the message gateway as
   `category: backup`.
4. `src/Homon.Domain/Backups/README.md` — rewritten to one short paragraph: the slot is
   retired, its requirement is met by `Messaging`, see plan 021. Do **not** delete the folder
   in this plan; a removal is its own commit and its own decision.
5. `plans/README.md` — 008 becomes
   `REJECTED (superseded by 021)`, 009's `Depends on` becomes `002, 021`, and a 021 row is
   added in the house format. The dependency note about 009 reusing
   `BackupJobEvaluator` is re-pointed at `MessageProbeEvaluator`.
6. `plans/009-alerts-by-email.md` — a short note at the top of its Status block only: its
   backup-alert step now reads from `Messaging`, its `BackupJob.LastNotifiedState` column
   becomes `Reporter`-scoped, and its STOP condition naming 008 is void. Do not rewrite the
   plan.
7. `README.md` — the status paragraph still says "no module has landed yet", which has been
   false since 002. Fix that sentence while you are here; it is one line and this plan's
   feature is the one a reader will go looking for.

**Verify**: `grep -c '^### 3\.' docs/ARCHITECTURE.md` is exactly three higher than it was.

## Test plan

| Layer | File | What it proves |
| --- | --- | --- |
| Domain | `MessageProbeEvaluatorTests` | D6 row for row; overdue beats success; no deadline never goes overdue |
| Domain | `MessageRecurrenceTests` | `PT25H`/`P1D`/`P5Y` parse; both-present refused; negative, zero and >10y refused; `expectNextBy` in the past refused |
| Domain | `MessageBodyTests` | a 200 KiB body keeps its last 64 KiB, the marker is first, multi-byte characters are not split |
| Domain | `ProbeStateMachine`/derived | a derived status wins over the counters; counters still advance |
| Infra | `MessageRetentionServiceTests` | 32-day cutoff; **the newest message per reporter always survives** |
| Infra | `MessageProbeRunnerTests` | each D6 case end to end through the runner; an unknown identifier reads Unknown, never throws |
| API | `MessageEndpointTests` | scope refusal both ways, identifier mismatch 403, truncation, recurrence echo, rate limit returns 429 |
| API | `ReporterEndpointTests` | create reveals a token once; rotate revokes the old key; delete refused while watched; history is the only route with a body; both auth matrices |
| API | `ApiKeyEndpointTests` | list includes revoked and computes expired; revoke is idempotent; a reporter's bound key cannot be revoked here; both auth matrices |
| API | `StatusEndpointTests` | the `message` object appears only for message probes; no body and no reported name for an `Administrator`-visibility reporter; capped at 2000 for a `Reader` one |
| Web | `admin-reporters-page.test.tsx` | create posts the right body; the token block is an alert; delete is two-step |
| Web | `admin-probes-page.test.tsx` | kind `message` swaps Host for the Reporter select and submits `host` |
| Web | `status.test.ts` | the chip word map; a row with no `message` still renders |
| e2e | `reporters.spec.ts` | the full round trip in both viewports |
| e2e | `admin.spec.ts`, `layout.spec.ts` | the new route navigates, fits both viewports and has no overflow |

## Done criteria

1. `./ci/run-ci.sh web`, `./ci/run-ci.sh api` and `./ci/run-ci.sh e2e` each pass, with
   **0 skipped** tests.
2. A reporter created through `/admin/reporters` yields a token that reports successfully from
   a plain `curl`, and the same token is refused if its scope is `read`.
3. A message probe with `recurrence: "PT1M"` goes red within two poll intervals of the
   deadline, with no report arriving and nothing else touched.
4. `GET /api/v1/status` for an `Administrator`-visibility reporter contains no part of the
   message body or the reported name anywhere in the response.
5. The retention sweep, invoked directly, leaves a 40-day-silent reporter's last message in
   place.
6. `docs/ARCHITECTURE.md` gained exactly three sections and renumbered none; `plans/README.md`
   shows 008 `REJECTED (superseded by 021)` and a 021 row; `docs/MODULES.md` and the `Backups`
   slot README point at `Messaging`.
7. The branch `plan/021-message-gateway` is still checked out and the working tree is clean.

## STOP conditions

- `ProbeResult`, `IProbeRunner`, `ProbeScheduler`'s write block, `Probe.RecordObservation` or
  `ProbeStateMachine` do not match the "Current state" excerpts.
- The generated migration touches the `Probes` table in any way (D3 has been violated).
- EF Core will not translate the retention predicate **and** the fallback would need more than
  two queries — stop and report rather than inventing a third shape.
- Adding `ProbeKind.Message` turns out not to be additive (any existing stored value changes,
  or a test asserts the enum's member count).
- The `Reader` policy or `/status` would have to change shape for an existing kind — this plan
  only *adds* an optional object.
- Any requirement here would need a secret stored outside `ISecretProtector`, or an API key's
  secret to be readable after minting.
- `./ci/run-ci.sh api` reports a skipped test (the `[DatabaseFact]` guard means
  `HOMON_TEST_CONNECTION` is unset — fix the environment, do not weaken the test).

## Maintenance notes

- **Plan 009's seam** is `MessageProbeEvaluator.Evaluate(latest, now)` plus the ordinary probe
  transition the scheduler already produces: because a message probe writes real observations
  and real status transitions, 009's `IProbeTransitionPublisher` covers it with **no
  message-specific code at all**. That is the main dividend of D2, and 009's separate
  backup-watcher `BackgroundService` is no longer needed.
- **Additive later, deliberately not now**: an idempotency key on ingestion; a client-supplied
  `occurredAt` distinct from `ReceivedAt`; a reader-facing history page; per-reporter retention
  windows (the sweep is already scoped per reporter, which assumes this); a key scoped to
  several reporters; structured fields beside the free-text body (`durationSeconds`,
  `bytesTransferred`) — a nullable owned jsonb on `Message` in the §3.17 shape.
- **The category vocabulary is data, not code.** The SPA's icon map is the only place that
  knows any slug by name, and its fallback is the contract. Resist a `MessageCategory` table.
- **If a second push-style kind ever arrives** (a webhook from a service that posts its own
  format), it is a new `ProbeKind` with its own runner reading the same `Message` table, not a
  second ingestion model. The identifier-in-`Host` convention (D3) is what makes that cheap.

## Amendments after design review (2026-10-01, before Step 1 landed)

A design pass over the live code found four things this plan had wrong or under-specified.
These amend the decisions above; where they conflict, **the amendment wins**.

**A1 (amends D5, D6). A derived `Unknown` writes no observation row.** D6 has two rows that
reach no verdict at all — no message has ever arrived, and the reporter reported
`MessageStatus.Unknown`. Writing a failed observation for those (as D5 implied) would drag the
probe's uptime ratio to 0.00% for a reporter that has simply not spoken yet, when §3.16 says a
probe with no observations reads `—`. So `ProbeScheduler` skips the `ProbeObservation` insert
when `result.DerivedStatus is ProbeStatus.Unknown`, and nothing else changes:
`RecordObservation` is still called, so `LastObservedAt` still advances — without that the
scheduler's due query (`LastObservedAt == null`) would re-dispatch the probe on every 5-second
tick forever. The streak counters keep advancing from `Succeeded` so `Unpause` and
`ChangeFailureThreshold` have something to read; for a message probe that re-derived status is
a placeholder the next poll corrects within one tick, which is documented on the method.

*Rejected*: making `ProbeResult.Succeeded` nullable and adding a `Probe.RecordNoJudgement`
method. It is the same three files, but it forces every future runner to answer "can my kind
reach no verdict", and it cannot express D6's `Warning` → `Unstable` row at all.

**A2 (amends D12). The evaluator is handed a snapshot that carries no free text.**
`MessageProbeEvaluator.Evaluate` takes a `MessageSnapshot(MessageStatus, DateTimeOffset
ReceivedAt, DateTimeOffset? NextExpectedAt)`, not a `Message`. D12's rule — a reader-facing
detail string never contains the reporter's name, description or body — is then structural
rather than a promise: there is nothing in scope to leak.

**A3 (amends the wire contract). The ingestion endpoint resolves its reporter by joining on
`TokenId`.** `HomonClaimTypes.ApiKeyId` carries the key's **`TokenId`**, not its `Guid Id`
(`ApiKeyAuthenticationHandler.cs:92`) — 008 flagged this and it is still true. One join from
`Reporters` through `ApiKeys` on `TokenId`, no second round trip. Also: `ApiKeyIssuer` is not
registered in DI today (`Program.cs` news it up for the CLI verb), so this plan adds
`services.AddScoped<ApiKeyIssuer>()`.

**A4 (amends the status payload). Three fields, not seven.** The `message` object on a
`kind: "message"` status row is `{ status, overdue, body }` — the status and the overdue flag
because the chip word needs them (a `none` report is "Reported", not "Succeeded"; an overdue
one is "Overdue", not "Failed"), the body because D11 gates it. `category`, `receivedAt`,
`nextExpectedAt` and the declaration are administrative detail and stay on `/reporters`; the
timing a reader needs is already in `detail` and `lastCheckedAt`.

**A5 (additions, all small).** `Reporter.Name` is unique case-insensitively via a
`NormalizedName` column and index, the `ProbeGroup` pattern — it is what lets every row control
be named `Edit {name}` under Playwright's strict mode. `Message.Truncated` is stored, so the
admin page can say so rather than making the reader spot the marker. `Reporter.BodyVisibility`
gets **no** `HasDefaultValue`: `Administrator` is the CLR default, and a SQL default would make
EF omit the column from inserts. `GET /reporters/{id}/messages` takes `?limit=` (1–200,
default 50). `docs/deployment-runbook.md`'s "mint a read-write key by hand for the backup
scripts" paragraph is stale and joins the docs step.

## Execution notes (2026-10-01, branch `plan/021-message-gateway`)

**All three suites pass, one at a time, with 0 skips**: `web` (167 vitest), `api` (453 xunit, 0 skipped),
`e2e` (113 Playwright across both viewports, run twice to check the one timing-sensitive
assertion). The combined `./ci/run-ci.sh` was not used, per this machine's known abort.

**What changed against the plan as written.**

1. **A1 landed as planned and is the one real surprise this plan held.** The design review caught
   that D5 as originally written would have recorded a *failed* observation for a reporter that had
   never reported, dragging its uptime to 0.00% when §3.16 promises an em dash. The fix — the
   scheduler skips the observation row when the derived status is `Unknown` — is three lines and no
   migration. `ProbeResult.Succeeded` was **not** made nullable; the derived status already carries
   the distinction, and the nullable version could not express D6's `Warning → Unstable` row at all.
2. **`?limit=0` falls back to the default rather than clamping to one row.** Clamping to `[1, 200]`
   was the first implementation and made `?limit=0` return a single message, which reads like a bug
   in the data rather than a rejected argument.
3. **`src/test/fetch.ts` gained a method-qualified key** (`'POST /api/v1/reporters'`), additively.
   Creating a reporter answers with the reporter *and its one-time key* while listing them answers
   with an array, and a single body per path cannot express both. Existing fixtures are untouched.
4. **One existing assertion was edited rather than extended**: `admin-probes-page.test.tsx`'s kind
   select now expects three options. That is the deliberate consequence of adding a creatable kind,
   not drift.
5. **`Message.Truncated` is derived from the marker, not stored.** The architect's design had a
   column; the marker is already part of the stored text and cannot disagree with it, so
   `MessageBody.WasTruncated` reads it instead and the migration stayed at two tables.
6. **The reporters page's "Add a reporter" link is named that, not "Add one"** — the probes page
   already has an "Add one" link to probe groups, and Playwright's strict mode refuses the
   ambiguity.

**What the running application caught that the tests did not.** The suites prove the derivation and
the payload; they do not watch a reporter actually go silent, because the scheduler's clock is real.
Run by hand against the throwaway database, with a probe polling every 15 seconds and a reporter
promising `PT1M`:

```
16:29:40  state=up       uptime=100    message: success, reported 2026-10-01 14:28Z
16:29:52  state=down     uptime=80     message: overdue — none since 2026-10-01 14:28Z, expected by 2026-10-01 14:29Z
16:30:28  state=down     uptime=57.14  message: overdue — none since …
   (reports again, promising PT1H)
16:31:12  state=up       uptime=50     message: success, reported 2026-10-01 14:30Z
   (reports status "unknown")
16:31:29  state=unknown  uptime=50     message: status unknown, reported 2026-10-01 14:31Z
16:31:46  state=unknown  uptime=50     message: status unknown, reported 2026-10-01 14:31Z
```

Three things that only a real clock shows:

- The switch fires within one poll interval of the deadline, and the detail names **both**
  timestamps — when it last reported and when it was due — which is exactly what an operator needs
  at a glance and is more useful than the "overdue by 3 h" phrasing the review suggested.
- **Uptime decays while a reporter is overdue** (100 → 80 → 66.67 → 57.14 → 50), because every tick
  writes a failed observation. That is correct and meaningful — "how much of the retained window was
  this backup healthy" — but it means a long outage walks a message probe's uptime towards 0, and
  recovery does not restore the figure. Worth knowing before anybody reads the dashboard's aggregate
  as "how reliable is the household".
- **A1 is visible**: across the two `unknown` ticks, uptime does not move at all. No observation is
  being written, which is the whole point of the no-verdict path.

**Left for later, deliberately** (also in Maintenance notes): email alerting, which plan 009 now
gets for free through the ordinary probe transition; a reader-facing history page; an idempotency
key; structured fields beside the free-text body.
