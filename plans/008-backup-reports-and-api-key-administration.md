# 008 — Backup reports and API-key administration

> **Executor instructions**: Follow step by step. Run every verification command and confirm
> the expected result before moving on. On a STOP condition, stop and report — do not
> improvise. When done, update this plan's status row in `plans/README.md`.
>
> **Drift check (run first)**: `git diff --stat f4e7261..HEAD -- src/Homon.Domain/Backups
> src/Homon.Domain/Auth src/Homon.Api/Endpoints src/Homon.Api/Authentication
> src/Homon.Api/Cli src/Homon.Infrastructure/Persistence src/Homon.Web/src/pages
> src/Homon.Web/src/lib src/Homon.Web/e2e docs/ARCHITECTURE.md docs/MODULES.md
> docs/design-brief.md`. `Program.cs`, `HomonDbContext.cs`, the migration snapshot, `App.tsx`,
> `dashboard-page.tsx`, `admin-home-page.tsx` and `e2e/helpers.ts` are also edited by plans
> 002–007 and 009–011, which run first in numeric order. **Don't diff those files whole** —
> anchor each edit on the marker named in *Current state*, confirm the marker still exists
> verbatim, and make the smallest edit that adds this plan's line beside it. A new `<li>`
> from an earlier plan above your anchor is expected, not drift.

## Status

- **Priority**: P2 · **Effort**: L · **Risk**: MED — touches the boundary an API-key
  principal must never cross, and six files other plans also edit.
- **Depends on**: none — builds only on plumbing already merged (`ApiKey`, the `ApiKey`
  scheme, `create-api-key`).
- **Category**: direction
- **Planned at**: commit `f4e7261`, 2026-09-15

## Context

`docs/MODULES.md`: "**Backups.** Scripts on the servers (restic) must report success; an API
endpoint the bash scripts write their logs to, authenticated with API keys." Row: "Report
endpoint for scripts (API key), job lateness, admin key management." Today `create-api-key`
is the only way to mint a key, revoking one means touching the database directly, and there
is no record of whether a backup ran — the household learns a job died by noticing the NAS
is full. This plan gives a restic wrapper one `curl` call, gives the administrator a page to
mint/revoke keys, and gives the dashboard a Backups panel that turns orange when late, red
when failed.

## Decisions

- **Job identity is a slug (`Key`); the job must exist first.** `POST
  /backups/jobs/{key}/runs`. Auto-creating on first report was rejected: a script typo would
  spawn a phantom job with no `ExpectedInterval`/`Grace`, so it could never be "late" and the
  real job would sit `Unknown` forever, silently. Requiring the job first turns a typo into
  an immediate 404.
- **`succeeded: bool`, not an exit code.** The wrapper already knows success/failure; restic's
  several distinct nonzero codes are what free-text `summary` is for.
- **Log excerpts are truncated (last 64 KiB kept), never rejected.** A truncated proof-of-run
  beats a failed report; Kestrel's 30 MB body limit is the real backstop.
- **`finishedAt` tolerance: 5 minutes in the future, else 400.** Beyond that is a script bug
  or a replay, not clock drift.
- **Lateness is computed at read time from a pure function, not tracked as state.** No
  scheduler; `GET /backups` recomputes `BackupJobState` from `(job, latestRun, now)` on every
  read — correct even if the API was down when a job should have reported. This function is
  the seam plan 009 needs (*Maintenance notes*).
- **Any key may report to any job in phase 1**; `ReportedByKeyId` records which one did.
  Scoping is deferred (*Scope*).
- **`create-api-key` stays** — the verb needs no running server, so it is still how a fresh
  install mints its first key before an administrator can even sign in.
- **Keys are soft-revoked**, matching what `ApiKey.cs:44-45` already says: "Set when the
  admin revokes the key. A revoked key is refused, never deleted." `DELETE /api-keys/{id}`
  sets `RevokedAt`; `GET /api-keys` still lists revoked keys, for the audit trail the entity
  already promises.
- **No `TimeProvider` seam added.** `Evaluate` takes `now` as a plain parameter, so pure
  tests need no fake clock. The two spots needing "now" at the API layer use
  `DateTimeOffset.UtcNow` directly, matching every other timestamp in this codebase
  (`ApiKeyIssuer.cs:37`, `AuthenticationEndpoints.cs:120`). `Directory.Packages.props` has no
  `Microsoft.Extensions.TimeProvider.Testing` today (verified — no `TimeProvider` hits under
  `src/`); adding a central package for one deterministic clock-skew check isn't worth it. If
  plan 009 needs to freeze time later, that's where the seam belongs.
- **`GET /backups/jobs/{id}/runs` (with log excerpts) is Administrator-only.** A restic log
  can carry host paths and hostnames — detail the phone-glancing Reader has no business
  seeing. `GET /backups` never includes `logExcerpt`, only `summary`.

## Defaults taken (change before implementation if wanted)

- `BackupJob.Key`: 1–60 chars, `^[a-z0-9]+(-[a-z0-9]+)*$`, unique.
- `BackupJob.Position`, admin-ordered — same rule as plan 002's `ProbeGroup.Position` (a sort
  key, renumbered on every rewrite). Not in the README's stated shape but matches
  `docs/design-brief.md`'s "Rows keep the administrator's order", which the Backups table
  explicitly reuses.
- `BackupRun.Summary` ≤ 280 chars; `LogExcerpt` ≤ 65536 chars, truncated keeping the **tail**
  (failures usually explain themselves at the end), marker `"[truncated, showing the last 64 KiB]\n"` prepended.
- Retention: runs older than 180 days deleted via `ExecuteDeleteAsync` after each report
  insert, scoped to that job — no `BackgroundService`.
- The reveal-once warning uses `role="alert"` (assertive) — nothing else on that page is
  unrecoverable if missed.
- Report endpoint rate limit: 30 requests / 5 minutes, keyed by the caller's key id (not IP —
  a script's IP is its server's, shared with everything else there).

## Current state

**`src/Homon.Domain/Auth/ApiKey.cs`** already has everything the admin page needs:
`Id`, `Name`, `TokenId`, `SecretHash`, `CreatedAt`, `LastUsedAt` (`:42`), `RevokedAt` (`:45`).
No role — a key principal genuinely cannot administer.

**`ApiKeyIssuer.IssueAsync(name, ct)`** (`src/Homon.Api/Authentication/ApiKeyIssuer.cs`)
validates the name, mints, saves, returns `(ApiKey, Presented)`. Not registered in DI today
(`new`'d by the CLI verb and by tests) — this plan adds
`builder.Services.AddScoped<ApiKeyIssuer>();`.

**`ApiKeyAuthenticationHandler.cs:72-77`** already throttles `LastUsedAt` writes to a
5-minute resolution via `ExecuteUpdateAsync` — nothing here needs to add that.

**`HomonPolicies.cs`**: `Reader`, `Administrator` (`RequireRole`), `ApiKey`
(`RequireAuthenticatedUser()` + `RequireClaim(AuthenticationKind, ApiKeyAuthentication)`). A
key principal carries no role claim, so `Administrator` already refuses it with a **403**
(authenticated, just not authorized) — see `ReaderPolicyTests.cs` for exercising a policy
through the real `IAuthorizationService`. `ApiKeyRefusalMiddleware.cs` already fails a bad
key's whole request rather than demoting it to anonymous. None of these three files change.

**`create-api-key`** (`Program.cs:89-119`, `Cli/CreateApiKeyArguments.cs`) stays as is.
`docs/ARCHITECTURE.md:78` currently reads "Until the Backups module ships its admin page,
`create-api-key --name …` is the minter" — this plan's docs step makes that past tense.

**`ApiKeyConfiguration.cs`** is the exemplar for the new `IEntityTypeConfiguration<T>`s:
`ToTable`, `HasKey`, `HasMaxLength`+`IsRequired`, one `HasIndex(...).IsUnique()`.
`HomonDbContext.cs:22` is the one place `DbSet<ApiKey>` is exposed — this plan adds two more
beside it. `InitialCreate.cs:16-30` is the `ApiKeys` table today (types, nullability); no
`BackupJobs`/`BackupRuns` exist yet.

**Endpoint exemplars**: `AuthenticationEndpoints.cs:12-20` is the CSRF-guard pattern
(`application/json` required on mutations); `MetaEndpoints.cs` is the smallest
`MapXxxEndpoints`. Plan 002's `ProbeGroupEndpoints.cs` (admin CRUD + `/order`) has not landed
yet, so *Steps* below spells that shape out fully rather than pointing at code that doesn't
exist.

**`Program.cs`** markers this plan edits:
- `:342`, `builder.Services.AddRateLimiter(options => { ... AddPolicy(SignInThrottlePolicy, ...) ... })`
  — add a second `AddPolicy("backup-report", ...)` inside the same block.
- `:477-479`, the commented module block:
  ```
  //   v1.MapProbeEndpoints();   v1.MapStatusEndpoints();   v1.MapLinkEndpoints();
  //   v1.MapPageEndpoints();    v1.MapBackupEndpoints();   v1.MapApiKeyEndpoints();
  //   v1.MapWeatherEndpoints(); v1.MapCalendarEndpoints();
  ```
  Uncomment exactly `MapBackupEndpoints()` and `MapApiKeyEndpoints()`; leave the other four —
  they belong to other plans.

**SPA**: `App.tsx:17` already lazy-imports `AdminApiKeysPage` and routes it at
`admin/api-keys` (`:45`) — the page itself is a one-paragraph placeholder ("Not implemented
yet — the Backups module (plan 008) adds key management here"); this plan replaces its body,
route unchanged. `admin-home-page.tsx:12-27` has `<nav aria-label="Admin sections">` with
`Probes`, `Links`, `Pages`, `API keys` — this plan inserts `Backups` between `Pages` and
`API keys`. No `/admin/backups` route exists — add one following `App.tsx:14`'s
`AdminProbesPage` pattern. `dashboard-page.tsx:14-21` renders exactly two sections
(`services-heading`, `links-heading`) — add a Backups section between them, matching
`docs/design/dashboard/Main.dc.html:168-186` (Backups and Links side by side, Backups first).
`e2e/helpers.ts:12-19` (`ADMIN_ROUTES`) and `e2e/admin.spec.ts:17`
(`['Probes','Links','Pages','API keys']`) both need the same `Backups` insertion in the same
position — that spec iterates every admin nav link.

**Design brief**: `docs/design-brief.md:250-251` — Backups table columns "Outcome 132px ·
Job 1.4fr · Last run 1.4fr · Schedule 140px, schedule in mono `muted`"; `:236` — chips reuse
"Succeeded (up), Late (unstable), Failed (down)". The artboard
(`docs/design/dashboard/Main.dc.html:174-184`) shows "Last run" as `today 02:14` or `3 days
ago · expected Sunday`, "Schedule" as mono `every night` / `weekly` — confirming schedule is
a formatted phrase built by the SPA, not a raw duration on the wire.

## Commands you will need

| Purpose | Command | Expected |
| --- | --- | --- |
| Full gate | `./ci/run-ci.sh` | `PASS — web api e2e`, 0 skips |
| API only | `./ci/run-ci.sh api` | build (Release), migrate, test |
| Web only | `./ci/run-ci.sh web` | npm ci, lint, build, vitest |
| e2e only | `./ci/run-ci.sh e2e` | Playwright, two viewports |
| New migration | `HOMON_DESIGNTIME_CONNECTION='Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x' dotnet dotnet-ef migrations add AddBackups --project src/Homon.Infrastructure --startup-project src/Homon.Api --output-dir Persistence/Migrations` | migration + updated snapshot |
| Apply locally | `dotnet run --project src/Homon.Api -- migrate` | `applied     : all of them.` |
| Mint a key manually | `dotnet run --project src/Homon.Api -- create-api-key --name "test"` | prints `hmn_…` once |

## Scope

**In scope**: `src/Homon.Domain/Backups/{BackupJob,BackupRun,BackupJobEvaluator}.cs` +
`README.md`; `Persistence/Configurations/{BackupJob,BackupRun}Configuration.cs`;
`HomonDbContext.cs`; one migration `AddBackups`; `Endpoints/{Backup,ApiKey}Endpoints.cs`;
`Program.cs` (the two markers above + `AddScoped<ApiKeyIssuer>()`);
`lib/{backups,api-keys}.ts`; `pages/admin-api-keys-page.tsx` (replace body);
`pages/admin-backups-page.tsx` (new); `pages/dashboard-page.tsx`; `pages/admin-home-page.tsx`;
`App.tsx`; `e2e/helpers.ts`, `e2e/admin.spec.ts`, new `e2e/backups.spec.ts`;
`docs/backup-reporting.md` (new), `README.md`, `docs/ARCHITECTURE.md` (§3.3 + new §3.16),
`docs/MODULES.md`, `docs/design-brief.md`, `plans/README.md`.

**Out of scope, and why**:
- Key scoping to specific jobs — deferred (*Decisions*, *Maintenance notes*).
- Alert email on "became late/failed" — plan 009's job; this plan only leaves the evaluator
  as the seam.
- `ApiKeyRules.cs`, `ApiKeyAuthenticationHandler.cs`, `ApiKeyRefusalMiddleware.cs`,
  `HomonPolicies.cs` — already do everything these endpoints need; touching them risks the
  "never administer" guarantee (`docs/ARCHITECTURE.md §3.3`).
- Styling (`className`, tokens) — plan 012's job.
- `MapProbeEndpoints`, `MapStatusEndpoints`, `MapLinkEndpoints`, `MapPageEndpoints`,
  `MapWeatherEndpoints`, `MapCalendarEndpoints` in the `Program.cs` comment — leave commented.

## Steps

Two slices, each independently green — API keys first (no new Domain entities, de-risks the
endpoint/SPA pattern this plan reuses for Backups).

### Slice A — API-key administration

**A1. Register `ApiKeyIssuer`.** `Program.cs`, near the other scoped registrations:
`builder.Services.AddScoped<ApiKeyIssuer>();`
**Verify**: `dotnet build src/Homon.Api` → 0.

**A2. `ApiKeyEndpoints.cs`.** New file, `MapApiKeyEndpoints(this RouteGroupBuilder parent)`,
group `/api-keys`:

| Route | Policy | Behaviour |
| --- | --- | --- |
| `GET /api-keys` | Administrator | `[{id, name, tokenId, createdAt, lastUsedAt, revokedAt}]`, newest first, revoked included. |
| `POST /api-keys` | Administrator | `{name}` (`application/json`). Calls the injected `ApiKeyIssuer`. 201 → `{id, name, tokenId, createdAt, token}` — `token` appears nowhere else. Empty/>100-char name → 400 `ValidationProblem`. |
| `DELETE /api-keys/{id:guid}` | Administrator | `ExecuteUpdateAsync` sets `RevokedAt = UtcNow` if unset. 204 if the row exists (idempotent even if already revoked), 404 otherwise. |

Uncomment `v1.MapApiKeyEndpoints();`.
**Verify**: `dotnet build src/Homon.Api` → 0.

**A3. `admin-api-keys-page.tsx` + `lib/api-keys.ts`.** `lib/api-keys.ts` modelled on
`lib/session.ts`: types for both shapes above, `useApiKeys()`, `useCreateApiKey()` (returns
the created record including `token` to the caller's local state — **never** into the query
cache, which a refetch or stale read could resurface), `useRevokeApiKey()`. Page: a "Name"
form + "Create key" button; on success, a reveal block replacing the form — read-only
labelled field with the token, "Copy key" button (`navigator.clipboard.writeText`),
`role="alert"` text "This key will not be shown again. Store it now." Below: a list of
existing keys (name, created, last used or "—", a "Revoke {name}" button, inline-confirmed,
disabled/labelled "Revoked" for already-revoked rows). No `className`.
**Verify**: `npm --prefix src/Homon.Web run build` → 0.

**A4.** Run `./ci/run-ci.sh api` and `./ci/run-ci.sh web` → both PASS, 0 skips. Commit slice
A here if splitting commits.

### Slice B — Backups

**B1. Domain.** `BackupJob`: `Id`, `Name` (≤100), `Key` (≤60, slug), `ExpectedInterval`
(`TimeSpan`), `Grace` (`TimeSpan`), `Position` (`int`), `CreatedAt`, `Runs` (`List<BackupRun>`).
`BackupRun`: `Id`, `JobId`, `StartedAt`, `FinishedAt`, `Succeeded` (`bool`), `Summary` (≤280),
`LogExcerpt` (`string?`, ≤65536), `ReportedByKeyId`, `CreatedAt` (ingestion time).
`BackupJobEvaluator` — the pure seam:
```csharp
public enum BackupJobState { Unknown, Succeeded, Late, Failed }

public static class BackupJobEvaluator
{
    // now is a parameter, not DateTimeOffset.UtcNow — testable with fixed values, and the
    // seam plan 009 evaluates against.
    public static BackupJobState Evaluate(BackupJob job, BackupRun? lastRun, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (lastRun is null) return BackupJobState.Unknown;
        if (!lastRun.Succeeded) return BackupJobState.Failed;
        return now - lastRun.FinishedAt <= job.ExpectedInterval + job.Grace
            ? BackupJobState.Succeeded : BackupJobState.Late;
    }
}
```
Replace `Backups/README.md` with the entities, state words, and a pointer to
`docs/backup-reporting.md`.
**Verify**: `dotnet build src/Homon.Domain` → 0.

**B2. Persistence + migration.** `BackupJobConfiguration`: `ToTable("BackupJobs")`,
`HasMaxLength`+`IsRequired` on `Name`/`Key`, `HasIndex(j => j.Key).IsUnique()`.
`BackupRunConfiguration`: `ToTable("BackupRuns")`, `HasMaxLength` on `Summary`; FK to
`BackupJob` (`WithMany(j => j.Runs)`, `Cascade` — deleting a job deletes its runs, unlike
`ProbeGroupMembership` there's no "keep orphans" case here); FK to `ApiKey` (`WithMany()`,
`Restrict` — `ApiKey` rows are never deleted, per `ApiKey.cs:44`, so this should never fire;
`Restrict` makes that assumption loud if it's ever wrong); `HasIndex(r => new {r.JobId,
r.FinishedAt})` (retention delete and "latest run" lookup both use it). `HomonDbContext.cs`:
add `DbSet<BackupJob> BackupJobs`, `DbSet<BackupRun> BackupRuns`, extend the class comment.
Generate the migration (command above, name `AddBackups`); inspect it creates both tables,
the unique index on `Key`, both FKs, the composite index.
**Verify**: `dotnet build Homon.sln` (Release) → 0; `dotnet run --project src/Homon.Api --
migrate` → `applied     : all of them.`

**B3. `BackupEndpoints.cs`.** `MapBackupEndpoints(this RouteGroupBuilder parent)`, group
`/backups`:

| Route | Policy | Behaviour |
| --- | --- | --- |
| `POST /backups/jobs/{key}/runs` | ApiKey, `RequireRateLimiting("backup-report")` | `{startedAt, finishedAt, succeeded, summary, logExcerpt?}` (`application/json`). Look up by `Key`; 404 naming it if missing. Validate `finishedAt >= startedAt`, `finishedAt <= UtcNow + 5min`, `summary.Length <= 280` → 400 on failure. Truncate `logExcerpt` to the last 65536 chars (prepend the marker) if longer. `ReportedByKeyId` from the caller's `HomonClaimTypes.ApiKeyId` claim (that claim carries `TokenId` today per `ApiKeyAuthenticationHandler.cs:83` — resolve the row's `Guid Id` by `TokenId` first; see STOP conditions if this needs to change). Run the 180-day retention delete for that `JobId` after insert. 201 → `{id}`. |
| `GET /backups` | Reader | `{jobs: [{id, name, key, state, schedule: {expectedIntervalSeconds, graceSeconds}, lastRun: {startedAt, finishedAt, succeeded, summary} \| null}]}`, `Position` order, `state` via `Evaluate(..., UtcNow)`. **No `logExcerpt` anywhere.** |
| `GET /backups/jobs` | Administrator | Same jobs, admin shape (adds `expectedIntervalSeconds`, `graceSeconds`, `position`; drops computed `state`/`lastRun` — reuse `/backups` on the admin page for outcomes). |
| `POST /backups/jobs` | Administrator | `{name, key, expectedIntervalSeconds, graceSeconds}` → 201. `key` validated against the slug pattern; duplicate → 400 (check first, and catch a racing Postgres `23505` the same way plan 002's `ProbeGroupEndpoints` does). New job goes last (`Position = max + 1`). |
| `PUT /backups/jobs/{id:guid}` | Administrator | same body → 200 or 404. |
| `DELETE /backups/jobs/{id:guid}` | Administrator | → 204 or 404; cascades to `BackupRuns` via the FK. |
| `PUT /backups/jobs/order` | Administrator | `{jobIds: Guid[]}` → 204; must list every job id exactly once. `:guid` on the sibling routes is what keeps `order` from matching `{id:guid}` — same as plan 002's `/probe-groups/order`. |
| `GET /backups/jobs/{id:guid}/runs` | Administrator | `[{id, startedAt, finishedAt, succeeded, summary, logExcerpt, reportedByKeyId, reportedByKeyName}]`, newest first — the **only** place `logExcerpt` is ever returned. |

Uncomment `v1.MapBackupEndpoints();`. Rate limiter, same `AddRateLimiter` block as
`SignInThrottlePolicy` (`:342`):
```csharp
options.AddPolicy("backup-report", httpContext =>
{
    var keyId = httpContext.User.FindFirst(HomonClaimTypes.ApiKeyId)?.Value ?? "unknown";
    return RateLimitPartition.GetFixedWindowLimiter($"backup-report:{keyId}",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 });
});
```
**Verify**: `dotnet build src/Homon.Api` → 0.

**B4. SPA.** `lib/backups.ts`: types for both `GET /backups` shapes; `useBackups()`,
`useBackupJobs()`, `useCreateBackupJob()`, `useUpdateBackupJob()`, `useDeleteBackupJob()`,
`useReorderBackupJobs()` (model on `lib/session.ts`); a pure `formatSchedule(seconds)` for
the mono "every night"/"weekly"/"every N hours" text
(`docs/design/dashboard/Main.dc.html:177,183`) — pure, so unit-testable without a query.

`dashboard-page.tsx`: insert, between `services-heading` and `links-heading`:
```tsx
<section aria-labelledby="backups-heading">
  <h2 id="backups-heading">Backups</h2>
  {/* empty state, else Outcome · Job · Last run · Schedule per design-brief.md:250-251 */}
</section>
```
`admin-backups-page.tsx` (new), route `/admin/backups`: ordered job list with "Move
{name} up/down", "Edit {name}", "Delete {name}" (inline-confirmed), plus a create form
(Name, Key, Expected interval, Grace). Wire: lazy import in `App.tsx` matching
`AdminProbesPage` (`:14`), route inside the admin subtree; `admin-home-page.tsx` gets
`<li><Link to="/admin/backups">Backups</Link></li>` between `Pages` and `API keys`
(`:20-25`). `e2e/helpers.ts`: insert `{path: '/admin/backups', name: 'backups'}` into
`ADMIN_ROUTES` between `pages` and `api-keys`. `e2e/admin.spec.ts:17`: insert `'Backups'`
between `'Pages'` and `'API keys'`.
**Verify**: `npm --prefix src/Homon.Web run build` → 0.

**B5. Docs.** New `docs/backup-reporting.md`: the report wire shape, a `curl` snippet
reading the key from a file (`Authorization: Bearer $(cat /etc/homon/restic-api-key)`,
never inline), a short restic wrapper example — generic names (`example-job`,
`backup-host`), no household specifics. `README.md`: add the doc to "Where to read next".
`docs/ARCHITECTURE.md`: make §3.3's last line past tense (`create-api-key` **and**
`POST /api-keys` now both mint, via the shared `ApiKeyIssuer`); add **§3.16** "Backup
lateness is computed at read time, not tracked" — `grep -n '^### 3\.1[3-9]'
docs/ARCHITECTURE.md` first; 003/007 may have claimed `§3.14`/`§3.15` by execution time, so
renumber to the next free slot if `§3.16` is taken. Content: the evaluator, why no background
job exists, that plan 009 needs a states-across-reads comparison for the transition event
(*Maintenance notes*). `docs/MODULES.md`: extend the 008 row; add an "Added after the brief"
paragraph (plan 002's pattern) for `Key`, `Position`, the 180-day default. `docs/design-brief.md`:
Admin gains the `/admin/backups` bullet; "Not drawn yet" gains the reveal-once flow and the
job form. `Backups/README.md` already replaced in B1 — confirm it points at the new doc.
`plans/README.md`: status row.
**Verify**: `grep -n "create-api-key --name … is the minter" docs/ARCHITECTURE.md` → no
match; `grep -c '^### 3\.' docs/ARCHITECTURE.md` → one more section than before.

**B6.** `./ci/run-ci.sh api`, `./ci/run-ci.sh web`, `./ci/run-ci.sh e2e`, then
`./ci/run-ci.sh` → all `PASS`, 0 skips.

## Test plan

**xunit — pure**, `BackupJobEvaluatorTests` (table-driven, fixed `DateTimeOffset`s, no fake
clock — see *Decisions*): no run → `Unknown`; latest run failed → `Failed` even if recent;
inside `ExpectedInterval + Grace` → `Succeeded`; one second past → `Late`; exactly at the
boundary → `Succeeded` (assert the actual `<=`, don't just restate the code).

**xunit — `[DatabaseFact]`**, `ApiKeyEndpointTests`: anonymous `POST /api-keys` → 401; **an
API-key principal on `POST /api-keys` and `GET /api-keys` → 403 each** (the load-bearing test
this module's trust boundary depends on); administrator `POST /api-keys` → 201 with `token`;
a subsequent `GET /api-keys` never includes a `token` field; `DELETE /api-keys/{id}` → 204,
then that key fails to authenticate (reuse the "revoked" message from
`ApiKeyAuthenticationTests`); a second `DELETE` on the same id → 204 (idempotent); unknown id
→ 404; empty/101-char name → 400.

`BackupEndpointTests`: valid report → 201, `GET /backups` reflects it as `lastRun`/`Succeeded`;
unknown job key → 404 naming it; revoked key → 401; **a cookie-authenticated administrator
posting a report → 403** (mirror image of the administrator-only tests — a browser must
never file a report); `finishedAt` before `startedAt` → 400; `finishedAt` >5min future → 400;
oversized `logExcerpt` → 201, stored length exactly 65536 with the truncation marker
(assert via the admin run-history endpoint); `GET /backups` response body never contains the
string `logExcerpt` at all, for any job, even one with runs that have one; non-administrator
on `GET /backups/jobs/{id}/runs` → 403/401; lateness end-to-end — seed a job (1h interval, 0
grace), insert a run directly via `HomonDbContext` with `FinishedAt = UtcNow.AddHours(-2)`
(no clock control needed — just place it in the past), `GET /backups` → `state == "late"`.

**Vitest**: `lib/backups.test.ts` — `formatSchedule` for daily/weekly/arbitrary, and
zero/undefined doesn't throw. `admin-api-keys-page.test.tsx` — reveal block shows the token
and `role="alert"`; the token is gone from a second render triggered by the keys list
refetching (proves it's local state, not the query cache); "Copy key" calls
`navigator.clipboard.writeText` with the exact token; revoking updates the row.
`dashboard-page.test.tsx` (extend) — a region named "Backups" exists; chip words are exactly
`Succeeded`/`Late`/`Failed`/`Unknown` (`docs/design-brief.md:228-236`).

**Playwright**, new `e2e/backups.spec.ts` (signed-in project, `admin.spec.ts`'s structure):
create a job under `/admin/backups`, mint a key on `/admin/api-keys`, report via a direct
authenticated `POST` from the test (scripts hit this, not the UI), assert the dashboard
shows "Succeeded"; reveal-once — create a key, assert the token shown once, navigate away and
back, assert it's gone from that row; revoke a key, assert the row reflects it; run
`expectNoHorizontalOverflow`/`expectNoOverlap` at both viewports on `/admin/backups` and the
dashboard's Backups section; clean up jobs/keys in `afterEach` (the e2e database is shared).

## Done criteria

- [ ] `./ci/run-ci.sh` → `PASS — web api e2e`, 0 skipped api tests.
- [ ] `dotnet build Homon.sln` (Release) → 0 warnings.
- [ ] `grep -n "MapBackupEndpoints\|MapApiKeyEndpoints" src/Homon.Api/Program.cs` shows both
  uncommented and called.
- [ ] `grep -n "create-api-key --name … is the minter" docs/ARCHITECTURE.md` → no match.
- [ ] A `[DatabaseFact]` test asserts `GET /backups`'s body never contains `logExcerpt`.
- [ ] A `[DatabaseFact]` test asserts an API-key principal gets 403 from all three
  `/api-keys` routes.
- [ ] `git status` shows no file outside *Scope*; shared-file diffs sit only at the named
  markers.
- [ ] `plans/README.md` status row for 008 updated.

## STOP conditions

- Any "Current state" excerpt doesn't match the live file — reconcile before proceeding.
- §3.13/§3.14/§3.15 are already taken and §3.16 is too by the time this executes — pick the
  next free number and note the renumber; don't overwrite another plan's section.
- A step's verification fails twice after a reasonable fix attempt.
- `HomonClaimTypes.ApiKeyId` turns out not to carry what `ReportedByKeyId` needs for a
  single-query lookup — resolving by `TokenId` first is a one-line fix, not a redesign; make
  it, but note the detail in the PR rather than changing `HomonClaimTypes` silently.
- Anything requires touching `ApiKeyRules.cs`, `ApiKeyAuthenticationHandler.cs`,
  `ApiKeyRefusalMiddleware.cs`, or `HomonPolicies.cs` — out of scope; stop and report what
  forced it.

## Maintenance notes

- **The seam for plan 009** is `BackupJobEvaluator.Evaluate(job, lastRun, now)` — stateless,
  so nothing here remembers "the state we last saw" for plan 009 to diff against. Two routes
  to the transition event it needs, neither built here: (a) synchronous — a `Succeeded =
  false` report already knows the instant a job fails, a trivial hook; (b) "became late" has
  no event to hook (no report ever arrives), so it needs a low-frequency `BackgroundService`
  comparing `Evaluate`'s result against a new column (e.g. `BackupJob.LastNotifiedState`)
  this plan deliberately does not add — plan 009 should add it when it needs it.
- **Key scoping** (a key restricted to one job), if it lands, is additive — a nullable
  `BackupJob.RestrictedToKeyId` or a join table, checked before the insert in
  `POST /backups/jobs/{key}/runs`. No shape here needs to change to add it.
- **Retention** (180 days, per-job on report insert) is a default, not a setting; making it
  configurable is a field on `BackupJob` — scoping the delete to `JobId` rather than global
  already assumes that.
- A reviewer should scrutinize the policy on every new route in `BackupEndpoints.cs` and
  `ApiKeyEndpoints.cs` against the tables in *Steps* — a swapped policy is a silent
  trust-boundary break, not a visible bug.
- If plan 002 lands first and adds its own `Position` note to `docs/ARCHITECTURE.md`, this
  plan's `BackupJob.Position` comment can point at that section instead of restating the rule.
