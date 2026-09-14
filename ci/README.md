# `ci/` — the gate, run here as well as on GitHub

`./ci/run-ci.sh` reproduces `.github/workflows/build.yml` on this machine. It exists so the
full suite — including the backend tests that need a real PostgreSQL — can be proved locally
before anything is pushed, and it is stricter than GitHub in one way: it refuses a run in
which any backend test skipped.

```bash
./ci/run-ci.sh                 # everything: web, then api, then e2e
./ci/run-ci.sh web             # front end only — no Docker involved at all
./ci/run-ci.sh api             # backend only
./ci/run-ci.sh e2e             # Playwright only, against the real stack
./ci/run-ci.sh api --keep      # leave the CI database up; the next run is faster
./ci/run-ci.sh api --no-db     # the database is already up; don't start or stop it
./ci/run-ci.sh api --reset     # throw the CI database away and start from empty (prompts)
```

## The three suites

**`web`** — `npm ci`, `npm run lint`, `npm run build` (which is `tsc -b && vite build`, so
it is the typecheck too) and `npm run test`, from `src/Homon.Web`.

**`api`** — `dotnet restore`, `dotnet build --configuration Release` (the formatting gate as
well: `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` are on in
`Directory.Build.props`), the `migrate` verb, then `dotnet test` — **0 skipped**, or it fails.

**`e2e`** — Playwright, at two viewports, against the built SPA and a running API.

The api suite's zero is the point. Without `HOMON_TEST_CONNECTION`, `DatabaseFactAttribute`
skips the database-backed tests and `dotnet test` still exits 0 — a pass that proves
roughly nothing and looks identical to a real one. The script reads the skip count out of
the run and fails if it is not zero.

`ci/results/homon.trx` is the authority on the counts — every gate run regenerates it.

## The database

`ci/compose.ci.yaml`, Compose project **`homon-ci`**, `postgres:18-alpine` — the same image,
database, role and password as the service container in `build.yml`, so a suite that is
green here and red there is telling you about your code.

It publishes **127.0.0.1:55433**, not 5432: on the maintainer's machine 5432 belongs to the
shared container that holds the *development* database. Override with `HOMON_CI_DB_PORT`
if 55433 is taken.

It runs `max_connections=200` rather than the default 100, because one server holds a
database per test class at up to many-way parallelism.

It starts empty, and `migrate` builds the schema in the database named `homon`. **The tests
do not run against that database.** They run against `homon_test_*` — one per test class,
cloned from a `homon_test_template` that `TestDatabase` builds by migrating an empty
database. Between runs the script `stop`s the container rather than taking it `down`, so the
migrated database and the template survive; every `homon_test_*` left by a run that died is
swept before the next run builds its template.

## The docker guard

`ci/guard-docker.py` is a `PreToolUse` hook on `Bash`, registered in `.claude/settings.json`.
It blocks any docker command that removes, stops or `exec`s into something outside the
`homon-ci` project. Read-only calls — `ps`, `logs`, `inspect` — are untouched anywhere.

It is a hook rather than a permission rule because permission rules match the *start* of a
command: `Bash(docker compose down:*)` never sees `docker compose -f x.yaml down -v`. The
`deny` list in `.claude/settings.json` catches the literal forms and puts the rule somewhere
a human reads; the hook is the layer that actually holds.

Self-test:

```bash
printf '{"tool_name":"Bash","tool_input":{"command":"docker rm open-webui"}}' | ./ci/guard-docker.py; echo $?   # 2
printf '{"tool_name":"Bash","tool_input":{"command":"docker ps -a"}}'          | ./ci/guard-docker.py; echo $?   # 0
```

## End-to-end testing

`src/Homon.Web/playwright.config.ts` is the whole configuration and carries the reasoning;
what follows is the shape.

**Two servers, started and stopped by Playwright itself** (`webServer`), not by Compose:
the API, `dotnet run` against the `homon-ci` database; and the SPA, `vite build && vite
preview` — the **built bundle**, proxying `/api` to the API so the whole suite runs against
one origin exactly as production does.

**Two projects, same specs**: `mobile` (Pixel 7) and `desktop` (1440×900). Ports are
5310/5311, off the development 5300/5301, so a run cannot collide with — or silently *use* —
a dev server somebody left up.

**A `setup` project runs first** and leaves the administrator's cookie in
`e2e/.results/admin.json`. The administrator is injected into the API by the config
(`Administrator__Email` / `Administrator__PasswordHash`, a committed non-secret pair whose
password is in `e2e/admin.ts`), and the setup signs in through the form.

**Skips are deliberate here**, which is why `run_e2e` has no `assert_nothing_skipped`:
viewport-scoped specs skip on purpose.

### Two things that will bite

**An environment variable whose name contains a dot does not survive the shell.** Playwright
spawns the servers through `sh -c`, which drops names that are not valid identifiers — so
`Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=Warning` reaches nothing,
silently. Such keys are passed as `--key=value` command-line arguments instead.

**The suite must not be able to send email.** `dotnet run` in Development loads the
developer's user-secrets store, which may hold a real `Email:ResendApiToken`.
`Email__ResendApiToken: ''` in the webServer environment is what puts `LoggingEmailSender`
back. Do not remove it.
