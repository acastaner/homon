# Plan 016: Make the ICMP sysctl applicable under rootless Docker

> **Executor instructions**: Follow this plan step by step. Run every verification command and
> confirm the expected result before moving on. If anything under "STOP conditions" occurs,
> stop and report — do not improvise. When done, update this plan's row in `plans/README.md`.
>
> **Drift check (run first)**:
> ```bash
> git diff --stat 5ff263a..HEAD -- compose.prod.yaml docs/deployment-runbook.md \
>   src/Homon.Api/Dockerfile deploy.sh
> ```
> Empty output means no drift. If either file changed, compare the "Current state" excerpt
> below against the live code before proceeding; on a mismatch, treat it as a STOP condition.

## Status

- **Priority**: P1 — the api container does not start at all on a rootless host.
- **Effort**: S (one value, plus its comment and a runbook note)
- **Risk**: LOW. No source change, no image change, no test change.
- **Depends on**: none. Should ship in the **same release as plan 015**.
- **Category**: bug
- **Planned at**: commit `5ff263a`, 2026-09-18
- **Reviewed**: 2026-09-18 (review-plan, against `5ff263a`). The excerpt's line range was
  corrected, the `APP_UID` claim was given its evidence, and the verification commands were
  split into the ones an executor can actually run here and the ones only the maintainer can
  run on the host — two of the originals are refused outright by `ci/guard-docker.py`.

## Why this matters

On the first real deployment (`clockmaster`, rootless Docker, 2026-09-18) `docker compose up -d`
brought up `postgres` and ran `migrator` to completion, then failed:

```
Error response from daemon: failed to create task for container: failed to create shim task:
OCI runtime create failed: runc create failed: unable to start container process: error during
container init: failed to write sysctl net.ipv4.ping_group_range = "0 2147483647":
write fsmount:fscontext:proc/./sys/net/ipv4/ping_group_range: invalid argument
```

The kernel's handler for this sysctl (`ipv4_ping_group_range`) resolves **both** bounds through
`make_kgid(current_user_ns(), …)` and returns `-EINVAL` if either is unmappable. Inside a rootless
container the user namespace maps only the service account's subordinate range — a typical
`/etc/subgid` entry of `dockersvc:100000:65536` yields container gids `0..65536` and nothing
above. `2147483647` is never mappable there, so the write fails and the container never starts.

The irony is in the line's own comment, which says it is written this way **because** the
deployment is rootless:

> Set here rather than with CAP_NET_RAW because this is a rootless Docker deployment and a
> namespaced sysctl is the one knob that works there.

The namespaced-sysctl *approach* is right; the *value* is the one that works under a **root**
daemon, where the init user namespace maps the whole gid space. It has evidently never been
exercised on a rootless host.

`65536` is not a compromise. The api image ends with `USER $APP_UID`
(`src/Homon.Api/Dockerfile:44`) on top of `mcr.microsoft.com/dotnet/aspnet:10.0-alpine`
(`src/Homon.Api/Dockerfile:25`), whose `app` user and group are **1654** — that is the .NET base
image's `APP_UID` default, set by the base image and never overridden here. 1654 sits
comfortably inside `0 65536`, so .NET's `Ping` still gets its unprivileged ICMP socket when the
Monitoring module's ping probe runs. The number comes from the base image rather than from this
repo, so the post-deploy check below confirms it with `id` instead of trusting it.

## Current state

`compose.prod.yaml:145-151`, the tail of the `api` service:

```yaml
    # ICMP. The ping probe (Monitoring module, plan 002) needs an unprivileged ICMP socket:
    # .NET's Ping on Linux opens SOCK_DGRAM/IPPROTO_ICMP, which the kernel allows only for
    # groups inside net.ipv4.ping_group_range — and the default range is empty. Set here
    # rather than with CAP_NET_RAW because this is a rootless Docker deployment and a
    # namespaced sysctl is the one knob that works there. Harmless before the module lands.
    sysctls:
      net.ipv4.ping_group_range: "0 2147483647"
```

## Decisions

**D1 — change the value, keep the mechanism.** A namespaced sysctl is still the right tool;
`CAP_NET_RAW` is not available rootless. Only the upper bound is wrong.

**D2 — hard-code `65536`, do not make it configurable.** `65536` is the ceiling of the
conventional 65536-wide subuid/subgid allocation that `useradd` and the distro defaults produce,
and it covers every uid a container image realistically runs as. A host with a narrower range is
an exotic case that can override the whole service in its own Compose override; adding an
environment variable for it would be a knob nobody sets, and `.env.example` is already long.

Note what `65536` costs: it is the **exact top** of the conventional allocation, not a value with
headroom. A rootless mapping of `dockersvc:100000:65536` makes container gids `0` and `1..65536`
mappable, so `65536` is the last valid gid and there is zero margin — a host whose subgid count
is even slightly smaller fails the write exactly as `2147483647` does. That is deliberate: only
the *image's own* gid has to be inside the range for `Ping` to work (1654, above), so a value
with margin such as `0 65534` would be equally functional and strictly more forgiving. The
maximal-but-valid value is chosen so that the range stays useful if the image's user ever
changes, and the STOP condition below catches the narrow-host case explicitly. Do not lower it
on a hunch; lower it only against a host's real `/etc/subgid` entry, and then in that host's own
override.

**D3 — the value must remain valid under a root daemon too.** `0 65536` is accepted by both, so
this is not a rootless-only fix and needs no conditional.

## What the gate does not cover

**Nothing in `./ci/run-ci.sh` reads `compose.prod.yaml`.** The three suites build and test the
solution and the SPA; the only other Compose file in the repo is `ci/compose.ci.yaml`, which
stands up the CI PostgreSQL and has no `sysctls` block. So the gate cannot fail on this change
and cannot prove it either — it is run here only to show nothing else moved. The change is
verified by `docker compose config` (Step 1) and, finally, by the maintainer on a rootless host.

Do not write a test for this. There is nothing in `tests/` that could observe a container's
sysctls, and adding a Compose-parsing test to a .NET suite would be a worse artefact than the
one-line fix it guards.

## Scope

**In scope**: `compose.prod.yaml` (the `sysctls` value and its comment),
`docs/deployment-runbook.md` (the ICMP section), `plans/README.md`.

**Out of scope**: `src/Homon.Api/` — there is no source change here. The `Dockerfile`s. Anything
to do with how the ping probe itself opens its socket.

## Git workflow

```bash
git switch -c plan/016-rootless-ping-group-range
```

Commit with the repo's message style, naming the plan — e.g.
`Deploy: cap net.ipv4.ping_group_range at 65536 so a rootless host can start the api (plan 016)`.
Do not merge and do not push; merging is the maintainer's call.

**This plan and plan 015 both change `compose.prod.yaml`.** Keep them on separate branches
anyway — they touch different lines and merge cleanly — but flag in your report that the two
must be released together, because a host takes one copy of the compose file per release (see
Maintenance notes).

## Steps

### Step 1: Correct the value and the comment

In `compose.prod.yaml`, change the value and extend the comment so the next reader does not
"restore" the old one:

```diff
     sysctls:
-      net.ipv4.ping_group_range: "0 2147483647"
+      # The upper bound is 65536, not the 2147483647 you will see elsewhere: the kernel
+      # resolves BOTH bounds through make_kgid() against the caller's user namespace and
+      # returns EINVAL if either is unmappable. A rootless container maps only the service
+      # account's subordinate range — conventionally 65536 wide — so 2147483647 fails the
+      # write and the container does not start at all. 65536 is valid under a root daemon
+      # too, and comfortably covers this image's own gid (1654, the .NET APP_UID default),
+      # which is the gid that has to be inside the range for Ping to work.
+      net.ipv4.ping_group_range: "0 65536"
```

**Verification** — the file must still parse. `compose.prod.yaml` requires six variables
(`HOMON_PUBLIC_ORIGIN`, `POSTGRES_PASSWORD`, `RESEND_API_TOKEN`, `HOMON_FROM_ADDRESS`,
`HOMON_ADMIN_EMAIL`, `HOMON_ADMIN_PASSWORD_HASH`) that are declared `:?` and abort the parse when
unset **or empty**, so point Compose at the committed example file rather than at a `.env` you do
not have. Five of the six carry a placeholder there; `RESEND_API_TOKEN` is deliberately left
empty (`.env.example:22` — it is a real credential), so supply a throwaway word for it on the
command line. This exact invocation was run against `5ff263a` and exits 0:

```bash
RESEND_API_TOKEN=placeholder \
  docker compose --env-file .env.example -f compose.prod.yaml config -q
```

Expected: no output, exit 0. Without the `RESEND_API_TOKEN=` prefix it fails with
*"required variable RESEND_API_TOKEN is missing a value"* — that is the example file being
correct about secrets, not a problem with your change.

`config` reads files and resolves variables; it starts nothing. If Docker is unavailable in your
environment, say so in your report and move on — the value is a string in YAML either way, and
the maintainer parses it at deploy time.

### Step 2: Note it in the runbook

`docs/deployment-runbook.md:80` opens an `## ICMP` section, whose lines 84–85 read:

```
check `docker compose exec api cat /proc/sys/net/ipv4/ping_group_range` — it should read
`0 2147483647`.
```

Change that expected reading to `0 65536`. Keep the single space the file already uses — the
kernel actually prints the two numbers separated by a tab, but this document has always written
it with a space and a re-spacing diff here is noise.

Then add a short paragraph below it: a container that refuses to start with
`failed to write sysctl … invalid argument` means the upper bound exceeds what the host's user
namespace maps (a rootless host maps only the service account's subordinate gid range, see
`/etc/subgid`) — the fix is a smaller upper bound in a host-specific Compose override, **not**
`privileged: true` and **not** `cap_add: NET_RAW`, neither of which is available to a rootless
daemon.

### Step 3: Reconcile the index

Set this plan's row in `plans/README.md` to `DONE (<date>, <commit>)`.

## Done criteria

Machine-checkable, here, by the executor:

```bash
# The new value is in place and the old one survives only inside the comment that explains it.
grep -c 'net.ipv4.ping_group_range: "0 65536"' compose.prod.yaml       # 1
grep -c 'net.ipv4.ping_group_range: "0 2147483647"' compose.prod.yaml  # 0
grep -c '2147483647' compose.prod.yaml                                 # 2  (see below)
grep -c '2147483647' docs/deployment-runbook.md                        # 0

RESEND_API_TOKEN=placeholder \
  docker compose --env-file .env.example -f compose.prod.yaml config -q   # exit 0, no output
./ci/run-ci.sh                                # PASS — unchanged; nothing here is compiled
```

**On that count of 2** (corrected 2026-09-18, after execution): the comment Step 1 prescribes
names the old value twice — once in "not the 2147483647 you will see elsewhere" and once in "so
2147483647 fails the write" — and `grep -c` counts matching *lines*, so the answer is 2 and both
are inside the comment. Confirm that with `grep -n '2147483647' compose.prod.yaml`: both hits
must be comment lines, and the line-0 criterion above is what actually proves the setting itself
changed. Do not reword Step 1's comment to force the count to 1.

Stronger than a parse, and worth running because it is the closest thing to the real proof that
works off-host — it shows the value Compose will hand the container, not merely that the file is
valid YAML:

```bash
RESEND_API_TOKEN=placeholder \
  docker compose --env-file .env.example -f compose.prod.yaml config \
  | grep -A1 'sysctls:'
```

Expected: `net.ipv4.ping_group_range: 0 65536`.

Plus: `plans/README.md` row updated, and `git diff --stat` shows only `compose.prod.yaml`,
`docs/deployment-runbook.md` and `plans/README.md`.

## Post-deploy verification (for the maintainer, not the executor)

The real proof is a rootless host, and it cannot be run from here: **`ci/guard-docker.py` blocks
`docker compose … exec` and every other disturbing verb outside the `homon-ci` project**, so
an agent that tries these gets a refusal, not a result. They are the maintainer's, on
`clockmaster`, after copying the new `compose.prod.yaml` across (see Maintenance notes):

```bash
docker compose -f compose.prod.yaml up -d      # api and web reach Up (healthy)
docker compose -f compose.prod.yaml exec api cat /proc/sys/net/ipv4/ping_group_range
docker compose -f compose.prod.yaml exec api id
```

The second reads `0` and `65536` separated by a tab. The third must report a uid and gid inside
that range — expect `uid=1654(app) gid=1654(app)`; anything outside `0 65536` means the base
image changed its `APP_UID` and the range needs revisiting, not the other way round.

## STOP conditions

- **The drift check is non-empty** and the excerpt no longer matches.
- **The container still fails to start** with the same `invalid argument` on `0 65536`. That would
  mean the host's subgid allocation is narrower than 65536. Report the host's
  `/etc/subgid` entry rather than guessing a smaller number — the right fix then is a
  host-specific Compose override, not a lower value committed for everyone (D2).
- **You find yourself adding `privileged: true`, `cap_add: NET_RAW`, or removing the `sysctls`
  block.** All three are wrong: the first two are unavailable or unsafe rootless, and the third
  silently disables ping probes rather than fixing them.
- **A `docker` command is refused with "BLOCKED by ci/guard-docker.py".** That guard is correct
  and you do not work around it: it stops agents disturbing containers on a development machine
  that hosts other projects' databases. Only `config` is needed here; anything needing `up`,
  `exec` or `down` belongs in the post-deploy section and is the maintainer's to run.
- **The value ends up parameterised** (`${HOMON_PING_GROUP_RANGE:-…}` or similar). D2 rejected
  that; if you believe the host really needs its own value, stop and say so rather than adding a
  variable nobody else will set.

## Maintenance notes

- **This is currently patched by hand on the maintainer's host.** `clockmaster`'s
  `/home/dockersvc/docker-apps/homon/compose.prod.yaml` carries the `0 65536` edit locally, so
  that copy has diverged from the repo. Until this plan lands, **re-copying `compose.prod.yaml`
  from a release onto that host reintroduces the bug** and the api container stops starting.
- **`deploy.sh` does not carry this fix.** It pulls images and pins `HOMON_VERSION`; it never
  touches `compose.prod.yaml`. Any release whose compose file changed — this plan's, and plan
  015's new `Auth__AllowPlainTextSessions` line — must be accompanied by copying the new compose
  file onto the host *before* running `deploy.sh`. That is a sharp edge in the update procedure
  worth addressing separately: `deploy.sh` could warn when the deployed compose file differs from
  the one shipped in the release it is deploying.
