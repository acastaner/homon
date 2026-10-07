# Implementation plans

Numbered in one monotonic sequence; a number is never reused. Each plan is a file
`NNN-short-imperative-title.md` and records what it changed and what it decided; the
decisions themselves live in `docs/ARCHITECTURE.md`.

**A plan that is finished with moves to `archive/`.** `plans/` holds only what might still be
executed — the `planned` rows below — so that opening the directory answers "what is outstanding"
rather than "what has ever been written". A plan is archived once its row reads `DONE` or
`REJECTED`, under the same filename; the table below is the index to both, and it is the authority
on status. Archiving is the last step of merging a plan, after its row is updated. Nothing else
about a plan changes when it moves: paths written *inside* an archived plan are left as they were
at execution time (see `archive/README.md`), because a plan is a record and rewriting it would make
it a worse one.

**024 is merged.** `main` carries one commit, `9adce5f`, fast-forwarded from `be1fbd4` on 2026-10-07 on
the maintainer's word after a manual check in a browser on the branch. `/admin/probes` now lists probes in
the dashboard's sections and order, and moving a grouped probe reorders its group rather than
`Probe.Position` (which the dashboard reads only for the ungrouped section). Suites on the branch: `web` →
203 tests (191 before), `e2e` → 125 at both viewport projects; no C# changed. Released as `v0.6.0` the same day.

**023 is merged.** `main` carries its eleven commits, `67469f6`…`60c1d16`, fast-forwarded from
`fa5b2bd` on 2026-10-07 on the maintainer's word after a browser check on the branch, and released as
`v0.5.0` the same day. Every probe now has a page at `/probes/{id}`, reached from its name on the
dashboard: readers get the state, uptime, a 24 h / 7 d / 30 d latency graph and the 50 most recent
polls from a new Reader-gated `GET /status/probes/{id}` that never carries the host; administrators
also get the configuration, from the unchanged `GET /probes/{id}` (`docs/ARCHITECTURE.md` §3.29).
The reviewer re-ran the suites one at a time on the branch: `web` → 191 tests (168 before), `api` →
465 passed with 0 skips (455 before), `e2e` → 125 at both viewport projects (113 before).

**The first approval of 023 was wrong, and the gates could not have said so.** All three suites were
green and the review approved it; the maintainer then opened a real probe page with three polls in
a day and saw three tiny dashes in a 0–500 ms frame. The defect was in the plan's D8, not in the
execution: a stretched viewBox that forbade dots and in-SVG text, a line that broke at every bucket
nobody polled, and a ceiling that wasted half the height. e2e can seed no observations, so no test
ever drew data. D8′ redrew it in real pixels with dashed bridges over unpolled time, dots for sparse
samples, a tighter ceiling and a hover readout, and the reviewer then rendered the real component
in Chromium against realistic data before approving again — which is how a second defect (a chart
that mounted empty never measured itself and stayed 720px wide) was found and fixed. The lesson
worth keeping: *for anything visual, render it with realistic data before calling it done; a green
gate over an empty state proves the empty state.* The plan records three revisions in place; the
maintainer authorised a third revision round beyond the usual two.

**022 is merged.** `main` carries its four commits, `54dc488`…`14af996`, fast-forwarded from
`9c94ba0` on 2026-10-06 on the maintainer's word, after a visual check in a browser on the branch,
and released as `v0.4.0` the same day — the release workflow passed and it is deployed.
It was executed in this checkout, on `plan/022-http-ttfb-sparkline`, and the reviewer re-ran the
suites one at a time rather than trusting the executor's report: `web` → 168 tests (167 before),
`api` → 455 passed with 0 skips, `e2e` → 113 at both viewport projects. An HTTP probe's latency is
now its time to first byte (`docs/ARCHITECTURE.md` §3.28), so for up to 30 days after deploying,
an HTTP sparkline mixes old full-request times with the new values — expected, not a regression.

**020 is merged.** `main` carries its nine commits, `b045d8f`…`ca2a89b`, fast-forwarded from
`9957b7b` on 2026-10-01 on the maintainer's word, and released as `v0.2.0` — the first release
since `v0.1.2`, so that tag covers 019 as well. The suites were re-run on the branch immediately
before the merge, **one at a time**: `web` → 23 files / 150 tests (148 before the last fix, 133
before 020), `api` → 346 passed with 0 skips (287 before), `e2e` → 107 at both viewport projects
(87 before). Exit codes were checked rather than banners — piping the gate into `tail` reports
*tail's* exit code, which briefly made a failed run look clean.

**The combined `./ci/run-ci.sh` could not be used.** It aborts on this machine partway through
`dotnet test` with `Internal CLR error. (0x80131506)` (exit 134) — the environmental abort already
on record for this checkout. Suite-by-suite is the reliable form here, and the done criteria in
`plans/archive/020-weather-page-and-day-extremes.md` were rewritten to say so.

The maintainer manually validated it in a browser on 2026-10-01, on
`plan/020-weather-page` in this checkout, before the merge. That mattered more than usual here:
every suite was green while the hourly table showed a gusty hour's **mean** wind, `12 km/h`, in
red beneath a banner reading "gusts to 94 km/h". Nothing was wrong with the tint or the banner —
the row's severity was correct and the tests asserted exactly that. The defect was that the cell
carrying the colour held the one number that could not justify it. Each cell now takes its colour
only from an advisory about its own figure, and the wind cell shows the gust. The lesson is worth
keeping: *a test that asserts a tint is present does not ask whether the tint points at the right
number.*

020 is also the first plan whose own done criteria were wrong and had to be corrected in place
during execution — three of them, each recorded in the plan with what the real check is: `oxlint`
was never silent on this tree (a pre-existing warning in an untouched file), the hidden-column
grep matched only body cells because a `<th>` carries its width between the two classes, and the
`role="alert"` check matched the doc comment explaining that role's absence.

**019 is merged.** `main` carries it as `85b2420`, fast-forwarded from `1ce816e` on 2026-10-01 on
the maintainer's word. The full gate was re-run on the branch immediately before the merge:
`PASS — web api e2e`, web 133 tests (93 before, +40), api 287 passed with 0 skips, e2e 87 (75
before, +6 new tests × 2 viewport projects), `oxlint` silent. Nothing under `src/Homon.Api/`,
`src/Homon.Domain/`, `src/Homon.Infrastructure/` or `tests/Homon.Api.Tests/` changed, and the five
specs 019 had to leave alone — `dashboard-groups`, `layout`, `refresh`, `contrast`,
`dashboard-collapse`, plus `helpers.ts` and `collapsed-sections.ts` — are byte-identical to the
commit before it. Unlike 018, it was executed in this checkout on `plan/019-arrange-dashboard-sections`,
which is the rule 018 produced — and the maintainer manually validated it in a browser on
2026-10-01, on that branch in this checkout, before it was merged. That last clause is the whole
point of the rule: the gate can prove the headings are in the right order and the buttons clear
40px, and it cannot tell you whether the header row carrying `Dashboard` + `Arrange` +
`Reset order` beside the stat strip looks right.

019 departs from a maintenance note 018 wrote for exactly this case: it adds a second
`localStorage` key rather than widening 018's array into an object under the one key. The reason is
in its D3 and in `docs/ARCHITECTURE.md` §3.23 — one key would give two preferences one lifetime and
falsify the "expanding again removes the key" assertion. Anyone adding a *fourth* dashboard
preference should read §3.23 before reaching for a fourth key.

**018 is merged.** `main` carries it as `ead406e`, fast-forwarded from `9d70023` on
2026-09-21. Before the merge the reviewer re-ran all three suites on the branch independently
rather than trusting the executor's report: `web` → `PASS`, 93 tests (76 before, +17 new);
`api` → `PASS`, 287 passed, 0 skipped; `e2e` → `PASS`, 75 (67 before, +4 new tests × 2 viewport
projects). `oxlint` is silent. The diff was exactly the ten in-scope files, nothing under
`src/Homon.Api/`, `src/Homon.Domain/`, `src/Homon.Infrastructure/` or `tests/Homon.Api.Tests/`,
and the five "net" specs (`dashboard-groups`, `refresh`, `layout`, `contrast`, `helpers`) were
byte-identical to `main` — the tests were not bent to fit the change. One revision round was
required, against a defect in the plan rather than in the execution: the plan's snippet for
`summariseProbeStates` specified `.sort(`, which was the repo's only lint warning and sat 21
lines below a correct `.toSorted()` in the same file; `ead406e` fixes it.

**018 was executed in a worktree, and that is now forbidden.** The maintainer could not
manually test the feature after it was reported done, because the worktree held the only copy
— `main` and this checkout had none of it. `CLAUDE.md`'s "Where the work happens" section is
the rule that came out of it: work in this checkout, on a feature branch, and leave that branch
checked out for review. Every plan's "Git workflow" section that says otherwise is void.

Plan 018 was written on 2026-09-19 against commit `9d70023`, from a maintainer request
rather than from the roadmap: collapse any dashboard section, remembered per browser. It was
first specified with a cookie, as asked, and revised the same day to `localStorage` — a cookie
on this deployment carries a silent failure mode (a `Secure` attribute drops it on the
plain-HTTP LAN of §3.21) and rides on every `/status` poll for a value no server code reads;
`lib/theme.ts` already persists a per-browser preference the same way. See the plan's D1. It is
SPA-only and touches no API, but it is the first change to reshape the dashboard's section
markup since plan 012, so it carries an unusually long list of existing assertions it must not
break — see its "The four existing assertions that constrain the markup".

Plans 015–016 were written on 2026-09-18 against commit `5ff263a`, from defects found on the
first production deployment; both were reviewed on 2026-09-18 (`Reviewed:` line in each). Plans 002–013 were written on 2026-09-15 against commit `f4e7261`; 002, 003, 006, 007,
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
| 008 | REJECTED (superseded by 021) | L | — | Backup reports and API-key administration — the file stays for its reasoning, which 021 carries over by name |
| 009 | planned | L | 002, 021 | Alerts by email |
| 010 | DONE (2026-09-16, `c653799`) | M | — | Weather widget |
| 011 | planned | L | 003 (secret protector); follows 010's cache shape | Family calendar widget |
| 012 | DONE (2026-09-16, `51de1b4`) | L | 013, 002, 003, 006, 007, 010 (Slice A: 001 only) | Design pass (from `docs/design-brief.md`; target fixed: Status board, dark by default — `docs/design/`); styles only what has landed |
| 013 | DONE (2026-09-16, `1d969e0`) | S | 001 | API key scopes (read / read-write) and optional expiry — executes before 002 |
| 014 | DONE (2026-09-18, `5e01612`) | M | — | Dashboard auto-refresh: the freshness indicator, focus refetch and a manual Refresh control |
| 015 | DONE (2026-09-18, `2a78135`) | M | — | LAN-first sign-in: honour a forwarded scheme in nginx, and let the session cookie follow it |
| 016 | DONE (2026-09-18, `a8503ae`) | S | — (ships with 015) | Make the ICMP sysctl applicable under rootless Docker |
| 017 | planned | S | — | `deploy.sh` finds the Compose file (`compose.yaml` → `compose.yml` → `compose.prod.yaml`, or `-f`) |
| 018 | DONE (2026-09-21, `ead406e`) | M | — (builds on 002, 006, 007, 010, 012, 014) | Collapsible dashboard sections, remembered per browser in local storage |
| 019 | DONE (2026-10-01, `85b2420`, released `v0.2.0`) | M | — (builds on 002, 006, 007, 010, 012, 018) | Hide the empty ungrouped section; arrange the dashboard's sections, remembered per browser |
| 020 | DONE (2026-10-01, `ca2a89b`, released `v0.2.0`) | L | — (builds on 010, 012) | Today's extremes in the weather widget; a full `/weather` page with hourly and 7-day tables and derived severe-weather banners |
| 021 | DONE (2026-10-05, `f68ce63`, released `v0.3.0`) | XL | — (builds on 002, 003, 012, 013) | Message gateway: push-report ingestion, reporters, the `message` probe kind, 32-day retention and API-key administration — supersedes 008 |
| 022 | DONE (2026-10-06, `14af996`, released `v0.4.0`) | S | — (builds on 002, 003, 012) | HTTP probes measure time to first byte and get the 30-day sparkline |
| 023 | DONE (2026-10-07, `60c1d16`, released `v0.5.0`) | L | — (builds on 002, 003, 012, 021, 022) | Probe page: name, uptime, a 24 h / 7 d / 30 d latency graph and recent polls for readers; configuration for administrators only |
| 024 | DONE (2026-10-07, `9adce5f`, released `v0.6.0`) | S | — (builds on 002, 012, 023) | The probe admin page lists probes in the dashboard's sections and order, with icon row actions and an inline editor |

**Current run (2026-09-15/16):** 013 → 002 → 003 → 006 → 007 → 010 → 012, each on its own
branch, merged to `main` after a green `./ci/run-ci.sh`. 004, 005, 008, 009 and 011 are
deferred; 012 styles only what has landed and tells those plans how to reuse its primitives.
**008 is now REJECTED**, superseded by 021 (2026-10-01): the generic message gateway does
everything 008 specified and more, so `BackupJob`, `BackupRun` and `BackupJobEvaluator` will never
exist. 021 carried over 008's load-bearing decisions by name — the reporter must exist first,
bodies are truncated and never rejected, lateness is derived rather than tracked, and the report
endpoint is rate-limited per key.

**021 changed two things every later plan touching Monitoring must know.**
`ProbeResult` gained an optional `DerivedStatus`, and a runner that supplies
`ProbeStatus.Unknown` there means "no verdict" — the scheduler records no observation for it. Any
plan adding an `IProbeRunner` has to decide whether its kind can reach no verdict. 021 also took
`ProbeKind.Message`, additively: the column stores the name and carries no check constraint, so no
migration was needed.

**009's backup half is void.** Its Step 8, its `AddBackupAlertState` migration, its
`BackupJob.LastNotifiedState` column and its `BackupAlertWatcher` background service are all
unnecessary: a reporter is watched by a real `Probe`, so its overdue and failed transitions already
travel 009's own `ProbeTransition` seam from Steps 1–7. The pure function behind them is
`Homon.Domain.Messaging.MessageProbeEvaluator.Evaluate`. One consequence survives: such an alert's
subject reads `[Homon] {probe name} is down`, in the probe vocabulary, not `[Homon] Backup {name}
is late`.

**015 and 016 are the first plans written from a real deployment rather than from the
roadmap.** Homon went to its first production host on 2026-09-18 (`clockmaster`, rootless
Docker, port 8102, reached over plain HTTP on the LAN) and both defects were found there, not
in CI — 016 stopped the api container from starting at all, and 015 stops the administrator
from holding a session once it does. **They should ship in one release**, because both change
`compose.prod.yaml` and `deploy.sh` does not carry a compose change across (it pulls images and
pins `HOMON_VERSION`, nothing more).

**014 has since been merged** — `main` carries `5e01612` and the branch and worktree are gone.

**015 and 016 are both merged.** `main` carries them as `b511a62`; 015 fast-forwarded, 016 was
rebased onto it and fast-forwarded after. The merged tree was re-verified, not assumed:
`./ci/run-ci.sh api` on `main` → Release build clean, **287 passed, 0 skipped**, and
`docker compose --env-file .env.example -f compose.prod.yaml config` renders both changes
together — `Auth__AllowPlainTextSessions: "false"` and `net.ipv4.ping_group_range: 0 65536`.

**015's evidence.** The reviewer re-ran `./ci/run-ci.sh api` independently on the branch —
Release build clean, 287 passed, 0 skipped, with all three new `PlainTextSessionTests` confirmed
`Passed` in the TRX — and the executor additionally reported `./ci/run-ci.sh web` (76 tests) and
`./ci/run-ci.sh e2e` (67 tests, both viewports) green. The diff was exactly the eight files plan
015 listed in scope.

**016's evidence, and its limit.** The gate cannot observe that change — nothing in
`./ci/run-ci.sh` reads `compose.prod.yaml`, and the diff compiles nothing — so it was verified
by rendering the file, which shows the value the container is handed rather than merely that the
YAML parses. **A green gate says nothing about whether 016 works.** The real proof is a rootless
host: see plan 016's "Post-deploy verification".

### Neither plan is live until the host is updated

Both changed `compose.prod.yaml`, and **`deploy.sh` does not carry a compose change onto the
host** — it pulls images and pins `HOMON_VERSION`, nothing more. The new `compose.prod.yaml`
must be copied to `clockmaster` *before* `deploy.sh` runs, or the host keeps its old copy and
016's fix is absent, api still failing to start.

015 additionally needs a **rebuilt `homon-web` image**: `src/Homon.Web/nginx.conf` is baked in
at build time (`compose.prod.yaml`'s `web` service runs `ghcr.io/acastaner/homon-web`), so the
forwarded-scheme map only exists once a release is built, pushed and pulled. Setting
`HOMON_ALLOW_PLAINTEXT_SESSIONS=true` against an old web image gives the LAN a storable cookie
but leaves a WAF-fronted origin without one — exactly the half-state decision D5 was written to
avoid. Ship the image and the compose file together.

**Both changed `compose.prod.yaml`, and `deploy.sh` does not carry a compose change onto the
host.** Whichever of these ships, the new `compose.prod.yaml` has to be copied to `clockmaster`
*before* `deploy.sh` runs, or the host keeps its old copy — and for 016 that means the api
container goes on failing to start.

Status values: planned · IN PROGRESS · DONE · BLOCKED (one-line reason) · REJECTED (one-line reason).

## Dependency notes

- **023 adds a second latency allow-list consumer.** It extracts `StatusEndpoints.PlotsLatency`
  and `lib/status.ts plotsLatency` (one per side) and uses them for both the sparkline and the
  probe page, so 004 and 005 opt a kind in by changing those two lines. Its reader endpoint
  (`GET /status/probes/{id}`) deliberately never carries `Host`. The configuration block reuses
  the admin-only `GET /probes/{id}`, whose policy 023 leaves alone (plan 002's Decision 8).
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
- **015 introduced `Auth:AllowPlainTextSessions`** (default `false`, surfaced as
  `HOMON_ALLOW_PLAINTEXT_SESSIONS`) and the `$homon_forwarded_proto` allow-list map in
  `src/Homon.Web/nginx.conf`. Anything that later touches the session cookie's `SecurePolicy`,
  the forwarded-headers block, or that map must read `docs/ARCHITECTURE.md` §3.21 first: the
  flag is only safe *because* nginx forwards the client's real scheme, and the two are a pair.
- **016 also edits `compose.prod.yaml`**; it and 015 touched different lines and merged cleanly.
  Both shipped in v0.1.1.
- **017 is independent of everything** and touches only `deploy.sh` and the runbook. It exists
  because the maintainer's host names its Compose file `compose.yaml`, which `deploy.sh` cannot
  find, so the documented update path does not run there. Note what 017 deliberately does *not*
  fix: `deploy.sh` still never carries a changed `compose.prod.yaml` onto the host. That is the
  sharper problem — v0.1.1 changed that file twice — and it needs its own plan.
- **015 introduces `Auth:AllowPlainTextSessions`** (default `false`) and the
  `$homon_forwarded_proto` allow-list map in `src/Homon.Web/nginx.conf`. The two changes are
  coupled and ordered: nginx currently overwrites `X-Forwarded-Proto` with its own `$scheme`,
  so relaxing the cookie policy on its own would strip `Secure` from the WAF-fronted path as
  well as the LAN one. 015 also claims `docs/ARCHITECTURE.md` §3.21 (confirm at execution).
- **015 is the first plan to boot a test host in `Production`.** `HomonApiFactory` forces
  `Development`, and a Production host runs two extra validators — `Email:ResendApiToken` must
  be non-empty and `Weather:Provider` must not be `Fake`. 015's derived factory supplies the
  first; any later plan needing a Production host should reuse that shape rather than
  rediscovering it.
- **016 is independent of 015** but shares its release. Note that `compose.prod.yaml` on the
  maintainer's host already carries 016's fix as a local edit, so that copy has diverged from
  the repo until this lands.
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
  (005), TipTap under the report-only CSP in a real browser (007).
- **Open-Meteo, confirmed 2026-10-01** (plan 020, Step 0), so no longer a verify-at-execution
  item: `utc_offset_seconds` is a top-level integer; the `hourly` block is anchored at today
  00:00 *local*, 24 rows per forecast day; `daily` carries `forecast_days` entries with today
  first; `sunrise`/`sunset` are local zone-less ISO stamps; `snowfall_sum` is **centimetres**
  while `precipitation_sum` is millimetres; and `precipitation_unit=inch` converts both. The
  attribution wording the dashboard shipped ("Weather data by Open-Meteo.com", linking
  <https://open-meteo.com/>) was left as plan 010 wrote it.
