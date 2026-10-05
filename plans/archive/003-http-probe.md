# 003 — HTTP/HTTPS probe

> **Executor instructions**: Follow this plan step by step. Run every verification command
> and confirm the expected result before moving on. If anything in "STOP conditions"
> occurs, stop and report — do not improvise. This plan does **not** ask you to update
> `plans/README.md`; the reviewer maintains that index for this run.
>
> **First step**: read "Contract assumed from plan 002" below — it now names 002's own
> committed contract table verbatim (002 has been reviewed and its names are pinned).
> Confirm the code 002's executor actually landed matches those names; 002 runs immediately
> before this plan in the same session, so if its landed code still diverged from its own
> plan text, use the real names throughout instead. **STOP if the `IProbeRunner` seam or a
> per-kind options storage mechanism does not exist in any form** — this plan has nothing to
> attach to.
>
> **Drift check (run first)**:
> `git diff --stat f4e7261..HEAD -- src/Homon.Domain/Monitoring src/Homon.Infrastructure/Monitoring src/Homon.Infrastructure/Persistence src/Homon.Infrastructure/InfrastructureServiceCollectionExtensions.cs src/Homon.Api/Endpoints/ProbeEndpoints.cs src/Homon.Api/Program.cs src/Homon.Web/src/pages/admin-probes-page.tsx src/Homon.Web/src/lib docs/ARCHITECTURE.md docs/MODULES.md`
> 002 legitimately touches shared files (`Program.cs`, `HomonDbContext.cs`,
> `InfrastructureServiceCollectionExtensions.cs`, `admin-probes-page.tsx`) — expected, not
> drift. Compare only the regions named in "Current state" / "Scope"; a change elsewhere in
> the same file is not a STOP condition.

## Status

- **Priority**: P2
- **Effort**: M
- **Risk**: MED — new patterns here (a jsonb owned-type column, `IDataProtectionProvider`-
  backed secrets, outbound HTTP to admin-configured hosts)
- **Depends on**: `plans/002-monitoring-core-groups-and-ping.md` — executes immediately
  before this plan in this session, and is complete
- **Category**: direction
- **Planned at**: commit `f4e7261`, 2026-09-15
- **Reviewed**: 2026-09-15 (review-plan against completed plan 002; execution order
  013 → 002 → 003 → 006 → 007 → 010 → 012 — 004, 005, 008, 009, 011 do not run this session).
  Maintainer answers applied 2026-09-15: Q1 (success criteria) changed to 2xx-only by
  default; Q2 (methods) confirmed HEAD/GET; Q3 (timeout) changed to a per-probe setting
  (Decision 4a); Q4 (redirects) confirmed on.

## Context

`docs/MODULES.md` (verbatim):

> HTTP requires a method (HEAD, GET, …), a URI (`api/health`), an optional matching text
> and/or status code (each negatable), and optional credentials (bearer key, basic auth)
> compatible with the existing services' API requirements. Admins add/edit/delete probes.

And the constraint governing every secret this and its siblings (004 SMB, 011 Calendar)
will store:

> **Probe secrets** (SMB password, HTTP bearer) are stored encrypted with ASP.NET Data
> Protection. The key ring is the `dataprotection-keys` volume in production; losing it
> means re-entering every secret, which is why the runbook says how to export it.

Plan 002 has landed the probe model, scheduler, state machine, groups and the ping runner in
full — HTTP is untouched there by design (002's own Scope excludes "SMB/HTTP/SNMP options
and runners (003–005)" and "the credential/secret-protector seam (003)"). This is the second
probe kind, and it is where the *pattern* for every future kind's options and secrets gets
set: 004 (SMB) and 005 (SNMP) are expected to copy this plan's shape by name, whenever they
run (not this session).

## Contract assumed from plan 002

Not implemented here — only consumed. Confirm each exists; if 002 named something
differently, use 002's real name throughout instead of duplicating it.

| Assumed | Shape | Where |
| --- | --- | --- |
| `Probe` | `Guid Id`, `string Name`, `string Host`, `ProbeKind Kind`, `TimeSpan PollInterval`, `int FailureThreshold`, `bool IsPaused`, `int Position` (002's own contract table's order), plus live-state fields (`Status`, two consecutive counters, `LastObservedAt`, `LastLatencyMs`, `LastDetail`) this plan does not touch — `HttpProbeRunner` only ever reads `probe.Host` and `probe.HttpOptions`. | `Homon.Domain/Monitoring/Probe.cs` |
| `ProbeKind` | Enum incl. `Ping`, `Http` (and `Smb`, `Snmp` reserved for 004/005, not run this session). | `Homon.Domain/Monitoring/` |
| `ProbeObservation` | `long Id`, `Guid ProbeId`, `DateTimeOffset ObservedAt`, `bool Succeeded`, `double? LatencyMs`, `string? Detail`. | `Homon.Domain/Monitoring/` |
| `ProbeStatus` | up/unstable/down/unknown/paused + consecutive counters. | `Homon.Domain/Monitoring/` |
| `IProbeRunner` | `ProbeKind Kind { get; }`, `Task<ProbeResult> RunAsync(Probe, CancellationToken)`; the scheduler resolves the runner for a probe with `runners.FirstOrDefault(r => r.Kind == probe.Kind)` over `IEnumerable<IProbeRunner>` (`scope.ServiceProvider.GetServices<IProbeRunner>()`). | `Homon.Infrastructure/Monitoring/IProbeRunner.cs` |
| `ProbeResult` | `record ProbeResult(bool Succeeded, double? LatencyMs, string? Detail)`. | `Homon.Infrastructure/Monitoring/` |
| `ProbeEndpoints` | `internal static class ProbeEndpoints`, `MapProbeEndpoints(this RouteGroupBuilder)`, CRUD under `/probes`. **Policy** (resolved by the maintainer): plan 013 (API key scopes and expiry) now runs before 002 in this session and adds `HomonPolicies.AdministratorOrApiKey`; 002 uses it on `GET /probes`/`GET /probes/{id:guid}`, and keeps every write on `HomonPolicies.Administrator`. This plan still does **not** hard-code a policy anywhere in its own new code — it only adds fields to the existing `Http` DTOs and validation branches, so it inherits whichever policy attribute the landed `ProbeEndpoints.cs` already carries per route; if the landed code uses a different policy name for reads, follow that instead of `AdministratorOrApiKey`. By the time this plan runs, 002's own Slice 4 has already replaced the commented `v1.MapProbeEndpoints();` at `Program.cs:477` with an active call (with `v1.MapStatusEndpoints();`/`v1.MapProbeGroupEndpoints();` beside it) — this plan still does not touch `Program.cs`, only what `ProbeEndpoints.cs` validates. | `Homon.Api/Endpoints/ProbeEndpoints.cs` |
| `admin-probes-page.tsx` | 002's Slice 7 has already replaced today's placeholder with a real `ProbeForm` (Name, Host, Kind select, Poll interval, Failure threshold, Groups fieldset). Its per-kind fieldset region is already a conditional block keyed on the selected kind (002 Decision 10), structured even though nothing renders inside it for `Ping` — Step 6 below is the first thing to put content in that region, not the plan that invents it. | `Homon.Web/src/pages/admin-probes-page.tsx` |

If the landed page has no `ProbeForm`, or no per-kind conditional region at all, that is
drift from 002's own plan — see "STOP conditions" rather than inventing the pattern
yourself, since 004/005 (later, not this session) will copy whatever shape lands here.

## Decisions

**1. Per-kind options storage: one nullable owned type per kind, mapped to its own `jsonb`
column via EF Core's `OwnsOne(...).ToJson()`.**

```csharp
// On Probe (002's entity):
public HttpProbeOptions? HttpOptions { get; set; }   // null unless Kind == ProbeKind.Http

// In ProbeConfiguration.cs (002's file):
builder.OwnsOne(p => p.HttpOptions, http =>
{
    http.ToJson();                    // one jsonb column, "HttpOptions"
    http.OwnsOne(o => o.Credential);  // nested owned type, same document
});
```

*Rejected* (record as a comment in `ProbeConfiguration.cs`): a separate table per kind —
every probe-list read needs a conditional join per kind, and a new kind is a migration
touching the shared read path; a single kind-agnostic jsonb blob — loses compile-time field
names and mixes each kind's validation together; flat nullable scalar columns prefixed by
kind — four kinds × ~6 fields is 20+ mostly-null columns with no natural home for negation
flags.

**2. The secret protector.** `Homon.Infrastructure/Security/ISecretProtector.cs` +
`DataProtectionSecretProtector.cs`, one purpose string for every probe secret Homon ever
stores (SMB password in 004, calendar credentials in 011 included):

```csharp
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedValue);
}

public sealed class DataProtectionSecretProtector(IDataProtectionProvider provider)
    : ISecretProtector
{
    private const string Purpose = "Homon.Secrets.v1";
    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    // Throws CryptographicException when the key ring cannot unprotect — typically because
    // dataprotection-keys was lost. Callers reading a credential at poll time MUST catch
    // this and turn it into a failed observation, not let it reach the scheduler.
    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}
```

*Wire semantics (write-only secrets)*, binding on 004 and 011 too: a probe response never
carries a secret, only `hasSecret: bool` (`ProtectedSecret is not null`). On write,
`credential.secret`: **absent/null** → keep the stored value; **`""`** → clear it
(`ProtectedSecret = null`); **non-empty** → `Protect()` and replace. `credential.username`
is not a secret and round-trips in the clear (basic auth only). `credential.type ==
"none"` clears any stored secret regardless of what else is sent.

*Key-ring loss*: `HttpProbeRunner` unprotects inside `try { } catch (CryptographicException)`
and returns `new ProbeResult(false, null, "credentials unreadable — re-enter them")`.

Record this as **`docs/ARCHITECTURE.md` §3.17** — 013 (which runs before 002 in this session)
claims §3.13, and 002 claims §3.14 (probe groups), §3.15 (scheduler shape) and §3.16 (uptime
definition), so this plan's slot is §3.17. Still verify with
`grep -n '^### 3\.' docs/ARCHITECTURE.md` before writing, per `plans/README.md`'s own
rule that section numbers are decided at execution time, not fixed in advance — renumber if
something landed differently.

**3. `IDataProtectionProvider` is already available for injection once the host is built —
but `Homon.Infrastructure` needs a new package to see the type at all.**
`Program.cs:219–226` calls `AddDataProtection().PersistKeysToFileSystem(...)` only when
`DataProtection:KeyRingPath` is configured — that conditional controls *where* keys
persist, not *whether* the provider is registered (ASP.NET Core's hosting defaults register
it unconditionally once `Homon.Api`, a `Microsoft.NET.Sdk.Web` project, builds the host;
cookie auth and Identity's token providers already depend on it). Register
`DataProtectionSecretProtector` unconditionally in `AddHomonInfrastructure`.

*New package, confirmed necessary, not merely possible*: `Homon.Infrastructure.csproj`
targets plain `Microsoft.NET.Sdk` (`<Project Sdk="Microsoft.NET.Sdk">`), so unlike
`Homon.Api` it gets no implicit `FrameworkReference` to `Microsoft.AspNetCore.App`, and
nothing in its dependency graph pulls in Data Protection today
(`grep -rn "DataProtection" src/Homon.Infrastructure/obj/project.assets.json` finds
nothing). Add `<PackageReference Include="Microsoft.AspNetCore.DataProtection.Abstractions"
/>` to `Homon.Infrastructure.csproj` and `<PackageVersion
Include="Microsoft.AspNetCore.DataProtection.Abstractions" Version="10.0.11" />` to
`Directory.Packages.props` — that version is published on nuget.org and matches every other
ASP.NET Core package's pin in this repo. `IDataProtectionProvider` will not resolve as a
type inside `Homon.Infrastructure` without this reference.

**4. HTTP runner: one named `HttpClient`, redirects on, TLS opt-out per-request via
`HttpRequestOptions`, not a second client.**

```csharp
services.AddHttpClient(HttpProbeRunner.HttpClientName)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = true,   // a reverse-proxied home service redirecting to its own
                                    // canonical host/path is common; not following it would
                                    // misreport that as a status mismatch.
        ServerCertificateCustomValidationCallback = (request, _, _, errors) =>
            errors == System.Net.Security.SslPolicyErrors.None
            || (request.Options.TryGetValue(HttpProbeRunner.IgnoreCertificateErrorsOption, out var ignore)
                && ignore),
    });
```

`HttpProbeRunner` sets `request.Options.Set(IgnoreCertificateErrorsOption, probe.HttpOptions.IgnoreCertificateErrors)`
per request. The callback receives the `HttpRequestMessage` itself, so one pooled handler
makes a per-request decision — no second client, no duplicated timeout/redirect config.
*Rejected*: two named clients (strict/permissive) — doubles every non-TLS setting for no
benefit once the callback can read a per-request flag.

*`IgnoreCertificateErrors` itself* is a deliberate per-probe trade-off: self-signed certs
are common on home NAS/media servers; without an opt-out every HTTPS probe against one
would permanently read "down" regardless of whether the service is up. It disables
validation only for that probe's own requests, to a host the admin already chose — it does
not widen what the admin's network access already permits. Default `false`; the form's
checkbox must say "Skip certificate validation (self-signed certificates)" explicitly.

**4a. Timeout is per-probe, resolved by the maintainer — not a runner constant.**
`HttpProbeOptions.TimeoutSeconds: int`, default `10`, validated `1`–`25` inclusive.
`HttpProbeRunner.RunAsync` enforces it with a linked
`CancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(probe.HttpOptions.TimeoutSeconds))`
— matching `ResendEmailSender.cs:23,44-45`'s `SendTimeout`/linked-token-source precedent
rather than `HttpClient.Timeout` (the client is shared across every HTTP probe via the
factory). `Detail` on expiry is `"timeout after {n} s"`, `n` the probe's own configured
value.

*Why the upper bound is 25, not 30*: 002's `ProbeScheduler.PollAndPersistAsync` (002
Decision 3) already wraps every runner's `RunAsync` call, regardless of kind, in its own
linked `CancellationTokenSource.CancelAfter(MonitoringOptions.PerPollTimeout)` (default
30 s) — the outer budget that protects the scheduler's tick from a hung runner. A per-probe
`TimeoutSeconds` at or above 30 could race that outer timeout, and the poll would then
surface as the scheduler's own generic `"probe timed out"` detail instead of this runner's
more specific `"timeout after {n} s"`; the 5 s margin keeps `HttpProbeRunner`'s own timeout
always the one that fires first, so the admin gets the detailed message. The same
relationship `PingProbeRunner.Timeout` (a fixed 5 s, 002 Decision 4) already has with the
outer budget — do not remove, extend, or duplicate the scheduler's own `CancelAfter`.

**STOP if 002's landed `MonitoringOptions.PerPollTimeout` default is not `30` seconds** —
the `1`–`25` bound above assumes it is. If it landed differently, recompute the upper bound
as "5 s under whatever 002's default is" and record the new number here (and in Step 2's
domain validation and Step 5's API validation) instead of silently keeping `25`.

*URI*: `{scheme}://{probe.Host}/{path}`, `scheme` = `https` iff `UseHttps`, `path` =
`HttpProbeOptions.Path` with any leading `/` trimmed (the brief's own example, `api/health`,
has none). Validation rejects a `Path` containing `"://"`.

*Status/body matching, each negatable*: `ExpectedStatusCode: int?` +
`ExpectedStatusCodeNegate: bool`; `ExpectedBodyText: string?` + `ExpectedBodyTextNegate:
bool`. `HEAD` + non-null `ExpectedBodyText` → 400 at validation (a HEAD response has no
body). *Body cap*: read at most `HttpProbeRunner.MaxBodyBytes = 64 * 1024` bytes before
testing `Contains`.

*Status evaluation, resolved by the maintainer — exact combination table.* With no
`ExpectedStatusCode` configured, only a 2xx response (`200`–`299`) counts as a successful
poll — a completed non-2xx response is a failed poll. This is a change from this plan's
original draft ("any completed response counts as success"); see "Defaults taken".
Configuring `ExpectedStatusCode` (possibly negated) **replaces** the 2xx rule entirely for
that probe. `ExpectedBodyText` (possibly negated), when configured, is evaluated *in
addition to* whichever status rule applies — both must pass. Status is evaluated first;
the body check runs only if the status check passes.

| `ExpectedStatusCode` | Status check passes when | `ExpectedBodyText` (only checked if status passes) |
| --- | --- | --- |
| unset | actual status is 2xx | unset → not evaluated; set (not negated) → body must contain text; set (negated) → body must not contain text |
| set, not negated | actual == expected | as above |
| set, negated | actual != expected | as above |

*Detail* (design-brief's own example: `"HTTP 503"`):

| Situation | Detail |
| --- | --- |
| Status check fails, `ExpectedStatusCode` unset (actual is not 2xx) | `"HTTP {actual}"` |
| Status check fails, `ExpectedStatusCode` set | `"HTTP {actual} (expected {[not ]}{expected})"` |
| Status check passes, body check fails | `"body did not contain \"{text}\""` / negated: `"body contained \"{text}\""` |
| Status check passes, body check passes or `ExpectedBodyText` unset | `"HTTP {actual}"` |
| Timeout | `"timeout after {n} s"` (`n` = the probe's own `HttpProbeOptions.TimeoutSeconds`, Decision 4a) |
| Connection failure | `"connection failed: {exception.Message}"` |
| TLS failure (not ignored) | `"TLS certificate error: {exception.Message}"` |
| Credentials unreadable | `"credentials unreadable — re-enter them"` |

*Latency*: `Stopwatch` around the whole request through the capped body read. *SSRF*: not a
finding — probes reach LAN hosts the admin already controls, by design; do not block
private ranges.

## Defaults taken (change before implementation if wanted)

- **Resolved by the maintainer.** No `ExpectedStatusCode` configured → only a 2xx response
  counts as success; a completed non-2xx response is a failure, `Detail` `"HTTP {actual}"`.
  Configuring `ExpectedStatusCode` (possibly negated) replaces the 2xx rule entirely.
  `ExpectedBodyText` (possibly negated), when configured, is evaluated in addition to
  whichever status rule applies, status first. See Decision 4's combination table. (This
  supersedes an earlier draft of this plan, which defaulted to "any completed response
  counts as success.")
- `HttpProbeMethod` is `Head`/`Get` only this phase (confirmed by the maintainer) — the
  brief's "…" reads as extensible.
- `ExpectedStatusCode` and `ExpectedBodyText` are independent; both may be set, both must
  pass, status evaluated first.
- **Resolved by the maintainer.** `HttpProbeOptions.TimeoutSeconds` is per-probe, default
  `10`, validated `1`–`25` inclusive (Decision 4a). (This supersedes an earlier draft of
  this plan, which used a fixed, non-admin-configurable `HttpProbeRunner.RequestTimeout`
  constant.)
- Migration name `AddHttpProbeOptions`, layered on 002's `AddMonitoring` (additive
  `ALTER TABLE ... ADD COLUMN`, not a rewrite of 002's migration).

## Current state

- `src/Homon.Api/Program.cs:212-226` — Data Protection block; only
  `PersistKeysToFileSystem` is conditional (Decision 3).
- `src/Homon.Api/Program.cs:476-479` is a commented module-registration block today; 002's
  own Slice 4 replaces `v1.MapProbeEndpoints();` there with an active call before this plan
  starts (`v1.MapStatusEndpoints();`/`v1.MapProbeGroupEndpoints();` land beside it). This
  plan does not touch `Program.cs` — only what `ProbeEndpoints.cs` validates.
- `src/Homon.Infrastructure/InfrastructureServiceCollectionExtensions.cs` —
  `AddHomonInfrastructure` composes `AddHomonDatabase`/`AddHomonEmail`/`AddAdministrator`
  as `private static void AddXxx(this IServiceCollection …)`, with plan 013's
  `services.AddSingleton(TimeProvider.System);` as its very first line (register nothing new
  for `TimeProvider` — reuse that one registration, same as 002 does); 002 adds a fourth
  `AddXxx`, `AddHomonMonitoring` (registering `IIcmpPinger`, `PingProbeRunner`,
  `ProbeScheduler`, `ProbeObservationRetentionService`), called alongside the rest — this
  plan's `HttpProbeRunner`/named `HttpClient`/`ISecretProtector` registrations extend that
  same method rather than adding a fifth.
- `src/Homon.Infrastructure/Email/ResendEmailSender.cs:14-60` — the outbound-HTTP-with-
  timeout exemplar (`SendTimeout`, linked `CancellationTokenSource`, exceptions turned into
  a typed failure, never propagated raw) this runner follows.
- `src/Homon.Infrastructure/Persistence/Configurations/ApiKeyConfiguration.cs` — the
  `IEntityTypeConfiguration<T>` shape (`internal sealed class X : IEntityTypeConfiguration<X>`,
  rejected alternatives recorded as comments — see `plans/002-monitoring-core-groups-and-ping.md`'s
  Slice 2 for that convention applied to `ProbeConfiguration`/`ProbeGroupConfiguration` (not
  `§2`, which is an `ARCHITECTURE.md` section number and does not apply to plan files)).
- `src/Homon.Infrastructure/Persistence/HomonDbContext.cs:19-29` — one `DbSet<T>` per
  aggregate; this plan adds none (HTTP options are owned by `Probe`, not a root).
- `src/Homon.Api/Endpoints/AuthenticationEndpoints.cs:13-19,149-159` — the CSRF posture
  (mutating endpoints require `application/json`) and `TypedResults.Problem(...)` pattern;
  use `TypedResults.ValidationProblem` for field-level 400s per 002's own stated convention.
- `src/Homon.Api/Authentication/HomonPolicies.cs:17-26` — `Reader` for reads,
  `Administrator` for writes; no new policy needed.
- `src/Homon.Web/src/lib/meta.ts` — the `lib/*.ts` shape (typed interface with a doc
  comment naming the C# type, query key, fetch fn, hook) the HTTP wire types follow.
- `src/Homon.Web/src/test/fetch.ts:8-33` — `stubFetch`, the only network stub used (no MSW).
- `src/Homon.Web/e2e/helpers.ts:11-17` — `ADMIN_ROUTES` already has `/admin/probes`.
- `src/Homon.Web/e2e/layout.spec.ts:28-34` — asserts an `h2` "Services" heading on `/`
  today; do not rename it.
- `Directory.Packages.props` — CPM; this plan needs two **new** pins, both confirmed
  published on nuget.org at the repo's existing `10.0.11` pattern:
  `Microsoft.AspNetCore.DataProtection.Abstractions` (Decision 3) and
  `Microsoft.Extensions.Http` (Decision 4, for `AddHttpClient`). Neither is already
  reachable the easy way: `Homon.Infrastructure` targets plain `Microsoft.NET.Sdk`, not
  `Microsoft.NET.Sdk.Web`, so — unlike `Homon.Api` — it gets no `FrameworkReference` to
  `Microsoft.AspNetCore.App`. `Microsoft.Extensions.Http` currently reaches
  `Homon.Infrastructure` only as an *undeclared transitive* dependency of the `Resend`
  package, pinned at `8.0.0` there (not this repo's own `10.0.11`) — add it directly rather
  than relying on that. Add both `<PackageReference>`s to `Homon.Infrastructure.csproj` and
  both `<PackageVersion>`s here.
- `docs/ARCHITECTURE.md` ends at §3.12 today (§4 is "not yet decided"). 013 (runs before 002 in
  this session) claims §3.13; 002 claims §3.14 (probe groups), §3.15 (scheduler shape) and
  §3.16 (uptime definition), so this plan's secret-protector section (Decision 2) is §3.17 —
  verify with `grep -n '^### 3\.' docs/ARCHITECTURE.md` before writing, in case something
  landed differently.
- `plans/README.md:15` — `| 003 | planned | M | 002 | HTTP/HTTPS probe |`.

## Commands you will need

| Purpose | Command | Expected on success |
| --- | --- | --- |
| Full gate | `./ci/run-ci.sh` | `PASS — web api e2e`, 0 skipped api tests |
| API only | `./ci/run-ci.sh api` | same, api suite |
| Web only | `./ci/run-ci.sh web` | `npm ci`, lint, build (=typecheck), vitest all pass |
| e2e only | `./ci/run-ci.sh e2e` | Playwright, two viewports |
| Release build (format gate) | `dotnet build Homon.sln --configuration Release` | 0 warnings/errors |
| New migration | `HOMON_DESIGNTIME_CONNECTION='Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x' dotnet dotnet-ef migrations add AddHttpProbeOptions --project src/Homon.Infrastructure --startup-project src/Homon.Api --output-dir Persistence/Migrations` | migration files created |
| Apply locally | `dotnet run --project src/Homon.Api -- migrate` | `applied     : all of them.` |
| One xunit class | `dotnet test tests/Homon.Api.Tests --filter FullyQualifiedName~HttpProbeRunnerTests` | all pass |
| One Vitest file | `npm --prefix src/Homon.Web run test -- admin-probes-page` | all pass |

## Scope

**In scope**:
- `src/Homon.Domain/Monitoring/HttpProbeOptions.cs`, `HttpCredential.cs` (create)
- `src/Homon.Domain/Monitoring/Probe.cs` (extend — add `HttpOptions`; 002's file)
- `src/Homon.Infrastructure/Security/ISecretProtector.cs`, `DataProtectionSecretProtector.cs` (create)
- `src/Homon.Infrastructure/Monitoring/HttpProbeRunner.cs` (create)
- `src/Homon.Infrastructure/Persistence/Configurations/ProbeConfiguration.cs` (extend, 002's file)
- `src/Homon.Infrastructure/Persistence/Migrations/*AddHttpProbeOptions*` (create, `dotnet ef`)
- `src/Homon.Infrastructure/InfrastructureServiceCollectionExtensions.cs` (extend, 002's
  `AddHomonMonitoring` method)
- `src/Homon.Infrastructure/Homon.Infrastructure.csproj`, `Directory.Packages.props`
  (extend — two new package pins, see "Current state")
- `src/Homon.Api/Endpoints/ProbeEndpoints.cs` (extend, 002's file — also lift 002's
  `kind == "http"` rejection, see Step 5)
- `src/Homon.Web/src/lib/probes.ts` (extend, 002's file)
- `src/Homon.Web/src/pages/admin-probes-page.tsx` (extend)
- `docs/ARCHITECTURE.md`, `docs/MODULES.md`, `src/Homon.Domain/Monitoring/README.md`,
  `docs/design-brief.md` (if the probe form needs a line beyond what's already there —
  check first)
- Tests: `tests/Homon.Api.Tests/HttpProbeRunnerTests.cs`, `StubHttpMessageHandler.cs`
  (create); `ProbeEndpointTests.cs` (extend, 002's file); `admin-probes-page.test.tsx`
  (extend, 002's file); `src/Homon.Web/e2e/admin.spec.ts` (extend, if present after 002)

**Not in scope, and not edited by this plan's executor**: `plans/README.md` — the reviewer
maintains the index for this run (see the executor-instructions banner above); do not touch
it, including the 003 status row.

**Out of scope**: `IProbeRunner`/`ProbeResult`/scheduler/state machine/uptime/ping
retention (002); SMB (004) and SNMP (005) runners and options (later plans — copy this
plan's pattern by name); any `className`/styling (plan 012 owns it); alerts (009) — a
failed observation is this plan's job, mailing anyone about it is not.

## Steps

### Step 1: Confirm the plan 002 contract

Read `Probe.cs`, `IProbeRunner.cs`, `ProbeResult`, `ProbeEndpoints.cs` as they actually
landed. Note any name differing from the contract table and use the real name throughout.

**Verify**: the four types exist and `dotnet build Homon.sln` succeeds. Missing any → STOP.

### Step 2: Domain — `HttpProbeOptions` and `HttpCredential`

Create `HttpCredential.cs` (`HttpCredentialType { None, Bearer, Basic }`, `Type`,
`Username` (plain, basic-only), `ProtectedSecret` (encrypted, never plaintext — doc-comment
pointing at `ISecretProtector`)) and `HttpProbeOptions.cs` (`HttpProbeMethod { Head, Get }`,
`Method`, `Path` (no leading slash, e.g. `"api/health"`), `UseHttps`,
`IgnoreCertificateErrors`, `TimeoutSeconds` (`int`, default `10` — Decision 4a),
`ExpectedStatusCode`/`ExpectedStatusCodeNegate`, `ExpectedBodyText`/`ExpectedBodyTextNegate`,
`Credential`) per the shapes in "Decisions". Add `public HttpProbeOptions? HttpOptions { get;
set; }` to `Probe.cs`.

**Verify**: `dotnet build src/Homon.Domain` → 0 errors/warnings.

### Step 3: Persistence — owned-jsonb mapping and the migration

In `ProbeConfiguration.cs`, add (with the rejected-alternatives comment from Decision 1):

```csharp
builder.OwnsOne(p => p.HttpOptions, http =>
{
    http.ToJson();
    http.OwnsOne(o => o.Credential);
});
```

Generate the migration (command above). Read it: expect a single additive
`migrationBuilder.AddColumn<string>(name: "HttpOptions", table: "Probes", type: "jsonb",
nullable: true)` (names may differ if 002 diverged). If EF instead tries to alter or
recreate unrelated columns from 002's migration, STOP — the model snapshot disagrees with
what 002 shipped.

**Verify**: `dotnet run --project src/Homon.Api -- migrate` → `applied: all of them.`;
`psql`'s `\d "Probes"` shows `"HttpOptions" jsonb`.

### Step 4: Infrastructure — packages, the secret protector and the runner

Add the two packages from Decision 3/"Current state" to `Homon.Infrastructure.csproj` and
`Directory.Packages.props` first — the rest of this step will not compile without them.

Create `ISecretProtector.cs`/`DataProtectionSecretProtector.cs` per Decision 2; register
`services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>()`.

Create `HttpProbeRunner.cs` implementing `IProbeRunner`: `Kind => ProbeKind.Http`;
`RunAsync` builds the request from `HttpOptions` (composed URI, method, credential —
unprotecting inside `try/catch (CryptographicException)`), sends it through the named
client with a linked
`CancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(probe.HttpOptions.TimeoutSeconds))`
(Decision 4a — the probe's own configured value, not a runner constant), reads at most
`MaxBodyBytes`, evaluates status/body per Decision 4's combination table, measures elapsed
with a `Stopwatch`, and maps every failure mode to the Detail table. Register the named
`HttpClient` (Decision 4) and `services.AddScoped<IProbeRunner, HttpProbeRunner>();` inside
002's own `AddHomonMonitoring` method in `InfrastructureServiceCollectionExtensions.cs` — it
already registers `PingProbeRunner` the same way (scoped, so `HttpProbeRunner`'s scoped
`ISecretProtector` dependency resolves cleanly).

**Verify**: `dotnet build Homon.sln --configuration Release` → 0 errors/warnings.

### Step 5: API — wire types and validation on `ProbeEndpoints.cs`

**Lift 002's `kind` rejection for `"http"` first.** 002's own validation (its Decision 8)
accepts only `"ping"` and 400s every other value — including `"http"` — with a detail
noting the kind "ships with a later plan"; 002's own `ProbeEndpointTests` has a case
asserting exactly that. Remove `"http"` from that rejection (leave `"smb"`/`"snmp"`
rejected — 004/005 do not run this session) and update that test case to expect success
instead of a 400.

Extend 002's own `ProbeRequest`/`ProbeResponse` records (not new types) with an optional
`Http` object (write-only credential per Decision 2). Validate with
`TypedResults.ValidationProblem`: `kind == "http"` requires `http` present, any other kind
requires it absent; `http.path` non-empty and without `"://"`; `method == "head"` +
non-null `expectedBodyText` → 400; `credential.type == "basic"` requires non-empty
`username`; `http.timeoutSeconds`, when present, must be `1`–`25` inclusive (Decision 4a) —
`0` and `26` are both 400s. On create, an omitted `timeoutSeconds` defaults to `10` before
saving; on update, an omitted `timeoutSeconds` keeps the probe's current value (the same
"absent = keep" rule Decision 2 already uses for the secret — do not reset it to `10`). On
write, `Protect()` the secret per the write-only rules; on read, emit `hasSecret` only,
never the value.

**Verify**: `dotnet build Homon.sln --configuration Release` → 0 errors.

### Step 6: SPA — the HTTP fieldset and docs

Extend `ProbeForm` in `admin-probes-page.tsx`: 002's Decision 10 already built the per-kind
fieldset region as a conditional block keyed on the selected kind (nothing renders inside it
for `Ping`) — fill that region for `"http"` rather than introducing a new
`<fieldset><legend>HTTP options</legend>` pattern next to it. Fields: method (HEAD/GET),
path, a labelled number input "Timeout (seconds)" (`min={1}`, `max={25}`, defaulting to
`10` on create — Decision 4a), `useHttps` checkbox, "Skip certificate validation
(self-signed certificates)" checkbox, expected status + negate, expected body text +
negate, credential type (None/Bearer/Basic) with matching fields, and on edit a "Replace
credential" affordance —
never a pre-filled secret (it isn't in the response). No `className`; semantic
`<fieldset>`/`<legend>`/`<label>` only (`CLAUDE.md`, `docs/ARCHITECTURE.md` §3.10). If the
landed page has no such conditional region at all, that is drift from 002 — see "STOP
conditions" rather than inventing the pattern here.

Extend `lib/probes.ts` with matching TypeScript types, doc-commented to the C# wire type,
following `lib/meta.ts:5-14`. Update `docs/ARCHITECTURE.md` (§3.17), `docs/MODULES.md`,
`src/Homon.Domain/Monitoring/README.md`. Do not edit `plans/README.md` — the reviewer
maintains it for this run.

**Verify**: `npm --prefix src/Homon.Web run build` (typecheck) → 0 errors.

## Test plan

**xunit**
- `HttpProbeRunnerTests` (pure): `StubHttpMessageHandler : HttpMessageHandler` overriding
  `SendAsync` to script a response or throw, wrapped in an `HttpClient` an `IHttpClientFactory`
  stub returns for `HttpProbeRunner.HttpClientName`. Cases (Decision 4's combination table,
  resolved by the maintainer): 200 with no `ExpectedStatusCode` → success, `"HTTP 200"`; 204
  with no `ExpectedStatusCode` → success, `"HTTP 204"` (2xx generally, not just 200, is the
  implicit rule); a redirect followed to a final 200 (`AllowAutoRedirect`, Decision 4) →
  success, `"HTTP 200"`; 404 with no `ExpectedStatusCode` → failure, `"HTTP 404"`; 503 with
  no `ExpectedStatusCode` → failure, `"HTTP 503"`; `ExpectedStatusCode: 401` configured,
  actual 401 → success (the configured rule replaces the 2xx rule, so a probe can
  deliberately expect an auth challenge); `ExpectedStatusCode: 200,
  ExpectedStatusCodeNegate: true` configured, actual 200 → failure (negated match); body
  match; negated body mismatch; status passes but body fails → the body's own `Detail`, not
  the status `Detail`; a delayed response past a probe seeded with `HttpOptions.TimeoutSeconds
  = 1` (the domain-validated minimum — the real wait is ~1 s, not 10 s) →
  `"timeout after 1 s"`; a body past `MaxBodyBytes` with the expected text beyond the cap →
  mismatch; bearer sets `Authorization: Bearer …`; basic sets base64 `Authorization: Basic
  …`; a fake `ISecretProtector.Unprotect` throwing `CryptographicException` →
  `Succeeded == false`, `"credentials unreadable — re-enter them"`, no exception escapes
  `RunAsync`. (HEAD+body-expectation is an API-layer 400 — cover it in `ProbeEndpointTests`,
  not here.)
- `ProbeEndpointTests` (extend, `[DatabaseFact]`/`ApiDatabaseFactory` pattern): update 002's
  own `kind: "http"` → 400 ("not yet supported") case to assert success instead (Step 5's
  lift), leaving its `"smb"`/`"snmp"` → 400 cases untouched; Http probe round-trips `http`
  options; `timeoutSeconds` omitted on create → stored as `10`; `timeoutSeconds: 0` and
  `timeoutSeconds: 26` → 400 (Decision 4a's `1`–`25` bound); `timeoutSeconds` omitted on
  update keeps the probe's current value; response never contains `protectedSecret` or
  plaintext, only `hasSecret`; update omitting `credential.secret` keeps `hasSecret: true`;
  update with `credential.secret: ""` clears it; 400s for missing/extra `http` object,
  HEAD+body expectation, `path` containing `"://"`, `basic` with no username; anonymous
  write 401, API-key write 403 (pattern: `ReaderPolicyTests`/`ApiKeyAuthenticationTests`).

**Vitest**
- Extend `admin-probes-page.test.tsx`: selecting kind "http" reveals the HTTP fieldset and
  hides others; submit sends the right body via `stubFetch`; credential section never
  pre-fills a secret, shows "Replace credential" only when `hasSecret` is true.

**Playwright**
- Extend `e2e/admin.spec.ts` if present post-002: create an HTTP probe (paused, mirroring
  002's own use of paused ping probes for determinism) through the admin form, confirm it
  appears in the list — form interaction only, no live-poll assertion. Run
  `expectNoHorizontalOverflow`/`expectTappable` on the new fieldset at both viewports.

## Done criteria

- [ ] `dotnet build Homon.sln --configuration Release` → 0 warnings/errors
- [ ] `dotnet run --project src/Homon.Api -- migrate` applies `AddHttpProbeOptions` cleanly
- [ ] New xunit tests exist and pass: `HttpProbeRunnerTests`, the `ProbeEndpointTests` additions
- [ ] `npm --prefix src/Homon.Web run build` exits 0
- [ ] New/extended Vitest tests pass
- [ ] `grep -rn "ProtectedSecret" src/Homon.Api/Endpoints/ProbeEndpoints.cs` shows it is
      never read into a response DTO (only `hasSecret` derivations)
- [ ] `./ci/run-ci.sh` → `PASS — web api e2e`, 0 skipped api tests
- [ ] `docs/ARCHITECTURE.md` §3.17 (or next free number) added; `docs/MODULES.md` and
      `src/Homon.Domain/Monitoring/README.md` updated
- [ ] No files outside "Scope" modified (`git status`) — `plans/README.md` is not one of them;
      the reviewer updates it after merging

## STOP conditions

- Plan 002 has not landed, or `IProbeRunner`/`ProbeResult`/a per-kind options mechanism
  does not exist in any form.
- 002's `ProbeEndpoints.cs` still rejects `kind == "http"` after Step 5's lift, or a
  `dotnet build` failure traces back to `IDataProtectionProvider`/`AddHttpClient` not
  resolving after the two packages from Decision 3/4 are added — that means the package
  assumptions in this plan no longer hold against what actually landed; do not work around
  it with a different package or a hand-rolled substitute without recording why, the same
  way Decision 1 records its own rejected alternatives.
- The generated migration touches anything beyond an additive `HttpOptions` column.
- `OwnsOne(...).ToJson()` does not behave as expected against
  Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3 (verify with a scratch migration before
  writing application code around it) — do not silently fall back to flat nullable columns
  without recording the same rationale as Decision 1 for the fallback.
- `IDataProtectionProvider` is not actually resolvable without the `KeyRingPath` branch
  (contradicting Decision 3) — add an unconditional `builder.Services.AddDataProtection()`
  above that block with a comment, rather than skipping secret protection.
- 002's landed `MonitoringOptions.PerPollTimeout` default is not `30` seconds — Decision 4a's
  `HttpProbeOptions.TimeoutSeconds` upper bound of `25` assumes it is. Report the real
  default and use "5 s under whatever 002 shipped" as the new upper bound (in Decision 4a,
  Step 2's domain validation and Step 5's API validation) instead of silently keeping `25`.
- A step's verification fails twice after a reasonable fix attempt.
- `admin-probes-page.tsx`'s landed shape has no `ProbeForm`, or no per-kind conditional
  fieldset region, at all — reconcile with what 002's executor actually shipped before
  writing the HTTP fieldset, and report what you found (do not invent the conditional-block
  pattern yourself; 004/005 will copy whatever shape lands here, later, not this session).

## Maintenance notes

- **004 (SMB) and 005 (SNMP) must reuse, by name**: `ISecretProtector` /
  `DataProtectionSecretProtector` (one purpose string, `"Homon.Secrets.v1"`, for every
  probe secret — no second purpose per kind), the owned-type-per-kind `.ToJson()` pattern
  (`SmbProbeOptions`, `SnmpProbeOptions`), and the write-only credential wire semantics
  (absent = keep, `""` = clear, non-empty = replace).
- `IgnoreCertificateErrorsOption` is specific to `HttpProbeRunner`'s named client — a later
  `HttpClient` elsewhere does not inherit this callback automatically.
- `HttpProbeOptions.TimeoutSeconds` is per-probe (Decision 4a), not a runner constant —
  raising its upper bound above `25` needs re-checking against 002's own
  `MonitoringOptions.PerPollTimeout` first (see the matching STOP condition) so a probe's
  own timeout can never race the scheduler's outer one.
- A reviewer should check: no code path serialises `ProtectedSecret` into a response; the
  `CryptographicException` catch is the *only* place a lost key ring surfaces (must not
  throw through the scheduler); the jsonb column round-trips `null` cleanly for non-HTTP
  probes (a Ping probe must never gain a stray `HttpOptions` document); every status
  evaluation checks the status rule before the body rule, not the reverse (Decision 4).
- Deferred: an admin "test this probe now" button; HTTP methods beyond HEAD/GET.
