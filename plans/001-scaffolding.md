# 001 — Scaffolding

**Status: DONE** (2026-09-14). The ground work, before any feature.

## What was built

- **Solution**: `Homon.sln` with `Homon.Domain`, `Homon.Infrastructure`, `Homon.Api`,
  `tests/Homon.Api.Tests`; .NET 10, Central Package Management, warnings and style
  violations as errors, `dotnet-ef` as a local tool.
- **Plumbing**: `HomonDbContext` (Identity users/roles + `ApiKeys`) with the `InitialCreate`
  migration; the `migrate`, `hash-password` and `create-api-key` verbs; a configuration-supplied
  administrator; cookie session; `ApiKey` scheme with refusal middleware; `Reader` /
  `Administrator` / `ApiKey` policies with the `Auth:RequireSignInForReaders` switch;
  `IAlertEmailSender` (Resend / log); ProblemDetails with trace ids; OpenAPI at
  `/api/v1/openapi.json`; `/health`; `GET /api/v1/meta`; `/api/v1/auth/{sign-in,session,sign-out}`.
- **Module slots**: a README under each of `Domain/{Monitoring,Links,Pages,Backups,Weather,Calendar}`.
- **SPA**: Vite 8 + React 19 + TypeScript 7, react-router 8, TanStack Query 5; routes for the
  dashboard, pages, sign-in and the admin subtree behind `RequireAdministrator`; unstyled.
- **Tests**: 47 xunit tests (7 database-backed, cloned per class from a migrated template);
  11 Vitest tests; Playwright with a `setup` project and two viewports.
- **Gate**: `ci/run-ci.sh` (`web` / `api` / `e2e`), `ci/compose.ci.yaml` on 55433,
  `ci/guard-docker.py`, `.claude/settings.json`, `.vscode/`; GitHub `build.yml` (PR +
  dispatch), `release.yml` (GHCR on tag), Dependabot.
- **Docker**: multi-stage Dockerfiles for the API and the SPA; nginx proxying `/api/`;
  `compose.prod.yaml` (postgres bind-mounted, migrator, api, web on one port);
  `docker-compose.yml` for a standalone dev database; `.env.example`; `deploy.sh`.
- **Docs**: README, CLAUDE.md, ARCHITECTURE (§3.1–§3.11), STACK, MODULES, design brief,
  postgres setup, deployment runbook, this index.

## Decisions recorded

`docs/ARCHITECTURE.md` §3.1–§3.11. The departures from the sibling project: one published
port with nginx proxying `/api/`; PostgreSQL bind-mounted for the host's restic job; an
optional administrator; `pull_request` triggering `build.yml`; roles rather than a rank
ladder; a single generic email method.

## Left for the maintainer

- Create the `homon` role and database in the shared development container and set the
  user secrets (`docs/postgres-setup-dev.md` §2–§3).
- Create the GitHub repository and push; the first tag publishes images.
- Supply the design guidelines section of `docs/design-brief.md`.
