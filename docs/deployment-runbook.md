# Deployment runbook

Production runs as four Compose services from `compose.prod.yaml` — `postgres`, a one-shot
`migrator`, `api`, `web` — publishing **one port**. The `web` container's nginx serves the
bundle and proxies `/api/` to `api` on the Compose network; nothing else is published.
Whatever sits in front (a reverse proxy with trusted-network rules, a WAF, or nothing on a
LAN) only has to know that one port.

Images come from GHCR, built by `.github/workflows/release.yml` on a `vX.Y.Z` tag:
`ghcr.io/acastaner/homon-api` and `homon-web`, tagged with the bare version. The repository
is public, so no registry credential is needed to pull.

## The maintainer's host

| | |
| --- | --- |
| Host | `clockmaster` (Ubuntu 24.04, bare metal) |
| Docker | **rootless**, under the `dockersvc` service account — every command below runs as that user, no `sudo` |
| Compose directory | `/home/dockersvc/docker-apps/homon/` — the host's convention, one directory per app |
| Port | **8102**, bound `0.0.0.0` — the host's convention (next in sequence after `chronolectum-mcp` on 8101) |
| Access control | the network in front: FortiWeb's virtual server rules for anything internet-facing; the LAN otherwise. Homon itself leaves readers anonymous (`HOMON_REQUIRE_SIGN_IN_FOR_READERS=false`) |
| Backups | the host's restic job backs up `/home/dockersvc/docker-apps/` nightly — which is why `compose.prod.yaml` bind-mounts PostgreSQL's data at `./data/postgres` instead of using a named volume. The data-protection key ring **is** a named volume and is not covered; see below |

## First bring-up

```bash
# as dockersvc
mkdir -p ~/docker-apps/homon && cd ~/docker-apps/homon
curl -fsSLO https://raw.githubusercontent.com/acastaner/homon/main/compose.prod.yaml
curl -fsSLO https://raw.githubusercontent.com/acastaner/homon/main/deploy.sh && chmod +x deploy.sh
curl -fsSL  https://raw.githubusercontent.com/acastaner/homon/main/.env.example -o .env
$EDITOR .env             # every :? variable in compose.prod.yaml; the admin hash from `hash-password`
docker compose -f compose.prod.yaml config -q          # proves .env is complete before anything runs
docker compose -f compose.prod.yaml pull
docker compose -f compose.prod.yaml up -d              # postgres → migrator → api → web, ordered by Compose
docker compose -f compose.prod.yaml ps
curl -s http://127.0.0.1:8102/api/v1/meta              # release, environment, administratorConfigured
```

Mint the administrator's hash on any machine with the SDK:
`dotnet run --project src/Homon.Api -- hash-password` — only the hash goes in `.env`.

Then, for the backup scripts: `docker compose -f compose.prod.yaml exec api dotnet Homon.Api.dll create-api-key --name "clockmaster restic"`
prints a key once. Put it where the script reads it (`/home/resticsvc/.restic_env` on the
maintainer's host, alongside the restic credentials). The report endpoint arrives with the
Backups module; until then the key can be verified with
`curl -H "Authorization: Bearer hmn_…" http://127.0.0.1:8102/api/v1/auth/session`.

## Updating

```bash
cd ~/docker-apps/homon && ./deploy.sh 0.2.0     # bare version, no leading v
```

Attended and confirmed. It takes a `pg_dump` (verified by its `PGDMP` header), prunes to the
newest ten, pins `HOMON_VERSION` in `.env`, pulls, `up -d`, polls `/health`, and reads the
version back from `/api/v1/meta` so a silently no-op'd pull is not mistaken for a deploy.

Rollback with no migration in the release: edit `HOMON_VERSION` back and `up -d`. With a
migration: restore the dump first (`pg_restore --clean` into the `postgres` service), then
roll the version back.

## The key ring

`dataprotection-keys` protects session cookies and, once the Monitoring module lands, every
stored probe secret. Losing it logs everyone out and means re-entering every SMB password
and HTTP token. Export it whenever the restic job runs:

```bash
docker run --rm -v homon_dataprotection-keys:/keys -v "$PWD":/out alpine tar czf /out/dataprotection-keys.tgz -C /keys .
```

`./dataprotection-keys.tgz` then lives under `docker-apps/homon/` and restic picks it up.

## ICMP

The `api` service sets `net.ipv4.ping_group_range` so .NET's `Ping` can open an unprivileged
ICMP socket under rootless Docker. If the first ping probe reports "permission denied",
check `docker compose exec api cat /proc/sys/net/ipv4/ping_group_range` — it should read
`0 2147483647`.
