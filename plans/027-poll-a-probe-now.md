# Plan 027: "Poll now" — poll one probe on demand from the Probes admin page

> **Where to work** (`CLAUDE.md`, hard rules): in `/home/acastaner/Git/homon` itself — **never a git worktree**. Branch `plan/027-poll-a-probe-now` from `main`, commit on it, and **leave it checked out** when done. Do not merge, push, switch back to `main`, delete branches or `git stash`.

## Status

- **Priority**: P3 · **Effort**: S · **Risk**: LOW. It moves the scheduler's poll into a class of its own but leaves its body unchanged. No migration and no new configuration.
- **Depends on**: nothing outstanding (builds on 002, 003, 021, 024, 025, 026 — all DONE).
- **Category**: feature · **Planned at**: commit `fd4f2e9`, 2026-10-10 · executed in this checkout on the branch above, 2026-10-11.

## Why this matters

A probe is polled only when the scheduler finds it due, at `LastObservedAt + PollInterval`. After
fixing a service or editing a probe, the administrator waits up to a whole interval to see the
result, and an interval can be as long as 24 h. Each row of `/admin/probes` now has a **Poll now**
button that runs one real poll of that probe straight away and shows the result in the row.

The maintainer chose the scope: **per row, on `/admin/probes` only.** There is no "poll all" and no button on
`/probes/{id}`. Either is a small follow-up if wanted.

## Decisions

**D1. A manual poll is the same poll.** It uses the same runner, `Probe.RecordObservation`,
observation row, outage outbox row (§3.31) and single save. It therefore counts toward the
failure threshold, can send a Down or Up mail, and moves `LastObservedAt` forward, which pushes the
next scheduled poll a full interval out. *Rejected*: a "dry run" that shows a result without
recording it. That would be a second code path that could disagree with the real one, and it
leaves the dashboard showing the stale state the administrator pressed the button to replace.

**D2. `ProbePoller` (a singleton) owns the poll, and the scheduler and the endpoint share it.**
- `ProbeScheduler` keeps the due query and the `MaxConcurrentPolls` gate. `PollAndPersistAsync` and the `_inFlight` set moved, unchanged, into `ProbePoller.PollAsync`.
- `PollAsync` returns a `ProbePollOutcome`: `Polled`, `NotFound`, `Paused`, `AlreadyPolling` or `NoRunner`.
- *Rejected*: registering the scheduler as a singleton as well as a hosted service and calling it from the API. That mixes *when* with *how* and exposes scheduler internals across assemblies.

**D3. The in-flight set is the overlap guard, for both callers.**
- A manual poll of a probe that is mid-poll, scheduled or manual, returns `AlreadyPolling` (409) at once. It neither waits nor runs a second poll.
- The scheduler now claims the probe inside the gate rather than before it. A probe already in flight therefore takes a gate slot for the instant `PollAsync` needs to return. This has no practical effect, and a comment says so.

**D4. A started poll is not cancelled by its request.**
- The endpoint passes `IHostApplicationLifetime.ApplicationStopping`, not the request's token.
- With the request's token, a closed tab would cancel the poller's linked timeout source. The `catch … when timeoutSource.IsCancellationRequested` would then record "probe timed out", a failure that never happened, counted toward the threshold.
- The request is bounded by `PerPollTimeout` (30 s). Nothing in the path cuts it short: `apiFetch` has no timeout, the production nginx `proxy_read_timeout` is 60 s, and the API registers no request timeouts.

**D5. The endpoint is `POST /api/v1/probes/{id}/poll`, Administrator session only.**
- It takes no body, but requires `application/json` (415 otherwise), the CSRF guard copied from `AlertEndpoints.SendTestAsync`. The SPA sends `'{}'`.
- Responses:
  - 200 with the `ProbeResponse` as it now stands;
  - 404 if the probe does not exist;
  - 409 problem for paused, already polling or no runner.
- Manual polls bypass `MaxConcurrentPolls`: one click polls one probe.

**D6. The row button.**
- It is `RefreshCw`, placed between Edit and Pause, named "Poll {name} now", with the title "Poll now".
- It is disabled while the probe is paused or the poll is pending. While pending, it carries `aria-busy` and the icon spins (`motion-safe:animate-spin`).
- `IconButton` gained an optional `isBusy` prop for this.
- The action column grows from 216px to 256px (six 40px buttons and the divider).
- The outcome of a poll that ran is the row's status chip, Down included. A poll that could not run (409) or a failed request shows the API's `detail` in a `role="alert"` line under the row. That line clears on the next press. Without it, the button would just stop spinning.

## What changed

| Area | Files |
| --- | --- |
| Poller (new) | `src/Homon.Infrastructure/Monitoring/ProbePoller.cs` |
| Scheduler | `src/Homon.Infrastructure/Monitoring/ProbeScheduler.cs`, `InfrastructureServiceCollectionExtensions.cs` (`AddSingleton<ProbePoller>()`) |
| Endpoint | `src/Homon.Api/Endpoints/ProbeEndpoints.cs` (`PollProbeAsync`) |
| SPA | `src/Homon.Web/src/lib/probes.ts` (`usePollProbe`), `src/pages/admin-probes-page.tsx`, `src/components/icon-button.tsx` (`isBusy`) |
| Tests | `tests/Homon.Api.Tests/ProbePollEndpointTests.cs` (new, its own factory with a fake runner), `ProbeSchedulerTests.cs` (+3), `FakeProbeRunner.cs` (moved out of the scheduler tests), `admin-probes-page.test.tsx` (+3), `e2e/admin.spec.ts` (disabled on a paused probe) |
| Docs | `docs/ARCHITECTURE.md` §3.32, `src/Homon.Domain/Monitoring/README.md` |

The e2e suite checks only that the button is disabled on a paused probe. Pressing it on a live
probe would poll for real, and the gate must make no outbound request. The poll path is covered
by `ProbePollEndpointTests` and `ProbeSchedulerTests`.

## Done criteria

- `./ci/run-ci.sh web`, `./ci/run-ci.sh api` (0 skips) and `./ci/run-ci.sh e2e` pass, each run on its own.
- A manual check in a browser on the branch:
  - Poll now on a long-interval probe updates its chip, and its probe page shows the new poll.
  - The button is disabled on a paused probe.
  - The actions column still lines up at `lg`, and the row folds cleanly at 412px.

## Execution record

Executed 2026-10-11 in this checkout. The suites were run one at a time:
- `api` → 521 passed, 0 skipped (513 before; +3 scheduler, +5 endpoint);
- `web` → 264 (261 before);
- `e2e` → 149 at both viewport projects. The count is unchanged because the new check is one assertion inside an existing test.

The manual browser check is the maintainer's, still to do.

**Independent review (2026-10-11): APPROVE.** The reviewer had fresh context and did not see the
execution. It re-ran the suites one at a time in this checkout and got the same counts (web 264, api
521 / 0 skipped, e2e 149, all exit 0, no retries). It found scope clean and `PollAndPersistAsync`
behaviourally identical to `main`. It raised four non-blocking points:
- `PollProbeAsync` returns `IResult`, so the OpenAPI document lists none of its responses (200, 404, 409, 415). `AlertEndpoints.SendTestAsync` has the same gap. `.Produces<ProbeResponse>()` / `.ProducesProblem(409)` would close it.
  **Fixed afterwards, at the maintainer's request.** `PollProbeAsync` now returns `Results<Ok<ProbeResponse>, NotFound, ProblemHttpResult, StatusCodeHttpResult>`, the style `DeleteProbeAsync` uses. The route adds `.ProducesProblem(409)`, `.Produces(415)` and a description. `ProbePollEndpointTests` asserts that the document lists exactly 200, 404, 409 and 415, with their content types (api → 522 passed, 0 skipped). `SendTestAsync` is plan 026's and is left as it was.
- The row's poll-error line survives a later Pause or Edit. It clears only on the next poll.
- A probe in two groups gets a 409 on its second row if both rows are pressed. This is correct, and the reviewer accepts it.
- A runner that ignores its token holds the request past `PerPollTimeout`. The scheduler has always had this; nginx's 60 s is the outer bound.

Untested paths:
- the endpoint's 409 for AlreadyPolling and for NoRunner;
- a manual poll that crosses the threshold and queues an outage row;
- the busy spinner.
