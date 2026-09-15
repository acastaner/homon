# CLAUDE.md

Guidance for Claude Code when working in this repository.

## What this is

Homon — a self-hosted home dashboard (service status probes, links, pages, weather,
calendar, backup reports). Open source, MIT, and **generic**: nothing about any particular
household may be hard-coded. `README.md` is the front door; `docs/ARCHITECTURE.md` is the
decision record; `docs/MODULES.md` is the roadmap.

## Repository state

**Phase 0 — scaffolding — is complete and the gate is green** (`./ci/run-ci.sh` →
`PASS — web api e2e`). Every module is an empty slot: a `README.md` under
`src/Homon.Domain/<Module>/` stating what it will own, and nothing compiled. The plumbing
that exists: PostgreSQL + EF Core migrations (`migrate` verb, never on startup), ASP.NET
Identity with a configuration-supplied bootstrap administrator and a cookie session, an
`ApiKey` authentication scheme (`hmn_…` keys, `create-api-key` verb), a `Reader` policy
switched by `Auth:RequireSignInForReaders`, an `IAlertEmailSender` seam (Resend or the
log), `/health`, `/api/v1/meta`, `/api/v1/auth/*`, OpenAPI at `/api/v1/openapi.json`, an
unstyled React shell with a working sign-in form, and the three-suite gate.

**There is deliberately no style yet.** No theme tokens, no fonts, no `className` on any
element. The design pass (plan 012) owns that, and its target is now fixed: the **Status
board** direction, dark by default, specified under "Design guidelines" in
`docs/design-brief.md` and pictured in `docs/design/`. Until plan 012 lands, do not add
classes to make something "look right". Semantic HTML, landmarks and accessible names are
the contract with the tests; keep them.

## The gate

```bash
./ci/run-ci.sh            # everything; run this before calling any change done
./ci/run-ci.sh web        # npm ci, lint, build (= typecheck), vitest — no Docker
./ci/run-ci.sh api        # dotnet build (Release = the formatting gate), migrate, dotnet test — 0 skips or it fails
./ci/run-ci.sh e2e        # Playwright, two viewports, against the built SPA and a real API
```

`ci/run-ci.sh api` fails on a skipped test. Without `HOMON_TEST_CONNECTION` the
`[DatabaseFact]` tests skip and `dotnet test` still exits 0, which is why the script counts.

## Layout

```
src/Homon.Domain/          entities, no dependencies; one README per module slot
src/Homon.Infrastructure/  EF Core (Persistence/), Identity/, Email/, Administration/
src/Homon.Api/             Program.cs (CLI verbs, then the host), Authentication/, Configuration/, Endpoints/, Cli/
src/Homon.Web/             Vite + React + TS; e2e/ is Playwright; nginx.conf proxies /api/ in production
tests/Homon.Api.Tests/     xunit; HomonApiFactory (no DB) and ApiDatabaseFactory (a clone per class)
ci/                        the gate, the CI Postgres, the docker guard
docs/  plans/              decisions, roadmap, runbooks; numbered plans
```

## Conventions that bite

- **Comments carry the reasoning.** Config files, scripts and Dockerfiles explain the
  alternative that was rejected and why. Keep that up; a bare setting is a setting somebody
  will "simplify" back.
- **C#**: `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, `IDE0055` is an error, file-scoped
  namespaces, `[LoggerMessage]` not `LogWarning(...)`, `ArgumentNullException.ThrowIfNull` on
  public entry points. `dotnet format` is broken on this SDK; the Release build is the gate.
- **Endpoints**: one `internal static class XxxEndpoints` per resource with
  `MapXxxEndpoints(this RouteGroupBuilder)`, `TypedResults`, RFC 9457 problems, mutating
  endpoints take `application/json` (that is the CSRF guard). Read endpoints carry
  `.RequireAuthorization(HomonPolicies.Reader)`; writes `HomonPolicies.Administrator`; report
  endpoints for scripts `HomonPolicies.ApiKey`.
- **Configuration read before `builder.Build()` misses test overrides.** Resolve `IOptions<T>`
  from the built container (see the rate limiter in `Program.cs` and `ReaderHandler`).
- **Migrations**: `HOMON_DESIGNTIME_CONNECTION='Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x' dotnet dotnet-ef migrations add <Name> --project src/Homon.Infrastructure --startup-project src/Homon.Api --output-dir Persistence/Migrations`. Never applied on startup.
- **TypeScript**: kebab-case files, named exports (`App.tsx` is the only default), `@/` alias,
  no MSW — stub `fetch` with `src/test/fetch.ts`. `npm ci`, never `npm install`, in the gate.
- **Ports**: dev 5300 (SPA) / 5301 (API); e2e 5310 / 5311; CI Postgres 55433; production one
  port, 8102 by default.
- **Docker**: `ci/guard-docker.py` blocks destructive docker commands outside the `homon-ci`
  Compose project. The development database lives in a container another project owns; do
  not touch it. Ask.

## Secrets

Never in a committed file. Development: `dotnet user-secrets --project src/Homon.Api`.
Production: `.env` beside `compose.prod.yaml`. The administrator is a password *hash*
(`hash-password` verb), never a password.
