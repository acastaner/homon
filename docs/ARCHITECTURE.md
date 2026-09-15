# ARCHITECTURE.md — Homon

The decision record. Numbered so code comments can cite a section (`§3.4`); a section is
never renumbered, only superseded by a later one that says so.

## 1. What Homon is

A self-hosted home dashboard for one household, open-sourced so another household can run
it unchanged. Readers are the family, on phones, glancing; the administrator is one person
who configures probes, links and pages. It runs as containers on a home server, behind
whatever network access control the household already has.

## 2. Shape

- **One repository, four .NET projects, one SPA.** `Homon.Domain` (entities, no
  dependencies) → `Homon.Infrastructure` (EF Core, Identity stores, email, the
  administrator) → `Homon.Api` (the only host: minimal APIs, auth, CLI verbs) ←
  `Homon.Api.Tests`. `Homon.Web` is a Vite app beside them, not in the solution.
- **Modules, not layers, are the unit of work.** Each feature — Monitoring, Links, Pages,
  Backups, Weather, Calendar — lands as one plan that touches Domain, Infrastructure, Api and
  Web together. Phase 0 left a README per module under `src/Homon.Domain/`;
  `docs/MODULES.md` is the index and the record of the constraints already known.
- **Modelled on a sibling.** The repository transposes the conventions of the maintainer's
  earlier project (Chronolectum): the layout, the CPM/analyzer settings, the endpoint
  pattern, the test fixtures, the CI gate, the Docker topology and the comment culture.
  Where Homon departs from it, the departure is recorded below.

## 3. Decisions

### 3.1 Readers are anonymous; the network is the gate; one switch closes it

The brief: no authentication for readers, access enforced by trusted-network rules on the
reverse proxy — but the ability to require sign-in later. So every read endpoint carries
the `Reader` policy (`HomonPolicies.Reader`), whose handler admits everyone while
`Auth:RequireSignInForReaders` is false and only authenticated principals once it is true.
The option is read from the built container on every evaluation (`ReaderHandler` takes
`IOptionsMonitor<AuthOptions>`), so a test host's override is honoured and no endpoint knows
the switch exists. `GET /api/v1/meta` and `/health` stay anonymous either way: the SPA
needs the former to know which mode it is in.

*Rejected:* two route groups (open and closed) swapped at startup — every future endpoint
would have had to be registered in the right one, and the mistake is silent.

### 3.2 The administrator is configuration, not a row

`Administrator:Email` + `Administrator:PasswordHash` (Identity's PBKDF2, minted by the
`hash-password` verb). No database row: a dump of the database yields nothing that signs
in, and a leaked environment yields only a hash. The sign-in endpoint checks this account
*first* and verifies the hash unconditionally, so a wrong address and a wrong password take
the same time. The cookie's security-stamp validation is skipped for this one subject —
its id is not a Guid, and the lookup would throw.

Identity's user and role tables exist anyway (`HomonDbContext : IdentityDbContext<…>`), and
the sign-in path for a database-backed account is complete, so a second administrator later
is a feature rather than an auth rewrite. Roles, not a rank ladder: Homon has exactly one
privilege level to name.

**Both values are optional**, and that is a departure from the sibling. A self-hoster's
first `docker compose up` should show them the dashboard; `/meta` reports
`administratorConfigured: false`, the admin gate says why sign-in refuses everyone, and a
warning is logged at boot. Setting one without the other is refused at startup.

### 3.3 API keys are for scripts, and never administer

The backup scripts must report to the API, so there is a second way in:
`Authorization: Bearer hmn_<tokenId>_<secret>` (or `X-Api-Key`). The token id is a public
Crockford-base32 handle; the secret is 32 random bytes whose SHA-256 is stored — a
password hasher would only add latency to an automated call. A policy scheme forwards each
request to the key handler or the cookie handler by what it carries, so no endpoint knows
which it got.

A key's principal has no user id and no role: `HomonPolicies.ApiKey` admits it, `Reader`
admits it, `Administrator` never does. And a key that *fails* to authenticate fails the
whole request (`ApiKeyRefusalMiddleware`) rather than being demoted to anonymous — because
readers are anonymous by default, a revoked key would otherwise keep getting 200s from
every read while its reports silently 401.

Until the Backups module ships its admin page, `create-api-key --name …` is the minter.

### 3.4 One origin, one published port

The SPA calls the relative `/api/v1`; in development the Vite dev server proxies it, in
production the SPA container's nginx does (`src/Homon.Web/nginx.conf`, `location /api/`).
The session cookie is therefore first-party, CORS never engages, and a deployment publishes
exactly one port (`HOMON_HOST_PORT`, 8102 by default). The `api` and `postgres` services
publish nothing.

*Departure from the sibling*, whose host nginx does the split and whose containers bind
loopback. Homon's intended host has no host nginx — its front door is a WAF's virtual
server or the LAN itself — and a generic self-hoster expects one port to point their proxy
at. `HOMON_BIND_ADDRESS` exists for a host whose proxy is local.

### 3.5 PostgreSQL's data is a bind mount in production

`compose.prod.yaml` mounts `./data/postgres` rather than a named volume. The maintainer's
host backs up bind mounts under its docker-apps directory with restic and backs up no named
volume; a named volume would have been the one piece of state in the stack no backup
covered. The key ring (`dataprotection-keys`) stays a named volume and the runbook says how
to export it. On PostgreSQL 18 the mount is `/var/lib/postgresql`, not `…/data`.

### 3.6 Migrations are a verb, never a startup call

`migrate` is a CLI verb on the API host (`Program.cs`), and `compose.prod.yaml` runs it as a
one-shot `migrator` service from the same image before `api` starts. One image means the
migration code and the application code are always the same build; a verb means applying a
migration is a deliberate act with a lock and a failure mode, not something every replica
races for on boot. `dotnet run -- migrate` is also how a developer applies them.

### 3.7 Email is one seam with two transports

`IAlertEmailSender.SendAsync(EmailMessage)` — a single generic method, because alerts are
the only mail Homon sends and the alerting module owns the templates. `ResendEmailSender`
and `LoggingEmailSender` are both registered; which one resolves is decided from
`IOptions<EmailOptions>` at resolve time, so a test's configuration override wins, and a
development machine never mails anyone. A Production host without a token refuses to start.

### 3.8 The gate runs locally and refuses skips

`./ci/run-ci.sh` runs `web`, `api` and `e2e` against a throwaway `postgres:18-alpine`
(`homon-ci`, port 55433) and fails the api suite if any test was skipped — because without
`HOMON_TEST_CONNECTION` the `[DatabaseFact]` tests skip and `dotnet test` still exits 0.
Each test class gets its own database cloned from a migrated template (`TestDatabase`), so
the collections run in parallel and share no data. GitHub's `build.yml` is the same three
jobs, and runs only when asked: `workflow_dispatch`, or `workflow_call` from `release.yml`,
which runs it on a `v*.*.*` tag and pushes no image unless it passes. There is no
`pull_request` trigger — Dependabot's pull requests fired it on every bump — so a pull
request is proved locally or by a dispatch against its branch.

`ci/guard-docker.py` is a Claude Code hook that blocks destructive docker commands outside
`homon-ci`, because the development database lives in a container another project owns.

### 3.9 Ports

Development 5300 (SPA) / 5301 (API); e2e 5310 / 5311; CI PostgreSQL 55433; production
8102. All chosen not to collide with the sibling's, so both stacks run on one machine.

### 3.10 Phase 0 ships no style

The SPA renders semantic HTML with landmarks and accessible names and not one `className`.
Tailwind v4 and the shadcn toolchain are installed and unused. The design pass
(`docs/design-brief.md`, plan 012) starts from that blank sheet; the unit and Playwright
tests query by role and name, so they survive it — and must keep surviving it.

### 3.11 Security headers, problem details, request ids

Every response carries `X-Content-Type-Options`, `X-Frame-Options: DENY`,
`Referrer-Policy` and `X-Request-Id` (the bare TraceId, also the problem body's `traceId`
and the JSON log scope, so a report greps straight to its log line). Failures are RFC 9457
problems. Mutating endpoints require `application/json` — a cross-site form cannot produce
it — which is the CSRF posture, and why there is no antiforgery token. nginx adds a
report-only CSP on the SPA.

### 3.12 The look is a status board, dark by default

Chosen on 15 September 2026 from four directions drawn at phone and desktop width
(tinted tiles, status board, warm home, dark console; all kept in `docs/design/`). The
status board won because it reads like a table where a table is the truth — one row per
probe, uptime and last check in aligned columns — and still answers the family's
question at a glance: anything wrong tints its whole row and carries a glyph and a word.
The tinted tiles, closest to the reference screenshots, were rejected because orange and
red fight a saturated ground; the warm home because its sentence headline has to stay
honest from zero probes to forty; the dark console because its light variant has no
character.

Dark is served by default to every visitor, not only to those whose system asks for it.
That overrides the brief's original "respect `prefers-color-scheme`": a home dashboard is
often left open on a shared screen, and the maintainer wants one predictable look. The
warm-paper light scheme is a first-class alternative behind an explicit choice, stored
per browser. Following the system instead is a one-line change in the theme bootstrap,
and is the obvious thing to revisit if readers ask.

The written specification — tokens in both schemes, type ramp, component rules — is the
"Design guidelines" section of `docs/design-brief.md`. The artboards illustrate it; where
they disagree, the guidelines win.

## 4. Things this record does not yet decide

The probe scheduler's shape (one `BackgroundService` with a per-probe timer, or a channel
of due work), the uptime window's exact definition, the WYSIWYG editor, the weather and
calendar providers. Each is a module plan's decision and will be recorded here when made.
