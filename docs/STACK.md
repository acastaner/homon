# STACK.md — Homon

Per-project skill manifest. Agents read this file to decide which skill families apply in
this repository, and which to leave alone.

## Stack

Homon is a self-hosted home dashboard: service status probes, links, explanation pages, a
weather widget, a family calendar and backup-run reports. A React single-page front end
talks to an ASP.NET Core Web API backend over one origin.

## Languages and runtimes

| Tier | Language | Runtime / framework |
| --- | --- | --- |
| Backend | C# | .NET 10 (`net10.0`); `global.json` pins the SDK floor to `10.0.100` with `rollForward: latestMinor` |
| Frontend | TypeScript 7 | React 19, built with Vite 8 |
| UI components | TypeScript / CSS | shadcn/ui toolchain (Base UI, `nova` preset) + Tailwind CSS v4 — **installed, unused until the design pass** |
| Database | SQL | PostgreSQL 18 through EF Core 10 + Npgsql |
| Tests | C# / TypeScript | xUnit 2.9 on VSTest (`WebApplicationFactory`, a database clone per test class); Vitest 5 + Testing Library; Playwright |

Node.js: `.nvmrc` says 24 and `ci/run-ci.sh` refuses any other major. NuGet pins exactly
via Central Package Management (`Directory.Packages.props`); npm uses caret ranges in
`src/Homon.Web/package.json`, with `package-lock.json` as the resolved source of truth.

**Read `docs/ARCHITECTURE.md`** for the design decisions behind all of this.

## Skill families in scope

| Family | Why it applies here |
| --- | --- |
| `dotnet` | Local SDK setup and pinning for the .NET 10 toolchain. |
| `dotnet-webapi` / `dotnet-aspnet` | The backend is a minimal-API Web API — endpoint design, HTTP semantics, OpenAPI metadata, error handling. |
| `dotnet-data` | Probes, observations, links, pages and backup runs are relational; EF Core modelling and query work belongs here. |
| `dotnet-test` | Test authoring and execution under `tests/`. |
| `dotnet-msbuild` | Shared build settings across the multi-project solution (`Directory.Build.props`), and binlog analysis when builds fail. |
| `dotnet-nuget` | Central Package Management so the projects share one set of package versions. |
| `shadcn` | The component layer of the React front end will be built on shadcn/ui. `shadcn`, `tailwindcss`, `@tailwindcss/vite` live in `devDependencies` — none is imported from JS/TS — so **plain `npm audit`, not `npm audit --omit=dev`**, is the command that covers the build chain. |
| `vercel-react-best-practices` | General React and TypeScript authoring guidance for the Vite app. |

## Families to avoid

Installed on the maintainer's machine, but **out of scope** here — do not invoke them:

| Family | Why not |
| --- | --- |
| `dotnet-blazor` | The front end is React. There are no `.razor` components. |
| `dotnet-maui` | No mobile or desktop client. |
| `dotnet-upgrade` | Greenfield on .NET 10 — nothing to migrate from. |
| `dotnet-template-engine` | Scaffolding is done; not part of ongoing work. |
| `dotnet-diag` | No profiling or dump analysis until there is a running system with a measured problem. |
| `chronolectum`, `forgelog`, `chronovox` | Sibling projects' skills. Unrelated. |

## Conventions for this project

### Build and test commands

A change is not done until every one passes.

```bash
./ci/run-ci.sh          # all three suites: web, then api, then e2e
./ci/run-ci.sh web      # front end only — no Docker involved
./ci/run-ci.sh api      # backend only, against a throwaway PostgreSQL
./ci/run-ci.sh e2e      # Playwright against the real stack, at two viewports
```

It reproduces `.github/workflows/build.yml` step for step against a `postgres:18-alpine` in
the `homon-ci` Compose project, and fails if any backend test was skipped. `ci/README.md`.

Individually: `dotnet build Homon.sln` (the formatting gate — `dotnet format` is broken on
this SDK), `dotnet test Homon.sln`, and in `src/Homon.Web`: `npm run lint`, `npm run build`
(`tsc -b && vite build`), `npm test`, `npx playwright test`.

### Project layout

```
src/Homon.Domain/            entities, enums, invariants. No dependencies.
src/Homon.Infrastructure/    EF Core DbContext + migrations, Identity, email, the administrator
src/Homon.Api/               the only host: Program.cs, Authentication/, Configuration/, Endpoints/, Cli/
src/Homon.Web/               the SPA; e2e/ holds Playwright; nginx.conf and Dockerfile ship it
tests/Homon.Api.Tests/       the only test project
```

### Other conventions

- Endpoints are `internal static class XxxEndpoints` with `MapXxxEndpoints(this RouteGroupBuilder)`,
  registered in one list in `Program.cs` under the `/api/v1` group. `TypedResults`; problem
  details; XML doc comments on wire types (they feed the OpenAPI document).
- Options are `public sealed class XOptions { public const string SectionName; … }` bound with
  `ValidateDataAnnotations().ValidateOnStart()`.
- CLI verbs (`hash-password`, `migrate`, `create-api-key`) are parsed in `Program.cs` before the
  web host is built, with a `Cli/*Arguments.cs` parser each.
- Front end: kebab-case files, named exports, `-page.tsx` suffix for pages, `admin-` prefix for
  admin pages, `lib/` for everything non-visual, colocated `*.test.tsx`.
- Comments explain the decision and the rejected alternative. That is the house style.

### Source vs design files

`docs/design-brief.md` and, later, `docs/<design-project>/` hold the design pass's inputs
and outputs. They are documentation, never imported by the build.
