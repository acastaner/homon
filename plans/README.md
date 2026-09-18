# Implementation plans

Numbered in one monotonic sequence; a number is never reused. Each plan is a file
`NNN-short-imperative-title.md` and records what it changed and what it decided; the
decisions themselves live in `docs/ARCHITECTURE.md`.

Plans 002–013 were written on 2026-09-15 against commit `f4e7261`; 002, 003, 006, 007,
010, 012 and 013 were then reviewed cold (`Reviewed:` line in each). Each plan opens with a
drift check and a list of STOP conditions. The status column is maintained by whoever
reviews and merges the work.

| Plan | Status | Effort | Depends on | Title |
| --- | --- | --- | --- | --- |
| 001 | DONE | — | — | Scaffolding — solution, toolchain, plumbing, gate, Docker, docs |
| 002 | DONE (2026-09-16, `2bacef1`) | L | 013 | Monitoring core, probe groups and the ping probe |
| 003 | DONE (2026-09-16, `b92eb30`) | M | 002 | HTTP/HTTPS probe |
| 004 | planned | M | 002, 003 | SMB/CIFS probe (managed client) |
| 005 | planned | S | 002, 003 | SNMP probe scaffold |
| 006 | DONE (2026-09-16, `191df56`) | M | — (reuses 002's ordering convention) | Links |
| 007 | DONE (2026-09-16, `9b47556`) | M | — | Pages and the WYSIWYG editor |
| 008 | planned | L | — | Backup reports and API-key administration |
| 009 | planned | L | 002, 008 | Alerts by email |
| 010 | DONE (2026-09-16, `c653799`) | M | — | Weather widget |
| 011 | planned | L | 003 (secret protector); follows 010's cache shape | Family calendar widget |
| 012 | DONE (2026-09-16, `51de1b4`) | L | 013, 002, 003, 006, 007, 010 (Slice A: 001 only) | Design pass (from `docs/design-brief.md`; target fixed: Status board, dark by default — `docs/design/`); styles only what has landed |
| 013 | DONE (2026-09-16, `1d969e0`) | S | 001 | API key scopes (read / read-write) and optional expiry — executes before 002 |
| 014 | DONE (2026-09-18, `5e01612`) | M | — | Dashboard auto-refresh: the freshness indicator, focus refetch and a manual Refresh control |

**Current run (2026-09-15/16):** 013 → 002 → 003 → 006 → 007 → 010 → 012, each on its own
branch, merged to `main` after a green `./ci/run-ci.sh`. 004, 005, 008, 009 and 011 are
deferred; 012 styles only what has landed and tells those plans how to reuse its primitives.

**014 is executed but NOT merged.** It sits on the branch `plan/014-dashboard-auto-refresh`
(worktree `.claude/worktrees/plan-014`, head `5e01612`, six commits), reviewed green on
`./ci/run-ci.sh web` and `./ci/run-ci.sh e2e`. Merging is the maintainer's call:
`git merge --ff-only plan/014-dashboard-auto-refresh`, then
`git worktree remove .claude/worktrees/plan-014 && git branch -d plan/014-dashboard-auto-refresh`.

Status values: planned · IN PROGRESS · DONE · BLOCKED (one-line reason) · REJECTED (one-line reason).

## Dependency notes

- **013 runs before 002.** 002's `GET /probes` uses `HomonPolicies.AdministratorOrApiKey`
  (admin session or any unexpired API key of either scope; probe writes stay admin-only),
  and 013 registers the shared `TimeProvider`. 013 claims `docs/ARCHITECTURE.md` §3.13;
  002 then takes §3.14–§3.16 and 003 §3.17.
- **002 delivers the contract 003–005 and 009 build on:** `Probe` + `ProbeKind`,
  `ProbeObservation`, `ProbeStatus`, `IProbeRunner.RunAsync` → `ProbeResult`,
  `ProbeScheduler` (30 s per-poll cap), `ProbeEndpoints`, the probe form's per-kind region.
  FailureThreshold defaults to 2 (1–10 per probe); aggregate uptime is the mean of
  per-probe uptime; an unpaused probe is due on the next tick.
- **002 under-delivered its own Decision 10** (the probe form's kind selector shipped inert,
  with no per-kind conditional region). Plan 003 builds that structure instead, since it is
  the first plan with a second kind; 004 and 005 extend whatever 003 lands.
- **003's maintainer decisions:** with no expectation configured only a 2xx response is up;
  HEAD/GET only; per-probe timeout 1–25 s (default 10); redirects followed.
- **003 sets patterns that others reuse by name:** per-kind probe options as one nullable
  owned type per kind in its own `jsonb` column (004, 005), and
  `Security/ISecretProtector` with purpose `Homon.Secrets.v1` and write-only secret wire
  semantics (004, 005, 011). 004, 005 and 011 narrow `""` from "clear" to a 400 where the
  secret is mandatory; each says so.
- **009 adds the transition seam to 002's scheduler** (`ProbeTransition` on a bounded
  channel) and, as a separable last step beyond the brief's wording, backup Failed/Late
  alerts using 008's `BackupJobEvaluator` plus a `BackupJob.LastNotifiedState` column.
- **011 follows 010's `WeatherCache` shape** (pull-based, 15 min fresh, stale-on-error,
  single-flight) and builds the cache from its own description if 010 has not landed.
- **012 runs last**; its Slice A (tokens, fonts, theme bootstrap, toggle) depends only on
  001 and can land early.
- Every module plan edits shared files (`Program.cs`, `HomonDbContext.cs`, the model
  snapshot, `App.tsx`, `dashboard-page.tsx`, `admin-home-page.tsx`, `e2e/helpers.ts`).
  Each tells its executor to compare only the regions it edits in the drift check.
- `docs/ARCHITECTURE.md` section numbers are not fixed in advance: each plan greps the
  highest `### 3.N` at execution time and takes the next.
- **014 depends on nothing and touches only `src/Homon.Web`.** It retires the "refreshed N s
  ago" follow-up above and records its deviations from `docs/design-brief.md`'s Banner rule in
  `docs/ARCHITECTURE.md`, not in the brief. The dashboard poll interval stays a hard-coded 30 s
  in `lib/status.ts`; a configurable one was weighed on 2026-09-18 and deferred.

## Cross-plan follow-ups (not in any plan yet)

- **Design details 012 deliberately simplified**, each because the alternative would have
  broken a test the suites depend on, or exceeded "style only":
  - the dashboard stat strip stays flat text — the brief colours the unstable and down
    values, but wrapping each in its own element breaks `dashboard-page.test.tsx`'s
    whole-string `getByText`, which matches only direct text children;
  - the Services table's phone layout hides the sparkline and "Checked" columns rather than
    folding each row into two lines, because overriding `display` on `<tr>`/`<td>` strips
    the implicit ARIA row/cell roles the e2e specs query by. `Sparkline`'s `size="inline"`
    variant exists but is unused;
  - "Signed in as … / Sign out" is one always-visible block, not a responsive banner/page-header
    pair (jsdom applies no stylesheet, so two copies both resolve and `getByRole` throws);
  - ~~the banner's "refreshed N s ago" timestamp~~ — **retired by plan 014** (2026-09-18),
    which wired it in `AppShell` from a *disabled* cache observer, so no route gains a
    `/status` fetch. `Status.generatedAt` stays deliberately unconsumed: the browser's own
    `dataUpdatedAt` is the clock (`docs/ARCHITECTURE.md` §3.20);
  - shadcn's `src/components/ui/*` primitives are installed and committed but unused; the
    pages hand-style native controls. A later plan can adopt them.
- **Tap targets (for 012).** The design brief asks for controls ≥40px, but nothing asserts
  it: native unstyled checkboxes are 13×13 and selects 24px tall before the design pass, so
  plan 003's e2e probe-form test deliberately checks only horizontal overflow. When 012
  styles the forms, add the tap-target assertion to the admin specs.

- **Time zone.** 009 formats alert times in UTC because no zone setting exists; 011 then
  introduces `Calendar:TimeZone` (validated IANA id, `tzdata` added to the Alpine runtime
  image). Once 011 lands, alerts should use the same zone — or promote it to a
  household-wide setting.
- **Alerts at boot.** 009 mails only transitions between known states, so a probe that is
  already down when the API starts sends no alert until it recovers and fails again.
  Deliberate (it prevents a mail storm on every restart); revisit if that gap matters.
- **Verify at execution** (flagged inside the plans): SMBLibrary 1.5.8's authentication
  method signature (004), SharpSnmpLib 12.5.7's GET call shape and net10.0 compatibility
  (005), Open-Meteo's exact attribution wording (010), TipTap under the report-only CSP
  in a real browser (007).
