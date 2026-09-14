#!/usr/bin/env bash
set -euo pipefail

# Homon production deploy/update script — single attended command.
#
# Usage: ./deploy.sh <version>
#   <version>  Release version WITHOUT the leading 'v', e.g. ./deploy.sh 0.1.0
#              Matches a published GitHub Release tag vX.Y.Z that has homon-api / homon-web
#              images on GHCR (produced by .github/workflows/release.yml).
#
# This script is intentionally ATTENDED, not automatic: a human must invoke it, and it
# prompts for confirmation before touching the running stack. It does NOT run on a timer
# or watch for new releases — see docs/deployment-runbook.md for the rationale.
#
# This script is for UPDATING an already-running stack. For the very first bring-up on a
# fresh host, follow docs/deployment-runbook.md's "First bring-up" section by hand instead.
#
# The `migrate` verb is baked into the api image itself (src/Homon.Api/Program.cs), so the
# migrator service in compose.prod.yaml just runs that image with a different command —
# migration code and application code are always the same build.
#
# Run this script as the user that owns the Docker daemon (on a rootless host, the service
# account), from the same directory as compose.prod.yaml. No sudo inside.

COMPOSE_FILE="compose.prod.yaml"
ENV_FILE=".env"
BACKUP_DIR="backups"
BACKUP_KEEP=10

if [[ $# -ne 1 ]]; then
  echo "Usage: $0 <version>   e.g. $0 0.1.0" >&2
  exit 1
fi

VERSION="$1"

# The easy mistake: the git tag carries a leading 'v' (v0.1.0), but the GHCR image tag and
# this script's argument do not (0.1.0) — docker/metadata-action's type=semver strips it.
if [[ "${VERSION}" =~ ^v ]]; then
  echo "ERROR: ${VERSION} has a leading 'v'. Pass the bare version, e.g. $0 ${VERSION#v}" >&2
  exit 1
fi

if [[ ! "${VERSION}" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "ERROR: '${VERSION}' is not a bare semantic version (expected X.Y.Z, e.g. 0.1.0)." >&2
  exit 1
fi

TAG="v${VERSION}"

if [[ ! -f "${COMPOSE_FILE}" ]]; then
  echo "ERROR: ${COMPOSE_FILE} not found in $(pwd). Run this script from the directory containing ${COMPOSE_FILE}." >&2
  exit 1
fi

if [[ ! -f "${ENV_FILE}" ]]; then
  echo "ERROR: ${ENV_FILE} not found in $(pwd). Copy .env.example to .env and fill in real values first." >&2
  exit 1
fi

if ! command -v docker >/dev/null 2>&1; then
  echo "ERROR: docker is required." >&2
  exit 1
fi

# Prove Compose can actually READ the topology before anything else runs. `config -q` is
# the cheapest possible check and touches nothing: it parses the file, interpolates it
# against .env, and says so if it cannot. Without it a missing variable surfaces as "the
# pre-migration backup failed", pointing at a healthy database.
if ! COMPOSE_CONFIG_ERROR=$(docker compose -f "${COMPOSE_FILE}" config -q 2>&1); then
  echo "ERROR: ${COMPOSE_FILE} cannot be read with the current ${ENV_FILE}:" >&2
  echo >&2
  echo "${COMPOSE_CONFIG_ERROR}" >&2
  echo >&2
  echo "       This is a CONFIGURATION problem, not a database or image one, and nothing" >&2
  echo "       has been touched. A 'required variable ... is missing a value' line above" >&2
  echo "       means ${COMPOSE_FILE} demands a variable ${ENV_FILE} does not set." >&2
  exit 1
fi

# `set -e` aborts this script on any failed command — but on its own it aborts *silently*,
# printing nothing after whatever banner was last echoed. The trap guarantees every abort
# says so explicitly.
on_exit() {
  local rc=$?
  if (( rc != 0 )); then
    echo >&2
    echo "ERROR: deploy aborted (exit code ${rc}). See the message above for the cause." >&2
    echo "       The running stack was NOT updated. Re-run this script once the cause is fixed." >&2
  fi
}
trap on_exit EXIT

echo "=================================================================="
echo "Homon production deploy"
echo "  Target version : ${VERSION}  (release tag ${TAG})"
echo "  Compose file   : ${COMPOSE_FILE}"
echo "  Images         : ghcr.io/acastaner/homon-api:${VERSION}"
echo "                   ghcr.io/acastaner/homon-web:${VERSION}"
echo "=================================================================="
read -r -p "Proceed with deploying version ${VERSION}? This will run the migrator and restart api/web. [y/N] " CONFIRM
if [[ "${CONFIRM}" != "y" && "${CONFIRM}" != "Y" ]]; then
  echo "Aborted — no changes made."
  exit 0
fi

echo "--- Taking pre-migration backup ---"
# mkdir -p first: the shell redirect below creates BACKUP_FILE by opening it, but it will
# not create BACKUP_DIR.
mkdir -p "${BACKUP_DIR}"
BACKUP_FILE="${BACKUP_DIR}/homon_pre_${VERSION}_$(date +%Y%m%d_%H%M%S).dump"

# Wrapped so a non-zero pg_dump does not trip `set -e` before the diagnostics below — and
# checked for content as well as exit status, because the shell redirect creates
# ${BACKUP_FILE} before pg_dump ever runs, so a failed dump still leaves a plausible-looking
# file behind. "PGDMP" is the custom-format magic. This dump is the only rollback path if
# the migration goes wrong, so a silently bogus one is worse than no deploy at all.
BACKUP_STATUS=0
docker compose -f "${COMPOSE_FILE}" exec -T postgres \
  pg_dump -U homon -d homon --format=custom > "${BACKUP_FILE}" || BACKUP_STATUS=$?

if (( BACKUP_STATUS != 0 )) || [[ "$(head -c 5 "${BACKUP_FILE}" 2>/dev/null)" != "PGDMP" ]]; then
  echo "ERROR: the pre-migration backup failed (pg_dump exit ${BACKUP_STATUS}); the dump is missing" >&2
  echo "       its PGDMP header and is not a usable rollback point." >&2
  echo "       Stopping before anything is changed. Check the database is up:" >&2
  echo "         docker compose -f ${COMPOSE_FILE} ps postgres" >&2
  echo "         docker compose -f ${COMPOSE_FILE} logs --tail=50 postgres" >&2
  rm -f "${BACKUP_FILE}"   # never leave an unusable file that looks like a backup
  exit 1
fi
echo "Backup written to ${BACKUP_FILE} ($(du -h "${BACKUP_FILE}" | cut -f1))"
echo "NOTE: this backup is the DATABASE ONLY. The data-protection key ring is a named"
echo "      volume — docs/deployment-runbook.md says how to export it."

echo "--- Pruning old backups (keeping the newest ${BACKUP_KEEP}) ---"
# Runs only after THIS deploy's dump verified above — a failed dump exits before reaching
# here, so pruning can never eat the last good rollback point to make room for a bad one.
mapfile -t OLD_BACKUPS < <(ls -1t "${BACKUP_DIR}"/homon_pre_*.dump 2>/dev/null | tail -n +$((BACKUP_KEEP + 1)))
if (( ${#OLD_BACKUPS[@]} > 0 )); then
  echo "Pruning ${#OLD_BACKUPS[@]} old backup(s):"
  for old in "${OLD_BACKUPS[@]}"; do echo "  ${old}"; done
  rm -f -- "${OLD_BACKUPS[@]}"
fi

echo "--- Pinning HOMON_VERSION=${VERSION} in ${ENV_FILE} ---"
if grep -q '^HOMON_VERSION=' "${ENV_FILE}"; then
  sed -i "s/^HOMON_VERSION=.*/HOMON_VERSION=${VERSION}/" "${ENV_FILE}"
else
  echo "HOMON_VERSION=${VERSION}" >> "${ENV_FILE}"
fi

echo "--- Pulling ${TAG} images ---"
docker compose -f "${COMPOSE_FILE}" pull api web migrator

echo "--- Applying migration + restarting api/web (postgres/migrator/api ordering enforced by Compose) ---"
docker compose -f "${COMPOSE_FILE}" up -d

echo "--- Verifying api health ---"
# Poll rather than a single sleep: the healthcheck's own start_period is 20s.
API_HEALTHY=0
for _ in $(seq 1 12); do
  if docker compose -f "${COMPOSE_FILE}" exec -T api wget -q -O- http://localhost:8080/health > /dev/null 2>&1; then
    API_HEALTHY=1
    break
  fi
  sleep 5
done

if (( API_HEALTHY != 1 )); then
  echo "ERROR: api did not become healthy within 60 seconds. Inspect logs:" >&2
  echo "  docker compose -f ${COMPOSE_FILE} logs migrator" >&2
  echo "  docker compose -f ${COMPOSE_FILE} logs api" >&2
  echo "  docker compose -f ${COMPOSE_FILE} ps" >&2
  exit 1
fi

# Verify the *right* thing, not just that something answers: read the version back, so a
# stack that came up healthy on the PREVIOUS image (a pull that silently no-op'd, say)
# is told apart from an actually successful deploy.
REPORTED_RELEASE=$(docker compose -f "${COMPOSE_FILE}" exec -T api \
  wget -q -O- http://localhost:8080/api/v1/meta | grep -o '"release":"[^"]*"' | cut -d'"' -f4 || true)

if [[ "${REPORTED_RELEASE}" != "${VERSION}" ]]; then
  echo "ERROR: api is healthy but reports release '${REPORTED_RELEASE}', not '${VERSION}'." >&2
  echo "  docker compose -f ${COMPOSE_FILE} logs migrator" >&2
  echo "  docker compose -f ${COMPOSE_FILE} logs api" >&2
  echo "  docker compose -f ${COMPOSE_FILE} ps" >&2
  exit 1
fi

echo "=================================================================="
echo "Deploy of version ${VERSION} complete."
echo "  Backup         : ${BACKUP_FILE}"
echo ""
echo "  Rollback (only if this release added no migration — nothing un-applies one):"
echo "    sed -i \"s/^HOMON_VERSION=.*/HOMON_VERSION=<previous version>/\" ${ENV_FILE}"
echo "    docker compose -f ${COMPOSE_FILE} up -d"
echo "  If it DID add a migration, restore ${BACKUP_FILE} first."
echo "=================================================================="
