# 004 — SMB/CIFS probe (managed client)

> **Executor instructions**: Follow this plan step by step. Run every verification command
> and confirm the expected result before moving on. If anything in "STOP conditions"
> occurs, stop and report — do not improvise. When done, update this plan's row in
> `plans/README.md` unless a reviewer told you they maintain the index.
>
> **First step**: read "Contract assumed" and confirm 002 and 003 landed with that shape
> (map names if they differ). **STOP if `IProbeRunner`, a per-kind options storage
> mechanism, or `ISecretProtector` do not exist in any form.**
>
> **Drift check (run first)**:
> `git diff --stat f4e7261..HEAD -- src/Homon.Domain/Monitoring src/Homon.Infrastructure/Monitoring src/Homon.Infrastructure/Security src/Homon.Infrastructure/Persistence src/Homon.Api/Endpoints/ProbeEndpoints.cs src/Homon.Api/Program.cs src/Homon.Web/src/pages/admin-probes-page.tsx src/Homon.Web/src/lib Directory.Packages.props docs/ARCHITECTURE.md docs/MODULES.md README.md`
> 002 and 003 legitimately touch shared files (`Program.cs`, `HomonDbContext.cs`,
> `ProbeConfiguration.cs`, `admin-probes-page.tsx`, `Directory.Packages.props`) — expected,
> not drift. Compare only the regions named in "Current state"/"Scope" below.

## Status

- **Priority**: P2
- **Effort**: M
- **Risk**: MED — a fully synchronous third-party library with no cancellation support, a
  new LGPL-3.0 dependency to account for correctly, and a credential shape 003's
  `HttpCredential` does not fit
- **Depends on**: `plans/002-monitoring-core-groups-and-ping.md`,
  `plans/003-http-probe.md` (both must land first — this plan reuses their contract and
  patterns by name, not by redesign)
- **Category**: direction
- **Planned at**: commit `f4e7261`, 2026-09-15

## Context

`docs/MODULES.md` (verbatim): "SMB requires credentials and a mount target." And the
constraint recorded during scaffolding:

> **SMB cannot be a mount.** Mounting CIFS needs `CAP_SYS_ADMIN`, which a rootless
> container does not have and should not be given. The probe therefore uses a managed SMB
> client — connect, authenticate, list the share root, disconnect — and "mount target" in
> the brief becomes a share path (`//host/share`). Candidate: `SMBLibrary` (LGPL-3.0;
> usable from an MIT application as a NuGet dependency, and worth noting in the licence
> section of the README when it lands).

Shared with 003 and 011 (Calendar): "Probe secrets (SMB password, HTTP bearer) are stored
encrypted with ASP.NET Data Protection."

This is the third probe kind. 003 set the pattern for options storage, secret protection
and wire semantics; this plan reuses it by name and adds exactly two new things: a
credential shape that isn't HTTP's (username + password + optional domain, no
"none"/"bearer" states), and a runner built on a fully synchronous third-party library
instead of `HttpClient`.

## Contract assumed (from 002 and 003)

Not implemented here — only consumed. Confirm each exists (or under 002/003's real name).

| Assumed | Shape | Where |
| --- | --- | --- |
| `Probe` | `Guid Id`, `Name`, `Host`, poll interval, failure threshold, `IsPaused`, `Position`, `ProbeKind Kind`, plus `HttpProbeOptions? HttpOptions` (003). | `Homon.Domain/Monitoring/Probe.cs` |
| `ProbeKind` | Enum incl. `Ping`, `Http`, `Smb` (this plan), `Snmp` (005). | `Homon.Domain/Monitoring/` |
| `IProbeRunner` | `ProbeKind Kind { get; }`, `Task<ProbeResult> RunAsync(Probe, CancellationToken)`. | `Homon.Infrastructure/Monitoring/IProbeRunner.cs` |
| `ProbeResult` | `record ProbeResult(bool Succeeded, double? LatencyMs, string? Detail)`. | `Homon.Infrastructure/Monitoring/` |
| `ISecretProtector`/`DataProtectionSecretProtector` | `Protect`/`Unprotect`, purpose `"Homon.Secrets.v1"`, `AddSingleton` in `AddHomonInfrastructure`. `Unprotect` throws `CryptographicException` on a lost key ring. | `Homon.Infrastructure/Security/` |
| Owned-jsonb per-kind options | `builder.OwnsOne(p => p.XxxOptions, x => { x.ToJson(); x.OwnsOne(o => o.Credential); })`, one nullable owned type per kind. | `ProbeConfiguration.cs` |
| Write-only secret wire semantics | Response never carries a secret, only `hasSecret: bool`. Write: absent → keep; `""` → clear; non-empty → `Protect()` and replace. Plain fields round-trip in the clear. | `ProbeEndpoints.cs` (003) |
| `ProbeEndpoints` | `MapProbeEndpoints`, admin CRUD, `TypedResults.ValidationProblem`. | `Homon.Api/Endpoints/ProbeEndpoints.cs` |
| `admin-probes-page.tsx` | `ProbeForm` with a kind selector and a per-kind fieldset (whatever shape 003 actually shipped). | `Homon.Web/src/pages/admin-probes-page.tsx` |

If 003's real shape differs from this table in a way that matters, use 003's actual names
throughout and say so in your summary — do not silently redesign 003.

## Decisions

### 1. Library: `SMBLibrary` 1.5.8, LGPL-3.0-or-later — verified, not guessed

Verified against the live NuGet API while writing this plan (reproduce before trusting the
numbers a second time — this plan may execute long after it was written):

```bash
curl -s https://api.nuget.org/v3-flatcontainer/smblibrary/index.json          # latest version
curl -s https://api.nuget.org/v3/registration5-semver1/smblibrary/1.5.8.json  # -> catalogEntry URL
curl -s '<that catalogEntry URL>' | python3 -m json.tool                      # licence, dependencies
```

At planning time: `1.5.8`, `licenseExpression: "LGPL-3.0-or-later"`,
`licenseUrl: https://licenses.nuget.org/LGPL-3.0-or-later`, author Tal Aloni, one
dependency (`System.Buffers >= 4.5.1`), target `netstandard2.0` — no native interop, so
`src/Homon.Api/Dockerfile` needs no change (contrast the rejected `smbclient` alternative
below, which would).

**LGPL-3.0, plainly**: Homon references `SMBLibrary` unmodified via a normal
`<PackageReference>` — dynamic linking, which does not make Homon's own code LGPL. What it
does require: the licence text/notice ships with the distribution (→ `README.md`'s new
Licence section, Step 8) and a recipient can replace the component with a modified build
(→ satisfied by an ordinary CPM pin — swap the version in `Directory.Packages.props`, no
vendored source needed because Homon never modifies it). If Homon ever forks the library's
source directly, that fork must be published too — out of scope; revisit this section
first if it comes up.

**Rejected** (comment where `SmbProbeRunner` is created):
- **Kernel CIFS mount** — needs `CAP_SYS_ADMIN`; the `api` container runs as `$APP_UID`,
  non-root, deliberately (`Dockerfile:43-44`); granting that capability for one probe would
  widen the container's privilege for every probe.
- **Shelling out to `smbclient`** — absent from `mcr.microsoft.com/dotnet/aspnet:10.0-alpine`;
  parsing its text output is locale/version-fragile next to a typed `NTStatus`.
- **A bare TCP connect to 445** — proves the port is open, nothing about auth or the
  share's existence; an expired password would misreport as "up".

**STOP** if re-running the verification shows a licence other than an LGPL-3.0 variant, or
a dependency graph pulling in native code — do not apply the obligations above blind.

### 2. `SmbProbeOptions` — share name + host composition, not the brief's raw `//host/share`

```csharp
public sealed class SmbProbeOptions
{
    public required string ShareName { get; set; }  // bare name, e.g. "backups" — no
                                                      // leading slashes, no host
    public int Port { get; set; } = 445;
    public required SmbCredential Credential { get; set; }
}
```

Composed at connect time as `Probe.Host` + `ShareName`. *Rejected*: accepting the brief's
`//host/share` as one field — duplicates `Probe.Host`, every kind's destination field
already, and a mismatch needs its own validation rule for no benefit. Mirrors 003's
`HttpProbeOptions.Path` exactly (bare, no leading slash; `Probe.Host` supplies the rest) —
cite it rather than re-litigate.

*Validation*: `ShareName` non-empty, ≤80 chars (SMB's own share-name ceiling), no
`/`/`\`/control characters (a path segment, not a UNC path). `Port` 1–65535.

### 3. `SmbCredential` — a new, minimal type; `HttpCredential` does not fit

`HttpCredential.Type` (`None`/`Bearer`/`Basic`, 003) is HTTP-specific: SMB has no bearer
concept, and "none" is not a real state (`docs/MODULES.md`: "SMB requires credentials").
Minimal extension instead:

```csharp
public sealed class SmbCredential
{
    public required string Username { get; set; }   // plain, round-trips in the clear
    public string? Domain { get; set; }              // plain, optional — empty/null means
                                                      // no domain (local account/workgroup)
    public string? ProtectedSecret { get; set; }     // encrypted password; ISecretProtector,
                                                      // same purpose string as HttpCredential's
}
```

Write-only rules reuse 003's for `Username`/`Domain` (always round-trip) and for `secret`
(absent → keep; non-empty → `Protect()` and replace) — **one deviation**: `secret: ""`
(clear) is **rejected with 400**. `HttpCredential` can legitimately have no secret
(`Type == None`); `SmbCredential` cannot — there is no valid "SMB probe, no password"
state. Document this deviation in the endpoint's validation comment.

### 4. No per-probe SMB dialect field — the runner never attempts SMB1

`SMBLibrary.Client.SMB2Client` (confirmed present, see Decision 5) negotiates SMB 2.x/3.x
itself; the library also ships `SMB1Client`, deliberately unused. *Rejected*: a per-probe
"allow SMB1" opt-in — SMB1 carries a well-known unauthenticated RCE history
(MS17-010/EternalBlue), and every NAS/Samba build from the last decade defaults SMB2/3 on.
`SmbProbeOptions` therefore has no `Dialect` field — a runner-level constant, not an admin
choice.

### 5. Runner: a caller-side timeout, because the library gives none

**Library verification** — done during planning; reproduce before writing
`SmbProbeRunner` if this plan executes much later than written:

```bash
curl -fsSL -o smblibrary.nupkg https://api.nuget.org/v3-flatcontainer/smblibrary/1.5.8/smblibrary.nupkg
unzip -o -q smblibrary.nupkg -d extracted
grep -oE 'name="[TM]:SMBLibrary\.[^"]*"' extracted/lib/netstandard2.0/SMBLibrary.xml | sort -u
strings -a extracted/lib/netstandard2.0/SMBLibrary.dll | grep -E '^(Connect|Login|Disconnect|TreeConnect|TreeDisconnect|CreateFile|CloseFile|QueryDirectory|LogoffAndDisconnect)$'
```

Confirmed at planning time: `SMB2Client` is fully **synchronous** — no `CancellationToken`
parameter appears anywhere in the assembly's public surface (the only occurrence is an
internal keep-alive timer). Confirmed callable: `Connect(string, SMBTransportType)`,
`TreeConnect(string, bool, out NTStatus)`, `LogoffAndDisconnect()`,
`SMB2Client(int, bool, bool)` ctor, and (file-store interfaces) `CreateFile`, `CloseFile`,
`QueryDirectory`. Confirmed `NTStatus` constants: `STATUS_SUCCESS`,
`STATUS_LOGON_FAILURE`, `STATUS_WRONG_PASSWORD`, `STATUS_ACCOUNT_DISABLED`,
`STATUS_ACCOUNT_LOCKED_OUT`, `STATUS_PASSWORD_EXPIRED`, `STATUS_BAD_NETWORK_NAME`,
`STATUS_ACCESS_DENIED`, `STATUS_IO_TIMEOUT`.

**Not confirmed — do not guess**: the exact authentication call. No bare `Login`
identifier appears anywhere in the 1.5.8 assembly, which is surprising against this
library's older, commonly-documented shape (renamed, folded into a `Connect` overload, or
missed by the search above). **Before writing `SmbProbeRunner`, open the extracted
`SMBLibrary.xml` (or, once restored, `~/.nuget/packages/smblibrary/1.5.8/lib/netstandard2.0/SMBLibrary.xml`)
and confirm the real call's name/signature and the exact directory-listing overload with
IntelliSense or a decompiler — do not assume the historical `Login(domain, username,
password)` shape still holds.**

No cancellation means 003's pattern (hand a linked `CancellationTokenSource` to an async
call) has nothing to attach to. Use `Task.WhenAny` instead:

```csharp
var work = Task.Run(() => RunBlocking(probe, credential));
var winner = await Task.WhenAny(work, Task.Delay(Timeout, cancellationToken));
if (winner != work)
{
    return new ProbeResult(false, null, "timeout after 10 s");
    // `work` is abandoned, not cancelled — the library gives no interrupt hook. It keeps
    // running on its own thread-pool thread until the underlying call itself unblocks (a
    // TCP RST/refusal, or the OS's own connect timeout) and disposes the session in a
    // `finally` when it does. This bounds what the probe *reports*, not the OS-level socket.
}
return await work;
```

*Rejected*: a singleton `SMB2Client` shared across probes, mirroring 003's one named
`HttpClient` — rejected, `SMB2Client` holds per-connection session state (login, tree
connect), unlike stateless-per-request `HttpClient`. Every `RunAsync` gets its own client
through the seam below.

**Seam for testability**, mirroring 003's `StubHttpMessageHandler`:

```csharp
internal interface ISmbSession : IDisposable
{
    bool Connect(string host, int port);
    NTStatus Authenticate(string? domain, string username, string password); // rename to
                                                                              // match the
                                                                              // confirmed call
    NTStatus TreeConnectAndListRoot(string shareName);
    void Disconnect();
}

internal interface ISmbClientFactory
{
    ISmbSession Create();
}
```

`DefaultSmbSession` wraps `SMB2Client` (the only file touching `SMBLibrary` types
directly); `SmbProbeRunner` depends on `ISmbClientFactory` only. Tests inject a fake
factory that scripts `NTStatus` values and exceptions — no socket involved.

**`RunBlocking`**: `factory.Create()` → `Connect(probe.Host, options.Port)` → unprotect the
password inside `try/catch (CryptographicException)` → `Authenticate(...)` →
`TreeConnectAndListRoot(options.ShareName)` → `Disconnect()` in `finally`. Latency =
`Stopwatch` around the whole body, matching 003.

**`NTStatus` → Detail**:

| Situation | Detail |
| --- | --- |
| `Connect` returns `false` / `SocketException` | `"host unreachable"` |
| `STATUS_LOGON_FAILURE`, `STATUS_WRONG_PASSWORD`, `STATUS_ACCOUNT_DISABLED`, `STATUS_ACCOUNT_LOCKED_OUT`, `STATUS_PASSWORD_EXPIRED` | `"authentication failed"` |
| `STATUS_BAD_NETWORK_NAME` | `"share not found"` |
| `STATUS_ACCESS_DENIED` | `"access denied"` |
| Overall `Task.WhenAny` timeout | `"timeout after 10 s"` |
| `CryptographicException` unprotecting the password | `"credentials unreadable — re-enter them"` |
| `STATUS_SUCCESS` throughout | success; `Detail == null` |

`docs/design-brief.md:37` uses `"share unreachable"` as its own illustrative example for
this row — not identical to `"share not found"` above. Either reads fine; if literal
matching against the brief matters to the maintainer, it's a one-line change here.

### 6. A Samba-container integration test: deferred, not built

`ci/compose.ci.yaml` runs Postgres only; adding Samba changes the gate itself, and the
seam in Decision 5 already makes the status-mapping logic testable without one. Defer — a
follow-up plan if end-to-end confidence is wanted later, not part of this plan's Done
criteria.

## Defaults taken

- `SmbProbeRunner.Timeout = TimeSpan.FromSeconds(10)`, matching `HttpProbeRunner.RequestTimeout`
  — not admin-configurable this phase.
- `Port` defaults `445`, admin-editable (a forwarded/non-standard port is plausible).
- Migration name `AddSmbProbe`, layered on 002's `AddMonitoring` and 003's
  `AddHttpProbeOptions` (additive `ALTER TABLE ... ADD COLUMN`, not a rewrite).
- `docs/ARCHITECTURE.md` gets the next free numbered section — verify first (003 assumed
  002 takes §3.13 and claimed §3.14 for itself; if both landed as assumed, this plan is
  §3.15).

## Current state

- `src/Homon.Api/Program.cs:219-226` — unconditional `IDataProtectionProvider` registration
  (003 Decision 3); this plan only consumes `ISecretProtector`. `Program.cs:476-479` —
  commented module-registration block, unchanged here (003 is expected to have uncommented
  `v1.MapProbeEndpoints();` already).
- `src/Homon.Infrastructure/InfrastructureServiceCollectionExtensions.cs:23-41` —
  `AddHomonInfrastructure`'s composition pattern (`AddXxx(this IServiceCollection …)`); add
  a matching block, or extend whatever 003 named its own.
- `Directory.Packages.props:39-42` — today: "Probe libraries (SNMP, SMB) are deliberately
  absent…". Replace the SMB half with the pinned `PackageVersion` (Decision 1); leave SNMP
  for 005. `Homon.Infrastructure.csproj:16-25` — `<PackageReference>` list, no `Version`
  attribute (CPM); add `SMBLibrary`.
- `src/Homon.Api/Dockerfile` — confirmed no native packages installed; unchanged by this
  plan since `SMBLibrary` is pure managed (`netstandard2.0`).
- `compose.prod.yaml:144-151` — the ICMP `sysctls:` block, precedent for a module-specific
  runtime note. SMB needs no compose change, only a runbook line (Step 8) — outbound TCP
  445 is ordinary container traffic.
- `docs/ARCHITECTURE.md` ends at §3.12 today; 003 assumes 002 adds §3.13 and reserves §3.14
  for itself — verify before taking §3.15.
- `README.md` — no Licence section today; line 20 states "MIT licence." inline in the
  Stack paragraph. `LICENSE` at the repo root is plain MIT text.
- `docs/design-brief.md:64-66` already lists "SMB: share + credentials" under Admin →
  Probes; no change expected unless the fieldset needs more than that (check first).
- `plans/README.md:12` — `| 004 | planned | SMB/CIFS probe (managed client) |`.

## Commands you will need

| Purpose | Command | Expected on success |
| --- | --- | --- |
| Full gate | `./ci/run-ci.sh` | `PASS — web api e2e`, 0 skipped api tests |
| API only | `./ci/run-ci.sh api` | same, api suite |
| Web only | `./ci/run-ci.sh web` | `npm ci`, lint, build (=typecheck), vitest pass |
| e2e only | `./ci/run-ci.sh e2e` | Playwright, two viewports |
| Release build | `dotnet build Homon.sln --configuration Release` | 0 warnings/errors |
| Library verification | Decision 5's `curl`/`unzip`/`grep`/`strings` block | confirms real `SMB2Client` API before coding |
| New migration | `HOMON_DESIGNTIME_CONNECTION='Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x' dotnet dotnet-ef migrations add AddSmbProbe --project src/Homon.Infrastructure --startup-project src/Homon.Api --output-dir Persistence/Migrations` | migration files created |
| Apply locally | `dotnet run --project src/Homon.Api -- migrate` | `applied     : all of them.` |
| One xunit class | `dotnet test tests/Homon.Api.Tests --filter FullyQualifiedName~SmbProbeRunnerTests` | all pass |
| One Vitest file | `npm --prefix src/Homon.Web run test -- admin-probes-page` | all pass |

## Scope

**In scope**:
- `src/Homon.Domain/Monitoring/SmbProbeOptions.cs`, `SmbCredential.cs` (create)
- `src/Homon.Domain/Monitoring/Probe.cs` (extend — add `SmbOptions`; shared with 002/003)
- `src/Homon.Infrastructure/Monitoring/ISmbSession.cs`, `ISmbClientFactory.cs`,
  `DefaultSmbSession.cs`, `SmbProbeRunner.cs` (create)
- `src/Homon.Infrastructure/Persistence/Configurations/ProbeConfiguration.cs` (extend, shared)
- `src/Homon.Infrastructure/Persistence/Migrations/*AddSmbProbe*` (create, `dotnet ef`)
- `src/Homon.Infrastructure/InfrastructureServiceCollectionExtensions.cs` (extend, shared)
- `src/Homon.Infrastructure/Homon.Infrastructure.csproj`, `Directory.Packages.props` (extend)
- `src/Homon.Api/Endpoints/ProbeEndpoints.cs` (extend, shared)
- `src/Homon.Web/src/lib/probes.ts`, `src/Homon.Web/src/pages/admin-probes-page.tsx` (extend, shared)
- `README.md` (new Licence section), `docs/ARCHITECTURE.md`, `docs/MODULES.md`,
  `src/Homon.Domain/Monitoring/README.md`, `docs/deployment-runbook.md`,
  `docs/design-brief.md` (only if needed — check first), `plans/README.md`
- Tests: `tests/Homon.Api.Tests/SmbProbeRunnerTests.cs`, `FakeSmbSession.cs`,
  `FakeSmbClientFactory.cs` (create); `ProbeEndpointTests.cs` (extend, shared);
  `admin-probes-page.test.tsx` (extend, shared); `src/Homon.Web/e2e/admin.spec.ts` (extend,
  if present after 002/003)

**Out of scope**: `IProbeRunner`/`ProbeResult`/scheduler/state machine (002); HTTP runner
(003); SNMP (005 — copy this plan's and 003's pattern by name); any `className`/styling
(012); alerts (009); a Samba-container integration test (Decision 6).

## Steps

### Step 1: Confirm contracts and the library

Read `Probe.cs`, `IProbeRunner.cs`, `ProbeResult`, `ISecretProtector.cs`,
`DataProtectionSecretProtector.cs`, `HttpProbeOptions.cs`/`HttpCredential.cs`,
`ProbeEndpoints.cs` as they landed; use real names throughout if they differ. Run Decision
5's library-verification commands and confirm the authentication call's real
name/signature before Step 5.

**Verify**: assumed types exist; `dotnet build Homon.sln` succeeds. Missing any → STOP.

### Step 2: Package reference

Add to `Directory.Packages.props` (replacing the SMB half of the comment at lines 39-42):

```xml
<ItemGroup Label="Probe libraries">
  <!-- Managed SMB2/3 client, LGPL-3.0-or-later, plan 004 Decision 1. -->
  <PackageVersion Include="SMBLibrary" Version="1.5.8" />
</ItemGroup>
```

Add `<PackageReference Include="SMBLibrary" />` to `Homon.Infrastructure.csproj`.

**Verify**: `dotnet restore Homon.sln` succeeds; `dotnet build Homon.sln` → 0 errors.

### Step 3: Domain — `SmbProbeOptions` and `SmbCredential`

Create both per Decisions 2 and 3. Add `public SmbProbeOptions? SmbOptions { get; set; }`
to `Probe.cs`.

**Verify**: `dotnet build src/Homon.Domain` → 0 errors/warnings.

### Step 4: Persistence — owned-jsonb mapping and the migration

In `ProbeConfiguration.cs` (comment citing Decisions 2/3 for the credential shape, matching
003's rejected-alternatives comment style):

```csharp
builder.OwnsOne(p => p.SmbOptions, smb =>
{
    smb.ToJson();
    smb.OwnsOne(o => o.Credential);
});
```

Generate the migration. Expect a single additive `AddColumn<string>(name: "SmbOptions",
table: "Probes", type: "jsonb", nullable: true)`. If EF touches 002's or 003's migrations,
STOP.

**Verify**: `dotnet run --project src/Homon.Api -- migrate` → `applied: all of them.`;
`psql`'s `\d "Probes"` shows `"SmbOptions" jsonb`.

### Step 5: Infrastructure — the seam and the runner

Create `ISmbSession.cs`/`ISmbClientFactory.cs` per Decision 5 (rename `Authenticate` to
whatever Step 1's verification found). `DefaultSmbSession` wraps `SMB2Client`; register
`AddSingleton<ISmbClientFactory, DefaultSmbClientFactory>()`.

Create `SmbProbeRunner.cs`: `Kind => ProbeKind.Smb`; `RunAsync` runs the
`Task.WhenAny(Task.Run(...), Task.Delay(...))` pattern from Decision 5, unprotecting the
password inside `try/catch (CryptographicException)`, mapping every `NTStatus`/exception
per the Detail table. Register `AddScoped<IProbeRunner, SmbProbeRunner>()` — or however
002's scheduler expects runners registered; check first.

**Verify**: `dotnet build Homon.sln --configuration Release` → 0 errors/warnings.

### Step 6: API — wire types and validation

Add an optional `Smb` object to the create/update request and response records. Validate:
`kind == "smb"` requires `smb` present, any other kind requires it absent; `shareName`
non-empty, ≤80 chars, no `/`/`\`/control characters; `port` 1–65535; `credential.username`
non-empty; `credential.secret == ""` → 400 (Decision 3). On write, `Protect()` a non-empty
`secret`; on read, emit `hasSecret` only.

**Verify**: `dotnet build Homon.sln --configuration Release` → 0 errors.

### Step 7: SPA — the SMB fieldset and wire types

Extend `ProbeForm` with an SMB section shown only for kind `"smb"` (reuse 003's
kind-conditional pattern; do not introduce a second one). Fields: share name, port
(default 445), username, domain (optional), password (write-only, "Password is set — leave
blank to keep", no "clear" affordance per Decision 3). No `className`; semantic
`<fieldset>`/`<legend>`/`<label>` only. Extend `lib/probes.ts` following `lib/meta.ts:5-14`.

**Verify**: `npm --prefix src/Homon.Web run build` → 0 errors.

### Step 8: Docs

- `README.md`: new **Licence** section — Homon is MIT; names `SMBLibrary` (Tal Aloni,
  LGPL-3.0-or-later, `github.com/TalAloni/SMBLibrary`) as used unmodified, linking
  `https://licenses.nuget.org/LGPL-3.0-or-later` (Decision 1).
- `docs/ARCHITECTURE.md`: next free `§3.x` (verify the number — see Defaults) recording
  Decisions 2-5; rejected alternatives as bullets, matching §3.10-§3.12's style.
- `docs/MODULES.md`, `src/Homon.Domain/Monitoring/README.md`: extend with
  `SmbProbeOptions`/`SmbCredential` if not already implied by the current text.
- `docs/deployment-runbook.md`: one line — the `api` container must reach TCP 445 on every
  SMB probe's target; a household firewall can block it silently (probe reads "host
  unreachable" with nothing else to check).
- `plans/README.md`: flip row 004 to `DONE`.

**Verify**: `grep -n "Licence" README.md` and `grep -n "SMBLibrary" README.md docs/ARCHITECTURE.md` both match.

## Test plan

**xunit** — `SmbProbeRunnerTests` (pure): `FakeSmbSession : ISmbSession` scripted per case
via a `FakeSmbClientFactory`. Cases: full success (`STATUS_SUCCESS` throughout) →
`Succeeded == true`, `Detail == null`; `Connect` false / `SocketException` → `"host
unreachable"`; each logon-failure-shaped `NTStatus` → `"authentication failed"`;
`STATUS_BAD_NETWORK_NAME` → `"share not found"`; `STATUS_ACCESS_DENIED` → `"access
denied"`; blocking past an injected short timeout (e.g. 50ms — never the real 10s) →
`"timeout after 10 s"`, with the abandoned task's `Dispose`/`Disconnect` still eventually
running (assert via a `TaskCompletionSource` the fake signals on disposal); a fake
`ISecretProtector.Unprotect` throwing `CryptographicException` → `"credentials unreadable —
re-enter them"`, no exception escapes `RunAsync`.

`ProbeEndpointTests` (extend, `[DatabaseFact]`/`ApiDatabaseFactory`): SMB probe round-trips
`smb` options; response never contains `protectedSecret` or plaintext, only `hasSecret`;
update omitting `credential.secret` keeps `hasSecret: true`; `credential.secret: ""` → 400;
400s for missing/extra `smb` object, `shareName` containing `/`/`\`, empty `username`,
out-of-range `port`; anonymous write 401, API-key write 403.

**Vitest** — extend `admin-probes-page.test.tsx`: kind "smb" reveals the SMB fieldset and
hides others; submit sends the right body via `stubFetch`; the password field never
pre-fills a secret and shows no "clear" affordance (replace only).

**Playwright** — extend `e2e/admin.spec.ts` if present post-003: create an SMB probe
(paused) through the admin form, confirm it appears in the list — form interaction only,
no live connection. Run `expectNoHorizontalOverflow`/`expectTappable` at both viewports.

## Done criteria

- [ ] `dotnet build Homon.sln --configuration Release` → 0 warnings/errors
- [ ] `dotnet run --project src/Homon.Api -- migrate` applies `AddSmbProbe` cleanly
- [ ] New xunit tests exist and pass: `SmbProbeRunnerTests`, the `ProbeEndpointTests` additions
- [ ] `npm --prefix src/Homon.Web run build` exits 0; new/extended Vitest tests pass
- [ ] `grep -rn "ProtectedSecret" src/Homon.Api/Endpoints/ProbeEndpoints.cs` shows it is
      never read into a response DTO (only `hasSecret` derivations)
- [ ] `./ci/run-ci.sh` → `PASS — web api e2e`, 0 skipped api tests
- [ ] `docs/ARCHITECTURE.md` gets its next free `§3.x`; `README.md` has a Licence section
      naming SMBLibrary/LGPL-3.0-or-later; `docs/MODULES.md`,
      `src/Homon.Domain/Monitoring/README.md`, `docs/deployment-runbook.md` updated
- [ ] `plans/README.md`'s 004 row is `DONE`
- [ ] No files outside "Scope" modified (`git status`)

## STOP conditions

- Plan 002 or 003 has not landed, or `IProbeRunner`/`ProbeResult`/`ISecretProtector`/a
  per-kind options mechanism does not exist in any form.
- Re-running Decision 1's licence check shows anything other than an LGPL-3.0 variant, or a
  dependency graph pulling in native code.
- The real `SMB2Client` authentication or directory-listing call differs materially from
  what Decision 5 found — confirm via the package's own XML doc comments/IntelliSense
  before writing `SmbProbeRunner`; do not guess a signature.
- The generated migration touches anything beyond an additive `SmbOptions` column.
- `IDataProtectionProvider`/`ISecretProtector` is not resolvable as 003 described —
  reconcile with 003's actual shape before inventing a second secret-protection path.
- A step's verification fails twice after a reasonable fix attempt.
- `admin-probes-page.tsx`'s post-003 shape has no kind-conditional `ProbeForm` pattern to
  extend — reconcile with what 003 actually shipped, and report what you found.

## Maintenance notes

- **005 (SNMP) must reuse, by name**: `ISecretProtector`/`DataProtectionSecretProtector`
  (one purpose string, `"Homon.Secrets.v1"`), the owned-type-per-kind `.ToJson()` pattern
  (`SnmpProbeOptions`), and the write-only credential wire semantics.
- A host that silently drops every packet (firewalled, not merely down) can pin one
  thread-pool thread for the OS's own TCP connect timeout — Decision 5's accepted,
  documented risk. If it matters in practice, the fix is a pre-flight
  `TcpClient.ConnectAsync` with Homon's own timeout before handing off to `SMBLibrary`, not
  a change to the abandonment strategy.
- SMB1 is deliberately unreachable from this runner (Decision 4). A legacy device needing
  it is a deliberate follow-up plan with its own security review, not a silent library flip.
- A reviewer should check: `SmbCredential.ProtectedSecret` never serialises into a
  response; the `CryptographicException` catch is the only lost-key-ring surface; the
  abandoned `Task.Run` on the timeout path really disposes `ISmbSession` eventually (no
  leaked socket/handle) — the test plan's `TaskCompletionSource` assertion covers this.
- Deferred: an admin "test this probe now" button (003 deferred the same); a per-probe
  timeout; SMB1 support; a Samba-container integration test (Decision 6).
