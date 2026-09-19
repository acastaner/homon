# Plan 017: Let `deploy.sh` find the Compose file instead of hard-coding its name

> **Executor instructions**: Follow this plan step by step. Run every verification command and
> confirm the expected result before moving on. If anything under "STOP conditions" occurs,
> stop and report — do not improvise. When done, update this plan's row in `plans/README.md`
> (Step 5).
>
> **Drift check (run first)**:
> ```bash
> git diff --stat 143b38d..HEAD -- deploy.sh docs/deployment-runbook.md \
>   docker-compose.yml compose.prod.yaml .github/workflows/release.yml
> ```
> Empty output means no drift. If any of those files changed, compare the "Current state"
> excerpts below against the live code before proceeding; on a mismatch, treat it as a STOP
> condition.

## Status

- **Priority**: P2 — not a running-system defect, but the documented update path does not run
  at all on the host it was written for.
- **Effort**: S
- **Risk**: LOW for the change itself (one shell script, no compiled code, no test suite
  touched). MEDIUM for getting it *wrong*, because the failure mode of bad discovery is
  "deployed the wrong topology", which is why D2 and D3 exist.
- **Depends on**: none
- **Category**: dx / bug
- **Planned at**: commit `143b38d`, 2026-09-18

## Why this matters

`deploy.sh` hard-codes the Compose file name at line 25 and refuses to run without it at
line 51:

```
ERROR: compose.prod.yaml not found in /home/dockersvc/docker-apps/homon.
       Run this script from the directory containing compose.prod.yaml.
```

The maintainer's production host names that file **`compose.yaml`** — the name Docker itself
looks for first, so every bare `docker compose …` command on that host works without `-f`. The
consequence is that `deploy.sh` cannot be used there at all, and the update procedure
`docs/deployment-runbook.md:64` documents (`./deploy.sh 0.2.0`) is unusable.

What that costs is not convenience. Everything `deploy.sh` does *around* the two Compose
commands is the safety rail, and a hand-run `docker compose pull && docker compose up -d`
skips all of it:

- the verified pre-migration `pg_dump` — checked for its `PGDMP` magic, because the shell
  redirect creates a plausible-looking file even when the dump fails (lines 107–131);
- pruning old backups only *after* this deploy's dump verified (lines 135–143);
- pinning `HOMON_VERSION` in `.env` so the stack is reproducible (lines 145–150);
- polling `/health` for 60s rather than sleeping once (lines 158–175);
- reading `release` back from `/api/v1/meta` and **failing if it is not the version asked
  for**, which is what tells a successful deploy apart from a pull that silently no-op'd and
  left the previous image running (lines 177–189).

Renaming the file back on the host would also "fix" it, and is the wrong fix: `compose.yaml`
is the conventional name, the one Docker resolves with no flag, and the reason every command
in that host's shell history is shorter than the runbook's. The script should meet the
convention, not fight it.

## Current state

### The files that matter

- `deploy.sh` — the whole subject. Twenty-one lines mention `COMPOSE_FILE`, and the breakdown
  matters because it is what "do not touch the call sites" means concretely:
  **1** assignment (line 25, which Step 1 removes), **6** real `docker compose` invocations
  (lines 70, 119, 153, 156, 162, 180), **2** in the existence check (lines 51–52, which Step 2
  replaces), and **12** that interpolate the name into the banner or into an error hint. Only
  the 1 + 2 change; the 6 and the 12 are left byte-identical.
- `docker-compose.yml` — **the development stack**, at the repo root. Read its header before
  Step 1; D2 exists entirely because of this file.
- `compose.prod.yaml` — the production stack, the current default.
- `docs/deployment-runbook.md` — bring-up (line 27) and update (line 64) procedures.
- `.github/workflows/release.yml:129` — the release notes tell the operator to run
  `./deploy.sh <version>`. That string must keep working verbatim (D5).

### Excerpts, as they exist at `143b38d`

`deploy.sh:6-7` and `:23` — the usage comment in the header:

```bash
# Usage: ./deploy.sh <version>
#   <version>  Release version WITHOUT the leading 'v', e.g. ./deploy.sh 0.1.0
…
# Run this script as the user that owns the Docker daemon (on a rootless host, the service
# account), from the same directory as compose.prod.yaml. No sudo inside.
```

`deploy.sh:25-35` — the hard-coded name and the current argument handling, which is the whole
of it:

```bash
COMPOSE_FILE="compose.prod.yaml"
ENV_FILE=".env"
BACKUP_DIR="backups"
BACKUP_KEEP=10

if [[ $# -ne 1 ]]; then
  echo "Usage: $0 <version>   e.g. $0 0.1.0" >&2
  exit 1
fi

VERSION="$1"
```

`deploy.sh:51-54` — the existence check this plan replaces:

```bash
if [[ ! -f "${COMPOSE_FILE}" ]]; then
  echo "ERROR: ${COMPOSE_FILE} not found in $(pwd). Run this script from the directory containing ${COMPOSE_FILE}." >&2
  exit 1
fi
```

`docker-compose.yml:1-5` — **read this; it is why D2 exists**:

```yaml
# Local development dependencies.
#
# Development runs the API and the Vite dev server natively (`dotnet run` and `npm run dev`);
# only PostgreSQL is containerised. Production is a different topology entirely — see
# compose.prod.yaml.
```

`docs/deployment-runbook.md:64` — the documented update command, which must keep working:

```bash
cd ~/docker-apps/homon && ./deploy.sh 0.2.0     # bare version, no leading v
```

### What the gate does not cover

**`deploy.sh` is not built, linted, or tested by anything.** `./ci/run-ci.sh` compiles the
solution and the SPA and runs three suites; none of them reads a shell script. There is no
shellcheck in `.github/workflows/`. Running the gate for this plan proves only that nothing
else moved.

That does **not** mean this change is unverifiable — Step 4 builds a real harness in a throwaway
directory and exercises seven scenarios against the actual script. Do not skip it, and do not
replace it with "I read the code and it looks right."

## Decisions — implement these exactly; the reasoning is the point

**D1 — discovery order is `compose.yaml`, `compose.yml`, `compose.prod.yaml`; first match wins.**
The first two are what Docker resolves on its own, so a host using the conventional name needs
no flag; the third keeps every existing installation working untouched.

**D2 — `docker-compose.yml` and `docker-compose.yaml` are deliberately NOT candidates**, even
though Docker itself falls back to them. **This repository ships a `docker-compose.yml` at its
root and it is the development stack** — PostgreSQL only, no `api`, no `web`, "a different
topology entirely" in its own words. If it were a candidate, running `./deploy.sh 0.1.1` from a
repo checkout would resolve to it and then try to `pull api web migrator` services it does not
define and `pg_dump` a container that is not running. A discovery list that *can* select the
development stack is worse than no discovery at all. A host that genuinely uses that name passes
`-f`.

**D3 — an explicit `-f` never falls back to discovery.** If the named file is missing, error and
stop. Falling back would mean a typo (`-f compose.prd.yaml`) silently deploys whatever else is
in the directory — the one outcome this whole plan exists to prevent.

**D4 — a single `-f`, not repeatable.** Docker allows `-f a.yaml -f b.yaml` to layer overrides.
This script has no use for it, and supporting it means making `COMPOSE_FILE` an array and
rewriting all six invocations from `-f "${COMPOSE_FILE}"` to `"${COMPOSE_ARGS[@]}"`, plus the
twelve places that interpolate the name into a message. More surface, more risk, no current
caller. It stays a clean extension for the day something needs it; do not build it now.

**D5 — the version stays positional and `-f` may appear on either side of it.** `./deploy.sh
0.1.1` must keep working character-for-character: that exact string is printed into every GitHub
release's notes (`.github/workflows/release.yml:129`) and into the runbook (line 64). Releases
already published cannot be edited, so breaking it breaks documentation that is already out in
the world.

**D6 — when more than one candidate is present, warn and name them.** This is expected rather
than exotic: `docs/deployment-runbook.md` tells the operator to copy a release's
`compose.prod.yaml` onto the host, so a host whose live file is `compose.yaml` ends up holding
both. Picking one silently is how you deploy last release's topology and spend an hour reading
migrator logs. Warn, say which won, and carry on — do not make it fatal, because the correct
file is the one discovery picks.

**D7 — never `export COMPOSE_FILE`.** `COMPOSE_FILE` is also an environment variable that
`docker compose` itself reads. The script's variable is internal and every call already passes
`-f "${COMPOSE_FILE}"` explicitly, which wins over the environment. Exporting it would make the
script's internal state leak into every Compose invocation, including any the operator runs
afterwards in the same shell.

## Commands you will need

| Purpose | Command | Expected on success |
| --- | --- | --- |
| Shell syntax | `bash -n deploy.sh` | exit 0, no output |
| Scenario tests | Step 4's harness | all seven scenarios as described |
| Full gate | see below | PASS; nothing here is compiled |

This machine cannot run `./ci/run-ci.sh` in one invocation (it gets killed by memory pressure).
Run the suites separately:

```bash
dotnet build-server shutdown
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0 DOTNET_gcServer=0
./ci/run-ci.sh web
./ci/run-ci.sh api --keep
./ci/run-ci.sh e2e --no-db
docker compose -p homon-ci -f ci/compose.ci.yaml stop
```

If `shellcheck` happens to be installed, `shellcheck deploy.sh` is a useful extra — but it is
not in CI and its absence is not a failure. Do not add it to CI in this plan.

## Scope

**In scope** — exactly these files:

- `deploy.sh`
- `docs/deployment-runbook.md`
- `plans/README.md`

**Explicitly out of scope** — do not touch, even though it looks related:

- **`docker-compose.yml`** — the development stack. Read it, never edit it.
- **`compose.prod.yaml`** — nothing about the production topology changes here.
- **`.github/workflows/release.yml`** — its `./deploy.sh <version>` line stays exactly as it is;
  D5 means it keeps working. Changing it would only desynchronise it from already-published
  release notes.
- **Making `deploy.sh` fetch or update the Compose file from the release.** That is a real and
  separate defect — `deploy.sh` pulls images and pins `HOMON_VERSION` but never carries a
  changed `compose.prod.yaml` onto the host, so a release that altered the compose file is
  silently half-applied (recorded in plan 016's maintenance notes). It needs its own plan. This
  one only decides *which file to read*.
- `ENV_FILE` — `.env` stays hard-coded; nobody asked for that to move.
- Anything under `src/`, `tests/`, `ci/`.

## Git workflow

```bash
git switch -c plan/017-deploy-compose-file-discovery
```

Commit with the repo's message style, naming the plan — e.g.
`Deploy: find the Compose file instead of hard-coding its name (plan 017)`. Do not merge and do
not push; merging is the maintainer's call.

## Steps

### Step 1: Replace the argument parsing and resolve the Compose file

In `deploy.sh`, replace **lines 25–35** — from `COMPOSE_FILE="compose.prod.yaml"` through
`VERSION="$1"` — with the block below. Keep `ENV_FILE`, `BACKUP_DIR` and `BACKUP_KEEP` exactly
as they are.

```bash
ENV_FILE=".env"
BACKUP_DIR="backups"
BACKUP_KEEP=10

# Empty until -f or discovery fills it in, below.
COMPOSE_FILE=""

# The names looked for, in order, when -f is not given. Deliberately NOT docker's own full
# list: `docker-compose.yml` and `docker-compose.yaml` are excluded because THIS repository
# ships a docker-compose.yml that is the *development* stack — PostgreSQL only, no api, no
# web, "a different topology entirely" in its own header. If it were a candidate, running
# this script from a repo checkout would resolve to it and then try to pull `api` and `web`
# services it does not define, and pg_dump a container that is not running. A discovery list
# that CAN select the development stack is worse than no discovery at all. A production host
# that really uses that name passes -f.
COMPOSE_CANDIDATES=(compose.yaml compose.yml compose.prod.yaml)

usage() {
  cat >&2 <<USAGE
Usage: $0 [-f <compose-file>] <version>

  <version>            Release version WITHOUT the leading 'v', e.g. $0 0.1.0
  -f, --file <file>    Compose file to deploy. Default: the first of
                       ${COMPOSE_CANDIDATES[*]} present in the current directory.
  -h, --help           Show this message.
USAGE
}

# Declared before the loop: under `set -u`, expanding an array that was never assigned is an
# error on older bash, and this script runs on whatever the host ships.
POSITIONAL=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    -f|--file)
      if [[ $# -lt 2 ]]; then
        echo "ERROR: $1 needs a file argument." >&2
        usage
        exit 1
      fi
      COMPOSE_FILE="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    -*)
      echo "ERROR: unknown option '$1'." >&2
      usage
      exit 1
      ;;
    *)
      POSITIONAL+=("$1")
      shift
      ;;
  esac
done

if (( ${#POSITIONAL[@]} != 1 )); then
  echo "ERROR: expected exactly one <version> argument, got ${#POSITIONAL[@]}." >&2
  usage
  exit 1
fi

VERSION="${POSITIONAL[0]}"
```

Leave the two version checks (the leading-`v` guard and the semver regex) and `TAG="v${VERSION}"`
exactly where they are and unchanged — they are the cheapest checks and the most common
mistake, so they stay first.

**Verification**:

```bash
bash -n deploy.sh
```

Expected: exit 0, no output. Full behaviour is tested in Step 4.

### Step 2: Replace the existence check with discovery

Replace the whole `if [[ ! -f "${COMPOSE_FILE}" ]]` block (lines 51–54 at `143b38d`, now further
down) with:

```bash
if [[ -n "${COMPOSE_FILE}" ]]; then
  # Explicit -f never falls back to discovery: a typo must fail loudly here rather than
  # silently deploying whatever else happens to be in this directory.
  if [[ ! -f "${COMPOSE_FILE}" ]]; then
    echo "ERROR: ${COMPOSE_FILE} not found in $(pwd)." >&2
    exit 1
  fi
else
  FOUND=()
  for candidate in "${COMPOSE_CANDIDATES[@]}"; do
    # Spelled out rather than `[[ -f x ]] && FOUND+=(x)`: as the last command in the loop
    # body, a false test makes the AND-list return 1 and `set -e` aborts the whole script.
    if [[ -f "${candidate}" ]]; then
      FOUND+=("${candidate}")
    fi
  done

  if (( ${#FOUND[@]} == 0 )); then
    echo "ERROR: no Compose file found in $(pwd)." >&2
    echo "       Looked for: ${COMPOSE_CANDIDATES[*]}" >&2
    echo "       Run this script from the directory holding the stack, or name the file:" >&2
    echo "         $0 -f <compose-file> ${VERSION}" >&2
    exit 1
  fi

  COMPOSE_FILE="${FOUND[0]}"

  # More than one is expected rather than exotic: docs/deployment-runbook.md tells the
  # operator to copy a release's compose.prod.yaml onto the host, so a host whose live file
  # is compose.yaml ends up holding both. Picking one silently is how last release's
  # topology gets deployed, so say which won.
  if (( ${#FOUND[@]} > 1 )); then
    echo "WARNING: more than one Compose file is present: ${FOUND[*]}" >&2
    echo "         Using ${COMPOSE_FILE} (first match). Pass -f to choose another." >&2
  fi
fi
```

Everything downstream keeps reading `"${COMPOSE_FILE}"` unchanged — do not touch any of the six
`docker compose -f "${COMPOSE_FILE}"` invocations (lines 70, 119, 153, 156, 162, 180), the banner
at what was line 97, or the rollback hints at the end. They already print and use the resolved
value.

**Verification**: `bash -n deploy.sh` → exit 0.

### Step 3: Update the header comment

Two edits in the header block, so the file documents itself:

```diff
-# Usage: ./deploy.sh <version>
+# Usage: ./deploy.sh [-f <compose-file>] <version>
 #   <version>  Release version WITHOUT the leading 'v', e.g. ./deploy.sh 0.1.0
 #              Matches a published GitHub Release tag vX.Y.Z that has homon-api / homon-web
 #              images on GHCR (produced by .github/workflows/release.yml).
+#   -f/--file  The Compose file. Without it the script looks for compose.yaml, then
+#              compose.yml, then compose.prod.yaml, and uses the first one present — so a
+#              host that keeps the conventional name needs no flag. docker-compose.yml is
+#              deliberately not in that list; in this repository that name is the
+#              DEVELOPMENT stack.
```

and

```diff
 # Run this script as the user that owns the Docker daemon (on a rootless host, the service
-# account), from the same directory as compose.prod.yaml. No sudo inside.
+# account), from the directory holding the stack's Compose file and .env. No sudo inside.
```

**Verification**: `bash -n deploy.sh` → exit 0.

### Step 4: Test it for real, in a throwaway directory

This is the step that actually proves the change; `bash -n` only proves it parses.

The script reaches its banner — which prints `Compose file : <resolved>` — and then stops at an
interactive confirmation prompt, *before* it touches anything. Answering `n` therefore exercises
the entire resolution path and exits 0 having changed nothing. Use that.

Set up a scratch directory (outside the repo; do not create these files in the working tree):

```bash
WORK="$(mktemp -d)"
cd "${WORK}"

# Minimal but genuinely valid: `docker compose config -q` must be able to parse it, and it
# must reference no ${VARIABLES} so an empty .env is enough.
cat > minimal.yaml <<'YAML'
services:
  postgres:
    image: postgres:17-alpine
YAML

: > .env
DEPLOY="<absolute path to your worktree>/deploy.sh"
```

Then run the seven scenarios. In each, `echo n |` answers the confirmation prompt, so **nothing
is ever pulled, started or dumped**:

```bash
# 1. Conventional name is found with no flag.
cp minimal.yaml compose.yaml
echo n | "${DEPLOY}" 9.9.9 2>&1 | grep 'Compose file'      # -> compose.yaml

# 2. Existing installations still work: only compose.prod.yaml present.
rm compose.yaml; cp minimal.yaml compose.prod.yaml
echo n | "${DEPLOY}" 9.9.9 2>&1 | grep 'Compose file'      # -> compose.prod.yaml

# 3. Both present: compose.yaml wins AND a warning names both (D6).
cp minimal.yaml compose.yaml
echo n | "${DEPLOY}" 9.9.9 2>&1 | grep -E 'WARNING|Compose file'

# 4. -f overrides discovery, on either side of the version (D5).
cp minimal.yaml other.yaml
echo n | "${DEPLOY}" -f other.yaml 9.9.9 2>&1 | grep 'Compose file'   # -> other.yaml
echo n | "${DEPLOY}" 9.9.9 -f other.yaml 2>&1 | grep 'Compose file'   # -> other.yaml

# 5. A typo'd -f fails and does NOT fall back (D3) — compose.yaml is sitting right there.
echo n | "${DEPLOY}" -f nope.yaml 9.9.9; echo "exit=$?"    # -> ERROR, exit=1, no deploy

# 6. Nothing present: the error lists what was searched.
rm -f compose.yaml compose.prod.yaml other.yaml
echo n | "${DEPLOY}" 9.9.9; echo "exit=$?"                 # -> lists the candidates, exit=1

# 7. Bad usage still rejected.
cp minimal.yaml compose.yaml
"${DEPLOY}"; echo "exit=$?"                                # -> usage, exit=1
"${DEPLOY}" 9.9.9 extra; echo "exit=$?"                    # -> "got 2", exit=1
"${DEPLOY}" v9.9.9; echo "exit=$?"                         # -> leading-'v' error, exit=1
"${DEPLOY}" --help; echo "exit=$?"                         # -> usage, exit=0
```

Clean up afterwards: `cd - && rm -rf "${WORK}"`.

**Expected**: every line as annotated. Scenario 5 is the important one — if it prints
`Compose file : compose.yaml` instead of erroring, D3 was not implemented and you must fix it
before moving on.

**Note**: these need `docker` on PATH, because the script checks for it and runs
`docker compose config -q` before the banner. That is read-only and allowed. If Docker is
unavailable in your environment, say so in your report and state plainly that scenarios 1–6 were
not executed — do not claim them.

### Step 5: Documentation and the index

In `docs/deployment-runbook.md`, under `## Updating`, note that `deploy.sh` finds the Compose
file itself — `compose.yaml`, then `compose.yml`, then `compose.prod.yaml` — so a host that
renamed the file to the conventional `compose.yaml` needs no flag, and that `-f` names one
explicitly. Keep it to two or three sentences and match the surrounding prose. Do not rewrite the
`## First bring-up` block: its `curl -O … compose.prod.yaml` still produces a file discovery
finds.

Then set this plan's row in `plans/README.md` to `DONE (<date>, <commit>)`.

## Test plan

| What | How | Why not a unit test |
| --- | --- | --- |
| Parses | `bash -n deploy.sh` | — |
| All seven resolution scenarios | Step 4's harness | Nothing in `tests/` can exercise a shell script; the repo has no shell test framework and this plan does not add one |
| Nothing else moved | the three suites, run separately | They compile nothing from `deploy.sh`; green means only "unrelated" |

Do **not** add a shell test framework, a `tests/deploy/` directory, or shellcheck to CI. Each is
defensible on its own and none is this plan.

## Done criteria

**Use `grep -F` on every pattern containing `${...}`.** Without it GNU grep reads the braces as
an interval expression and quietly matches nothing — a criterion that returns 0 because the
pattern is broken looks exactly like a criterion that returns 0 because the code is right. These
counts were measured against `143b38d`, with `-F`, and are correct:

```bash
bash -n deploy.sh                                            # exit 0

grep -cF 'COMPOSE_CANDIDATES=(compose.yaml compose.yml compose.prod.yaml)' deploy.sh  # 1
grep -cF 'COMPOSE_FILE="compose.prod.yaml"' deploy.sh        # 0  (the hard-coding is gone)
grep -cF 'export COMPOSE_FILE' deploy.sh                     # 0  (D7)

# The six invocations, untouched — this count is the same before and after this plan.
grep -cF 'docker compose -f "${COMPOSE_FILE}"' deploy.sh     # 6

# 0 before this plan — deploy.sh does not mention that name today. Step 1's D2 comment is
# what makes it non-zero, so this checks the reasoning landed, not just the code.
grep -c 'docker-compose' deploy.sh                           # >= 1, every hit a comment

git diff --stat main..HEAD    # only deploy.sh, docs/deployment-runbook.md, plans/README.md
```

Confirm the `docker-compose` hits are comment lines with
`grep -n 'docker-compose' deploy.sh` — if any is executable code, the development stack has
become reachable and D2 is violated.

If `grep -cF 'docker compose -f "${COMPOSE_FILE}"' deploy.sh` returns anything other than 6, an
invocation was edited or added; that is out of scope (D4) whichever direction it moved.

Plus: all seven Step 4 scenarios behaving as annotated, and the three suites green.

## STOP conditions

Stop and report — do not improvise — if any of these happen:

- **The drift check is non-empty** and the excerpts no longer match.
- **You find yourself adding `docker-compose.yml` or `docker-compose.yaml` to
  `COMPOSE_CANDIDATES`** to "match Docker". D2 forbids it: in this repository that name is the
  development stack, and making it discoverable is the single worst outcome available here.
- **Scenario 5 resolves a file instead of erroring.** Explicit `-f` must never fall back (D3).
- **You find yourself editing `docker-compose.yml`, `compose.prod.yaml`, or
  `.github/workflows/release.yml`.** All three are out of scope; the release workflow's
  `./deploy.sh <version>` line is supposed to keep working untouched, and that is what
  scenario 4 and D5 verify.
- **You find yourself changing any of the six `docker compose -f "${COMPOSE_FILE}"` invocations**,
  or making `COMPOSE_FILE` an array. That is D4's deferred extension, not this plan.
- **`./deploy.sh 0.1.1` with no flag stops working** in scenario 1 or 2. Already-published
  GitHub release notes contain that exact string and cannot be edited.
- **A verification fails twice** after one reasonable fix attempt.

## Post-deploy verification (for the maintainer, not the executor)

On `clockmaster`, where the file is named `compose.yaml`, the whole point of this plan is one
line:

```bash
cd ~/docker-apps/homon && ./deploy.sh <version>
```

It should print `Compose file : compose.yaml` in the banner and proceed. Before this plan it
exits 1 without reaching the banner.

## Maintenance notes

- **The real remaining gap is that `deploy.sh` never updates the Compose file itself.** It pulls
  images and pins `HOMON_VERSION`; a release that changed `compose.prod.yaml` — as v0.1.1 did,
  twice — is silently half-applied unless the operator copies the new file across by hand first.
  This plan makes the script *find* the file; it does nothing about keeping it current. That is
  the plan worth writing next, and it is more interesting than it looks: the host's file may be
  renamed (this plan) and may carry local edits, so "just overwrite it" is wrong. A diff-and-warn
  is probably the honest shape.
- **What a reviewer should scrutinise**: that `docker-compose*` appears in `deploy.sh` only
  inside comments; that explicit `-f` cannot fall back; that the six invocations are
  byte-identical to before; and that `COMPOSE_FILE` is never exported (D7) — it is also a
  variable `docker compose` reads, and exporting it would leak this script's internal state into
  every Compose command run afterwards in the same shell.
- **If Docker ever changes its default lookup order**, D1's first two entries should follow it;
  D2's exclusion should not.
