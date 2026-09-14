#!/usr/bin/env bash
set -euo pipefail

# Homon — the CI gate, run locally on this machine.
#
# Usage: ./ci/run-ci.sh [suite ...] [options]
#
#   Suites (default: all of them, in this order)
#     web        npm ci, lint, build, test — from src/Homon.Web. No Docker.
#     api        the backend suite against a throwaway PostgreSQL: restore, build,
#                migrate, dotnet test with HOMON_TEST_CONNECTION set.
#     e2e        Playwright against the real stack: the API on the CI database and the
#                BUILT SPA, driven at two viewports. Needs Docker and a Chromium.
#
#   Options
#     --keep     leave the CI database running when the run finishes (faster next run)
#     --reset    destroy and recreate the CI database before running (prompts)
#     --no-db    do not start or stop anything; assume the CI database is already up
#     --yes      answer yes to --reset's prompt (for non-interactive use)
#     -h|--help  this text
#
# This reproduces .github/workflows/build.yml step for step. When you change one, change
# the other: the value of this script is that a green run here means a green run there.
#
# ┌─────────────────────────────────────────────────────────────────────────────────────┐
# │ The ONLY containers this script may address belong to the `homon-ci` Compose        │
# │ project. The development machine runs containers for other projects, including the  │
# │ shared PostgreSQL that holds Homon's own development database. Every docker call    │
# │ below carries `-p homon-ci -f ci/compose.ci.yaml`, and ci/guard-docker.py blocks    │
# │ destructive docker commands that do not. See docs/postgres-setup-dev.md §0.         │
# └─────────────────────────────────────────────────────────────────────────────────────┘

CI_PROJECT="homon-ci"
CI_COMPOSE="ci/compose.ci.yaml"
CI_DB_PORT="${HOMON_CI_DB_PORT:-55433}"
RESULTS_DIR="ci/results"

# One place, so the migrate step and the test step can never disagree about which database
# they are talking to. Same shape as build.yml's, with the port moved off 5432.
CI_CONNECTION="Host=127.0.0.1;Port=${CI_DB_PORT};Database=homon;Username=homon;Password=homon"

compose() { docker compose -p "${CI_PROJECT}" -f "${CI_COMPOSE}" "$@"; }

usage() { sed -n '6,22p' "$0" | sed 's/^# \{0,1\}//'; }

# --- Arguments ---------------------------------------------------------------------------

SUITES=()
KEEP=0
RESET=0
NO_DB=0
ASSUME_YES=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    web|api|e2e) SUITES+=("$1") ;;
    all)         SUITES+=(web api e2e) ;;
    --keep)      KEEP=1 ;;
    --reset)     RESET=1 ;;
    --no-db)     NO_DB=1 ;;
    --yes|-y)    ASSUME_YES=1 ;;
    -h|--help)   usage; exit 0 ;;
    *)
      echo "ERROR: unknown argument '$1'." >&2
      echo >&2
      usage >&2
      exit 1
      ;;
  esac
  shift
done

if [[ ${#SUITES[@]} -eq 0 ]]; then
  # e2e is in the default run, not an opt-in. A suite nobody runs catches nothing.
  SUITES=(web api e2e)
fi

if (( RESET == 1 && NO_DB == 1 )); then
  echo "ERROR: --reset and --no-db contradict each other." >&2
  exit 1
fi

# --- Preconditions -------------------------------------------------------------------------

if [[ ! -f "Homon.sln" || ! -f "${CI_COMPOSE}" ]]; then
  echo "ERROR: run this from the repository root (Homon.sln and ${CI_COMPOSE} must be here)." >&2
  exit 1
fi

WANTS_DB=0
for suite in "${SUITES[@]}"; do
  [[ "${suite}" == "api" || "${suite}" == "e2e" ]] && WANTS_DB=1
done

if (( WANTS_DB == 1 )) && ! command -v docker >/dev/null 2>&1; then
  echo "ERROR: docker is required for the '${SUITES[*]}' suite(s). '${0} web' needs no Docker at all." >&2
  exit 1
fi

# --- Failure handling ----------------------------------------------------------------------
#
# `set -e` aborts on any failed command, which is what we want — but on its own it aborts
# *silently*, printing nothing after whatever banner was last echoed. Same trap, and the
# same reason, as deploy.sh.
on_exit() {
  local rc=$?
  if (( rc != 0 )); then
    echo >&2
    echo "ERROR: CI run aborted (exit code ${rc}). See the message above for the cause." >&2
    if (( WANTS_DB == 1 && KEEP == 0 && NO_DB == 0 )); then
      echo "       The CI database was left running so you can inspect it:" >&2
      echo "         docker compose -p ${CI_PROJECT} -f ${CI_COMPOSE} logs postgres" >&2
      echo "         psql '${CI_CONNECTION//;/ }'" >&2
    fi
  fi
}
trap on_exit EXIT

# --- The CI database -----------------------------------------------------------------------

reset_database() {
  echo "--- Destroying the ${CI_PROJECT} database ---"
  echo "This removes the ${CI_PROJECT} container and its volume. It does NOT touch the"
  echo "development database or anything else."
  if (( ASSUME_YES == 0 )); then
    if [[ ! -t 0 ]]; then
      echo "ERROR: --reset needs confirmation and stdin is not a terminal. Pass --yes if you mean it." >&2
      exit 1
    fi
    read -r -p "Destroy the ${CI_PROJECT} database and start from empty? [y/N] " CONFIRM
    if [[ "${CONFIRM}" != "y" && "${CONFIRM}" != "Y" ]]; then
      echo "Aborted — nothing removed."
      exit 0
    fi
  fi
  compose down -v
}

start_database() {
  echo "--- Starting the ${CI_PROJECT} database on 127.0.0.1:${CI_DB_PORT} ---"
  compose up -d

  # Poll for health rather than sleeping: an empty PostgreSQL is usually ready in a second
  # or two, but a cold image pull or a first-run initdb is not, and a fixed sleep reports
  # failure on the slow-but-fine case and races on the fast one.
  for _ in $(seq 1 60); do
    if compose exec -T postgres pg_isready -U homon -d homon >/dev/null 2>&1; then
      echo "Database is ready."
      return 0
    fi
    sleep 1
  done

  echo "ERROR: the CI database did not become ready within 60 seconds." >&2
  echo "       docker compose -p ${CI_PROJECT} -f ${CI_COMPOSE} logs postgres" >&2
  echo "       A likely cause is host port ${CI_DB_PORT} already being in use — set" >&2
  echo "       HOMON_CI_DB_PORT to something free and try again." >&2
  exit 1
}

stop_database() {
  # `stop`, never `down`: the container and its volume survive, so the next run reuses the
  # migrated database instead of rebuilding it. --reset is the way to throw it away.
  echo "--- Stopping the ${CI_PROJECT} database (volume kept; --reset discards it) ---"
  compose stop >/dev/null
}

# --- Suites --------------------------------------------------------------------------------

run_web() {
  echo
  echo "=================================================================="
  echo "Suite: web — npm ci, lint, build, test"
  echo "=================================================================="
  # This is the real enforcement of the Node pin. package.json's "engines" and .nvmrc are
  # both declarative — npm only warns on an engines mismatch, and .nvmrc is read by nvm only
  # when a human cd's into the web directory, never by this script.
  node_major="$(node --version | sed -E 's/^v([0-9]+).*/\1/')"
  if [ "${node_major}" != "24" ]; then
    echo "ERROR: Node ${node_major} found; this suite needs Node 24 (a green run here means a" >&2
    echo "       green run there only if both runs use the same major)." >&2
    exit 1
  fi
  # `npm ci`, never `npm install` — a deterministic install against the committed lockfile,
  # and it fails loudly when package.json and the lockfile disagree. Same as build.yml.
  npm --prefix src/Homon.Web ci
  npm --prefix src/Homon.Web run lint
  # `npm run build` is `tsc -b && vite build`, so this is the typecheck as well.
  npm --prefix src/Homon.Web run build
  npm --prefix src/Homon.Web run test
}

run_api() {
  echo
  echo "=================================================================="
  echo "Suite: api — the full backend suite against PostgreSQL"
  echo "  Database : ${CI_PROJECT} on 127.0.0.1:${CI_DB_PORT}"
  echo "  Results  : ${RESULTS_DIR}/homon.trx"
  echo "=================================================================="

  echo "--- Restoring ---"
  dotnet restore Homon.sln

  # TreatWarningsAsErrors and EnforceCodeStyleInBuild are both on in Directory.Build.props,
  # so this is the formatting gate too. `dotnet format --verify-no-changes` is deliberately
  # absent: it times out against its own MSBuild BuildHost on SDK 10.0.x, including on a
  # pristine `dotnet new classlib`, so it would fail here for reasons unrelated to the code.
  echo "--- Building (Release; warnings and style violations are errors) ---"
  dotnet build Homon.sln --configuration Release --no-restore

  # Nothing applies migrations on startup, deliberately (Program.cs). The database above
  # starts empty, so the schema has to be put there before anything reads it.
  #
  # The tests do not run against THIS database: each test class gets its own, cloned from a
  # template that TestDatabase builds by migrating an empty one. Keep this step anyway. It
  # is the one place the migration set is applied to a real empty database by the same verb
  # production runs, and a failure here names migrations instead of surfacing inside the
  # test process as a fixture that would not construct.
  #
  # --no-launch-profile: launchSettings.json is for `dotnet run` as a server, and its
  # applicationUrl means nothing to a CLI verb.
  #
  # `migrate` builds its host through CommandHost in Program.cs, which adds the user-secrets
  # store — and a later configuration source wins. CommandHost re-adds the environment
  # after the secrets for exactly this line: without that, a developer's own connection
  # string outranks this one and `migrate` reports "already up to date" while the CI
  # database sits empty.
  echo "--- Applying migrations ---"
  ConnectionStrings__Homon="${CI_CONNECTION}" \
    dotnet run --project src/Homon.Api --configuration Release --no-build \
      --no-launch-profile -- migrate

  echo "--- Running the suite ---"
  mkdir -p "${RESULTS_DIR}"
  local log="${RESULTS_DIR}/dotnet-test.log"
  # DOTNET_hostBuilder__reloadConfigOnChange=false is a local necessity, not a preference.
  # The suite boots many hosts in parallel, each of which would otherwise open a
  # FileSystemWatcher on its configuration; a desktop's fs.inotify.max_user_instances is
  # shared with every other process, so the suite exhausts it and fails with an IOException
  # that has nothing to do with the code. A GitHub runner has the limit to itself, which is
  # why build.yml does not need this and this script does.
  HOMON_TEST_CONNECTION="${CI_CONNECTION}" \
  DOTNET_hostBuilder__reloadConfigOnChange=false \
    dotnet test Homon.sln --configuration Release --no-build \
      --logger "trx;LogFileName=homon.trx" \
      --results-directory "${PWD}/${RESULTS_DIR}" | tee "${log}"

  assert_nothing_skipped "${log}"
}

# The failure this guards against is the whole reason the script exists. Without
# HOMON_TEST_CONNECTION, DatabaseFactAttribute marks the database-backed tests Skipped and
# `dotnet test` still exits 0 — a green run that proves roughly nothing, and one that looks
# identical to a real one at a glance.
assert_nothing_skipped() {
  local log="$1"
  local skipped
  # awk rather than `bc`: bc is not installed by default on a modern Ubuntu, and a missing
  # summing tool must never be the thing that silently turns a mass skip into a pass.
  skipped=$(grep -oE 'Skipped:[[:space:]]*[0-9]+' "${log}" \
    | grep -oE '[0-9]+' \
    | awk '{ total += $1 } END { if (NR == 0) exit 1; print total }') || skipped=""

  if [[ -z "${skipped}" ]]; then
    echo "ERROR: could not read a Skipped count out of the test output (${log})." >&2
    echo "       Refusing to call this run green — check the log by hand." >&2
    exit 1
  fi

  if (( skipped != 0 )); then
    echo >&2
    echo "ERROR: ${skipped} test(s) SKIPPED. A CI run with skips is not a pass." >&2
    echo "       DatabaseFactAttribute skips when HOMON_TEST_CONNECTION is unset or" >&2
    echo "       unreachable, so this usually means the CI database was not actually there." >&2
    exit 1
  fi

  echo "0 tests skipped — the database-backed suite really ran."
}

# The end-to-end suite. Playwright starts the two servers itself, as host processes, out of
# the artifacts the other two suites already build (see playwright.config.ts's `webServer`);
# the SPA under test is the BUILT bundle, served by `vite preview`, not the dev server.
run_e2e() {
  echo
  echo "=================================================================="
  echo "Suite: e2e — Playwright against the real stack, at two viewports"
  echo "  Database : ${CI_PROJECT} on 127.0.0.1:${CI_DB_PORT}"
  echo "  Servers  : API on 127.0.0.1:5311, built SPA on 127.0.0.1:5310"
  echo "=================================================================="

  # Standalone-safe. `./ci/run-ci.sh e2e` has to work on a machine where neither of the other
  # two suites has run, so every prerequisite is established here rather than inherited.
  npm --prefix src/Homon.Web ci

  # Downloads to ~/.cache/ms-playwright, needs no sudo, and is a no-op once present. Not
  # `--with-deps`: that installs system packages, and agents on the development machine have
  # no sudo. On a GitHub runner the shared libraries are already there.
  npm --prefix src/Homon.Web exec -- playwright install chromium

  echo "--- Building the API (Release) ---"
  dotnet build src/Homon.Api --configuration Release

  # Playwright's webServer boots the API against this database, but nothing applies
  # migrations on startup (Program.cs, deliberately). Without this step the first spec meets
  # an empty schema.
  echo "--- Applying migrations ---"
  ConnectionStrings__Homon="${CI_CONNECTION}" \
    dotnet run --project src/Homon.Api --configuration Release --no-build \
      --no-launch-profile -- migrate

  echo "--- Running the suite ---"
  # No assert_nothing_skipped here, and the difference is real rather than an omission:
  # viewport-scoped specs skip ON PURPOSE. What a skip cannot mean here is "the database was
  # not there", which is the failure the api rule exists to catch.
  (
    cd src/Homon.Web
    HOMON_E2E_CONNECTION="${CI_CONNECTION}" \
      npx playwright test
  )
}

# --- Run -----------------------------------------------------------------------------------

echo "=================================================================="
echo "Homon CI — local"
echo "  Suites : ${SUITES[*]}"
echo "=================================================================="

if (( WANTS_DB == 1 && NO_DB == 0 )); then
  (( RESET == 1 )) && reset_database
  start_database
fi

for suite in "${SUITES[@]}"; do
  case "${suite}" in
    web) run_web ;;
    api) run_api ;;
    e2e) run_e2e ;;
  esac
done

if (( WANTS_DB == 1 && NO_DB == 0 && KEEP == 0 )); then
  stop_database
fi

echo
echo "=================================================================="
echo "PASS — ${SUITES[*]}"
if (( WANTS_DB == 1 )); then
  if (( KEEP == 1 || NO_DB == 1 )); then
    echo "  The CI database is still running on 127.0.0.1:${CI_DB_PORT}."
  fi
  for suite in "${SUITES[@]}"; do
    [[ "${suite}" == "api" ]] && echo "  Test results: ${RESULTS_DIR}/homon.trx"
    [[ "${suite}" == "e2e" ]] && echo "  Playwright traces (failures only): src/Homon.Web/e2e/.results/"
  done
fi
echo "=================================================================="
