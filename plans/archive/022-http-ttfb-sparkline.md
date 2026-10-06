# Plan 022: HTTP probes measure time to first byte, and get the 30-day sparkline

> **Executor instructions**: Follow this plan step by step. Run every verification command and
> confirm the expected result before moving on. If a STOP condition occurs, stop and report — do
> not improvise. When done, set this plan's row in `plans/README.md` to `IN PROGRESS → review`
> only if told to; otherwise the reviewer maintains it.
>
> **Drift check (run first)**:
> `git diff --stat 9c94ba0..HEAD -- src/Homon.Infrastructure/Monitoring src/Homon.Api/Endpoints/StatusEndpoints.cs src/Homon.Web/src/pages/dashboard-page.tsx src/Homon.Web/src/components/sparkline.tsx tests/Homon.Api.Tests/HttpProbeRunnerTests.cs tests/Homon.Api.Tests/StatusEndpointTests.cs src/Homon.Web/src/pages/dashboard-page.test.tsx docs/design-brief.md docs/ARCHITECTURE.md docs/MODULES.md`
> Empty output → proceed. Otherwise compare the "Current state" excerpts against the live code;
> any mismatch is a STOP condition.

## Status

- **Priority**: P3 — a feature the maintainer asked for, 2026-10-06.
- **Effort**: S
- **Risk**: LOW. No migration, no wire-shape change (`sparkline: number[]` already exists on
  every probe in `GET /api/v1/status`). The one behavioural change is what an HTTP probe's
  `LatencyMs` means.
- **Depends on**: none (builds on 002, 003, 012 — all DONE)
- **Category**: direction (feature)
- **Planned at**: commit `9c94ba0`, 2026-10-06
- **Requested by**: the maintainer, 2026-10-06 — add a sparkline "similar" to the ping one for
  HTTP probes, measuring Time to First Byte, with the full request time as a fallback "but the
  TTFB only is preferred if easy to implement". It is easy, so there is no fallback.

## Why this matters

The dashboard's "30 days" column shows a latency sparkline for ping probes only, so every HTTP
probe has an empty cell. HTTP probes already record a latency per poll, but it is the full
request time *including the body download* (up to 64 KiB). That mixes server responsiveness with
page size. Time to first byte answers the question a household actually has — "is the service
getting slow to respond?" — and the runner already asks for headers-first responses, so measuring
it means moving one line.

## Decisions — implement these exactly

**D1. TTFB is "time until `SendAsync` returns".** The request is sent with
`HttpCompletionOption.ResponseHeadersRead`, so `SendAsync` completes once the response headers
are in. Strictly that is time to the *last header byte*, not the first byte; the difference is
negligible and not worth a custom `SocketsHttpHandler` callback. *Rejected*: instrumenting the
socket with `ConnectCallback` / `PlaintextStreamFilter` to catch the literal first byte — far
more code, and it would split the single named client that §3.17 and plan 003's Decision 4
rely on.

**D2. The body is still read and evaluated; it simply stops being timed.** `ExpectedBodyText`
still works, and the probe's own `TimeoutSeconds` still bounds the whole request including the
body (the linked `timeoutSource` is unchanged). Only the stopwatch moves.

**D3. Redirects are inside the measurement.** The named client has `AllowAutoRedirect = true`,
so an `http → https` hop or a login redirect counts as part of the TTFB, up to the final
response's headers. That is what a visitor waits for, so it is the right number. Do not turn
redirects off.

**D4. Connection setup is sometimes inside it, sometimes not.** `IHttpClientFactory` pools and
recycles the handler (every 2 minutes by default), so some polls include DNS, TCP connect and
the TLS handshake and others reuse a warm connection. The sparkline averages each day's
successful samples, which flattens this. Do not add a "fresh connection per poll" setting.

**D5. Old observations are left to age out — no migration, no purge.** Every HTTP observation
before this change holds full-request time. For up to `RetentionWindowDays` (30) days after
deployment, an HTTP probe's sparkline mixes the old full times with the new TTFB values, usually
showing as a step down. The retention sweep removes the old rows on its own. *Rejected*: a data
migration to null old `LatencyMs` values — it is destructive, irreversible, and fixes a cosmetic
effect that goes away on its own within a month.

**D6. The kind gate is an explicit allow-list of `Ping` and `Http`, on both sides.** Not
"every kind with latency": `message` probes have none, and SMB/SNMP (plans 004/005) have not
decided what their latency would mean. When they land, each plan opts in by adding its kind.

## Current state

- `src/Homon.Infrastructure/Monitoring/HttpProbeRunner.cs` — the runner. Today, in `RunAsync`:

  ```csharp
  var stopwatch = Stopwatch.StartNew();

  try
  {
      using var response = await client
          .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token)
          .ConfigureAwait(false);

      var body = await ReadCappedBodyAsync(response, timeoutSource.Token).ConfigureAwait(false);
      stopwatch.Stop();

      var (succeeded, detail) = Evaluate(options, response.StatusCode, body);

      return new ProbeResult(succeeded, stopwatch.Elapsed.TotalMilliseconds, detail);
  }
  ```

- `src/Homon.Api/Endpoints/StatusEndpoints.cs` — `BuildSparklinesAsync` (bottom of the file).
  Its doc comment says "for `ProbeKind.Ping` probes only", and it filters with:

  ```csharp
  var pingProbeIds = probes
      .Where(p => p.Kind == ProbeKind.Ping)
      .Select(p => p.Id)
      .ToHashSet();
  ```
  then queries `successfulPingObservations`. Bucketing and averaging stay as they are.

- `src/Homon.Web/src/pages/dashboard-page.tsx:210`:
  ```tsx
  {probe.kind === 'ping' ? <Sparkline samples={probe.sparkline} state={probe.state} /> : null}
  ```
- `src/Homon.Web/src/components/sparkline.tsx` — comments say "Ping probes only" (component doc)
  and "the brief: ping probes only" (above `DOT_COLOR`). No logic change.
- `src/Homon.Infrastructure/Monitoring/MonitoringOptions.cs:33` — "the ping sparkline's 30-day window".
- `docs/design-brief.md:41` ("for ping probes, room for a small RTT sparkline") and `:265`
  ("**Sparkline.** Ping probes only; other probe types leave the cell empty.").
- `docs/MODULES.md:27` — "Ping records the RTT for a 30-day sliding window".
- `docs/ARCHITECTURE.md` — the last decision is §3.27; `## 4. Things this record does not yet
  decide` follows it. New decisions go there as §3.28.
- `plans/archive/003-http-probe.md:262` records the decision this plan reverses ("`Stopwatch`
  around the whole request through the capped body read"). **Do not edit it** — archived plans
  are records (`plans/archive/README.md`).

**Conventions** (from `CLAUDE.md`): comments carry the reasoning and name the rejected
alternative; `TreatWarningsAsErrors` + `IDE0055` mean the Release build is the formatting gate;
TypeScript uses named exports and the `@/` alias; tests stub `fetch` with `src/test/fetch.ts`, no MSW.

## Commands you will need

Run the suites **one at a time** — the combined `./ci/run-ci.sh` aborts on this machine with
`Internal CLR error (0x80131506)`, which is environmental; retry it once if it happens. Never pipe
the gate into `tail`: you would see tail's exit code instead of the gate's.

| Purpose | Command | Expected on success |
| --- | --- | --- |
| Web (lint, typecheck, vitest) | `./ci/run-ci.sh web; echo "exit=$?"` | `exit=0` |
| API (Release build, migrate, tests, 0 skips) | `./ci/run-ci.sh api; echo "exit=$?"` | `exit=0`, 0 skipped |
| e2e (Playwright, two viewports) | `./ci/run-ci.sh e2e; echo "exit=$?"` | `exit=0` |

## Scope

**In scope** (the only files you modify):
- `src/Homon.Infrastructure/Monitoring/HttpProbeRunner.cs`
- `src/Homon.Infrastructure/Monitoring/MonitoringOptions.cs` (comment only)
- `src/Homon.Api/Endpoints/StatusEndpoints.cs`
- `src/Homon.Web/src/pages/dashboard-page.tsx`
- `src/Homon.Web/src/components/sparkline.tsx` (comments only)
- `tests/Homon.Api.Tests/HttpProbeRunnerTests.cs`
- `tests/Homon.Api.Tests/StatusEndpointTests.cs`
- `src/Homon.Web/src/pages/dashboard-page.test.tsx`
- `docs/ARCHITECTURE.md`, `docs/design-brief.md`, `docs/MODULES.md`
- `plans/README.md` (this plan's row only)

**Out of scope**:
- `ProbeObservation`, `ProbeResult`, migrations — nothing about storage changes (D5).
- `InfrastructureServiceCollectionExtensions.cs` — the named client's handler (redirects,
  TLS callback) stays as it is (D3, D4).
- `Sparkline` rendering logic, sizes and colours — the maintainer said the display is fine.
- The `30 days` column header text, and the `StatusResponse` / `ProbeStatusResponse` records.
- `plans/archive/**`.

## Git workflow

- Work in this checkout, `/home/acastaner/Git/homon`. **Never create a git worktree** (including
  the Agent tool's `isolation: "worktree"`).
- Branch: `plan/022-http-ttfb-sparkline`, from `main`. Leave it checked out when you are done.
  Do not switch back to `main`, merge, push, stash or delete branches.
- One commit per step, in the repo's style, e.g.
  `Monitoring: an HTTP probe's latency is its time to first byte (plan 022)`.

## Steps

### Step 1: Stop the HTTP stopwatch when the headers arrive

In `HttpProbeRunner.RunAsync`, move `stopwatch.Stop();` to directly after the `SendAsync`
statement, before `ReadCappedBodyAsync`. Above it, add a comment in the repo's voice: this is
time to first byte (D1), the body is still read and evaluated under the same timeout but not
timed (D2), redirects are included (D3), and the rejected alternative was "the whole request
through the capped body read" (plan 003) because it measures page size, not responsiveness.
Update the class `<summary>` to mention that the latency is TTFB.

**Test first** — add to `tests/Homon.Api.Tests/HttpProbeRunnerTests.cs`:

```csharp
[Fact]
public async Task Latency_is_time_to_first_byte_and_excludes_a_slow_body()
{
    var (runner, handler, _) = MakeRunner();
    handler.Handler = (_, _) => Task.FromResult(
        new HttpResponseMessage(HttpStatusCode.OK) { Content = new SlowContent(TimeSpan.FromMilliseconds(750), "ok") });

    var result = await runner.RunAsync(MakeProbe(MakeOptions(expectedBodyText: "ok")), CancellationToken.None);

    // The body was still read and evaluated (D2)…
    Assert.True(result.Succeeded);
    Assert.Equal("HTTP 200", result.Detail);
    // …but its 750 ms did not count. 500 leaves margin on a loaded CI box either way.
    Assert.NotNull(result.LatencyMs);
    Assert.True(result.LatencyMs < 500, $"LatencyMs was {result.LatencyMs}");
}
```

plus a private nested helper in the same class, next to `FakeSecretProtector`:

```csharp
/// <summary>
/// Content whose bytes arrive only after a delay. With <c>ResponseHeadersRead</c> the delay
/// falls inside <c>ReadAsStreamAsync</c>, i.e. after the headers — the stand-in for a slow body.
/// </summary>
private sealed class SlowContent(TimeSpan delay, string body) : HttpContent
{
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        await Task.Delay(delay);
        await stream.WriteAsync(Encoding.UTF8.GetBytes(body));
    }

    protected override bool TryComputeLength(out long length)
    {
        length = -1;
        return false;
    }
}
```
(`using System.Text;` is already at the top of the file.)

**Verify**: before the move, the new test fails (latency ≈ 750). After the move,
`./ci/run-ci.sh api; echo "exit=$?"` → `exit=0`, every existing `HttpProbeRunnerTests` case
still passes, and so does the new one.

### Step 2: Include HTTP probes in `/status`'s sparklines

In `StatusEndpoints.BuildSparklinesAsync`:
- filter on `p.Kind is ProbeKind.Ping or ProbeKind.Http`;
- rename `pingProbeIds` → `sparklineProbeIds` and `successfulPingObservations` →
  `successfulObservations`;
- rewrite the doc comment: ping probes plot RTT and HTTP probes plot time to first byte (§3.28);
  other kinds get `[]` until their own plan opts them in (D6).

In `MonitoringOptions.cs:33`, change "the ping sparkline's" to "the latency sparkline's".

**Test** — add to `tests/Homon.Api.Tests/StatusEndpointTests.cs`, modelled on
`A_ping_probe_with_rtt_observations_gets_a_non_empty_sparkline_and_one_with_none_gets_empty`
(line 132): `An_http_probe_with_latency_observations_gets_a_non_empty_sparkline`. Create the
probe with a local `POST /api/v1/probes` call (do **not** change the shared `CreateProbeAsync`
helper):

```csharp
var response = await client.PostAsJsonAsync("/api/v1/probes", new
{
    name = "Web probe",
    host = "web.test",
    kind = "http",
    pollIntervalSeconds = 30,
    failureThreshold = 2,
    groupIds = Array.Empty<Guid>(),
    http = new { method = "get", path = "api/health" },
});
Assert.Equal(HttpStatusCode.Created, response.StatusCode);
```
Seed one successful observation (`LatencyMs = 42.0`, an hour ago) exactly the way the ping test
does, then assert that probe's `sparkline` has length > 0 and that its first value is `42.0`.

**Verify**: `./ci/run-ci.sh api; echo "exit=$?"` → `exit=0`, 0 skipped, new test passed. If
`HOMON_TEST_CONNECTION` is unset the `[DatabaseFact]` tests skip and the script fails on
purpose — that is the gate working, not a STOP.

### Step 3: Render the sparkline for HTTP rows

In `dashboard-page.tsx:210`, change the condition to
`probe.kind === 'ping' || probe.kind === 'http'`. In `sparkline.tsx`, change "Ping probes only"
to "Ping and HTTP probes only (RTT and time to first byte)" in the component doc, and fix the
"ping probes only" wording in the `DOT_COLOR` comment the same way.

**Test** — add to `src/Homon.Web/src/pages/dashboard-page.test.tsx`, after the message-probe
tests, `it('draws a sparkline for ping and http probes and none for other kinds', …)`. Build a
`/api/v1/status` fixture in the shape of `statusWithAMessageProbe` with three ungrouped probes,
each with `sparkline: [10, 12, 11]`: `'Router'` (`kind: 'ping'`), `'Nextcloud'` (`kind: 'http'`)
and `'NAS share'` (`kind: 'smb'`; the server never sends samples for it, but this is what pins
the client gate). Every probe needs `message: null` on top of the existing fields. Include the
links, pages and weather stubs that every fixture there has. Then:

```ts
// Columns: Status · Service · Detail · Uptime · 30 days · Checked. The query is scoped to the
// "30 days" cell (index 4) because StatusChip renders an aria-hidden lucide <svg> glyph of its
// own, so an svg anywhere in the row proves nothing.
const sparklineCell = (name: string) => screen.getByText(name).closest('tr')!.querySelectorAll('td')[4]
expect(await screen.findByText('Router')).toBeInTheDocument()
expect(sparklineCell('Router').querySelector('svg')).not.toBeNull()
expect(sparklineCell('Nextcloud').querySelector('svg')).not.toBeNull()
expect(sparklineCell('NAS share').querySelector('svg')).toBeNull()
```

**Verify**: `./ci/run-ci.sh web; echo "exit=$?"` → `exit=0`, vitest count one higher than
before, lint clean.

### Step 4: Record the decision and fix the docs

- `docs/ARCHITECTURE.md`: add `### 3.28 An HTTP probe's latency is its time to first byte`
  between §3.27 and `## 4.`. Write it as prose in that file's style: D1–D6 condensed, the
  rejected alternatives named (full request time, the socket-level first-byte callback, purging
  old rows), and a line saying it supersedes plan 003's "Latency" note.
- `docs/design-brief.md:41` → "for ping and HTTP probes, room for a small latency sparkline (30
  days — RTT for ping, time to first byte for HTTP)". `:265` → "**Sparkline.** Ping and HTTP
  probes only; other probe types leave the cell empty." The rest of that paragraph stays.
- `docs/MODULES.md:27` → after "Ping records the RTT for a 30-day sliding window", add "and HTTP
  its time to first byte (plan 022, §3.28)".

**Verify**: `grep -rn -i "ping probes only\|ping sparkline" src docs --include=*.cs --include=*.ts --include=*.tsx --include=*.md | grep -v node_modules`
→ no output.

### Step 5: Full gate

Run `web`, `api` and `e2e` one at a time (see Commands). All three print `exit=0`. No e2e spec
asserts on the sparkline, so e2e should pass unchanged. If `contrast.spec.ts` or `layout.spec.ts`
fails, that is a STOP condition: the sparkline adds no text, so a failure there means something
else changed.

## Done criteria

- [ ] `./ci/run-ci.sh web`, `api` and `e2e`, run one at a time, each print `exit=0`; api reports 0 skipped
- [ ] `HttpProbeRunnerTests.Latency_is_time_to_first_byte_and_excludes_a_slow_body` passes
- [ ] `StatusEndpointTests.An_http_probe_with_latency_observations_gets_a_non_empty_sparkline` passes
- [ ] The new dashboard-page vitest case passes
- [ ] `grep -n "stopwatch.Stop" src/Homon.Infrastructure/Monitoring/HttpProbeRunner.cs` shows
      it on the line after the `SendAsync` statement, before `ReadCappedBodyAsync`
- [ ] The Step 4 grep returns nothing; `docs/ARCHITECTURE.md` has a `### 3.28` heading
- [ ] `git diff --stat main...HEAD` lists only in-scope files; `git diff main...HEAD -- '*Migrations*'` is empty
- [ ] Branch `plan/022-http-ttfb-sparkline` is checked out in this checkout

## STOP conditions

- The drift check shows changes, and the excerpts above no longer match.
- `HttpProbeRunner` no longer passes `HttpCompletionOption.ResponseHeadersRead`. Without it,
  `SendAsync` buffers the body and moving the stopwatch measures nothing new.
- The new runner test still sees latency ≥ 500 ms after the move. That means the body is being
  buffered before `SendAsync` returns; report it instead of loosening the threshold.
- Any step seems to need a migration, a change to `ProbeResult` / `ProbeObservation`, or a
  change to the named client's handler.
- A suite fails twice in a way the steps don't explain (one retry for the `0x80131506` CLR
  abort is expected and doesn't count).

## Maintenance notes

- **The first month after deploy shows a step down on HTTP sparklines** (D5). That's expected,
  not a regression; it is gone once the old rows pass the 30-day retention window.
- `ProbeObservation.LatencyMs` now means a different thing per kind: RTT for ping, TTFB for HTTP.
  Anything that compares latencies *across* probes (a future alert threshold, plan 009) must
  treat them as different units of meaning.
- Plans 004 (SMB) and 005 (SNMP) must decide whether their kind joins the allow-list in **both**
  `BuildSparklinesAsync` and `dashboard-page.tsx` (D6). Changing only one side produces either an
  empty cell or a payload nobody draws.
- Reviewer: check that the timeout still covers the body read (`timeoutSource.Token` is still
  passed to `ReadCappedBodyAsync`). Moving the stopwatch must not move the `try` boundary.
