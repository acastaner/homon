# PostgreSQL — development setup

How to get a working Homon database on a development machine.

> **On the maintainer's machine, Homon does not run its own database container.** It shares
> the PostgreSQL container belonging to a sibling project (`dotnet-forgelog-postgres-1`,
> which already publishes host port 5432). One server, several databases, one role each.
> Read [§0](#0-the-commands-that-must-never-be-run) before anything else.
>
> **On any other machine**, `cp .env.example .env`, set `POSTGRES_PASSWORD`, and
> `docker compose up -d` starts a PostgreSQL of your own on 5432 with the `homon` role and
> database already created. Skip to [§3](#3-tell-the-api-where-the-database-is).

---

## 0. The commands that must never be run

Sharing a container means sharing a fate. The first two destroy or disrupt **every**
project on the server, and Docker gives no warning for either.

| Never run | Why |
| --- | --- |
| `docker compose down -v` in the directory that owns the shared container | Removes its volume. Every database in it — including this project's — is gone, with no prompt. |
| `docker compose up -d` in this repository, on the maintainer's machine | The `postgres` service in `docker-compose.yml` would try to bind host port 5432, which the shared container already holds, and fail. |
| Anything destructive against a container outside the `homon-ci` Compose project | `ci/guard-docker.py` blocks agents from doing it; a human still has to mean it. |

**The one project that is safe to destroy** is `homon-ci` — the throwaway PostgreSQL that
`ci/run-ci.sh` uses. `./ci/run-ci.sh api --reset` throwing it away is the intended way to
use it. See `ci/README.md`.

---

## 1. Record the password

Copy `.env.example` to `.env` at the repository root and set a value:

```
POSTGRES_PASSWORD=<a password you choose>
```

`.env` is gitignored and must never be committed. It exists so there is one recorded answer
to "what password did we give the `homon` role", which steps 2 and 3 must agree on.

---

## 2. Create the role and database inside the shared container

These need Docker, so they are run by a human, not by an agent. Substitute the password
from `.env` where marked, and the container name and its superuser role for your server.

```bash
# Confirm the container is up, and note which PostgreSQL version you are joining.
docker ps --filter name=postgres --format '{{.Names}}\t{{.Image}}\t{{.Status}}'

# The role. LOGIN and CREATEDB — no CREATEROLE, no SUPERUSER.
#
# CREATEDB is there for the test suite and nothing else: every test class gets its own
# database, cloned from a template and dropped afterwards (tests/Homon.Api.Tests/
# TestDatabase.cs), so the role has to be able to create one. It grants no access to any
# other database on the server.
docker exec -it dotnet-forgelog-postgres-1 \
  psql -U forgelog -d postgres -v ON_ERROR_STOP=1 \
  -c "CREATE ROLE homon LOGIN CREATEDB PASSWORD '<POSTGRES_PASSWORD from .env>';"

# The database, owned by that role.
docker exec -it dotnet-forgelog-postgres-1 \
  psql -U forgelog -d postgres -v ON_ERROR_STOP=1 \
  -c "CREATE DATABASE homon OWNER homon;"
```

---

## 3. Tell the API where the database is

The connection string carries the password, so it lives in .NET user secrets — never in
`appsettings.Development.json`, which is committed.

```bash
dotnet user-secrets --project src/Homon.Api set "ConnectionStrings:Homon" \
  "Host=localhost;Port=5432;Database=homon;Username=homon;Password=<POSTGRES_PASSWORD>"
```

`Homon.Api` and `Homon.Infrastructure` share one `UserSecretsId` (`homon-api`), so the same
secret serves `dotnet run`, the `migrate` verb and `dotnet ef`.

While you are there, the administrator (optional — the API boots without one and
`GET /api/v1/meta` says so — but nothing can be configured until it is set):

```bash
dotnet user-secrets --project src/Homon.Api set "Administrator:Email" "you@example.com"
dotnet run --project src/Homon.Api -- hash-password        # prints a hash; then
dotnet user-secrets --project src/Homon.Api set "Administrator:PasswordHash" "<the hash>"
```

---

## 4. Apply the schema

```bash
dotnet run --project src/Homon.Api -- migrate
```

Nothing applies migrations on startup, deliberately. Run this again after pulling a change
that adds one.

---

## 5. Adding a migration

```bash
HOMON_DESIGNTIME_CONNECTION='Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x' \
  dotnet dotnet-ef migrations add <Name> \
    --project src/Homon.Infrastructure --startup-project src/Homon.Api \
    --output-dir Persistence/Migrations
```

No live server is needed to *add* a migration — EF diffs the model against the snapshot —
so the override above is any syntactically valid connection string. Without it the
design-time factory reads your user secrets and works just as well.

---

## 6. The test suite

`dotnet test` on its own skips every `[DatabaseFact]` and passes. The gate is
`./ci/run-ci.sh api`, which starts its own `postgres:18-alpine` on **127.0.0.1:55433**
(Compose project `homon-ci`), migrates it, and runs the suite with `HOMON_TEST_CONNECTION`
pointing at it. The tests then create one database per test class, named `homon_test_*`,
and drop them afterwards. To run the suite against your development server instead:

```bash
HOMON_TEST_CONNECTION='Host=localhost;Port=5432;Database=homon;Username=homon;Password=…' dotnet test
```

— which creates and drops `homon_test_*` databases there, and nothing else.

---

## 7. What agents may and may not do

Agents may run `./ci/run-ci.sh`, read-only docker commands (`ps`, `logs`, `inspect`), and
anything addressed to `-p homon-ci`. They may not run `docker exec`, `stop`, `rm` or
`down` against any other container; `ci/guard-docker.py` enforces it. Steps 2 of this
document are a human's.
