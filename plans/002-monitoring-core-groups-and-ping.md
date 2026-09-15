# 002 — Monitoring core, probe groups and the ping probe

> **Executor instructions**: Follow this plan step by step. Run every verification command
> and confirm the expected result before moving on. If anything in "STOP conditions" occurs,
> stop and report — do not improvise. This plan does **not** ask you to update
> `plans/README.md`; the reviewer maintains that index for this run.
>
> **This plan depends on `plans/013-api-key-scopes-and-expiry.md`, which must execute and land
> first.** Nothing under `src/Homon.Domain/Monitoring/`, `src/Homon.Infrastructure/Monitoring/`,
> or `src/Homon.Api/Endpoints/{Probe,ProbeGroup,Status}Endpoints.cs` exists yet — you are not
> reconciling against a landed shape there, you are creating it. But `HomonPolicies.
> AdministratorOrApiKey`, `ApiKeyScope` and `ApiKey.ExpiresAt` (013's contract, consumed below
> in "Contract this plan consumes") must already exist when this plan starts — **STOP if they do
> not**, per "STOP conditions". Plans 003 (HTTP probe), 009 (alerts) and others in turn depend on
> the exact type names and shapes this plan commits to in "Contract this plan delivers" below; do
> not rename anything there without a strong reason, and say so in your summary if you do.
>
> **Drift check (run first)**: `git diff --stat f4e7261..HEAD -- src/Homon.Domain/Monitoring
> src/Homon.Infrastructure/Monitoring src/Homon.Infrastructure/Persistence
> src/Homon.Infrastructure/InfrastructureServiceCollectionExtensions.cs src/Homon.Api/Endpoints
> src/Homon.Api/Program.cs src/Homon.Infrastructure/Homon.Infrastructure.csproj
> Directory.Packages.props tests/Homon.Api.Tests/HomonApiFactory.cs
> src/Homon.Web/src/pages/dashboard-page.tsx
> src/Homon.Web/src/pages/admin-probes-page.tsx src/Homon.Web/src/pages/admin-home-page.tsx
> src/Homon.Web/src/lib docs/ARCHITECTURE.md docs/MODULES.md`. If this is non-empty, something
> landed between when this plan was written and now — read what changed before proceeding; a
> mismatch against the "Current state" excerpts below is a STOP condition. Plan 013 having
> already changed `src/Homon.Api/Authentication/HomonPolicies.cs` and `src/Homon.Domain/Auth/
> ApiKey.cs` before this plan starts is expected, not drift — those files are outside the path
> list above precisely because 002 only reads them, never edits them. Plan 013 has also already
> added one line to `src/Homon.Infrastructure/InfrastructureServiceCollectionExtensions.cs`
> (`services.AddSingleton(TimeProvider.System);`, the first line inside `AddHomonInfrastructure`)
> — that file **is** in the path list above because this plan edits it too (Slice 3), so confirm
> that one line is there and reuse it; do not register `TimeProvider` a second time.

## Status

- **Priority**: P1 — every later module plan (003–011) and the design pass (012) build on
  what this plan ships.
- **Effort**: L
- **Risk**: MED — the scheduler is the first `BackgroundService` in the codebase and the
  first background work that must not make the CI gate flaky.
- **Depends on**: `plans/001-scaffolding.md` (implicit, as for every plan) and
  `plans/013-api-key-scopes-and-expiry.md` (must land first — `GET /probes` admits an
  Administrator session or a valid, unexpired API key of either scope; see Decision 8 and
  "Contract this plan consumes").
- **Category**: direction
- **Planned at**: commit `f4e7261`, 2026-09-15
- **Reviewed**: 2026-09-15 (cold review-plan; execution order 013 → 002 → 003 → 006 → 007 →
  010 → 012)

## Why this matters

Monitoring is the whole point of the dashboard — "is it broken, and if so what" — and every
other module plan either depends on it directly (003–005 add probe kinds; 009 alerts on the
state machine's transitions; 012 styles the tables and status chips this plan renders as
plain HTML) or sits beside it on the same dashboard (006–011). `src/Homon.Domain/Monitoring/`
today holds only a README; nothing compiles. This plan builds the probe model, the state
machine, the scheduler, the ping runner, uptime, retention, the status endpoint, probe CRUD,
and — because the administrator wants cards arranged into named groups from day one — the
`ProbeGroup` aggregate, in one migration and one dashboard restructure, so groups are never
retrofitted onto a live schema.

## Contract this plan delivers (read by plans 003 and 009)

Not optional renaming — 003's own "Contract assumed from plan 002" table and 009's "Contract
assumed from plans 002, 003 and 008" table were written against these exact names. Both plans
verify by reading the landed code before trusting this table, but keeping it accurate here is
what makes that verification a formality instead of a rename exercise.

| Type | Shape | Where |
| --- | --- | --- |
| `Probe` | `Guid Id`, `string Name`, `string Host`, `ProbeKind Kind`, `TimeSpan PollInterval`, `int FailureThreshold`, `bool IsPaused`, `int Position`, plus live-state fields (`Status`, the two counters, `LastObservedAt`, `LastLatencyMs`, `LastDetail`) | `Homon.Domain/Monitoring/Probe.cs` |
| `ProbeKind` | `enum { Ping, Http, Smb, Snmp }` — all four members from day one, even though only `Ping` has a runner | `Homon.Domain/Monitoring/ProbeKind.cs` |
| `ProbeStatus` | `enum { Unknown, Up, Unstable, Down, Paused }` | `Homon.Domain/Monitoring/ProbeStatus.cs` |
| `ProbeObservation` | `long Id`, `Guid ProbeId`, `DateTimeOffset ObservedAt`, `bool Succeeded`, `double? LatencyMs`, `string? Detail` | `Homon.Domain/Monitoring/ProbeObservation.cs` |
| `IProbeRunner` | `ProbeKind Kind { get; }`; `Task<ProbeResult> RunAsync(Probe probe, CancellationToken)` | `Homon.Infrastructure/Monitoring/IProbeRunner.cs` |
| `ProbeResult` | `record ProbeResult(bool Succeeded, double? LatencyMs, string? Detail)` | `Homon.Infrastructure/Monitoring/ProbeResult.cs` |
| `ProbeEndpoints` | `internal static class`, `MapProbeEndpoints(this RouteGroupBuilder)`, admin CRUD under `/probes`, registered at `Program.cs:477` | `Homon.Api/Endpoints/ProbeEndpoints.cs` |
| `admin-probes-page.tsx` | A real `ProbeForm` with a kind `<select>` (one option, `"ping"`, today) and room for 003 to add a conditional HTTP fieldset beside it | `Homon.Web/src/pages/admin-probes-page.tsx` |
| The scheduler | One `BackgroundService` under `Homon.Infrastructure/Monitoring/`, computing old/new `ProbeStatus` and calling `SaveChangesAsync` at one identifiable point — the hook 009's `IProbeTransitionPublisher` attaches to | `Homon.Infrastructure/Monitoring/ProbeScheduler.cs` |
| Per-kind options room | `Probe` carries no per-kind options column yet (`Ping` needs none), but nothing here blocks 003 from adding a nullable owned `HttpOptions` property mapped with `OwnsOne(...).ToJson()` — see Decision 1 | `Homon.Infrastructure/Persistence/Configurations/ProbeConfiguration.cs` |

**Numbering note for 003/009**: plan 013 (API key scopes and expiry) runs immediately before
this plan and already claims `docs/ARCHITECTURE.md` §3.13 — `docs/ARCHITECTURE.md` therefore
ends at §3.13, not §3.12, by the time this plan starts. This plan claims §3.14 (probe groups),
§3.15 (scheduler shape) and §3.16 (uptime definition) — three new sections. 003 and 009 both
verify the next free number with `grep -n '^### 3\.' docs/ARCHITECTURE.md` before writing
rather than assuming a fixed one, so this is not a contract break, just a heads-up: 003's
secret-protector decision lands at §3.17.

## Contract this plan consumes (from plan 013)

Not implemented here — read the landed code and confirm each exists before Slice 4; if 013
named something differently, use 013's real name throughout instead of duplicating it.

| Assumed | Shape | Where |
| --- | --- | --- |
| `ApiKeyScope` | `enum { Read, ReadWrite }` | `Homon.Domain/Auth/ApiKeyScope.cs` |
| `ApiKey.Scope` / `ApiKey.ExpiresAt` | `Scope` (stored as string, per 013), `ExpiresAt` (`DateTimeOffset?`, null = never expires) added to the existing `ApiKey` entity | `Homon.Domain/Auth/ApiKey.cs` |
| Expired-key refusal | The existing API-key refusal middleware refuses an expired key exactly like a revoked one — 401 for the whole request, never demoted to anonymous (`docs/ARCHITECTURE.md` §3.3's existing rule, extended by 013 to cover expiry) | `Homon.Api/Authentication/ApiKeyRefusalMiddleware.cs` |
| `HomonClaimTypes.ApiKeyScope` | A claim on the API-key principal naming its scope | `Homon.Infrastructure/Identity/HomonClaimTypes.cs` |
| `HomonPolicies.AdministratorOrApiKey` | Admits a session in the Administrator role **or** any authenticated API-key principal, of either scope — used only for `GET /probes` and `GET /probes/{id:guid}` in this plan (Decision 8). `HomonPolicies.Reader`, `Administrator` and `ApiKey` keep their existing meaning and are otherwise unchanged. | `Homon.Api/Authentication/HomonPolicies.cs` |

Every probe **write** (create, update, delete, pause, `PUT /order`, and every `/probe-groups`
write) stays `HomonPolicies.Administrator` — a session only, exactly as `docs/ARCHITECTURE.md`
§3.3 already states ("a key that fails to authenticate fails the whole request… and never
administers"). `AdministratorOrApiKey` only ever widens *reads*.

## Decisions

### 1. Domain shape: `Probe` carries its own live state; no separate status table

`ProbeStatus` (the enum) and the "current state" live directly on `Probe` —
`Status`, `ConsecutiveSuccessCount`, `ConsecutiveFailureCount`, `LastObservedAt`,
`LastLatencyMs`, `LastDetail` — rather than a second one-to-one table. Every read that needs
"is this probe up right now" (the admin list, the status endpoint) needs exactly these six
fields alongside the probe's own columns, so splitting them out would turn every such read
into a join for no isolation benefit — nothing else ever reads live state without the probe
it belongs to. `ProbeObservation` stays a separate, append-only table: it is what retention
prunes and what uptime and the sparkline are computed over, and unlike live state it grows
without bound.

*Room for 003*: `Probe` gets no per-kind options column in this plan — `Ping` needs none, only
`Host` and the shared fields. 003 adds `public HttpProbeOptions? HttpOptions { get; set; }` to
this same class and maps it with `OwnsOne(p => p.HttpOptions, http => { http.ToJson(); … })`
in `ProbeConfiguration.cs` — an additive migration, not a reshape of what this plan ships.

*Rejected*: a separate `ProbeState` table keyed by `ProbeId` — every dashboard and admin read
already needs both halves together, so the join would be mandatory on the hot path and the
only thing gained is a table that is empty exactly when its `Probe` row is not, which is not
independence, just distance.

Enum storage: both `ProbeKind` and `ProbeStatus` map to `varchar` via `HasConversion<string>()`
in their configurations, not the default `int`. Readable in `psql` without a lookup table, and
immune to a later PR inserting a member in the middle of the enum and silently reassigning
every stored value. No convention existed before this plan; it is the one later modules
(008's `BackupJobState`, for one) can follow by default.

### 2. The state machine: two pure functions, no database, no clock

```csharp
namespace Homon.Domain.Monitoring;

/// Pure — no I/O, no clock. The brief's rule, verbatim: N-or-more consecutive failures is
/// down, N-or-more consecutive successes is up, anything in between is unstable.
public static class ProbeStateMachine
{
    /// One poll outcome. Resets the opposite streak to zero — a single success clears a
    /// failure streak and vice versa, so "consecutive" means what it says.
    public static (int Successes, int Failures, ProbeStatus Status) Apply(
        int consecutiveSuccesses, int consecutiveFailures, int failureThreshold, bool succeeded)
    {
        var successes = succeeded ? consecutiveSuccesses + 1 : 0;
        var failures = succeeded ? 0 : consecutiveFailures + 1;

        return (successes, failures, Derive(successes, failures, failureThreshold, everPolled: true));
    }

    /// Recomputes Status from the counters alone, without a poll — used when an admin
    /// unpauses a probe or edits its FailureThreshold (Decisions below). Not a transition in
    /// its own right: it just re-reads what the counters already imply under the current
    /// (possibly just-changed) threshold.
    public static ProbeStatus Derive(
        int consecutiveSuccesses, int consecutiveFailures, int failureThreshold, bool everPolled)
    {
        if (!everPolled)
        {
            return ProbeStatus.Unknown;
        }

        if (consecutiveFailures >= failureThreshold)
        {
            return ProbeStatus.Down;
        }

        if (consecutiveSuccesses >= failureThreshold)
        {
            return ProbeStatus.Up;
        }

        return ProbeStatus.Unstable;
    }
}
```

**The exact transition table** (`failureThreshold = N`):

| From | Poll result | Streak after | To |
| --- | --- | --- | --- |
| Unknown (never polled) | any | 1 | Unstable, unless N = 1 (then Up/Down immediately) |
| Up / Unstable / Down | success, streak < N | successes < N | Unstable |
| Up / Unstable / Down | success, streak ≥ N | successes ≥ N | Up |
| Up / Unstable / Down | failure, streak < N | failures < N | Unstable |
| Up / Unstable / Down | failure, streak ≥ N | failures ≥ N | Down |
| Paused | (not polled — the scheduler skips paused probes) | unchanged | Paused |

**Paused is not derived — it is set directly.** `Probe.Pause()` sets `IsPaused = true` and
`Status = ProbeStatus.Paused` without touching the counters or `LastObservedAt`; the scheduler
never polls a paused probe (Decision 3), so nothing would call `Apply` for it anyway.
`Probe.Unpause()` sets `IsPaused = false` and calls `Derive` on the *existing* counters — a
probe paused while `Down` comes back `Down`, not `Unknown`, because its last real observation
still says so. It does **not** force an immediate poll; the scheduler picks it back up on its
own next tick (Decision 3), which in practice is within `MonitoringOptions.TickInterval`
(seconds), not up to a full `PollInterval` away.

**Editing `FailureThreshold`** re-derives `Status` from the unchanged counters under the new
threshold, unless the probe is paused (in which case the new threshold applies the next time
it is unpaused or polled). Lowering the threshold below the current failure streak flips a
probe straight to `Down` without waiting for the next poll; raising it can pull a probe back
from `Down`/`Up` into `Unstable`. This is deliberate: the number on screen should never lag an
admin's own configuration change.

### 3. Scheduler shape: one tick-based `BackgroundService`, not per-probe timers

`docs/ARCHITECTURE.md` §4 named this the open question. **Chosen: `ProbeScheduler`, a single
`BackgroundService` that wakes on a fixed tick (`MonitoringOptions.TickInterval`, default 5 s),
queries the database each tick for probes that are due, and dispatches each due probe's poll
under a bounded-concurrency gate.**

```csharp
public sealed class ProbeScheduler(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<MonitoringOptions> options,
    TimeProvider timeProvider,
    ILogger<ProbeScheduler> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, byte> _inFlight = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (options.CurrentValue.SchedulerEnabled)
            {
                try
                {
                    await TickAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Shutting down — the loop's own check ends it, not this catch.
                }
                catch (Exception ex)
                {
                    // TickAsync's own probe-selection query (inside the try below) is NOT
                    // wrapped the way PollAndPersistAsync's runner call is — a transient
                    // Postgres blip here would otherwise be an unhandled exception out of
                    // ExecuteAsync, and BackgroundServiceExceptionBehavior defaults to
                    // StopHost: one bad tick would stop the whole API, not just monitoring.
                    // Log and try again next tick instead.
                    LogTickFailed(logger, ex);
                }
            }

            await Task.Delay(options.CurrentValue.TickInterval, timeProvider, stoppingToken);
        }
    }

    internal async Task TickAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        List<Guid> due;

        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
            due = await database.Probes
                .Where(p => !p.IsPaused)
                .Where(p => p.LastObservedAt == null || now - p.LastObservedAt >= p.PollInterval)
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);
        }

        using var gate = new SemaphoreSlim(options.CurrentValue.MaxConcurrentPolls);

        var tasks = due
            .Where(id => _inFlight.TryAdd(id, 0)) // never overlap a poll already running
            .Select(async id =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    await PollAndPersistAsync(id, cancellationToken);
                }
                finally
                {
                    gate.Release();
                    _inFlight.TryRemove(id, out _);
                }
            });

        await Task.WhenAll(tasks);
    }

    private async Task PollAndPersistAsync(Guid probeId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        var runners = scope.ServiceProvider.GetServices<IProbeRunner>();

        var probe = await database.Probes.FindAsync([probeId], cancellationToken);
        if (probe is null || probe.IsPaused)
        {
            return; // deleted or paused since the scan above
        }

        var runner = runners.FirstOrDefault(r => r.Kind == probe.Kind);
        if (runner is null)
        {
            LogNoRunnerForKind(logger, probe.Kind);
            return;
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(options.CurrentValue.PerPollTimeout);

        ProbeResult result;
        try
        {
            result = await runner.RunAsync(probe, timeoutSource.Token);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            result = new ProbeResult(false, null, "probe timed out");
        }
        catch (Exception ex)
        {
            // A broken runner must never take the whole tick down with it — see Decision 3's
            // "survives runner exceptions" requirement.
            LogRunnerThrew(logger, probe.Id, ex);
            result = new ProbeResult(false, null, $"probe runner threw: {ex.GetType().Name}");
        }

        var observedAt = timeProvider.GetUtcNow();
        // oldStatus and probe.Status (after RecordObservation) are both in scope here, right
        // before the save — this is the hook plan 009's IProbeTransitionPublisher attaches to.
        var oldStatus = probe.Status;
        probe.RecordObservation(result.Succeeded, result.LatencyMs, result.Detail, observedAt);
        database.ProbeObservations.Add(new ProbeObservation
        {
            ProbeId = probe.Id,
            ObservedAt = observedAt,
            Succeeded = result.Succeeded,
            LatencyMs = result.LatencyMs,
            Detail = result.Detail,
        });

        await database.SaveChangesAsync(cancellationToken);
    }
}
```

**Rejected: a `System.Threading.Timer`/`PeriodicTimer` per probe.** It needs explicit lifecycle
management on every create, edit, pause and delete (register, reset, or unregister a timer),
which is a second source of truth for "what probes exist" running beside the database — a
probe created while the scheduler is mid-tick would need its own timer wired up out of band.
The tick-scan design "picks up create/edit/delete/pause without restart" for free: the next
scan simply sees the new state. It is also the more testable shape — one `TimeProvider`
governs the whole scheduler, rather than N independent timer objects.

Never-overlapping polls of the same probe: the `_inFlight` set. Bounded concurrency:
`MaxConcurrentPolls` (default 8). Per-poll timeout: `PerPollTimeout` (default 30 s), wrapping
every runner call regardless of kind — the ping runner has its own shorter internal timeout
too (Decision 4), but this outer one is what protects the scheduler from a kind whose runner
hangs entirely. Survives runner exceptions: the `catch (Exception ex)` records a failed
observation and logs, rather than propagating — one broken probe never stops the tick.

**Not run against the live network in the xunit suite.** `MonitoringOptions.SchedulerEnabled`
(default `true`) is read on every tick from `IOptionsMonitor<MonitoringOptions>`, so
`ApiDatabaseFactory` — which every `[DatabaseFact]` class shares — sets it `false` in
configuration. Nothing about the scheduler class itself is disabled; the loop still runs and
still sleeps, it just never ticks. `ProbeSchedulerTests` exercises `TickAsync` directly, with
a fake `IProbeRunner` and a hand-written `FixedTimeProvider : TimeProvider`, never through the
hosted loop. **The e2e suite runs it for real** — `Monitoring:SchedulerEnabled` is left at its
default `true` there, and determinism comes the way the groups tests already do it: seeded
probes are paused, so the scheduler has nothing to poll.

### 4. Ping runner: `System.Net.NetworkInformation.Ping` behind a fake-able seam

```csharp
namespace Homon.Infrastructure.Monitoring;

public interface IIcmpPinger
{
    Task<IcmpPingReply> SendAsync(string host, TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed record IcmpPingReply(bool Succeeded, double? RoundtripMs, string? FailureReason);

/// The one place System.Net.NetworkInformation.Ping is touched. PingProbeRunnerTests fakes
/// IIcmpPinger instead, so no unit test ever sends real ICMP.
public sealed class SystemIcmpPinger : IIcmpPinger
{
    public async Task<IcmpPingReply> SendAsync(
        string host, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var ping = new Ping();

        try
        {
            var reply = await ping
                .SendPingAsync(host, (int)timeout.TotalMilliseconds)
                .WaitAsync(cancellationToken);

            return reply.Status == IPStatus.Success
                ? new IcmpPingReply(true, reply.RoundtripTime, null)
                : new IcmpPingReply(false, null, reply.Status.ToString());
        }
        catch (PingException ex) when (ex.InnerException is SocketException
            { SocketErrorCode: SocketError.AccessDenied })
        {
            // The rootless-Docker ICMP case docs/deployment-runbook.md's "ICMP" section
            // already documents — the container's net.ipv4.ping_group_range is empty.
            return new IcmpPingReply(
                false, null, "permission denied — check net.ipv4.ping_group_range");
        }
        catch (PingException ex)
        {
            return new IcmpPingReply(false, null, ex.InnerException?.Message ?? ex.Message);
        }
    }
}

public sealed class PingProbeRunner(IIcmpPinger pinger) : IProbeRunner
{
    /// Not admin-configurable this phase — same posture as 003's HttpProbeRunner constant.
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public ProbeKind Kind => ProbeKind.Ping;

    public async Task<ProbeResult> RunAsync(Probe probe, CancellationToken cancellationToken)
    {
        var reply = await pinger.SendAsync(probe.Host, Timeout, cancellationToken);

        return reply.Succeeded
            ? new ProbeResult(true, reply.RoundtripMs, null)
            : new ProbeResult(false, null, $"ping: {reply.FailureReason}");
    }
}
```

Detail strings: `null` on success (nothing worth reporting); `"ping: {IPStatus}"` on a normal
failure (e.g. `"ping: TimedOut"`, `"ping: DestinationHostUnreachable"`); the permission-denied
sentence above when the container's ICMP group range is empty; `"ping: {message}"` for
anything else `PingException` wraps. `LatencyMs` is `reply.RoundtripTime`, milliseconds,
exactly what the 30-day sparkline and per-observation `LatencyMs` column store.

Register `services.AddSingleton<IIcmpPinger, SystemIcmpPinger>();` and
`services.AddScoped<IProbeRunner, PingProbeRunner>();` — scoped so that 003's `HttpProbeRunner`
(which needs a scoped `ISecretProtector`) can register the same way without the scheduler
caring which lifetime a given kind picked.

### 5. Uptime: a per-probe success ratio; the stat strip averages probes, not observations

`docs/ARCHITECTURE.md` §4's second open question. **Uptime, per probe, is
`successCount / totalCount` over `ProbeObservation` rows within the retained 30-day window,
as a percentage, rounded to two decimals; `null` (rendered `—`) when the count is zero.**

```csharp
public static class ProbeUptimeCalculator
{
    public static double? Calculate(int successCount, int totalCount) =>
        totalCount == 0 ? null : Math.Round(100.0 * successCount / totalCount, 2);
}
```

*Rejected: time-weighted uptime* (integrating success/failure duration between consecutive
observations). A probe's `PollInterval` can change mid-window and pausing leaves gaps with no
observations at all — reconciling both into a single duration-weighted number is real
complexity for a figure the brief only asks to be "computed over the retained observation
window," and a plain count ratio reads the same to a family glancing at `98.32%`.

**The stat strip's aggregate uptime is the mean of each probe's own `UptimePercent`, not a
single ratio pooled across every observation row.** A probe polled every 15 seconds would
otherwise contribute roughly sixty times as many rows as one polled every 15 minutes, and
silently dominate the number the whole household reads as "is everything basically fine" —
averaging per-probe percentages weights every service equally, matching "one row per probe."
Probes with no observations at all (`UptimePercent == null`) are excluded from the average,
not counted as 0%; an average over zero eligible probes is `null` (`—`). Per the groups
section (Decision 8), a probe belonging to two groups is still one probe in this average.

### 6. Retention: an hourly sweep, `ExecuteDeleteAsync`, one composite index

```csharp
public sealed class ProbeObservationRetentionService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<MonitoringOptions> options,
    TimeProvider timeProvider,
    ILogger<ProbeObservationRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (options.CurrentValue.RetentionEnabled)
            {
                try
                {
                    await SweepAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Shutting down — the loop's own check ends it, not this catch.
                }
                catch (Exception ex)
                {
                    // Same reasoning as ProbeScheduler.ExecuteAsync's catch: an unhandled
                    // exception here would stop the whole host (BackgroundServiceException
                    // Behavior defaults to StopHost), for a job that can simply try again next
                    // hour.
                    LogSweepFailed(logger, ex);
                }
            }

            await Task.Delay(options.CurrentValue.RetentionSweepInterval, timeProvider, stoppingToken);
        }
    }

    internal async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<HomonDbContext>();
        var cutoff = timeProvider.GetUtcNow() - TimeSpan.FromDays(options.CurrentValue.RetentionWindowDays);

        return await database.ProbeObservations
            .Where(o => o.ObservedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
```

Runs once an hour (`MonitoringOptions.RetentionSweepInterval`, default `TimeSpan.FromHours(1)`)
— frequent enough that the retained window never visibly overshoots 30 days, infrequent enough
that it costs nothing at Homon's single-household scale. One `DELETE … WHERE "ObservedAt" <
@cutoff`, no batching: `ExecuteDeleteAsync` issues one SQL statement and Homon's observation
volume (one row per probe per poll, one household) never approaches a size where that
statement needs chunking. `ProbeObservationConfiguration.cs` adds a composite index on
`(ProbeId, ObservedAt)` — the same index the uptime and sparkline queries need (both filter by
`ProbeId` and a date range) and the one the retention sweep's `WHERE` clause scans; one index
serves all three, and Postgres does not index a foreign key on its own.

Applies uniformly to every probe kind, not just `Ping`: every kind writes one
`ProbeObservation` row per poll (Decision 5 defines uptime the same way for all of them), so
retention and uptime never special-case `Kind`.

### 7. Probe groups (unchanged from the original brief-independent decision, tightened)

The administrator wants cards arranged into named groups they create themselves — "Hosts" for
servers and network devices, "Services" for Jellyfin, Immich, Audiobookshelf checks, say. A
group does not care what kind of probe is inside it. Because monitoring did not exist before
this plan, groups ship in the same migration and the dashboard is grouped from its first
commit rather than retrofitted onto a flat list later.

- **Many-to-many.** A probe can be in several groups (a NAS in both "Hosts" and "Storage").
- **Optional.** A probe in no group appears in a final section. With zero groups, the
  dashboard is exactly one "Services" table, matching the Status board direction unmodified.
- **`Probe.Position`** (an admin-set global order) governs the ungrouped section, and the
  whole dashboard when no groups exist at all.

```csharp
public sealed class ProbeGroup
{
    public const int NameMaxLength = 60;

    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public int Position { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<ProbeGroupMembership> Members { get; set; } = [];

    public static string Normalize(string name) => name.Trim().ToUpperInvariant();

    /// Updates the memberships already tracked, never removes and re-adds the same
    /// (GroupId, ProbeId) key — EF would throw a composite-key identity conflict — and
    /// renumbers every remaining position 0..n-1.
    public void ReplaceMembers(IReadOnlyList<Guid> orderedProbeIds)
    {
        var existing = Members.ToDictionary(m => m.ProbeId);
        var next = new List<ProbeGroupMembership>(orderedProbeIds.Count);

        for (var i = 0; i < orderedProbeIds.Count; i++)
        {
            var probeId = orderedProbeIds[i];

            if (!existing.TryGetValue(probeId, out var membership))
            {
                membership = new ProbeGroupMembership { GroupId = Id, ProbeId = probeId };
            }

            membership.Position = i;
            next.Add(membership);
        }

        Members.Clear();
        Members.AddRange(next);
    }

    /// Appends at the end; does nothing if the probe is already a member.
    public void Include(Guid probeId)
    {
        if (Members.Any(m => m.ProbeId == probeId))
        {
            return;
        }

        Members.Add(new ProbeGroupMembership { GroupId = Id, ProbeId = probeId, Position = Members.Count });
    }

    public void Exclude(Guid probeId) => Members.RemoveAll(m => m.ProbeId == probeId);
}

public sealed class ProbeGroupMembership
{
    public Guid GroupId { get; set; }
    public Guid ProbeId { get; set; }
    public int Position { get; set; }
    public ProbeGroup Group { get; set; } = null!;
}
```

- Deleting a group removes its memberships and leaves the probes alone (cascade FK on
  `GroupId`). Deleting a probe removes it from every group (cascade FK on `ProbeId`).
- `Position` is a sort key, not a dense index. Reads order by `(Position, Id)`; every write
  that rewrites a list renumbers it. **No unique index on `Position`** anywhere in this plan
  (`Probe`, `ProbeGroup`, `ProbeGroupMembership`): PostgreSQL checks a non-deferrable unique
  constraint row by row, so swapping two positions inside one transaction would fail partway
  through.
- **The ungrouped section is labelled "Services" when it is the only section shown, "Other"
  otherwise.** A group may also be named "Other"; the name is not reserved.
- Group names are unique ignoring case (via `NormalizedName`, the same `Normalized*` pattern
  Identity's own tables already use — not `citext`/ICU collations, kept consistent with the
  rest of the schema), at most 60 characters, and groups do not nest.
- **Empty groups are left out of the status payload.** A group whose probes are all paused is
  not empty.

*Rejected*: a single `GroupId` column on `Probe` — a probe could not appear in two groups
without duplicating the row, which means polling it twice; groups derived from `ProbeKind` —
the admin explicitly wants to mix kinds inside one group; free-text tags — no order, no
heading identity to key an `id` off; nested groups — nothing in the brief asks for them.

### 8. Probe CRUD: who may `GET /probes`, and the shape of the rest

**`GET /probes` and `GET /probes/{id:guid}` require `HomonPolicies.AdministratorOrApiKey`
(plan 013): an Administrator session, or any valid, unexpired, unrevoked API key of either
scope (`Read` or `ReadWrite`).** Not `Reader` — `/probes` carries `Host`, an internal hostname
or LAN IP an anonymous visitor has no reason to see, and by default
(`RequireSignInForReaders = false`) "anonymous" is every visitor; and not
`Administrator`-only either — the maintainer wants a household script minted with a read-only
key (say, a status board on a second device, or a monitoring tool that shells out to the API)
able to list probes without needing a session at all. **Every probe *write* stays
`HomonPolicies.Administrator`** — create, update, delete, pause and `PUT /order` all require a
session in the Administrator role; an API key, of either scope, is refused on every one of
them with 403, exactly as `docs/ARCHITECTURE.md` §3.3 already promises ("a key… never
administers"). `/probe-groups` is unaffected by this decision — its `GET` stays `Reader` (see
below), since it carries no field as sensitive as a raw `Host`.

```
GET    /probes                AdministratorOrApiKey   → ProbeResponse[], in Position order
GET    /probes/{id:guid}      AdministratorOrApiKey   → ProbeResponse
POST   /probes                Administrator   { name, host, kind, pollIntervalSeconds,
                                                 failureThreshold, groupIds[] } → 201
PUT    /probes/{id:guid}      Administrator   same body minus kind (immutable) → 200
PUT    /probes/{id:guid}/pause Administrator  { isPaused: bool } → 200, ProbeResponse
DELETE /probes/{id:guid}      Administrator   → 204 or 404
PUT    /probes/order          Administrator   { probeIds: Guid[] } → 204
```

Validation (`TypedResults.ValidationProblem`, matching the groups endpoints' own convention):

- `name`: required after trim, ≤ `Probe.NameMaxLength` (100).
- `host`: required after trim, ≤ `Probe.HostMaxLength` (255). No scheme/URI validation here —
  `Host` is a bare hostname or IP for every kind this plan or 003–005 add; HTTP's own path and
  scheme live in 003's `HttpProbeOptions`, not this field.
- `kind`: only `"ping"` is accepted; anything else (including the reserved `"http"`, `"smb"`,
  `"snmp"` names) is a 400 with a detail noting the kind ships with a later plan. `kind` is
  absent from the `PUT` body entirely — a probe's kind cannot change after creation, since
  003–005 attach kind-specific options that a kind change would orphan or leave missing.
- `pollIntervalSeconds`: integer, `Probe.MinPollIntervalSeconds` (15) to
  `Probe.MaxPollIntervalSeconds` (86 400 — one day).
- `failureThreshold`: an integer from `Probe.MinFailureThreshold` (1) to
  `Probe.MaxFailureThreshold` (10) when present. **On `POST`, it is optional** — omitted or
  `null` defaults to `Probe.DefaultFailureThreshold` (2), which is also the SPA form's initial
  value (Decision 10). **On `PUT` it is required**, matching every other field in the "same
  body minus kind" update — an edit replaces the whole configurable shape rather than patching
  one field. 1 is a valid value on either verb: it makes that probe strictly binary (`Unstable`
  is never reachable for it, Decision 2's transition table), which is the admin's call per
  probe, not a floor this plan enforces.
- `groupIds`: every id must name an existing `ProbeGroup`; unknown ids are a 400 naming the
  field, matching the groups endpoints' own `members`/`groupIds` validation.

`POST` assigns `Position` as the current maximum plus one (0 if none) and applies `groupIds`
by loading every `ProbeGroup`, calling `Include`/`Exclude` per Decision 7's `ProbeGroup`
methods — never re-adding a membership that already exists. `PUT` does the same diff against
the probe's current memberships, so a group added to the set calls `Include`, one removed
calls `Exclude`, and positions inside groups the probe stays in are untouched. `PUT
.../pause` calls `Probe.Pause()`/`Probe.Unpause()` (Decision 2) rather than setting `IsPaused`
directly, so the status re-derivation happens in one place. `DELETE` relies on cascade deletes
for `ProbeObservation` rows (`ProbeId` FK) and `ProbeGroupMembership` rows (`ProbeId` FK) —
no manual cleanup in the handler. `PUT /order` loads every probe and renumbers `Position`
0..n-1 in the given order, rejecting a list that is not an exact permutation of existing ids
(missing, extra or duplicate) with a `ValidationProblem`, the same rule the groups endpoints'
own `order` route already uses.

### 9. `GET /api/v1/status`: the groups payload, plus everything the dashboard renders

```csharp
public sealed record StatusResponse(
    StatusTotals Totals,
    ProbeStatusResponse[] Probes,
    ProbeGroupSummary[] Groups,
    Guid[] UngroupedProbeIds,
    DateTimeOffset GeneratedAt);

public sealed record StatusTotals(
    int Up, int Unstable, int Down, int Unknown, int Paused, double? UptimePercent);

public sealed record ProbeStatusResponse(
    Guid Id, string Name, ProbeKind Kind, ProbeStatus State, string? Detail,
    DateTimeOffset? LastCheckedAt, double? UptimePercent, double[] Sparkline);

public sealed record ProbeGroupSummary(Guid Id, string Name, Guid[] ProbeIds);
```

`ProbeKind` and `ProbeStatus` serialize as their camelCase names (`Program.cs`'s
`JsonStringEnumConverter`, already global) — `"kind": "ping"`, `"state": "unstable"` — so the
SPA never duplicates the enum as a second set of magic strings.

- `Totals` are computed from `Probes`, so a probe in two groups counts once (Decision 7).
  `UptimePercent` here is the Decision 5 average, `null` when no probe has any observations.
- `Probes` lists every probe once, in `Position` order.
- `Groups` lists only non-empty groups (Decision 7), each `{ id, name, probeIds[] }` in group
  order; `UngroupedProbeIds` lists the rest, in `Position` order.
- `LastCheckedAt` mirrors `Probe.LastObservedAt`; `UptimePercent` is Decision 5's per-probe
  ratio; `Sparkline` is populated only for `Kind == Ping` — every other kind gets `[]`.
- **Sparkline downsampling**: the 30-day window split into `MonitoringOptions.SparklineBucketCount`
  (default 30 — one bucket per day) equal-width buckets; each point is the mean `LatencyMs` of
  *successful* observations in that bucket. A bucket with no successful observations is
  omitted from the array rather than emitted as a zero or a null placeholder — a shorter array
  is a real gap a sparkline renderer can skip over; a fabricated zero would read as "instant."
- **`GeneratedAt`** is `TimeProvider.GetUtcNow()` at request time — the field plan 012's own
  Decision on the banner's "refreshed N s ago" text was left open for; this plan is what
  supplies the data source. Nothing in this plan renders it — plan 012 formats it into the
  banner.

Computed with two grouped queries against `HomonDbContext` (all probes' observation counts and
success counts for the uptime window in one query, keyed by `ProbeId`; the same shape again for
the ping sparkline buckets), not once per probe — a per-probe query here would turn `GET
/status` into an N+1 the moment the household has more than a handful of probes.

**SPA polling.** `useStatus()` polls every 30 seconds (`refetchInterval: 30_000` on the
`useQuery`) — frequent enough that a family glancing at the dashboard sees a state change
inside a poll interval or two, infrequent enough that forty open browser tabs on a home
network do not turn into a request storm.

### 10. SPA: the kind selector stays extensible, formatting stays minimal until plan 012

`admin-probes-page.tsx`'s `ProbeForm` gets a real `<select>` for `kind` with exactly one
`<option value="ping">Ping (ICMP)</option>` today — not a hidden/fixed field — so 003 adds its
`<option value="http">` beside it rather than introducing the element. The per-kind fieldset
region is structured (a conditional block keyed on the selected kind) even though nothing
renders inside it yet for `Ping`, for the same reason.

**Uptime, "checked" and the sparkline cell are rendered with the plainest text that is
correct, not styled or precisely formatted** — plan 012's Decision 6 explicitly owns
`format-uptime.ts` and `sparkline.tsx` as its own Slice C primitives, built *after* this
plan's markup exists. This plan therefore:

- formats uptime inline in `dashboard-page.tsx` (`v === null ? '—' : \`${v.toFixed(2)}%\`}`) —
  a one-line duplicate of what 012 will extract into `lib/format-uptime.ts`, not a blocker;
- renders `LastCheckedAt` through a small pure helper, `formatCheckedAt(lastCheckedAt, now)`,
  exported from `lib/status.ts` (`'Never'` when null, otherwise a plain relative string —
  `"2 min ago"`, `"1 h ago"`) so it is independently unit-testable rather than inlined;
- renders the "30 days" column's `<td>` empty for now (the header cell says "30 days" so
  012's Slice D2 only has to fill it in, not restructure the table).

## Defaults taken (change before implementation if wanted)

- `Probe.MinPollIntervalSeconds = 15`, `MaxPollIntervalSeconds = 86_400` (one day).
- `Probe.MinFailureThreshold = 1`, `MaxFailureThreshold = 10`, `DefaultFailureThreshold = 2` —
  a new probe whose `POST` body omits `failureThreshold` gets 2; the admin may set anything
  1–10 per probe thereafter (confirmed by the maintainer).
- `MonitoringOptions.TickInterval = 5s`, `MaxConcurrentPolls = 8`, `PerPollTimeout = 30s`,
  `RetentionSweepInterval = 1h`, `RetentionWindowDays = 30`, `SparklineBucketCount = 30`.
- `PingProbeRunner.Timeout = 5s`, a constant, not admin-configurable this phase — same posture
  003 takes for `HttpProbeRunner.RequestTimeout`.
- `GET /probes`/`GET /probes/{id}` require `HomonPolicies.AdministratorOrApiKey` (plan 013);
  every probe write stays `Administrator`-session-only (confirmed by the maintainer).
- The stat strip's aggregate uptime averages each probe's own `UptimePercent` equally
  (confirmed by the maintainer).
- Unpausing a probe does not force an immediate poll; it becomes due on the scheduler's next
  tick (seconds, not up to a full `PollInterval`) (confirmed by the maintainer).
- The ungrouped section is labelled "Other" when at least one named group exists, "Services"
  otherwise. A group may also be named "Other"; the name is not reserved.
- `ProbeObservation.Id` is a `long` identity column (Npgsql's default `GENERATED BY DEFAULT AS
  IDENTITY` for a `long` key with `ValueGeneratedOnAdd()` — not literally `bigserial`, no
  provider option changes that here), not a `Guid` — the one entity in this plan
  where an append-only, time-ordered, high-volume table makes a random-order key actively
  worse (index bloat, no clustering benefit) for no benefit a surrogate `Guid` usually buys.

## Current state

| File | Role |
| --- | --- |
| `src/Homon.Domain/Monitoring/README.md` | The module slot; rewritten in Slice 1. |
| `src/Homon.Api/Program.cs:476-479` | The commented module-registration block: `v1.MapProbeEndpoints();   v1.MapStatusEndpoints();   v1.MapLinkEndpoints(); …`. This plan activates the first two and adds a new `v1.MapProbeGroupEndpoints();` line beside them. |
| `src/Homon.Api/Authentication/HomonPolicies.cs` | `Reader`, `Administrator`, `ApiKey` are unchanged; plan 013 (landed before this plan starts) adds `AdministratorOrApiKey` — use it for `GET /probes`/`GET /probes/{id}` only (Decision 8); no new policy is added by this plan. |
| `src/Homon.Domain/Auth/{ApiKey,ApiKeyScope}.cs` | Plan 013's `ApiKey.Scope`/`ApiKey.ExpiresAt` and the `ApiKeyScope` enum — read only, never modified here; see "Contract this plan consumes." |
| `src/Homon.Infrastructure/Persistence/HomonDbContext.cs:9-11,22` | Class comment ("the API keys, and — as each module lands — the probes, links, pages and backup reports.") and the sole `DbSet<ApiKey> ApiKeys`. Add `DbSet<Probe>`, `DbSet<ProbeObservation>`, `DbSet<ProbeGroup>` and update the comment. |
| `src/Homon.Infrastructure/Persistence/Configurations/ApiKeyConfiguration.cs` | The only existing `IEntityTypeConfiguration<T>` — model every new configuration class on its shape (`internal sealed class`, one file per entity, rejected alternatives as comments). |
| `src/Homon.Infrastructure/InfrastructureServiceCollectionExtensions.cs` | Plan 013 lands first and adds `services.AddSingleton(TimeProvider.System);` as the very first line inside `AddHomonInfrastructure`, before the (still present) three `AddXxx(this IServiceCollection …)` calls (`AddHomonDatabase`/`AddHomonEmail`/`AddAdministrator`) — every line number below `:28` in the pre-013 file has shifted by one. Add a matching `AddHomonMonitoring`, called alongside the rest, and **reuse the existing `TimeProvider.System` registration — do not add a second one** inside `AddHomonMonitoring` (013's own Maintenance notes flag this exact risk). |
| `src/Homon.Infrastructure/Homon.Infrastructure.csproj` | Plain `Microsoft.NET.Sdk` (not `Microsoft.NET.Sdk.Web`) — no `Microsoft.Extensions.Hosting.Abstractions` and no `InternalsVisibleTo` are on its dependency graph today (confirmed by reading `obj/project.assets.json`). Both are needed for the scheduler/retention `BackgroundService`s and their tests — see Slice 3. |
| `Directory.Packages.props` | Central package versions, all `10.0.11` for the `Microsoft.*` family here. Add `Microsoft.Extensions.Hosting.Abstractions` at the same version — see Slice 3. |
| `src/Homon.Domain/Auth/ApiKey.cs` | The one existing domain entity — mutable class, `public` auto-properties, XML doc comments, constants for length bounds (`NameMaxLength`, `TokenIdLength`). Model every new entity on this shape. |
| `src/Homon.Api/Endpoints/MetaEndpoints.cs`, `AuthenticationEndpoints.cs` | The two existing endpoint classes — `internal static class XxxEndpoints`, `MapXxxEndpoints(this RouteGroupBuilder)`, `TypedResults`, mutating endpoints require `application/json` (`AuthenticationEndpoints.cs:149-161`'s explicit check is the pattern for a body-less mutation like `PUT /probes/{id}/pause` if its body could ever be empty — here it always carries `{ isPaused }`, so ordinary model binding already enforces the content type). |
| `src/Homon.Web/src/pages/dashboard-page.tsx` (24 lines, full file read) | Today: `<h1>Dashboard</h1>` then two hard-coded sections, `services-heading` and `links-heading`. This plan replaces the Services section with the grouped rendering; the Links section (still a placeholder) is untouched — plan 006 fills it in later, and must find `<section aria-labelledby="links-heading">` unchanged. |
| `src/Homon.Web/src/pages/admin-probes-page.tsx` (12 lines, full file read) | The whole placeholder this plan replaces with a real `ProbeForm` and list. |
| `src/Homon.Web/src/pages/admin-home-page.tsx` (30 lines, full file read) | `<nav aria-label="Admin sections">` with `Probes`/`Links`/`Pages`/`API keys`. Add a `Probe groups` `<li>` right after `Probes`. |
| `src/Homon.Web/src/lib/meta.ts`, `session.ts` | Exemplars for every new `lib/*.ts` file: a typed interface doc-commented to the C# response type, a query-key constant, `useQuery`/`useMutation` with `queryClient.invalidateQueries` on success. |
| `src/Homon.Web/src/lib/api.ts` | `apiFetch<T>`, `ApiError`, `problemDetail(error)` — reuse; only sets `Content-Type: application/json` when `init.body` is present. |
| `src/Homon.Web/src/test/fetch.ts` | `stubFetch(routes)` — the only network stub in the repo (no MSW). |
| `src/Homon.Web/App.tsx` | Route table: admin pages are lazy-imported and rendered inside `<RequireAdministrator>`. Add `AdminProbeGroupsPage` the same way, at `admin/probe-groups`. |
| `src/Homon.Web/e2e/helpers.ts:11-17` | `ADMIN_ROUTES` — add `/admin/probe-groups`, which brings the phone-width overflow check with it for free. |
| `src/Homon.Web/e2e/admin.spec.ts:17` | Iterates `['Probes', 'Links', 'Pages', 'API keys']` against the admin nav — add `'Probe groups'`. |
| `src/Homon.Web/e2e/layout.spec.ts:28-34` | Asserts `h2` "Services" and "Links" on `/` today. With zero probes and zero groups this plan's dashboard still renders an `h2 id="services-heading"` reading "Services" (Decision 7's default label), so this passes unmodified as long as no earlier spec left groups behind in the shared e2e database. |
| `tests/Homon.Api.Tests/HomonApiFactory.cs:38-53` | Every test factory's common base (`DatabaseBackedFactory` → `ApiDatabaseFactory` both derive from it); its connection string (`:41-42`) deliberately points at an unreachable database. Add `["Monitoring:SchedulerEnabled"] = "false"`, `["Monitoring:RetentionEnabled"] = "false"` to its `ConfigureAppConfiguration` dictionary — see Slice 3. |
| `tests/Homon.Api.Tests/ApiDatabaseFactory.cs`, `DatabaseBackedFactory.cs`, `TestDatabase.cs`, `DatabaseFactAttribute.cs` | The per-class PostgreSQL clone fixture; the new `AddMonitoring` migration is picked up automatically once it exists. No configuration change needed here — it inherits `HomonApiFactory`'s scheduler/retention overrides above. |
| `tests/Homon.Api.Tests/ApiKeyRulesTests.cs` | Shape for a pure, no-fixture test class — model `ProbeStateMachineTests`/`ProbeGroupTests` on it. |
| `tests/Homon.Api.Tests/ApiKeyAuthenticationTests.cs`, `MetaEndpointTests.cs:91-101` | Auth-matrix and `ConfiguredFactory : HomonApiFactory` patterns for the reader-switch tests. |
| `src/Homon.Web/playwright.config.ts` | No change needed — the e2e API host's environment already passes plain `KEY=value` pairs; `Monitoring:SchedulerEnabled` needs no override since its default (`true`) is exactly what the e2e suite wants (Decision 3). |
| `docs/ARCHITECTURE.md` | Plan 013 lands first and already claims §3.13, so the file ends at §3.13 (not §3.12) by the time this plan starts — verify with `grep -n '^### 3\.'` before writing. §4 lists "the probe scheduler's shape" and "the uptime window's exact definition" as undecided. This plan adds §3.14–§3.16 and removes those two items from §4. |
| `docs/MODULES.md`, `docs/design-brief.md`, `docs/deployment-runbook.md` | See "Steps", Slice 9. |
| `compose.prod.yaml:145-151` | Already sets `net.ipv4.ping_group_range` for this plan, with a comment naming it — no change needed. |

## Commands you will need

| Purpose | Command | Expected on success |
| --- | --- | --- |
| Full gate | `./ci/run-ci.sh` | `PASS — web api e2e`, 0 skipped api tests |
| API only | `./ci/run-ci.sh api` | same, api suite |
| Web only | `./ci/run-ci.sh web` | `npm ci`, lint, build (=typecheck), vitest all pass |
| e2e only | `./ci/run-ci.sh e2e` | Playwright, two viewports |
| Release build (format gate) | `dotnet build Homon.sln --configuration Release` | 0 warnings/errors |
| New migration | `HOMON_DESIGNTIME_CONNECTION='Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x' dotnet dotnet-ef migrations add AddMonitoring --project src/Homon.Infrastructure --startup-project src/Homon.Api --output-dir Persistence/Migrations` | migration files created |
| Apply locally | `dotnet run --project src/Homon.Api -- migrate` | `applied     : all of them.` |
| One xunit class | `dotnet test tests/Homon.Api.Tests --filter FullyQualifiedName~ProbeSchedulerTests` | all pass |
| One Vitest file | `npm --prefix src/Homon.Web run test -- dashboard-page` | all pass |

## Scope

**In scope**:
- `src/Homon.Domain/Monitoring/{Probe,ProbeKind,ProbeStatus,ProbeObservation,ProbeStateMachine,ProbeGroup,ProbeGroupMembership}.cs`, `README.md` (rewrite)
- `src/Homon.Infrastructure/Monitoring/{IProbeRunner,ProbeResult,IIcmpPinger,SystemIcmpPinger,PingProbeRunner,ProbeScheduler,ProbeObservationRetentionService,ProbeUptimeCalculator,MonitoringOptions}.cs` (create)
- `src/Homon.Infrastructure/Persistence/Configurations/{Probe,ProbeObservation,ProbeGroup,ProbeGroupMembership}Configuration.cs` (create)
- `src/Homon.Infrastructure/Persistence/HomonDbContext.cs` (extend)
- `src/Homon.Infrastructure/Persistence/Migrations/*AddMonitoring*` (create, `dotnet ef`)
- `src/Homon.Infrastructure/InfrastructureServiceCollectionExtensions.cs` (extend — `AddHomonMonitoring`)
- `src/Homon.Infrastructure/Homon.Infrastructure.csproj` (extend — one `PackageReference`, one `InternalsVisibleTo`; see Slice 3)
- `Directory.Packages.props` (extend — one `PackageVersion`; see Slice 3)
- `src/Homon.Api/Endpoints/{Probe,ProbeGroup,Status}Endpoints.cs` (create)
- `src/Homon.Api/Program.cs` (extend — activate two commented lines, add one new)
- `src/Homon.Web/src/lib/{probes,probe-groups,status}.ts` (create)
- `src/Homon.Web/src/pages/admin-probes-page.tsx` (rewrite), `admin-probe-groups-page.tsx` (create), `dashboard-page.tsx` (extend — Services section only), `admin-home-page.tsx` (extend)
- `src/Homon.Web/src/App.tsx`, `src/Homon.Web/e2e/helpers.ts`, `src/Homon.Web/e2e/admin.spec.ts` (extend)
- `docs/ARCHITECTURE.md`, `docs/MODULES.md`, `src/Homon.Domain/Monitoring/README.md`, `docs/design-brief.md`, `docs/deployment-runbook.md`
- Tests: `tests/Homon.Api.Tests/{ProbeStateMachine,ProbeGroup,PingProbeRunner,ProbeScheduler,ProbeObservationRetentionService,ProbeEndpoint,ProbeGroupEndpoint,StatusEndpoint}Tests.cs`, `FakeIcmpPinger.cs`, `FixedTimeProvider.cs` (create); `tests/Homon.Api.Tests/HomonApiFactory.cs` (extend — scheduler/retention disabled by default; see Slice 3); `src/Homon.Web/src/lib/status.test.ts`, `src/Homon.Web/src/pages/{dashboard-page,admin-probes-page,admin-probe-groups-page}.test.tsx` (create/extend); `src/Homon.Web/e2e/dashboard-groups.spec.ts` (create)

**Out of scope**: SMB/HTTP/SNMP options and runners (003–005); the credential/secret-protector
seam (003); alerts/`IProbeTransitionPublisher` (009 — this plan's scheduler hook point exists,
but nothing publishes through it yet); backups, pages, weather, calendar (007, 008, 010, 011);
any `className`/styling (plan 012 — semantic HTML only, matching `docs/ARCHITECTURE.md` §3.10).

## Git workflow

- Commit on the worktree's current branch. Do not create or switch branches, and never merge
  or push.
- One commit per slice below (each slice ends with `./ci/run-ci.sh` green) — not one commit
  for the whole plan. Message style from `git log`: `"Area: summary (plan 002)"`, e.g.
  `"Monitoring: add the probe model and state machine (plan 002)"`.
- Do not touch `plans/README.md` at all — the reviewer maintains it for this run (see the
  executor-instructions banner above).

## Steps

Nine slices, each ending in a commit. Each slice's own **Verify** line names the exact
command to run at its end — the narrowest suite that actually exercises what the slice
touched (`dotnet build`, `./ci/run-ci.sh api`, `npm test`, `./ci/run-ci.sh e2e`, …), not the
full three-suite `./ci/run-ci.sh` every time: `web` has nothing of this plan's to typecheck
before Slice 7 and `e2e` nothing to drive before Slice 8, so running either earlier proves
nothing this plan changed. The full, unqualified `./ci/run-ci.sh` (`PASS — web api e2e`) runs
exactly once, as Slice 9's own final verification. `dotnet build Homon.sln --configuration
Release` after any C# change is faster than even the narrowest suite for catching a mistake
early — run it after every file, save each slice's own **Verify** command for the end of the
slice.

### Slice 1 — Domain: entities and the state machine

Create every file under `src/Homon.Domain/Monitoring/` from Decisions 1, 2 and 7:
`ProbeKind.cs`, `ProbeStatus.cs`, `Probe.cs` (including `RecordObservation`, `Pause`,
`Unpause`, `ChangeFailureThreshold` — the last calls `ProbeStateMachine.Derive` exactly as
`Unpause` does, guarded by `if (!IsPaused)`), `ProbeObservation.cs`, `ProbeStateMachine.cs`,
`ProbeGroup.cs`, `ProbeGroupMembership.cs`. Rewrite `README.md`: the final entity list, the
scheduler/runner/retention split (`Homon.Infrastructure/Monitoring/`), the endpoint list, and
the groups rules (deletes, `Position`, empty-group visibility) from Decision 7.

Create `tests/Homon.Api.Tests/ProbeStateMachineTests.cs` (pure, no fixture — model
`ApiKeyRulesTests.cs`): every row of the transition table above, table-driven
(`[Theory]`/`[InlineData]`); `Derive` with `everPolled: false` is always `Unknown` regardless
of counters; a `FailureThreshold` of 1 skips `Unstable` entirely in both directions.

Create `tests/Homon.Api.Tests/ProbeGroupTests.cs` (pure): `ReplaceMembers` renumbers 0..n-1 in
the given order and reuses existing membership rows (assert the same object reference survives
a reorder, not a new one); `Include` does nothing on a repeat call; `Exclude` removes exactly
one membership; `Normalize` trims and uppercases.

**Verify**: `dotnet build src/Homon.Domain --configuration Release` → 0 errors/warnings;
`dotnet test tests/Homon.Api.Tests --filter FullyQualifiedName~ProbeStateMachineTests|FullyQualifiedName~ProbeGroupTests`
→ all pass (no database needed — these run even without `HOMON_TEST_CONNECTION`).
**Commit**: `"Monitoring: add the probe and group domain model, and the state machine (plan 002)"`.

### Slice 2 — Persistence: configurations and the one `AddMonitoring` migration

Create the four `IEntityTypeConfiguration<T>` classes under
`src/Homon.Infrastructure/Persistence/Configurations/`, modelled on `ApiKeyConfiguration.cs`:

- `ProbeConfiguration.cs`: table `"Probes"`, `Name`/`Host` required with their max lengths,
  `Kind`/`Status` via `HasConversion<string>().HasMaxLength(20)` (Decision 1's comment on why,
  copied in verbatim), no unique index on `Position` (Decision 7's comment, copied).
- `ProbeObservationConfiguration.cs`: table `"ProbeObservations"`, `Id` as an identity `long`
  (`ValueGeneratedOnAdd()`), a composite index on `(ProbeId, ObservedAt)` (Decision 6), a
  cascading FK to `Probe` on `ProbeId`.
- `ProbeGroupConfiguration.cs`: unique index on `NormalizedName`, `Members` cascade on delete.
- `ProbeGroupMembershipConfiguration.cs`: composite key `(GroupId, ProbeId)`, cascading FK to
  `Probe` on `ProbeId` (EF creates the index PostgreSQL will not create for you).

Edit `HomonDbContext.cs`: add `DbSet<Probe> Probes`, `DbSet<ProbeObservation>
ProbeObservations`, `DbSet<ProbeGroup> ProbeGroups` after `ApiKeys`; update the class comment
to "the API keys, the probes and their groups, and — as each module lands — the links, pages
and backup reports."

**Verify**: `dotnet build src/Homon.Infrastructure --configuration Release` → 0.

Generate the migration (from the repository root, per `CLAUDE.md`):

```bash
HOMON_DESIGNTIME_CONNECTION='Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x' \
  dotnet dotnet-ef migrations add AddMonitoring \
  --project src/Homon.Infrastructure --startup-project src/Homon.Api \
  --output-dir Persistence/Migrations
```

Read the generated migration: it should create exactly `Probes`, `ProbeObservations`,
`ProbeGroups`, `ProbeGroupMemberships` and nothing else. If it touches `AspNetUsers`,
`AspNetRoles` or `ApiKeys`, the model snapshot has drifted — STOP.

**Verify**: `ls src/Homon.Infrastructure/Persistence/Migrations/*AddMonitoring*` lists a `.cs`
and a `.Designer.cs`; `dotnet run --project src/Homon.Api -- migrate` → `applied     : all of
them.`; `dotnet build Homon.sln --configuration Release` → 0.
**Commit**: `"Monitoring: add the persistence mapping and the AddMonitoring migration (plan 002)"`.

### Slice 3 — Infrastructure: the ping runner, the scheduler, retention

**First, two project-file gaps this slice needs and the codebase does not yet have** (verified
by reading `src/Homon.Infrastructure/obj/project.assets.json`, which lists neither package):

- `src/Homon.Infrastructure/Homon.Infrastructure.csproj` uses plain `Microsoft.NET.Sdk`, not
  `Microsoft.NET.Sdk.Web` (that is `Homon.Api.csproj` only) — it gets no
  `FrameworkReference` to `Microsoft.AspNetCore.App`, so `BackgroundService`, `IHostedService`
  and the `AddHostedService<T>()` extension (all in
  `Microsoft.Extensions.Hosting.Abstractions`) do not resolve. Add
  `<PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" />` to
  `Homon.Infrastructure.csproj`'s existing `<ItemGroup>` of `PackageReference`s, and a matching
  `<PackageVersion Include="Microsoft.Extensions.Hosting.Abstractions" Version="10.0.11" />`
  in `Directory.Packages.props` (same version as every other `Microsoft.Extensions.*`/
  `Microsoft.AspNetCore.*` package there — add it to the "Configuration and options" group).
- `Homon.Infrastructure.csproj` has no `InternalsVisibleTo` at all (only
  `Homon.Api.csproj:24` does, to `Homon.Api.Tests`). `ProbeScheduler.TickAsync` and
  `ProbeObservationRetentionService.SweepAsync` below are `internal` — `ProbeSchedulerTests`/
  `ProbeObservationRetentionServiceTests` call them directly from `Homon.Api.Tests`, a
  different assembly. Add `<InternalsVisibleTo Include="Homon.Api.Tests" />` to
  `Homon.Infrastructure.csproj`, in a new `<ItemGroup>`, modelled on `Homon.Api.csproj:23-25`.

**Verify**: `dotnet build src/Homon.Infrastructure --configuration Release` → 0 (this succeeds
before any of the classes below exist — it only proves the two references resolve).

Create `MonitoringOptions.cs` (Decisions 3, 5, 6, 9's constants, Defaults taken's values) and
register it: `services.AddOptions<MonitoringOptions>().Bind(configuration.GetSection(MonitoringOptions.SectionName)).ValidateOnStart();`
inside a new `private static void AddHomonMonitoring(this IServiceCollection services,
IConfiguration configuration)` in `InfrastructureServiceCollectionExtensions.cs`, called from
`AddHomonInfrastructure` alongside the three existing `AddXxx` calls. **Do not register
`TimeProvider` here** — plan 013 already registers `services.AddSingleton(TimeProvider.System);`
as the first line inside `AddHomonInfrastructure` itself (confirm with
`grep -n "AddSingleton(TimeProvider.System)" src/Homon.Infrastructure/InfrastructureServiceCollectionExtensions.cs`
→ exactly one match); `ProbeScheduler`/`ProbeObservationRetentionService` below just take a
`TimeProvider` constructor parameter and it resolves from that one registration. In the same
method: `services.AddSingleton<IIcmpPinger, SystemIcmpPinger>();`,
`services.AddScoped<IProbeRunner, PingProbeRunner>();`,
`services.AddHostedService<ProbeScheduler>();`,
`services.AddHostedService<ProbeObservationRetentionService>();`.

Create `IProbeRunner.cs`, `ProbeResult.cs`, `IIcmpPinger.cs` + `SystemIcmpPinger.cs`,
`PingProbeRunner.cs` (Decision 4's code, verbatim), `ProbeScheduler.cs` (Decision 3's code,
verbatim, including its `try`/`catch` around `TickAsync`, plus three `[LoggerMessage]`
methods — `LogNoRunnerForKind` at `Warning`, `LogRunnerThrew` at `Warning`, `LogTickFailed` at
`Error`), `ProbeObservationRetentionService.cs` (Decision 6's code, verbatim, including its own
`try`/`catch` around `SweepAsync`, plus one `[LoggerMessage]` method — `LogSweepFailed` at
`Error`), `ProbeUptimeCalculator.cs` (Decision 5's code).

**Disable the scheduler and retention sweep for every test host, not only database-backed
ones.** `tests/Homon.Api.Tests/HomonApiFactory.cs:38-53` is the base every test factory in the
suite inherits — including `ApiDatabaseFactory` (via `DatabaseBackedFactory`) — and its
connection string (`HomonApiFactory.cs:41-42`) deliberately names an unreachable database
(`"Host=localhost;Port=5432;Database=homon_test;…"`). Once `AddHostedService<ProbeScheduler>()`
is registered, any test host built from plain `HomonApiFactory` (e.g. the `ConfiguredFactory :
HomonApiFactory` pattern `ProbeGroupEndpointTests`/`StatusEndpointTests` use for their
"`RequireSignInForReaders = true`, no database needed" case in Slices 5–6, or any existing
test unrelated to monitoring — `MetaEndpointTests`, `ApiKeyAuthenticationTests`, and the rest)
would otherwise start ticking against that unreachable database the moment the host starts.
The `try`/`catch` above stops that from crashing the host outright
(`BackgroundServiceExceptionBehavior` defaults to `StopHost`), but it is still a connection
attempt and a logged error on every unrelated test in the suite — add
`["Monitoring:SchedulerEnabled"] = "false"`, `["Monitoring:RetentionEnabled"] = "false"` to
the dictionary inside `HomonApiFactory.cs`'s own `ConfigureAppConfiguration` override
(`HomonApiFactory.cs:38-53`), not `ApiDatabaseFactory.cs` — `ApiDatabaseFactory` calls
`base.ConfigureWebHost(builder)` first (its own `ConfigureWebHost` override, near the top of
the file), so it inherits both keys automatically and needs no override of its own. A
`[DatabaseFact]` test that wants the scheduler polling for real still never gets it through
this path — every scheduler/retention test in this plan constructs `ProbeScheduler`/
`ProbeObservationRetentionService` by hand and calls `TickAsync`/`SweepAsync` directly, never
through the hosted loop.

**Verify**: `dotnet build Homon.sln --configuration Release` → 0.

Create `tests/Homon.Api.Tests/FakeIcmpPinger.cs` (`IIcmpPinger` returning a scripted
`IcmpPingReply` or throwing) and `tests/Homon.Api.Tests/FixedTimeProvider.cs` (`TimeProvider`
subclass with a settable `DateTimeOffset` — 009 reuses this file, don't duplicate it later).

Create `tests/Homon.Api.Tests/PingProbeRunnerTests.cs` (pure, `FakeIcmpPinger`): a successful
reply → `Succeeded == true`, `LatencyMs` set, `Detail == null`; a timeout-shaped reply →
`Detail == "ping: TimedOut"`; an access-denied reply → the permission-denied sentence.

Create `tests/Homon.Api.Tests/ProbeSchedulerTests.cs` (`ApiDatabaseFactory` + `[DatabaseFact]`,
constructing a `ProbeScheduler` by hand with a fake `IProbeRunner` and `FixedTimeProvider` —
never through the hosted loop): a probe due (no `LastObservedAt`, or one older than
`PollInterval`) is polled by `TickAsync` and gains one `ProbeObservation`; a probe whose
`LastObservedAt` is inside its `PollInterval` is skipped; a paused probe is never polled even
when "due" by time; two overlapping calls to `TickAsync` for the same still-running probe never
double-poll it (assert via a runner that blocks on a `TaskCompletionSource` until released); a
runner that throws still lets the tick finish and records a failed observation with `Detail`
starting `"probe runner threw:"`; advancing `FixedTimeProvider` past `PollInterval` makes a
polled probe due again.

Create `tests/Homon.Api.Tests/ProbeObservationRetentionServiceTests.cs` (`ApiDatabaseFactory` +
`[DatabaseFact]`, constructed by hand with `FixedTimeProvider`): seed observations older and
newer than the 30-day cutoff; `SweepAsync` deletes only the older ones and returns the count;
a second call with nothing left to delete returns 0.

**Verify**: `./ci/run-ci.sh api` → exit 0, 0 skipped, all new tests pass.
**Commit**: `"Monitoring: add the ping runner, scheduler and retention job (plan 002)"`.

### Slice 4 — API: probe CRUD

Create `src/Homon.Api/Endpoints/ProbeEndpoints.cs` per Decision 8: `internal static class
ProbeEndpoints`, `MapProbeEndpoints(this RouteGroupBuilder parent)` mapping `/probes`. `GET`
and `GET /{id:guid}` carry `.RequireAuthorization(HomonPolicies.AdministratorOrApiKey)`; every
other route carries `.RequireAuthorization(HomonPolicies.Administrator)`. Wire types:
`internal sealed record ProbeRequest(string? Name, string? Host, string? Kind, int?
PollIntervalSeconds, int? FailureThreshold, Guid[]? GroupIds)` — `FailureThreshold` stays
nullable on the shared record because `POST` treats `null` as "use the default" while `PUT`
treats it as a validation error (Decision 8); kind ignored/rejected on `PUT`, per Decision 8);
`internal sealed record PauseProbeRequest(bool IsPaused)`;
`internal sealed record ReorderProbesRequest(Guid[] ProbeIds)`; `public sealed record
ProbeResponse(Guid Id, string Name, string Host, ProbeKind Kind, int PollIntervalSeconds, int
FailureThreshold, bool IsPaused, int Position, ProbeStatus Status, string? LastDetail,
DateTimeOffset? LastCheckedAt, Guid[] GroupIds)` with XML doc comments (feeds OpenAPI, per
`Directory.Build.props`'s `GenerateDocumentationFile`).

The group-diff helper from Decision 8 (`ApplyGroupsAsync`, loading every `ProbeGroup` with
`.Include(g => g.Members)` and calling `Include`/`Exclude` per probe) is shared by `POST` and
`PUT`. Model the class layout, `TypedResults` usage and
`ArgumentNullException.ThrowIfNull(parent)` on `MetaEndpoints.cs`.

Edit `Program.cs:476-479`: activate `v1.MapProbeEndpoints();` only. That comment line holds
three calls on ONE physical line — `//   v1.MapProbeEndpoints();   v1.MapStatusEndpoints();   v1.MapLinkEndpoints();`
— so do not uncomment the line: split it. `MapLinkEndpoints` belongs to plan 006 and its
class does not exist yet; activating it breaks the build. After this step the block reads:

```csharp
v1.MapProbeEndpoints();

// Module endpoints register here as each module lands — see docs/MODULES.md:
//   v1.MapStatusEndpoints();   v1.MapLinkEndpoints();
//   v1.MapPageEndpoints();    v1.MapBackupEndpoints();   v1.MapApiKeyEndpoints();
//   v1.MapWeatherEndpoints(); v1.MapCalendarEndpoints();
```

**Verify**: `dotnet build src/Homon.Api --configuration Release` → 0.

Create `tests/Homon.Api.Tests/ProbeEndpointTests.cs` (`ApiDatabaseFactory` + `[DatabaseFact]`,
model `AuthenticationEndpointTests.cs`/`ApiKeyAuthenticationTests.cs` for minting a key with a
given `Scope`/`ExpiresAt` directly on the entity, per plan 013): `GET` on a fresh database →
`[]`; `POST` creates and appends at the end, `201` with the body; `POST` omitting
`failureThreshold` creates a probe with `FailureThreshold == 2` (`Probe.DefaultFailureThreshold`);
`PUT` omitting `failureThreshold` → `400`; validation 400s for empty name, empty host, a
name/host over its max length, an out-of-bounds `pollIntervalSeconds`/`failureThreshold` (when
present), `kind: "http"` (not yet supported), an unknown `groupIds` entry; `PUT` edits fields
and leaves `Kind`/`Position` unchanged, `404` on an unknown id; `PUT .../pause` toggles
`IsPaused` and re-derives `Status` per Decision 2 (seed a probe at `Down`, pause it, assert
`Status == Paused`, unpause it, assert `Status == Down` again without any new observation);
`DELETE` removes the probe and its observations, `404` on an unknown id; `PUT /order` accepts
a permutation and rejects missing/extra/duplicate ids; the groups round-trip (`groupIds` on
create/update add and remove memberships without moving the other members — seed two probes
in one group, add a third, assert the first two keep their positions).

**The `GET` auth matrix** (Decision 8, `HomonPolicies.AdministratorOrApiKey`): anonymous `GET`
→ `401`; an administrator session → `200`; a key with `Scope == Read` → `200`; a key with
`Scope == ReadWrite` → `200`; a key with `ExpiresAt` in the past → `401` (refused by the
existing middleware, exactly like a revoked key — `ApiKeyAuthenticationTests.cs`'s
`A_revoked_key_is_refused_with_a_reason` is the pattern); a revoked key → `401`, unchanged from
today. **The write auth matrix**: anonymous `POST`/`PUT`/`DELETE`/`.../pause`/`/order` → `401`;
a key of either scope on any of those routes → `403` (`HomonPolicies.Administrator` never
admits an API key, regardless of scope — §3.3 holds).

**Verify**: `./ci/run-ci.sh api` → exit 0, 0 skipped, all new tests pass.
**Commit**: `"Api: add probe CRUD, pause and reorder endpoints (plan 002)"`.

### Slice 5 — API: probe groups

Create `src/Homon.Api/Endpoints/ProbeGroupEndpoints.cs`: `MapProbeGroupEndpoints` under
`/probe-groups`, exactly the route table from Decision 7's original spec:

| Route | Policy | Body → result |
| --- | --- | --- |
| `GET /probe-groups`, `GET /{id:guid}` | Reader | → `{ id, name, probeIds[] }`, in order |
| `POST /probe-groups` | Administrator | `{ name }` → 201; the new group goes last |
| `PUT /{id:guid}` | Administrator | `{ name }` → 200 (rename) |
| `DELETE /{id:guid}` | Administrator | → 204 or 404 |
| `PUT /probe-groups/order` | Administrator | `{ groupIds[] }` → 204 |
| `PUT /{id:guid}/members` | Administrator | `{ probeIds[] }` → 200 |

`GET` is `Reader`-gated (unlike `/probes` itself, Decision 8) because the groups list carries
only names and probe ids, which the dashboard's own `/status` response already exposes to
every reader anyway — gating it `Administrator` would just be a second, inconsistent rule for
data the reader-facing endpoint already hands out. The `:guid` constraint keeps `order` from
matching `{id}`.

Validation: **name** non-empty after trim, ≤ `ProbeGroup.NameMaxLength`, unique ignoring case
(check first, and turn a Postgres `23505` unique-violation into the same `ValidationProblem`
in case two requests race); **`order`** must list every existing group exactly once;
**`members`** no duplicate ids, no unknown ids.

Edit `Program.cs`: add `v1.MapProbeGroupEndpoints();` on its own line directly after
`v1.MapProbeEndpoints();` (above the module comment), since it was never one of the
pre-written commented calls.

**Verify**: `dotnet build src/Homon.Api --configuration Release` → 0.

Create `tests/Homon.Api.Tests/ProbeGroupEndpointTests.cs` (`ApiDatabaseFactory` +
`[DatabaseFact]`): create puts the group last; name validation (empty, too long, a duplicate
differing only in case) → 400; `order` accepts a permutation and rejects
missing/extra/duplicate ids; `members` sets the order and rejects unknown/duplicate ids;
deleting a group keeps its probes (assert via `GET /probes/{id}` still returning them);
deleting a probe leaves the other members' relative order intact; an unknown group → 404; the
auth matrix (anonymous write → 401, API-key write → 403, anonymous `GET` → 200; with
`RequireSignInForReaders = true`, anonymous `GET` → 401 via the `ConfiguredFactory` pattern,
no database needed for that one case).

**Verify**: `./ci/run-ci.sh api` → exit 0, 0 skipped, all new tests pass.
**Commit**: `"Api: add probe group CRUD, reorder and membership endpoints (plan 002)"`.

### Slice 6 — API: the status endpoint

Create `src/Homon.Api/Endpoints/StatusEndpoints.cs`: `MapStatusEndpoints` maps `GET
/status`, `HomonPolicies.Reader`. Implement Decision 9's payload: one query joining
`Probes`/`ProbeObservations` grouped by `ProbeId` for the uptime window's success/total
counts; a second grouped query (ping probes only) bucketing `ProbeObservations` into
`MonitoringOptions.SparklineBucketCount` day-wide buckets for the mean successful `LatencyMs`;
assemble `Totals` (Decision 5's per-probe average for `UptimePercent`), `Probes`, `Groups`
(non-empty only, Decision 7), `UngroupedProbeIds`, and `GeneratedAt` from `TimeProvider`.

Edit `Program.cs`: move `v1.MapStatusEndpoints();` out of the comment onto its own active
line after `v1.MapProbeGroupEndpoints();`, leaving `//   v1.MapLinkEndpoints();` commented
(plan 006 activates it). The block then reads:

```csharp
v1.MapProbeEndpoints();
v1.MapProbeGroupEndpoints();
v1.MapStatusEndpoints();

// Module endpoints register here as each module lands — see docs/MODULES.md:
//   v1.MapLinkEndpoints();
//   v1.MapPageEndpoints();    v1.MapBackupEndpoints();   v1.MapApiKeyEndpoints();
//   v1.MapWeatherEndpoints(); v1.MapCalendarEndpoints();
```

**Verify**: `dotnet build src/Homon.Api --configuration Release` → 0.

Create `tests/Homon.Api.Tests/StatusEndpointTests.cs` (`ApiDatabaseFactory` +
`[DatabaseFact]`): with zero probes, `Totals` are all zero/`null` and one implicit "Services"
grouping (no `Groups` entries, empty `UngroupedProbeIds`); a probe's `UptimePercent` matches a
hand-computed ratio from seeded observations, `null` with none; a shared probe (two groups) is
listed once in `Probes`, counted once in `Totals`, and appears in both `Groups[].ProbeIds`; an
empty group is absent from `Groups`, a group with only paused probes is present; a `Ping`
probe with RTT observations across the window gets a non-empty `Sparkline`, a probe with none
gets `[]`; `LastCheckedAt` matches the most recent observation's `ObservedAt`; with
`RequireSignInForReaders = true`, an anonymous request → 401 (`ConfiguredFactory`
pattern, on the no-database factory, per `MetaEndpointTests.cs:91-101`).

**Verify**: `./ci/run-ci.sh api` → exit 0, 0 skipped, all new tests pass.
**Commit**: `"Api: add the dashboard status endpoint (plan 002)"`.

### Slice 7 — SPA: data layer and the admin pages

Create `src/Homon.Web/src/lib/probes.ts` (types mirroring `ProbeResponse`, `PROBES_QUERY_KEY`,
`useProbes`, `useCreateProbe`/`useUpdateProbe`/`useDeleteProbe`/`useReorderProbes`/
`useSetProbePaused`, every mutation invalidating both `PROBES_QUERY_KEY` and `STATUS_QUERY_KEY`
on success), `probe-groups.ts` (same shape for `/probe-groups`, invalidating groups, probes
and status), and `status.ts` (`useStatus()` with `refetchInterval: 30_000`; the pure
`dashboardSections(status, { phone })` function — one section per non-empty group, heading id
`probe-group-${id}-heading` never derived from the name; then the ungrouped section, keeping
`services-heading`, labelled "Services" when it is the only section shown and "Other"
otherwise; on phone, each section's rows sorted by severity `[down, unstable, unknown, up,
paused]` with a stable sort; zero probes returns one "Services" section so the existing empty
sentence still renders; `formatCheckedAt(lastCheckedAt, now)` per Decision 10).

Rewrite `admin-probes-page.tsx`: an ordered list (current status word, detail, "Move {name}
up/down", "Pause {name}"/"Unpause {name}", "Delete {name}" with an inline confirm), and a
`ProbeForm` below it (labelled `Name`, `Host`, `Kind` select — one option, `Poll interval
(seconds)`, `Failure threshold` (the "add" form's initial value is `2`, matching
`Probe.DefaultFailureThreshold`; editing an existing probe pre-fills its actual value), and
`<fieldset><legend>Groups</legend>` with one checkbox per
`ProbeGroup` — "No groups yet." plus a link to `/admin/probe-groups` when there are none).
Model the form's structure (one `<p>` per field, `<label htmlFor>`, `role="alert"` on the last
mutation error) on `sign-in-page.tsx:33-77`. No `className` anywhere.

Create `admin-probe-groups-page.tsx` at `/admin/probe-groups`: an ordered list of groups
("Move {name} up/down", "Rename {name}", "Delete {name}" — confirms inline, "Its probes are
kept."), each group's member list ("Move {probe} up/down", "Remove {probe}", a labelled "Add
probe to {name}" select), and a create form (`Group name` label, "Add group" button). Every
action sends the full list immediately; there is no unsaved state.

Wire it in: a lazy route in `App.tsx` (mirroring the existing admin lazy-import pattern), a
`Probe groups` `<li>` in `admin-home-page.tsx` right after `Probes`, an entry in `ADMIN_ROUTES`
in `e2e/helpers.ts`, and `'Probe groups'` added to the array in `admin.spec.ts:17`.

**Verify**: `npm run build` (in `src/Homon.Web`) → 0 errors (typechecks even before the
dashboard consumes these — Slice 8).

Create Vitest tests: `lib/status.test.ts` (zero groups → "Services"; groups → "Other"; a fully
grouped board has no "Other"; a shared probe appears in both sections; the phone sort is
stable within a section; a group named "Links" gets `probe-group-{id}-heading`, not a
name-derived id; `formatCheckedAt` cases: `null` → "Never", a few minutes ago → "N min ago").
`admin-probes-page.test.tsx` (the form's kind select has exactly one option; the add form's
failure-threshold field starts at `2`; submitting posts the trimmed field values with
`groupIds`; move/pause/delete buttons send the right request).
`admin-probe-groups-page.test.tsx` (move buttons send the right `PUT .../order` body; the
create form posts the trimmed name).

**Verify**: `npm test` (in `src/Homon.Web`) → all pass, including the new/extended files.
**Commit**: `"Web: add the probe and probe-group admin pages and their data layer (plan 002)"`.

### Slice 8 — SPA: the dashboard and e2e

Edit `dashboard-page.tsx`: replace the hard-coded `services-heading` section with
`dashboardSections(status, { phone })`'s output — one `<section aria-labelledby>` per section,
each an `<h2>` (the section's label) followed by a panel-less `<table>`: `<thead><tr><th>` for
`Status`, `Service`, `Detail`, `Uptime`, `30 days`, `Checked` (Decision 10 — the "30 days" cell
stays empty per row), then one `<tr>` per probe in the section's order, `Status` as the plain
capitalised word (`Up`/`Unstable`/`Down`/`Unknown`/`Paused`, no colour, no glyph — plan 012
adds both), `Uptime` via the inline formatter, `Checked` via `formatCheckedAt`. Add the stat
strip as a `<p>` under `<h1>`: `"{up} up · {unstable} unstable · {down} down · {paused} paused
· {uptime} uptime, 30 days"`, using the real field names from `StatusResponse.Totals` and
Decision 10's inline uptime formatter. Leave the `links-heading` section untouched (plan 006's
anchor). With zero probes, the empty "Services" section keeps its existing sentence.

**Verify**: `npm run build && npm test` → 0 errors, all pass (existing `App.test.tsx`
assertions on `heading, level: 1, name: 'Dashboard'` etc. are unaffected — no landmark or `h1`
text changed).

Create `src/Homon.Web/e2e/dashboard-groups.spec.ts`: seeds two `ProbeGroup`s and two **paused**
`Ping` probes through the authenticated API (paused keeps the run deterministic — no ICMP, and
the scheduler is live in e2e per Decision 3), one probe in both groups, one probe ungrouped;
asserts the `h2` order "Hosts", "Storage", "Other" (not "Services", since a group exists) and
no horizontal overflow at both viewports (`expectNoHorizontalOverflow`); cleans up every
seeded probe and group in `afterEach` — the e2e database is shared across the whole run, and
`layout.spec.ts:28-34` expects a bare "Services" heading on `/` when nothing else has left
groups behind.

**Verify**: `./ci/run-ci.sh e2e` → exit 0, `dashboard-groups.spec.ts` and `layout.spec.ts` both
pass at both viewports; `admin.spec.ts`'s extended nav loop passes.
**Commit**: `"Web: group the dashboard by ProbeGroup and add the stat strip (plan 002)"`.

### Slice 9 — Docs

`docs/ARCHITECTURE.md`:
- **§3.14** "Probe groups are optional, many-to-many and independent of kind" — Decision 7's
  content and rejected alternatives.
- **§3.15** "The probe scheduler is a tick-based scan, not per-probe timers" — Decision 3's
  content and rejected alternative.
- **§3.16** "Uptime is a per-probe ratio; the aggregate averages probes, not observations" —
  Decision 5's content and rejected alternative.
- Edit §4: remove "The probe scheduler's shape (…)" and "the uptime window's exact definition"
  from the list, leaving "the WYSIWYG editor, the weather and calendar providers."

`docs/MODULES.md`: extend the 002 row (core + ping + groups, in one sentence); add an "Added
after the brief" paragraph naming the groups feature (not in the original brief), the
`FailureThreshold` default of 2 (1–10 allowed per probe), and `GET /probes` admitting an API
key as well as an Administrator session (plan 013).

`src/Homon.Domain/Monitoring/README.md`: already rewritten in Slice 1 — confirm it still
matches what actually shipped (entity list, endpoint list, groups rules) and correct anything
that drifted during Slices 2–8.

`docs/design-brief.md`: apply the groups-era edits — Dashboard section becomes one section per
non-empty group followed by "Other"/"Services"; the Services component note that the table
repeats per section and the phone sort applies within each; the stat strip counts each probe
once; Admin gets the `/admin/probe-groups` bullet and the probe form's Groups fieldset; "Not
drawn yet" gains the groups page.

`docs/deployment-runbook.md`: extend the "ICMP" section with one line — after the first probe
is created, `curl http://127.0.0.1:8102/api/v1/status` (or the admin UI) should show a
non-`Unknown` state within `MonitoringOptions.TickInterval` once the container's
`net.ipv4.ping_group_range` is confirmed.

Do not edit `plans/README.md` — the reviewer maintains it for this run.

**Verify**: `grep -c '^### 3\.' docs/ARCHITECTURE.md` → 16 (was 13 once plan 013 had landed;
12 before it); `grep -n "probe scheduler's shape\|uptime window's exact definition"
docs/ARCHITECTURE.md` → no match outside the new §3.15/§3.16 headings themselves;
`./ci/run-ci.sh` → `PASS — web api e2e` one final time.
**Commit**: `"Docs: record the scheduler shape, uptime definition and probe groups (plan 002)"`.

## Test plan

Summarised per slice above; in full:

- **xunit, pure**: `ProbeStateMachineTests`, `ProbeGroupTests`, `PingProbeRunnerTests`.
- **xunit, `[DatabaseFact]`**: `ProbeSchedulerTests`, `ProbeObservationRetentionServiceTests`,
  `ProbeEndpointTests`, `ProbeGroupEndpointTests`, `StatusEndpointTests`.
- **Vitest**: `lib/status.test.ts`, `admin-probes-page.test.tsx`,
  `admin-probe-groups-page.test.tsx`, `dashboard-page.test.tsx` (extended).
- **Playwright**: `e2e/dashboard-groups.spec.ts` (new); `layout.spec.ts`, `admin.spec.ts`
  (both extended, both must keep passing unmodified in their existing assertions).
- **Verification**: `./ci/run-ci.sh` → `PASS — web api e2e`, 0 skipped api tests, after every
  slice, not only at the end.

## Done criteria

- [ ] `dotnet build Homon.sln --configuration Release` → 0 warnings/errors
- [ ] `dotnet run --project src/Homon.Api -- migrate` applies `AddMonitoring` cleanly against a
      fresh database
- [ ] Every new xunit test class listed above exists and passes
- [ ] `npm --prefix src/Homon.Web run build` exits 0; every new/extended Vitest file passes
- [ ] `grep -n "MapProbeEndpoints\|MapProbeGroupEndpoints\|MapStatusEndpoints" src/Homon.Api/Program.cs`
      shows all three active, not commented
- [ ] `./ci/run-ci.sh` → `PASS — web api e2e`, 0 skipped api tests
- [ ] `docs/ARCHITECTURE.md` §3.14–§3.16 added; §4 no longer lists the scheduler shape or the
      uptime definition as undecided
- [ ] `docs/MODULES.md`, `src/Homon.Domain/Monitoring/README.md`, `docs/design-brief.md`,
      `docs/deployment-runbook.md` updated
- [ ] No file outside "Scope" modified (`git status`) — `plans/README.md` is not one of them;
      the reviewer updates it after merging

## STOP conditions

- `HomonPolicies.AdministratorOrApiKey` (or an equivalent policy admitting an Administrator
  session or any authenticated API-key principal) does not exist when this plan starts — plan
  013 has not landed, or landed under different names. Re-read "Contract this plan consumes"
  against the actual code; if the names differ, use the real ones throughout instead of
  duplicating them, but if the *policy itself* is genuinely missing, this plan's `GET /probes`
  route has nothing to attach to — stop and report.
- The generated `AddMonitoring` migration touches `AspNetUsers`, `AspNetRoles` or `ApiKeys` —
  the model snapshot has drifted from what Phase 0 shipped; do not force it through.
- A step's verification fails twice after a reasonable fix attempt.
- `ProbeSchedulerTests`' overlap test cannot be made deterministic without sleeping a real
  wall-clock duration — report what you tried; `TaskCompletionSource`-gated fakes should make
  this unnecessary, and a flaky sleep-based test is worse than a missing one.
- Extending `admin.spec.ts`'s nav loop or `layout.spec.ts`'s heading assertions would require
  changing an *existing* accessible name or heading text (not just adding a new one) — that is
  a break in a contract plan 012 explicitly protects (`docs/ARCHITECTURE.md` §3.10); stop and
  report rather than editing around it.
- ICMP is refused inside the development or CI container even after `net.ipv4.ping_group_range`
  looks correct (`docker compose exec api cat /proc/sys/net/ipv4/ping_group_range` does not
  read `0 2147483647`) — this blocks only the *manual* verification step, not the automated
  gate (which never sends real ICMP, Decision 3/4); note it in your summary and continue.

## Maintenance notes

- **003 (HTTP), 004 (SMB), 005 (SNMP)** each add a runner (`IProbeRunner`), a `ProbeKind`
  member (already present in this plan's enum), and — for 003/004 — a nullable owned-type
  options column on `Probe` via `OwnsOne(...).ToJson()`, additive to this plan's migration,
  never a reshape of it.
- **009 (Alerts)** hooks `ProbeScheduler.PollAndPersistAsync`'s `oldStatus`/`probe.Status` pair,
  immediately before `SaveChangesAsync` — that is the one place both values are in scope
  together without a second database round trip. A reviewer of 009 should confirm it did not
  move that read earlier or later in the method.
- **The enum-as-string convention** (Decision 1) is this plan's, not an established repository
  rule before it — later modules with their own enums (008's `BackupJobState`, for one) may
  follow it or diverge with their own recorded reason; there is no hard requirement either way.
- **`MonitoringOptions.SchedulerEnabled`/`RetentionEnabled`** being `false` in `HomonApiFactory`
  (inherited by every fixture, including `ApiDatabaseFactory`) means no test in the suite ever
  exercises the hosted loop end to end — only `TickAsync`/`SweepAsync` called directly. If a
  future bug turns out to live in `ExecuteAsync`'s own loop (the `Task.Delay`/re-check cycle,
  or the `try`/`catch` this plan wraps `TickAsync`/`SweepAsync` in), it needs a test that
  actually runs the hosted service, which none of this plan's tests do.
- **Deferred**: an admin "poll this probe now" button (unpausing already gets a probe polled
  within seconds, which may be enough); a configurable `TickInterval`/`PerPollTimeout` exposed
  in the UI (both are `MonitoringOptions` today, environment-configurable, not admin-UI
  configurable); relative-time formatting nicer than `formatCheckedAt`'s plain string (plan
  012 may want to replace it, per Decision 10).
