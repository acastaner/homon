# 005 — SNMP probe scaffold

> **Executor instructions**: Follow this plan step by step. Run every verification command
> and confirm the expected result before moving on. If anything in "STOP conditions"
> occurs, stop and report — do not improvise. When done, update this plan's row in
> `plans/README.md` unless a reviewer told you they maintain the index.
>
> **First step**: confirm plan 002 landed with the shape in "Contract assumed", and that
> plan 003's `ISecretProtector`, owned-jsonb pattern and write-only secret wire semantics
> landed as `plans/003-http-probe.md` describes — this plan reuses all three by name and does
> not redefine them. **STOP if either is missing.**
>
> **Drift check (run first)**:
> `git diff --stat f4e7261..HEAD -- src/Homon.Domain/Monitoring src/Homon.Infrastructure/Monitoring src/Homon.Infrastructure/Security src/Homon.Infrastructure/Persistence src/Homon.Api/Endpoints/ProbeEndpoints.cs src/Homon.Api/Program.cs src/Homon.Web/src/pages/admin-probes-page.tsx src/Homon.Web/src/lib docs/ARCHITECTURE.md docs/MODULES.md docs/deployment-runbook.md Directory.Packages.props`
> 002, 003 and (if it landed first) 004 legitimately touch shared files (`Program.cs`,
> `HomonDbContext.cs`, `ProbeConfiguration.cs`, `ProbeEndpoints.cs`, `admin-probes-page.tsx`,
> `Directory.Packages.props`) — expected, not drift. Compare only the regions named in
> "Current state" / "Scope"; a change elsewhere in the same file is not a STOP condition.

## Status

- **Priority**: P3
- **Effort**: S
- **Risk**: LOW — smallest monitoring plan; one outbound UDP call behind a seam, no new
  secret or persistence pattern
- **Depends on**: `plans/002-monitoring-core-groups-and-ping.md`,
  `plans/003-http-probe.md` (both must land first)
- **Category**: direction
- **Planned at**: commit `f4e7261`, 2026-09-15

## Context

`docs/MODULES.md` (verbatim): "**SNMP.** Candidate: `Lextm.SharpSnmpLib`. Scaffold the
options and the runner's interface; implement `GET` of one OID only in the first phase." The
module table row: "Community/version + OID; shape only in the first phase."
`src/Homon.Domain/Monitoring/README.md:26`: "**SNMP** takes community/version and an OID;
first phase records the shape only."

The brief is ambiguous between "shape only" and "implement `GET` of one OID". This plan
resolves it: **build both** — options, validation, persistence, an admin fieldset, and a
runner that performs one real SNMP `GET` of one admin-configured OID. Everything past that
single `GET` (value matching, walks, traps, MIB loading, v3) is out of scope — see Decision 2.

This is the fourth probe kind and reuses, without redesigning, what 003 established as the
pattern for every kind after HTTP: the owned-jsonb-per-kind options column, the
`ISecretProtector` purpose string, and the write-only secret wire semantics. `plans/README.md`
lists 004 (SMB) before this plan; if it landed first its `SmbProbeOptions` addition is more
precedent to skim, but this plan depends only on 002 and 003.

## Contract assumed

From plan 002 (confirm each exists; if 002 named something differently, use the real name
throughout): `Probe` (`Guid Id`, `string Name`, `string Host`, poll interval, failure
threshold, `IsPaused`, `Position`, `ProbeKind Kind`) in `Homon.Domain/Monitoring/Probe.cs`;
`ProbeKind` (`Ping`, `Http`, `Smb`, `Snmp` reserved); `IProbeRunner` (`ProbeKind Kind { get;
}`, `Task<ProbeResult> RunAsync(Probe, CancellationToken)`) in
`Homon.Infrastructure/Monitoring/IProbeRunner.cs`; `ProbeResult` (`record ProbeResult(bool
Succeeded, double? LatencyMs, string? Detail)`); `ProbeEndpoints`
(`internal static class ProbeEndpoints`, `MapProbeEndpoints(this RouteGroupBuilder)`,
registered where `Program.cs:477` has `v1.MapProbeEndpoints();` commented out); a `ProbeForm`
in `admin-probes-page.tsx` with a kind selector and per-kind fieldsets.

From plan 003, reused by name, not redefined here: `ISecretProtector`
(`Protect(string)`/`Unprotect(string)`, purpose `"Homon.Secrets.v1"`, `Unprotect` throws
`CryptographicException` on a lost key ring) in `Homon.Infrastructure/Security/`; the
owned-jsonb-per-kind pattern (`public XxxOptions? XxxOptions { get; set; }` on `Probe`,
mapped `builder.OwnsOne(p => p.XxxOptions, x => x.ToJson())` in `ProbeConfiguration.cs`); the
write-only secret wire rule (write: absent/null → keep, `""` → clear where optional,
non-empty → `Protect()` and replace; read: `hasSecret: bool` only, never the value).

## Decisions

**1. The library: `Lextm.SharpSnmpLib`, verified.** Latest stable on NuGet is **12.5.7**
(published 2025-11-03), licence **MIT** (`licenseExpression: "MIT"`) — permissive and
compatible with Homon's own MIT licence, so no README acknowledgement section is warranted
(unlike `docs/MODULES.md:64`'s note on SMBLibrary's LGPL). Verified read-only against
`https://api.nuget.org/v3/registration5-semver1/lextm.sharpsnmplib/12.5.7.json`. The package
ships `lib/net8.0`/`lib/net471`, not `net10.0` — expected to resolve fine under `net10.0`
(only the declared API surface is fixed to the TFM), but **Step 1 must confirm with a real
`dotnet build`**, not assume it.

**2. Scope: both the shape and one working `GET`.** In scope: `SnmpProbeOptions` (persisted,
validated, editable) and `SnmpProbeRunner : IProbeRunner` performing one `GET` of the
configured OID, mapped to a `ProbeResult`. **Deferred** — record each in the module README:
expected-value matching/thresholds (no SNMP analogue to HTTP's negatable status/body yet);
`WALK`/`GETBULK`; inbound traps (a listener, a different kind of component than a poller);
MIB loading/resolution (Detail shows the raw OID and value, never a friendly name — Decision
6); SNMPv3 (Decision 3); an admin-configurable timeout (a constant this phase, like 003's
HTTP timeout).

**3. `SnmpVersion { V1, V2c, V3 }` — reserved, but `V3` is refused at validation.** Carrying
all three now makes a later v3 plan an additive validation change, not a migration.
`ProbeEndpoints.cs` 400s when `version == "v3"`, naming it unsupported. *Why refuse rather
than build it*: SNMPv3's USM model needs a second secret shape (`authKey`, `privKey`,
protocols) plus key-localisation against the agent's `engineID`/`engineBoots`/`engineTime`
(RFC 3414) — a discovery handshake before the `GET` itself, real work for its own plan. The
SPA's version `<select>` **omits** `V3` entirely (not shown disabled); the TS union type
keeps `'v3'` as a documented-reserved literal so a later plan needs only a new `<option>`.

**4. Community is a secret, same protector and purpose, but mandatory — "clear" is not a
valid transition.** SNMPv1/v2c authenticate with the community string alone (sent unencrypted
— it must never round-trip in a response). Store it via `ISecretProtector` under
`"Homon.Secrets.v1"`, the one purpose for every probe secret (003's Decision 2). Unlike
HTTP's *optional* credential, an Snmp probe cannot exist without a community, so the rule
narrows: **absent/null → keep**; **non-empty → `Protect()` and replace**; **`""` is a 400**
("community is required"), not a clear. `SnmpProbeOptions.ProtectedCommunity` is `string`
(never null) once persisted; the response DTO exposes `hasCommunity: true` always.
`SnmpProbeRunner` unprotects inside `try { } catch (CryptographicException)`, returning the
same `"credentials unreadable — re-enter them"` Detail 003 defined for HTTP.

**5. OID validation: dotted-numeric only, capped length.** `Oid: string`, validated against
`^\d+(\.\d+)+$` (rejects symbolic names like `sysUpTime.0` — no MIB loading means no way to
resolve one) and a max length of 256 characters. The admin form's placeholder is
`1.3.6.1.2.1.1.3.0 (sysUpTime)` with a hint sentence — illustrative only, **never persisted**
as a default; an empty OID on save is a 400, like an empty `Path` in 003.

**6. Detail format: `"{oid} = {value}"`, no friendly names, truncated.** The brief's own
example (`sysUpTime = 12d 03:14`) reads as a resolved name, but MIB loading is deferred
(Decision 2) and is disproportionate work for a scaffold. Resolve the tension with
`SnmpProbeRunner.MaxDetailValueLength = 64` and `Detail = $"{options.Oid} = {value}"`, where
`value` is the returned `ISnmpData`'s own `ToString()` —
`Lextm.SharpSnmpLib.TimeTicks.ToString()` already renders a human duration close to the
brief's example, no MIB work needed. **Step 4 must confirm the exact string SharpSnmpLib
produces for a `TimeTicks` value** and adjust the length cap so a value is never truncated
mid-number; do not build name resolution to chase the brief's exact wording.

**7. `ISnmpClient` seam, mirroring `HttpProbeRunner`'s testability shape.** Unit tests must
never open a real UDP socket:

```csharp
public interface ISnmpClient
{
    // Verify against the installed Lextm.SharpSnmpLib 12.5.7 API before implementing
    // (IntelliSense/ILSpy) — this is the advisor's best guess at the Messaging namespace's
    // shape, not a verified fact. Do not force an overload that doesn't compile.
    Task<SnmpGetResponse> GetAsync(
        IPEndPoint endpoint, string community, SnmpVersion version, string oid,
        CancellationToken cancellationToken);
}

// Succeeded is false only for a transport/protocol failure. A noSuchObject/noSuchInstance/
// endOfMibView value is still a completed response (Succeeded == true, Value set to the
// marker type) — SnmpProbeRunner, not the client, turns that into a failed observation.
public sealed record SnmpGetResponse(bool Succeeded, string? FailureDetail, ISnmpData? Value);
```

`SharpSnmpClient : ISnmpClient` wraps the real call (likely `Messenger.GetAsync`, under
`Messaging` — verify the exact member) behind a linked
`CancellationTokenSource.CancelAfter(RequestTimeout)`, matching
`ResendEmailSender.cs:14,44-45`. `SnmpProbeRunnerTests` stub `ISnmpClient` directly — no
socket — one seam higher than 003's `StubHttpMessageHandler` (no shared-client concept to
preserve for UDP, so the interface is the whole seam).

**8. Success mapping**, per the brief's constraint (a completed response, no error status,
value not one of the three SNMP exception types):

| Situation | `Succeeded` | Detail |
| --- | --- | --- |
| Ordinary value | `true` | `"{oid} = {value}"` (Decision 6) |
| Timeout | `false` | `"no response (timeout after {N} s)"` |
| Error status (v1 `GetResponse`, non-zero `ErrorStatus`) | `false` | `"SNMP error: {status}"` |
| `NoSuchObject` / `NoSuchInstance` / `EndOfMibView` | `false` | `"OID not found"` — same practical meaning for a scalar `GET`, one message for all three |
| Connection failure (`SocketException` etc.) | `false` | `"connection failed: {exception.Message}"` |
| Community unreadable | `false` | `"credentials unreadable — re-enter them"` |

**9. In-process UDP integration test: deferred, not built.** Considered binding a real UDP
listener on localhost with SharpSnmpLib's own agent/engine types to exercise
`SharpSnmpClient` end-to-end. Rejected: that engine needs its own MIB object store and
lifecycle inside the test process — extra surface a scaffold shouldn't carry, and a
flakiness risk the gate cannot tolerate (`ci/run-ci.sh api` fails on any skip; a
sometimes-timing-out test is worse than none). The `ISnmpClient` seam already covers the only
logic worth testing — the response-to-`ProbeResult` mapping. Not a STOP condition.

## Defaults taken (change before implementation if wanted)

- `Port` defaults to `161` but is an editable per-probe field (1–65535) — agents on
  non-standard ports are common enough on a home network to warrant a field, unlike HTTP's
  fixed timeout.
- `SnmpProbeRunner.RequestTimeout = TimeSpan.FromSeconds(5)`, a constant like 003's HTTP 10s
  — shorter because SNMP is one UDP round trip with no TLS/redirect chain. Not
  admin-configurable this phase; constructor-injectable for tests.
- `SnmpProbeRunner.MaxDetailValueLength = 64` (Decision 6) — revisit after Step 4 confirms
  `TimeTicks.ToString()`'s actual output.
- Migration name `AddSnmpProbe`, layered on 002's `AddMonitoring` migration (additive
  alongside 003's `AddHttpProbeOptions` / 004's SMB migration if either already landed).

## Current state

- `src/Homon.Api/Program.cs:212-226` — the Data Protection block 003 wires unconditionally
  into `AddHomonInfrastructure`; this plan adds no new registration there beyond the
  `ISnmpClient`/runner DI (Step 4). `Program.cs:477` — the commented
  `v1.MapProbeEndpoints();`; not touched, only what it validates.
- `src/Homon.Api/Authentication/HomonPolicies.cs:17-26` — `Reader` for reads, `Administrator`
  for writes; no new policy needed. `AuthenticationEndpoints.cs:12-19` — the CSRF posture
  (mutating endpoints require `application/json`); this plan's writes follow it unchanged.
- `src/Homon.Infrastructure/Email/ResendEmailSender.cs:14,44-45` — the linked
  `CancellationTokenSource.CancelAfter` timeout pattern `SnmpProbeRunner` follows.
- `src/Homon.Web/src/lib/meta.ts:5-14` — the `lib/*.ts` shape `lib/probes.ts`'s Snmp addition
  follows.
- `src/Homon.Web/src/pages/admin-probes-page.tsx` — today a placeholder (`<p>Not implemented
  yet — the Monitoring module (plan 002) adds probes here.</p>`); by execution time it is
  002/003's real `ProbeForm`. This plan adds one more conditional fieldset to it.
- `Directory.Packages.props:39-42` — the comment recording that probe libraries "are
  deliberately absent: they arrive with the module that uses them"; this plan adds
  `Lextm.SharpSnmpLib`.
- `compose.prod.yaml:145-151` — the `sysctls: net.ipv4.ping_group_range` comment for ICMP;
  SNMP needs no socket privilege, only outbound UDP — see Step 6 for the runbook note.
- `docs/ARCHITECTURE.md` ends at §3.12 today; 002 is expected to add §3.13 and 003 §3.14 —
  **verify the next free number before writing**, do not assume §3.15.
- `docs/deployment-runbook.md:76-82` — the "## ICMP" section's shape (one short paragraph,
  what to check if it fails) is the pattern for this plan's "## SNMP" addition.
- `plans/README.md:13` — `| 005 | planned | SNMP probe scaffold |`.

## Commands you will need

| Purpose | Command | Expected on success |
| --- | --- | --- |
| Full gate | `./ci/run-ci.sh` | `PASS — web api e2e`, 0 skipped api tests |
| API only | `./ci/run-ci.sh api` | same, api suite |
| Web only | `./ci/run-ci.sh web` | `npm ci`, lint, build (=typecheck), vitest all pass |
| e2e only | `./ci/run-ci.sh e2e` | Playwright, two viewports |
| Release build (format gate) | `dotnet build Homon.sln --configuration Release` | 0 warnings/errors |
| New migration | `HOMON_DESIGNTIME_CONNECTION='Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x' dotnet dotnet-ef migrations add AddSnmpProbe --project src/Homon.Infrastructure --startup-project src/Homon.Api --output-dir Persistence/Migrations` | migration files created |
| Apply locally | `dotnet run --project src/Homon.Api -- migrate` | `applied     : all of them.` |
| One xunit class | `dotnet test tests/Homon.Api.Tests --filter FullyQualifiedName~SnmpProbeRunnerTests` | all pass |
| One Vitest file | `npm --prefix src/Homon.Web run test -- admin-probes-page` | all pass |
| Confirm package resolves | `dotnet build src/Homon.Infrastructure --configuration Release` after adding the `PackageReference` | 0 errors — proves the `net8.0`-targeted package loads under `net10.0` |

## Scope

**In scope**:
- `src/Homon.Domain/Monitoring/SnmpProbeOptions.cs`, `SnmpVersion.cs` (create)
- `src/Homon.Domain/Monitoring/Probe.cs` (extend — add `SnmpOptions`; 002's file)
- `src/Homon.Infrastructure/Monitoring/ISnmpClient.cs`, `SharpSnmpClient.cs`,
  `SnmpProbeRunner.cs` (create)
- `src/Homon.Infrastructure/Persistence/Configurations/ProbeConfiguration.cs` (extend, 002's file)
- `src/Homon.Infrastructure/Persistence/Migrations/*AddSnmpProbe*` (create, `dotnet ef`)
- `src/Homon.Infrastructure/InfrastructureServiceCollectionExtensions.cs` (extend)
- `src/Homon.Api/Endpoints/ProbeEndpoints.cs` (extend, 002's file)
- `Directory.Packages.props` (extend — add `Lextm.SharpSnmpLib`)
- `src/Homon.Web/src/lib/probes.ts` (extend, 002/003's file)
- `src/Homon.Web/src/pages/admin-probes-page.tsx` (extend)
- `docs/ARCHITECTURE.md`, `docs/MODULES.md`, `src/Homon.Domain/Monitoring/README.md`,
  `docs/deployment-runbook.md`, `plans/README.md`
- Tests: `tests/Homon.Api.Tests/SnmpProbeRunnerTests.cs`, `StubSnmpClient.cs` (create);
  `ProbeEndpointTests.cs` (extend, 002's file); `admin-probes-page.test.tsx` (extend, 002's
  file); `src/Homon.Web/e2e/admin.spec.ts` (extend, if present after 002)

**Out of scope**: `IProbeRunner`/`ProbeResult`/scheduler/state machine (002); HTTP (003) and
SMB (004) runners and options; `ISecretProtector` itself (003 — this plan only consumes it);
value matching, thresholds, walks, traps, MIB loading, SNMPv3 (Decisions 2/3); any
`className`/styling (plan 012); alerts (009).

## Steps

### Step 1: Confirm the plan 002/003 contracts, add the package

Read `Probe.cs`, `IProbeRunner.cs`, `ProbeResult`, `ProbeEndpoints.cs`, `ISecretProtector.cs`
as they actually landed; use real names if 002/003 diverged from "Contract assumed". Add
`Lextm.SharpSnmpLib` at `12.5.7` to `Directory.Packages.props` (near the comment at lines
39-42) and a `PackageReference` (no `Version` — CPM) to `Homon.Infrastructure`.

**Verify**: `dotnet build src/Homon.Infrastructure --configuration Release` → 0
errors/warnings. Missing any of the 002/003 types → STOP.

### Step 2: Domain — `SnmpVersion` and `SnmpProbeOptions`

Create `SnmpVersion.cs` (`enum SnmpVersion { V1, V2c, V3 }`, doc comment noting `V3` is
reserved and refused per Decision 3) and `SnmpProbeOptions.cs` (`Version`, `Oid` (Decision
5), `Port` (default 161), `ProtectedCommunity` (never null once persisted — Decision 4)). Add
`public SnmpProbeOptions? SnmpOptions { get; set; }` to `Probe.cs`.

**Verify**: `dotnet build src/Homon.Domain` → 0 errors/warnings.

### Step 3: Persistence — owned-jsonb mapping and the migration

In `ProbeConfiguration.cs`, add (referencing, not duplicating, 003's rejected-alternatives
comment for the same pattern):

```csharp
builder.OwnsOne(p => p.SnmpOptions, snmp => snmp.ToJson());
```

Generate the migration. Read it: expect one additive
`migrationBuilder.AddColumn<string>(name: "SnmpOptions", table: "Probes", type: "jsonb",
nullable: true)`. If EF touches anything from 002's or 003's migrations, STOP.

**Verify**: `dotnet run --project src/Homon.Api -- migrate` → `applied: all of them.`;
`psql`'s `\d "Probes"` shows `"SnmpOptions" jsonb`.

### Step 4: Infrastructure — the SNMP client seam and the runner

Create `ISnmpClient.cs` per Decision 7, verifying the real call signature against the
resolved package before writing `SharpSnmpClient` — do not guess an overload that doesn't
compile. Confirm `TimeTicks.ToString()`'s exact output (Decision 6) with a throwaway check
against a hand-built value, and set `MaxDetailValueLength` accordingly.

Create `SnmpProbeRunner.cs` implementing `IProbeRunner`: `Kind => ProbeKind.Snmp`; `RunAsync`
unprotects the community inside `try/catch (CryptographicException)`, calls
`ISnmpClient.GetAsync` with a linked `CancellationTokenSource.CancelAfter(_timeout)`
(constructor-injectable, default `RequestTimeout`), measures elapsed with a `Stopwatch`, maps
per Decision 8. Register `SharpSnmpClient` and `SnmpProbeRunner` in
`InfrastructureServiceCollectionExtensions.cs` matching how 003 registered `HttpProbeRunner`.

**Verify**: `dotnet build Homon.sln --configuration Release` → 0 errors/warnings.

### Step 5: API — wire types and validation on `ProbeEndpoints.cs`

Add an optional `Snmp` object to the create/update request and response records. Validate
with `TypedResults.ValidationProblem`: `kind == "snmp"` requires `snmp` present, any other
kind requires it absent; `version == "v3"` → 400; `oid` matches `^\d+(\.\d+)+$`, ≤256 chars;
`port` in 1–65535; `community` absent on create → 400, `""` on create or update → 400
("community is required"), non-empty → `Protect()` and store. On read, emit `hasCommunity:
true`, never the value.

**Verify**: `dotnet build Homon.sln --configuration Release` → 0 errors.

### Step 6: SPA and docs — the SNMP fieldset

Extend `ProbeForm` with an SNMP section shown only when kind is `"snmp"`, mirroring 003's
HTTP fieldset: version select (`V1`/`V2c` only), community input (write-only, "Replace
community" affordance, never pre-filled), OID text input with the placeholder and hint from
Decision 5, port number input defaulting to `161`. No `className`; semantic
`<fieldset>`/`<legend>`/`<label>` only.

Extend `lib/probes.ts` with matching TS types, doc-commented to the C# wire type. Update
`docs/ARCHITECTURE.md` (verify the next free `§3.x`), `docs/MODULES.md` and
`src/Homon.Domain/Monitoring/README.md` (mark 005 delivered, list Decision 2's deferred
items), `plans/README.md`'s 005 row, and add a short `## SNMP` section to
`docs/deployment-runbook.md` after `## ICMP` (`:76-82`'s pattern): outbound UDP/161 needs no
published port and no sysctl, but a host with an outbound firewall may need an egress rule —
note how to tell that apart from an unreachable agent (watch the probe's Detail for `"no
response (timeout after 5 s)"` versus `"connection failed: …"`).

**Verify**: `npm --prefix src/Homon.Web run build` (typecheck) → 0 errors.

## Test plan

**xunit**
- `SnmpProbeRunnerTests` (pure): `StubSnmpClient : ISnmpClient` scripting a response or a
  throw. Cases: ordinary value → success, `"{oid} = {value}"`; a `TimeTicks` value → Detail
  matches Step 4's confirmed format, untruncated; timeout → `"no response (timeout after 5
  s)"`; `NoSuchObject`/`NoSuchInstance`/`EndOfMibView` → `"OID not found"`; non-zero
  `ErrorStatus` → `"SNMP error: {status}"`; a thrown exception → `"connection failed:
  {message}"`; a fake `ISecretProtector.Unprotect` throwing `CryptographicException` →
  `Succeeded == false`, `"credentials unreadable — re-enter them"`, no exception escapes
  `RunAsync`. (`v3` and OID-format rejection are API-layer 400s — cover in
  `ProbeEndpointTests`.)
- `ProbeEndpointTests` (extend, `[DatabaseFact]`/`ApiDatabaseFactory` pattern): Snmp probe
  round-trips `snmp` options; response never contains `protectedCommunity` or plaintext, only
  `hasCommunity: true`; update omitting `community` keeps the stored value; update with
  `community: ""` is a 400, not a clear; 400s for missing/extra `snmp` object, `version:
  "v3"`, a non-numeric OID, an OID over 256 characters, `port` outside 1–65535; anonymous
  write 401, API-key write 403 (pattern: `ReaderPolicyTests`/`ApiKeyAuthenticationTests`).

**Vitest**
- Extend `admin-probes-page.test.tsx`: selecting kind "snmp" reveals the fieldset and hides
  others; the version select offers only V1/V2c; submit sends the right body via `stubFetch`;
  community never pre-fills, shows "Replace community" only when editing.

**Playwright**
- Extend `e2e/admin.spec.ts` if present post-002: create an SNMP probe (paused, mirroring
  002/003) through the admin form, confirm it appears — form interaction only. Run
  `expectNoHorizontalOverflow`/`expectTappable` on the fieldset at both viewports.

## Done criteria

- [ ] `dotnet build Homon.sln --configuration Release` → 0 warnings/errors
- [ ] `dotnet run --project src/Homon.Api -- migrate` applies `AddSnmpProbe` cleanly
- [ ] New xunit tests exist and pass: `SnmpProbeRunnerTests`, the `ProbeEndpointTests` additions
- [ ] `npm --prefix src/Homon.Web run build` exits 0
- [ ] New/extended Vitest tests pass
- [ ] `grep -rn "ProtectedCommunity" src/Homon.Api/Endpoints/ProbeEndpoints.cs` shows it is
      never read into a response DTO (only `hasCommunity` derivations)
- [ ] `./ci/run-ci.sh` → `PASS — web api e2e`, 0 skipped api tests
- [ ] `docs/ARCHITECTURE.md` next free section added; `docs/MODULES.md`,
      `src/Homon.Domain/Monitoring/README.md` and `docs/deployment-runbook.md` updated
- [ ] `plans/README.md`'s 005 row is `DONE`
- [ ] No files outside "Scope" modified (`git status`)

## STOP conditions

- Plan 002 or 003 has not landed, or `ISecretProtector`/the owned-jsonb-per-kind pattern
  does not exist in any form.
- `Lextm.SharpSnmpLib` 12.5.7 does not resolve/build under `net10.0`, or its licence has
  changed from MIT since this plan was written (re-check the NuGet registration index) — a
  non-permissive or incompatible licence is a hard STOP; ask before substituting a library.
- The `ISnmpClient` signature this plan assumed (Decision 7) does not match the installed
  package's real API and the correct shape is not obvious from IntelliSense — stop and
  report the actual surface rather than forcing a mismatched call.
- The generated migration touches anything beyond an additive `SnmpOptions` column.
- A step's verification fails twice after a reasonable fix attempt.
- `admin-probes-page.tsx`'s post-002/003 shape has no `ProbeForm` at all — reconcile with
  what actually shipped before writing the fieldset, and report what you found.

## Maintenance notes

- **This plan is precedent for a fifth kind**, the way 003 is precedent for this one: reuse
  `ISecretProtector`, the owned-type-per-kind `.ToJson()` pattern, and the write-only wire
  semantics (adapted per Decision 4 if a future kind's secret is similarly mandatory).
- **SNMPv3 is a new plan, not an extension of this one** — a second secret shape and an
  engine-discovery handshake (Decision 3), not a field on `SnmpProbeOptions`.
- **The in-process UDP integration test was deliberately not built** (Decision 9). A later
  reviewer wanting transport-level coverage of `SharpSnmpClient` itself is new scope, not a
  gap in this plan's test count.
- **Value matching/thresholds are deferred** (Decision 2); when they land they should follow
  HTTP's negatable-pair shape for consistency, not a new pattern.
- A reviewer should check: `ProtectedCommunity` never serialises into a response; the
  `CryptographicException` catch is the only place a lost key ring surfaces; the jsonb column
  round-trips `null` cleanly for non-Snmp probes; the `v3` rejection message says what to do.
