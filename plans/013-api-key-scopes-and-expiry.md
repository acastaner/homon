# 013 — API key scopes and expiry

> **Executor instructions**: Follow this plan step by step. Run every verification command
> and confirm the expected result before moving on. If anything in "STOP conditions" occurs,
> stop and report — do not improvise. This plan does **not** ask you to update
> `plans/README.md`; the reviewer maintains that index for this run.
>
> **Drift check (run first)**:
> `git diff --stat f4e7261..HEAD -- src/Homon.Domain/Auth src/Homon.Infrastructure/Persistence/Configurations/ApiKeyConfiguration.cs src/Homon.Infrastructure/InfrastructureServiceCollectionExtensions.cs src/Homon.Api/Authentication src/Homon.Infrastructure/Identity/HomonClaimTypes.cs src/Homon.Api/Cli/CreateApiKeyArguments.cs src/Homon.Api/Program.cs src/Homon.Api/Endpoints/AuthenticationEndpoints.cs docs/ARCHITECTURE.md docs/deployment-runbook.md`
> This plan runs first in the session (before 002), so a non-empty diff means someone touched
> these files since 2026-09-15 — compare the changed regions against "Current state" below
> before proceeding; a change elsewhere in the same file is not itself a STOP condition.

## Status

- **Priority**: P1 — plan 002's probe-list read (`GET /api/v1/probes`) and every plan after it
  assume `HomonPolicies.AdministratorOrApiKey` already exists.
- **Effort**: S
- **Risk**: MED — small diff, but it changes the authentication handler and adds a new
  authorization policy; a mistake here propagates into every plan that reads through this key.
- **Depends on**: `plans/001-*.md` only (complete).
- **Category**: security
- **Planned at**: commit `f4e7261`, 2026-09-15
- **Reviewed**: 2026-09-15 (cold review-plan; execution order 013 → 002 → 003 → 006 → 007 →
  010 → 012)

## Why this matters

Today `ApiKey` has no scope and no expiry: a key either works or is revoked, and every key
can do everything a key can do. Plan 002 needs `GET /api/v1/probes` readable by an
administrator's session *or* by a minted key, and the maintainer wants that key narrowable to
read-only and able to expire on its own. This plan adds `ApiKeyScope` (`Read`/`ReadWrite`),
`ApiKey.ExpiresAt`, enforces expiry the same way revocation is already enforced, and adds
`HomonPolicies.AdministratorOrApiKey`. Minting stays on the `create-api-key` CLI verb — the
admin page for it is plan 008's, not built here.

## Decisions

**1. Scope is a two-value enum, persisted as its name, not a number** (`ApiKeyScope`, in
`Homon.Domain.Auth`, next to `ApiKey`). A dump stays legible (`'ReadWrite'`, not `1`) and a
future third scope does not renumber an already-stored value.

**2. Migration backfill: existing rows become `ReadWrite`, not `Read`.** *Product decision.*
Every key minted before this migration — the runbook's `"clockmaster restic"` key
(`docs/deployment-runbook.md:44`) — was minted for reporting duties plan 008's report
endpoint is expected to gate on `ReadWrite`; downgrading it to `Read` here would silently
break it the day 008 ships. *Rejected*: backfill to `Read` — correct in the abstract, wrong
for a key already deployed and doing a job nobody is re-minting it for.

**3. CLI default when `--scope` is omitted: `Read`.** *Product decision.* A newly minted key
has no history to protect, so an operator who forgets the flag gets the safer default. This
is the opposite of Decision 2 on purpose — one protects what already exists, the other nudges
what's new toward least privilege.

**4. `--expires <YYYY-MM-DD>` names a calendar date, valid through the whole of that day,
UTC** — read like a certificate's "valid until", not a timestamp the operator reasons about a
time zone for. Stored as that date's `23:59:59.9999999Z`. A date already past (UTC) is
refused at parse time.

**5. Expiry check sits directly after the revoked check, before the secret comparison** —
same place, same reasoning `ApiKeyAuthenticationHandler.cs` already states for revocation:
*"everything above is a property of a key that exists, and none of it is worth learning
without the secret."* An expired key fails the whole request via `ApiKeyRefusalMiddleware`,
exactly like a revoked one — never demoted to anonymous.

**6. `TimeProvider` is introduced here, registered once, for reuse.** Neither the handler nor
`ApiKeyIssuer` reads the clock through a seam today — cheap to fix while touching both, and
the only way `ApiKeyIssuer` can refuse to mint an already-expired key without a hidden
dependency on the wall clock. Registered once in `AddHomonInfrastructure`
(`services.AddSingleton(TimeProvider.System)`), so plan 002's scheduler (which also wants one)
finds it already there. `CreateApiKeyArguments.TryParseExpiry` still reads
`DateTimeOffset.UtcNow` directly — a pure static parser with no DI; `ApiKeyIssuer`'s own
`TimeProvider` check is the one that actually matters, the parser's exists only to fail fast
before a database round trip.

**7. `HomonPolicies.AdministratorOrApiKey` via `RequireAssertion`, not a new requirement +
handler.** Unlike `Reader` (needs `IOptionsMonitor<AuthOptions>`, hence a handler class), this
is a stateless boolean — a lambda is sufficient and just as testable through
`IAuthorizationService.AuthorizeAsync`, the way `ReaderPolicyTests.cs` already exercises
`Reader` without a real endpoint. It admits a session in the `Administrator` role, or *any*
authenticated API-key principal — scope is not discriminated, since nothing this session needs
tells `Read` and `ReadWrite` apart. A `ReadWrite`-only policy is **not** added here — 002's
`GET /probes` does not need one; 008's report endpoint, the first thing that will, can add it.

## Current state

- `src/Homon.Domain/Auth/ApiKey.cs` — no `Scope`, no `ExpiresAt`; has `TokenIdLength` (12) and
  `NameMaxLength` (100) as public consts beside the properties — follow that for `ScopeMaxLength`.
  `ApiKeyConfiguration.cs` maps `Name`, `TokenId` (unique index), `SecretHash`; nothing for
  scope/expiry. `Persistence/Migrations/20260914181358_InitialCreate.cs:15-30` — `ApiKeys`
  today: `Id, Name, TokenId, SecretHash, CreatedAt, LastUsedAt, RevokedAt`; this plan only
  adds columns.
- `InfrastructureServiceCollectionExtensions.cs:28-41` — `AddHomonInfrastructure` composes
  `AddHomonDatabase`/`AddHomonEmail`/`AddAdministrator`; no `TimeProvider` registration exists
  anywhere yet (`grep -rn "TimeProvider" src tests` finds nothing).
- `ApiKeyAuthenticationHandler.cs:41-90` — looks the key up by `TokenId`, checks `RevokedAt`,
  checks the secret (constant-time), stamps `LastUsedAt`, builds a `ClaimsIdentity` with four
  claims; `now` is `DateTimeOffset.UtcNow` at line 48. `ApiKeyIssuer.cs:11-45` —
  `IssueAsync(string name, ...)` validates the name, mints, sets `CreatedAt =
  DateTimeOffset.UtcNow`, saves; constructed with `new ApiKeyIssuer(database)` — not DI — at
  `Program.cs:111` and `ApiKeyAuthenticationTests.cs:86-92`.
- `HomonPolicies.cs:10-38` — three policies (`Reader`, `Administrator`, `ApiKey`), doc-commented
  "The three authorisation policies". `HomonClaimTypes.cs` has `AuthenticationKind`,
  `ApiKeyAuthentication`, `ApiKeyId`, `ApiKeyName` — no scope claim.
- `Cli/CreateApiKeyArguments.cs` — `record CreateApiKeyArguments(string Name)`; `Parse` handles
  only `--name`/`--name=`. `Program.cs:89-121` — the `create-api-key` verb: parses, builds a
  `CommandHost`, calls `new ApiKeyIssuer(database).IssueAsync(keyArguments!.Name)`, prints
  `name`/`token id`. `Program.cs:150-179` — `CommandHost(args)` calls
  `host.Services.AddHomonInfrastructure(...)`, the same extension the main app builder calls
  at line 210 — one registration point reaches both the CLI verbs and the running host.
- `AuthenticationEndpoints.cs:128-147` — `GetSession` builds `SessionResponse(Kind, Name)`;
  `Kind` becomes `ApiKey` when `AuthenticationKind == ApiKeyAuthentication`. The only endpoint
  today that already round-trips an API-key principal's claims — the runbook already tells an
  operator to curl it (`docs/deployment-runbook.md:47-48`) to verify a freshly minted key.
- `ApiKeyAuthenticationTests.cs` — three `[DatabaseFact]` tests via `ApiDatabaseFactory`/
  `TestClient` against `/api/v1/auth/session`. `ReaderPolicyTests.cs` — the pattern for
  testing a policy without a real endpoint: a bare `ServiceCollection` with
  `AddAuthorizationBuilder().AddHomonPolicies()`, hand-built `ClaimsPrincipal`s,
  `IAuthorizationService.AuthorizeAsync(principal, null, policyName)`.
  `AdministratorOrApiKey`'s tests follow this, not a new endpoint.
- `docs/ARCHITECTURE.md` ends at §3.12 (`grep -c '^### 3\.' docs/ARCHITECTURE.md` → 12); §3.3
  (lines 63-78) ends "Until the Backups module ships its admin page, `create-api-key --name …`
  is the minter." §4 (line 176) is "Things this record does not yet decide." This plan runs
  **before** 002/003, so it claims §3.13 — re-verify with the same grep before writing.
- `docs/deployment-runbook.md:44-48` documents `create-api-key --name "clockmaster restic"`
  with no `--scope`; under Decision 3's default that would now mint a `Read`-only key for a
  script Decision 2 says should be `ReadWrite`. `plans/README.md:25,27` already lists this
  plan and the execution order (013 → 002 → 003 → 006 → 007 → 010 → 012) — nothing to add.

## Commands you will need

| Purpose | Command | Expected on success |
| --- | --- | --- |
| Full gate | `./ci/run-ci.sh` | `PASS — web api e2e`, 0 skipped api tests |
| API only | `./ci/run-ci.sh api` | same, api suite |
| Release build (format gate) | `dotnet build Homon.sln --configuration Release` | 0 warnings/errors |
| New migration | `HOMON_DESIGNTIME_CONNECTION='Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x' dotnet dotnet-ef migrations add AddApiKeyScopeAndExpiry --project src/Homon.Infrastructure --startup-project src/Homon.Api --output-dir Persistence/Migrations` | migration files created |
| Apply locally | `dotnet run --project src/Homon.Api -- migrate` | `applied     : all of them.` |
| Focused tests | `dotnet test tests/Homon.Api.Tests --filter "FullyQualifiedName~ApiKey\|FullyQualifiedName~CliArguments\|FullyQualifiedName~AuthenticationEndpoint"` | all pass |
| Section count | `grep -c '^### 3\.' docs/ARCHITECTURE.md` | 13 (was 12) |

Run `dotnet restore Homon.sln` and `npm --prefix src/Homon.Web ci` yourself first — this
worktree starts from a fresh checkout of committed files only.

## Scope

**In scope**:
- `src/Homon.Domain/Auth/ApiKeyScope.cs` (create), `ApiKey.cs` (extend)
- `Persistence/Configurations/ApiKeyConfiguration.cs` (extend)
- `Persistence/Migrations/*AddApiKeyScopeAndExpiry*` (create, `dotnet ef`)
- `InfrastructureServiceCollectionExtensions.cs` (extend)
- `Authentication/ApiKeyAuthenticationHandler.cs`, `ApiKeyIssuer.cs`, `HomonPolicies.cs` (extend)
- `Homon.Infrastructure/Identity/HomonClaimTypes.cs` (extend)
- `Homon.Api/Cli/CreateApiKeyArguments.cs` (extend)
- `Homon.Api/Program.cs` (extend — only the `create-api-key` verb block and its comment)
- `Homon.Api/Endpoints/AuthenticationEndpoints.cs` (extend — `SessionResponse` gains `Scope`)
- `docs/ARCHITECTURE.md` (§3.3 gains one pointer sentence; new §3.13), `docs/deployment-runbook.md`
- Tests: `CliArgumentsTests.cs`, `ApiKeyAuthenticationTests.cs`, `AuthenticationEndpointTests.cs`
  (extend); `AdministratorOrApiKeyPolicyTests.cs` (create)

**Out of scope**: `ProbeEndpoints.cs`/anything under `Homon.Domain/Monitoring` (002, not run
yet); an admin page for keys (008); a `ReadWrite`-only policy (deferred to 008 — Decision 7);
any change to `HomonPolicies.Reader` or `.Administrator`; `src/Homon.Web` (no SPA change —
confirmed safe: `SessionResponse` gaining a `Scope` field is additive JSON, `src/Homon.Web/
src/lib/session.ts`'s `Session` interface and `session.test.ts` do not enumerate or reject
unknown response fields, and no e2e spec asserts on the session payload's exact shape).

## Git workflow

The harness has already put you on the worktree's branch. Do not create, switch, or push a
branch, and do not open a PR. Commit once, after every step verifies clean and
`./ci/run-ci.sh` passes, in the repo's own style (`git log --oneline`: `"<Category>: <summary>
(plan NNN)"`, e.g. `f4e7261 Design: fix the Status board direction, dark by default (plan
012)`). Suggested message: `Auth: add API key scope and expiry (plan 013)`.

## Steps

### Step 1: Domain — `ApiKeyScope` and `ApiKey`'s new fields

Create `ApiKeyScope.cs`:

```csharp
namespace Homon.Domain.Auth;

/// <summary>
/// What an <see cref="ApiKey"/> may do. A key never administers regardless of scope
/// (docs/ARCHITECTURE.md §3.3) — <see cref="ReadWrite"/> only distinguishes a read endpoint
/// from a report endpoint (the Backups module, plan 008), which is the first thing that
/// gates on it. Persisted as this name, not a number — see ApiKeyConfiguration.cs.
/// </summary>
public enum ApiKeyScope
{
    /// <summary>The default for a newly minted key. May reach a read endpoint only.</summary>
    Read,

    /// <summary>May reach a read endpoint or a report endpoint. Still never a write.</summary>
    ReadWrite,
}
```

In `ApiKey.cs`, add a const beside `TokenIdLength`/`NameMaxLength` and two properties beside
`RevokedAt`:

```csharp
/// <summary>Longest string <see cref="ApiKeyScope"/> is stored as. See ApiKeyConfiguration.</summary>
public const int ScopeMaxLength = 20;

/// <summary>
/// What this key may do. Defaults to the least-privileged value for an instance built
/// without setting it explicitly — the database column's own default differs (ReadWrite),
/// and exists only to backfill rows that predate this column; see the migration.
/// </summary>
public ApiKeyScope Scope { get; set; } = ApiKeyScope.Read;

/// <summary>
/// When this key stops authenticating, or null for a key that never expires. Refused
/// exactly like a revoked key — the whole request fails, never demoted to anonymous.
/// </summary>
public DateTimeOffset? ExpiresAt { get; set; }
```

**Verify**: `dotnet build src/Homon.Domain` → 0 errors/warnings.

### Step 2: Persistence — mapping and the migration

In `ApiKeyConfiguration.cs`, add after the `SecretHash` mapping:

```csharp
// String, not int: a dump stays readable ("Read"/"ReadWrite") and a future third scope does
// not silently renumber an already-stored value.
builder.Property(k => k.Scope)
    .HasConversion<string>()
    .HasMaxLength(ApiKey.ScopeMaxLength)
    // Existing rows (any key minted before this column, including the runbook's "clockmaster
    // restic" key) become ReadWrite on migration, not Read — see Decision 2. New keys default
    // to Read instead; see CreateApiKeyArguments.
    .HasDefaultValue(ApiKeyScope.ReadWrite)
    .IsRequired();

builder.Property(k => k.ExpiresAt);
```

Generate the migration (command above). Read it: expect exactly two additive `AddColumn`
calls on `ApiKeys` — `Scope` (`character varying(20)`, not null, default `'ReadWrite'`) and
`ExpiresAt` (`timestamp with time zone`, nullable) — and a matching `Down` dropping both. If
it touches `TokenId`, its index, or any other existing column, STOP.

**Verify**: `dotnet run --project src/Homon.Api -- migrate` → `applied: all of them.`; `psql`'s
`\d "ApiKeys"` shows both new columns.

### Step 3: Infrastructure — `TimeProvider`, and `ApiKeyIssuer`'s new parameters

In `InfrastructureServiceCollectionExtensions.cs`, add as the first line inside
`AddHomonInfrastructure` (before the three existing `AddHomonXxx` calls):

```csharp
// Registered once, here, so every clock read in the infrastructure and API layers goes
// through the same seam. Plans 002/003's scheduler/probe runners should reuse this
// registration rather than adding their own.
services.AddSingleton(TimeProvider.System);
```

In `ApiKeyIssuer.cs`, add `TimeProvider timeProvider` to the primary constructor and change
`IssueAsync`'s signature and body:

```csharp
internal sealed class ApiKeyIssuer(HomonDbContext database, TimeProvider timeProvider)
{
    internal async Task<(ApiKey Key, string Presented)> IssueAsync(
        string name,
        ApiKeyScope scope = ApiKeyScope.Read,
        DateTimeOffset? expiresAt = null,
        CancellationToken cancellationToken = default)
    {
        var trimmed = name.Trim();

        if (trimmed.Length is 0 || trimmed.Length > ApiKey.NameMaxLength)
        {
            throw new ArgumentException(
                $"A key name is 1 to {ApiKey.NameMaxLength} characters.", nameof(name));
        }

        var now = timeProvider.GetUtcNow();

        if (expiresAt is { } expiry && expiry <= now)
        {
            throw new ArgumentException(
                "A key cannot be minted already expired.", nameof(expiresAt));
        }

        var (tokenId, presented, secretHash) = ApiKeyRules.Mint();

        var key = new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = trimmed,
            TokenId = tokenId,
            SecretHash = secretHash,
            Scope = scope,
            ExpiresAt = expiresAt,
            CreatedAt = now,
        };

        database.ApiKeys.Add(key);
        await database.SaveChangesAsync(cancellationToken);

        return (key, presented);
    }
}
```

Update the call site in `ApiKeyAuthenticationTests.cs:91` from `new ApiKeyIssuer(database)` to
`new ApiKeyIssuer(database, TimeProvider.System)`.

**Verify**: `dotnet build Homon.sln --configuration Release` → 0 errors (the `Program.cs` call
site is still broken until Step 6 — expected here).

### Step 4: Authentication — expiry check and the scope claim

In `ApiKeyAuthenticationHandler.cs`: add `TimeProvider timeProvider` to the primary
constructor (after `HomonDbContext database`); replace `var now = DateTimeOffset.UtcNow;`
with `var now = timeProvider.GetUtcNow();`; insert directly after the `RevokedAt` check and
before `ApiKeyRules.Matches`:

```csharp
// Same reasoning as the revoked check above: an expiry date is a property of a key that
// exists, and none of it is worth learning to a caller who has not proven they hold the
// secret — so this runs before the secret comparison, not after.
if (key.ExpiresAt is { } expiresAt && expiresAt <= now)
{
    return AuthenticateResult.Fail("That API key has expired.");
}
```

Add a fifth claim to the `ClaimsIdentity` built in the success path:

```csharp
new Claim(HomonClaimTypes.ApiKeyScope, key.Scope.ToString()),
```

In `HomonClaimTypes.cs`, add:

```csharp
/// <summary>
/// The scope of the API key that authenticated this request — the same string
/// <see cref="Homon.Domain.Auth.ApiKeyScope"/> persists as ("Read" or "ReadWrite"). Absent
/// from a session-cookie principal's claims.
/// </summary>
public const string ApiKeyScope = "homon:key-scope";
```

**Verify**: `dotnet build Homon.sln --configuration Release` → 0 errors (DI resolves
`TimeProvider` for the handler automatically once Step 3 registers it).

### Step 5: Authorization — `HomonPolicies.AdministratorOrApiKey`

In `HomonPolicies.cs`, update the class doc comment ("three" → "four") and add:

```csharp
/// <summary>
/// A session in the Administrator role, or any authenticated API key of either scope. The
/// shape plan 002's probe-list read (<c>GET /probes</c>) needs. Scope is not discriminated
/// here — narrowing to ReadWrite only is a write concern and no write uses this policy
/// (writes stay <see cref="Administrator"/>, session only, per docs/ARCHITECTURE.md §3.3);
/// a ReadWrite-only variant is deferred to the Backups module (plan 008), the first thing
/// that needs one.
/// </summary>
public const string AdministratorOrApiKey = "AdministratorOrApiKey";
```

And in `AddHomonPolicies`, add a fourth `.AddPolicy(...)`:

```csharp
.AddPolicy(AdministratorOrApiKey, policy => policy.RequireAssertion(context =>
    context.User.IsInRole(HomonRoles.Administrator)
    || (context.User.Identity?.IsAuthenticated is true
        && context.User.HasClaim(HomonClaimTypes.AuthenticationKind, HomonClaimTypes.ApiKeyAuthentication))));
```

**Verify**: `dotnet build Homon.sln --configuration Release` → 0 errors.

### Step 6: CLI — `--scope` and `--expires`

Rewrite `CreateApiKeyArguments.cs` to parse `--scope read|read-write` (default `Read` when
omitted — Decision 3) and `--expires <YYYY-MM-DD>` (Decision 4), each with an `=`-joined and
space-separated spelling matching the existing `--name` handling. Add `using
Homon.Domain.Auth;` and `using System.Globalization;`. The record becomes
`CreateApiKeyArguments(string Name, ApiKeyScope Scope, DateTimeOffset? ExpiresAt)`. Two new
private helpers: `TryParseScope(string, out ApiKeyScope)` (exact match on `"read"`/
`"read-write"`, nothing else) and `TryParseExpiry(string, out DateTimeOffset?, out string?
error)` using `DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
DateTimeStyles.None, out var date)`, computing `new
DateTimeOffset(date.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero)` and refusing it when that
instant has already passed (`DateTimeOffset.UtcNow` — Decision 6 explains why this parser
does not take a `TimeProvider`). Keep the existing error strings
(`"is not a create-api-key option"`, `"needs a name"`) verbatim — Step 8's tests depend on
them unchanged.

In `Program.cs`'s `create-api-key` block (lines 89-121): update the leading comment to
mention `[--scope read|read-write] [--expires <YYYY-MM-DD>]`; resolve
`scope.ServiceProvider.GetRequiredService<TimeProvider>()` alongside `database`; construct
`new ApiKeyIssuer(database, timeProvider)`; call
`.IssueAsync(keyArguments!.Name, keyArguments.Scope, keyArguments.ExpiresAt)`; extend the
printed block:

```csharp
await Console.Error.WriteLineAsync(
    $"""
    name        : {key.Name}
    token id    : {key.TokenId}
    scope       : {key.Scope}
    expires     : {(key.ExpiresAt is { } expires ? expires.ToString("u") : "never")}
    key         : shown once, below, and never again. Store it where the script reads it.
    """);
```

Also update the top-of-block usage comment to show `--scope read-write` in the example,
matching Step 7's runbook change.

**Verify**: `dotnet build Homon.sln --configuration Release` → 0 errors.

### Step 7: API — expose scope through `/auth/session`; docs

In `AuthenticationEndpoints.cs` (already `using Homon.Domain.Auth;` at line 2 — no new using
needed), extend `SessionResponse` with a third parameter, `ApiKeyScope? Scope`, doc-commented
"Set only for an `ApiKey` session." In `GetSession`, after computing `kind`:

```csharp
ApiKeyScope? scope = kind == SessionKind.ApiKey
    && Enum.TryParse<ApiKeyScope>(user.FindFirst(HomonClaimTypes.ApiKeyScope)?.Value, out var parsed)
        ? parsed
        : null;

return TypedResults.Ok(new SessionResponse(
    Kind: kind,
    Name: user.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty,
    Scope: scope));
```

This is the only endpoint that already round-trips an API-key principal's claims (the runbook
already tells an operator to curl it to verify a new key) — not a new surface, only a new
field on an existing one.

Docs: in `docs/ARCHITECTURE.md` §3.3, after "Until the Backups module ships its admin page,
`create-api-key --name …` is the minter." add one sentence: "A key also carries a scope (read
or read-write) and an optional expiry as of §3.13." Then, immediately before `## 4.`
(re-verify the section is still free first), add:

```
### 3.13 API keys gain a scope and an optional expiry

Superseding part of §3.3, not replacing it: a key still never administers, and it is still
the mechanism a script authenticates with. A key minted from here on carries `ApiKeyScope`
(`Read` or `ReadWrite`, persisted as its name) and an optional `ExpiresAt`. Neither is
enforced by scope-specific policy yet — `HomonPolicies.AdministratorOrApiKey` (the policy
plan 002's probe-list read uses) admits a key of either scope equally, because nothing this
session needs to tell them apart. The distinction exists for the Backups module (008), whose
report endpoint is the first thing that should refuse a Read key.

Expiry is enforced immediately, the same way revocation already is: the authentication
handler fails the whole request — never demotes it to anonymous — the moment `ExpiresAt` is
in the past, checked right after the revoked check and before the secret comparison.

`create-api-key` defaults a new key to `Read` (least privilege for an operator who forgot
the flag) and to no expiry. Every key that existed before this section landed (the runbook's
"clockmaster restic" key among them) became `ReadWrite` on migration, so a report endpoint
gated on `ReadWrite` later does not retroactively lock out an already-deployed key.
```

In `docs/deployment-runbook.md:44-48`, change the example command to `create-api-key --name
"clockmaster restic" --scope read-write`, with a one-line note that the flag is deliberate
(the future report endpoint is expected to require it), and change the verification sentence
to note the `/auth/session` response's `scope` field should read `"readWrite"`.

**Verify**: `dotnet build Homon.sln --configuration Release` → 0 errors; `grep -c '^### 3\.'
docs/ARCHITECTURE.md` → 13.

### Step 8: Tests

- `CliArgumentsTests.cs`: default scope/no expiry when both flags are omitted; `--scope
  read-write` parses to `ApiKeyScope.ReadWrite`; an unrecognised `--scope` is refused;
  `--expires 2099-01-01` parses to that date's UTC end-of-day; a malformed date
  (`2099-1-1`) is refused; a date already past (`2000-01-01`) is refused as such.
- `ApiKeyAuthenticationTests.cs`: an expired key (mint via `IssueAsync`, then
  `ExecuteUpdateAsync` its `ExpiresAt` into yesterday — mirror the existing revoked-key test)
  is refused via `/api/v1/auth/session` with detail `"That API key has expired."`; a key
  minted with a future `ExpiresAt` still authenticates; a `[DatabaseTheory]` with
  `InlineData(ApiKeyScope.Read, "read")`/`InlineData(ApiKeyScope.ReadWrite, "readWrite")` that
  mints a key of that scope and asserts `session.GetProperty("scope").GetString()` matches.
- `AuthenticationEndpointTests.cs`: in `Administrator_signs_in_and_the_session_describes_them`,
  assert `session.GetProperty("scope").ValueKind == JsonValueKind.Null`.
- New `AdministratorOrApiKeyPolicyTests.cs`, modelled on `ReaderPolicyTests.cs`'s `Build()`
  (no `ReaderHandler` registration needed — these tests never evaluate `Reader`): anonymous is
  refused; a principal with `new Claim(ClaimTypes.Role, HomonRoles.Administrator)`
  (authentication type `"Test"`) is admitted; a principal with `AuthenticationKind`/
  `ApiKeyAuthentication` claims is admitted for both `ApiKeyScope.Read` and
  `ApiKeyScope.ReadWrite` (a `[Theory]`), all against `HomonPolicies.AdministratorOrApiKey`.

**Verify**: `dotnet test tests/Homon.Api.Tests --filter "FullyQualifiedName~ApiKey|FullyQualifiedName~CliArguments|FullyQualifiedName~AuthenticationEndpoint"` → all pass.

## Test plan

Covered in full by Step 8 above. Structural pattern for every database-backed case:
`ApiKeyAuthenticationTests`'s existing `A_revoked_key_is_refused_with_a_reason` (mint, patch a
column via `ExecuteUpdateAsync`, authenticate, assert the 401 detail). Verification:
`./ci/run-ci.sh api` → all pass, 0 skipped.

## Done criteria

- [ ] `dotnet build Homon.sln --configuration Release` → 0 warnings/errors
- [ ] `dotnet run --project src/Homon.Api -- migrate` applies `AddApiKeyScopeAndExpiry` cleanly
- [ ] New/extended xunit tests exist and pass: `CliArgumentsTests`, `ApiKeyAuthenticationTests`,
      `AuthenticationEndpointTests`, `AdministratorOrApiKeyPolicyTests`
- [ ] `grep -n "AdministratorOrApiKey" src/Homon.Api/Authentication/HomonPolicies.cs` shows the
      policy registered
- [ ] `grep -c '^### 3\.' docs/ARCHITECTURE.md` → 13
- [ ] `./ci/run-ci.sh` → `PASS — web api e2e`, 0 skipped api tests
- [ ] No files outside "Scope" modified (`git status`)
- [ ] One commit, in the repo's message style, on the worktree's current branch

## STOP conditions

- Any file in "Current state" does not match its excerpt — drift since 2026-09-15; reconcile
  before writing rather than assuming this plan's shape still applies.
- `docs/ARCHITECTURE.md` does not end at §3.12 (something already claims §3.13) — re-grep, use
  the next free number, and say so in the commit message; do not overwrite an existing section.
- The generated migration touches anything beyond the two additive `ApiKeys` columns, or
  `HasConversion<string>()` + `HasDefaultValue(ApiKeyScope.ReadWrite)` produces something other
  than that (e.g. EF also rewrites `TokenId` or its index) — do not work around it silently.
- A step's verification fails twice after a reasonable fix attempt, or
  `ApiKeyRefusalMiddleware` does not, in fact, fail the whole request for every route when a
  key is carried but does not authenticate (Decision 5 depends on this) — report either rather
  than improvising a fix.

## Maintenance notes

- **002's probe-list read** (`GET /api/v1/probes`) is expected to carry
  `.RequireAuthorization(HomonPolicies.AdministratorOrApiKey)` — this plan's own contract pins
  that exact name.
- **002/003 must reuse the `TimeProvider.System` registration** in `AddHomonInfrastructure`
  rather than adding a second one — a reviewer should check for a duplicate registration in
  `AddHomonMonitoring` once 002 lands, since 002's own plan text (written before 013 existed)
  describes registering it itself.
- **008** needs a scope-aware policy: its report endpoint should require
  `ApiKeyScope.ReadWrite` (a second `RequireAssertion` checking the `HomonClaimTypes.ApiKeyScope`
  claim, since `AdministratorOrApiKey` deliberately does not); its create-key request body
  should grow `scope`/`expiresAt` mirroring `CreateApiKeyArguments`; its key list should show
  both, plus whether a key has already expired (computed at read time, not stored).
- A reviewer should check: the expiry check sits before the secret comparison (Decision 5);
  the CLI's default really is `Read` while the migration's backfill really is `ReadWrite`
  (Decisions 2/3 are opposite on purpose, easy to transpose); no code path lets an expired
  key's claims reach a handler before the 401 is written.
- Deferred: an admin page for keys (008); a `ReadWrite`-only policy (008); revoking or editing
  a key's scope/expiry after minting (008 again); clock-skew tolerance on expiry (none added —
  an expiry already carries a full day of slack, per Decision 4).
