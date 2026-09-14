# Homon

A self-hosted **home dashboard** for a household: the status of the services you run
(ping, SMB, HTTP, SNMP probes with a green / orange / red dot and an uptime figure), links
to those services, a few explanation pages, the weather, the family calendar, and proof
that last night's backups ran — on one page that works on a phone.

Nothing about your house is hard-coded. Every probe, link and page is added by the
administrator through the admin pages; anyone on the trusted network reads the dashboard
without signing in (a switch closes it if you want that).

**Status: phase 0 — scaffolding.** The plumbing is complete and the gate is green, but no
module has landed yet. `docs/MODULES.md` is the roadmap; `docs/design-brief.md` is what the
design pass works from.

## Stack

ASP.NET Core 10 minimal API · PostgreSQL 18 · React 19 + Vite SPA served by nginx ·
Docker Compose on one published port. Cookie session for the administrator, API keys for
scripts, Resend for alert email. MIT licence.

## Quickstart (development)

```bash
# once: a PostgreSQL role and database — docs/postgres-setup-dev.md — and the secrets:
dotnet user-secrets --project src/Homon.Api set "ConnectionStrings:Homon" "Host=localhost;Port=5432;Database=homon;Username=homon;Password=…"
dotnet user-secrets --project src/Homon.Api set "Administrator:Email" "you@example.com"
dotnet run --project src/Homon.Api -- hash-password   # then:
dotnet user-secrets --project src/Homon.Api set "Administrator:PasswordHash" "<the hash>"
dotnet run --project src/Homon.Api -- migrate

# then, in two terminals:
dotnet run --project src/Homon.Api             # API on http://localhost:5301
npm --prefix src/Homon.Web run dev             # SPA on http://localhost:5300, proxying /api
```

Browse **:5300**. `/admin/sign-in` takes the address and password you just hashed.

## The gate

```bash
./ci/run-ci.sh          # web, api and e2e — all three, against a throwaway PostgreSQL
./ci/run-ci.sh web      # front end only, no Docker
```

It reproduces `.github/workflows/build.yml` locally and refuses a run in which any test
skipped. `ci/README.md` explains it.

## Where to read next

| File | What it holds |
| --- | --- |
| `docs/ARCHITECTURE.md` | The decision record: what was chosen, what was rejected, why. |
| `docs/STACK.md` | Languages, versions, conventions, and which agent skills apply. |
| `docs/MODULES.md` | The six modules that are not built yet, their requirements, and the constraints already known. |
| `docs/design-brief.md` | The brief for the design pass. |
| `docs/postgres-setup-dev.md` | The development database. |
| `docs/deployment-runbook.md` | Production on the household's server. |
| `plans/` | Numbered implementation plans; `plans/README.md` is the index. |

Agents: read `CLAUDE.md` first.
