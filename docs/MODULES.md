# Modules — the roadmap, and what is already known about each

Phase 0 built the plumbing and left six empty slots. Each slot is a folder under
`src/Homon.Domain/` with a `README.md` that says what the module owns; this file is the
index, the order, and the constraints found while scaffolding so nobody rediscovers them.

| Plan | Module | Slot | Summary |
| --- | --- | --- | --- |
| 002 | Monitoring — core, groups + ping | `Domain/Monitoring/` | Probe model, `ProbeGroup`, scheduler, state machine, uptime, the dashboard's status endpoint, the ICMP runner, 30-day RTT retention |
| 003 | Monitoring — HTTP/HTTPS | `Domain/Monitoring/` | Method, path, expected status / body text (negatable), bearer or basic credentials |
| 004 | Monitoring — SMB/CIFS | `Domain/Monitoring/` | Share path + credentials, connect-and-list through a managed client |
| 005 | Monitoring — SNMP (scaffold) | `Domain/Monitoring/` | Community/version + OID; shape only in the first phase |
| 006 | Links | `Domain/Links/` | URL, title, description, order; admin CRUD; new tab always |
| 007 | Pages | `Domain/Pages/` | Slug, title, sanitised HTML body; WYSIWYG editor; `/pages/{slug}` |
| 008 | Backups + API-key admin | `Domain/Backups/` | Report endpoint for scripts (API key), job lateness, admin key management |
| 009 | Alerts | cross-cutting | Email on unstable/down via `IAlertEmailSender`, recipients from `Email:AlertRecipients` |
| 010 | Weather | `Domain/Weather/` | One location, cached provider answer |
| 011 | Calendar | `Domain/Calendar/` | Several ICS sources merged, colour per source |
| 012 | Design pass | `src/Homon.Web/` | Tokens, components, dark mode — from `docs/design-brief.md` |

## Requirements, verbatim from the brief

**Probes.** Types: ping/ICMP, SMB/CIFS, HTTP/HTTPS, SNMP (roughly scaffolded first). Shared
parameters: name, destination IP, poll interval, attempts before failure — N or more
consecutive failed polls deem the target **down**, N or more consecutive successes **up**,
anything in between is **unstable** (orange). Ping records the RTT for a 30-day sliding
window; older samples are dropped from the database. SMB requires credentials and a mount
target. HTTP requires a method (HEAD, GET, …), a URI (`api/health`), an optional matching
text and/or status code (each negatable), and optional credentials (bearer key, basic auth)
compatible with the existing services' API requirements. Admins add/edit/delete probes.
*(HTTP built: plan 003 — `HttpProbeOptions`/`HttpCredential`, method restricted to HEAD/GET
this phase; with no status configured only a 2xx response counts as success. See
`docs/ARCHITECTURE.md` §3.17.)*

**Links.** URL, title, optional description; always open in a new tab; admin CRUD.

**Backups.** Scripts on the servers (restic) must report success; an API endpoint the bash
scripts write their logs to, authenticated with API keys.

**Dashboard.** Very simple and very clear. One card per service with a green/yellow/red dot
and the uptime overlaid as text with two decimals (`98.32%`). Reactive; works on mobile.

**Alerts.** Email through Resend when a service goes unstable or down; the key lives in
`.env` beside the container.

**Pages.** A light pages/articles feature with WYSIWYG editing for a few explanation pages.

**Weather** widget. **Family calendar** widget with several sources in one view.

**Auth.** No authentication for readers; only the admin has credentials; access is enforced
by trusted-network rules on the reverse proxy — but readers' sign-in must be switchable on
later. *(Built: `Auth:RequireSignInForReaders`.)*

## Constraints already known

**ICMP inside a container.** .NET's `Ping` on Linux opens an unprivileged ICMP datagram
socket, which the kernel allows only for groups inside `net.ipv4.ping_group_range` — empty
by default. `compose.prod.yaml` already sets that sysctl on the `api` service; it is the one
knob that works under rootless Docker, where `CAP_NET_RAW` is not available. Verify on the
first ping probe; if it still fails, the fallback .NET tries is the `ping` binary, which the
alpine image does not ship.

**SMB cannot be a mount.** Mounting CIFS needs `CAP_SYS_ADMIN`, which a rootless container
does not have and should not be given. The probe therefore uses a managed SMB client —
connect, authenticate, list the share root, disconnect — and "mount target" in the brief
becomes a share path (`//host/share`). Candidate: `SMBLibrary` (LGPL-3.0; usable from an MIT
application as a NuGet dependency, and worth noting in the licence section of the README
when it lands).

**SNMP.** Candidate: `Lextm.SharpSnmpLib`. Scaffold the options and the runner's interface;
implement `GET` of one OID only in the first phase.

**Probe secrets** (SMB password, HTTP bearer) are stored encrypted with ASP.NET Data
Protection. The key ring is the `dataprotection-keys` volume in production; losing it means
re-entering every secret, which is why the runbook says how to export it. *(Built: plan
003 — `Homon.Infrastructure.Security.ISecretProtector`/`DataProtectionSecretProtector`,
one purpose string for every probe secret, write-only wire semantics on every endpoint
that carries one. See `docs/ARCHITECTURE.md` §3.17.)*

**Uptime** is computed over the retained observation window and shown with two decimals;
a probe with no observations shows `—`, not `100.00%`. *(Built: plan 002, Decision 5 —
`ProbeUptimeCalculator`, a per-probe ratio; the dashboard's aggregate averages probes, not
observations. See `docs/ARCHITECTURE.md` §3.16.)*

## Added after the brief

Not asked for in the original brief; added because the maintainer wanted them once
monitoring existed.

- **Probe groups.** Named, admin-created, many-to-many arrangements of probes on the
  dashboard ("Hosts", "Services", …) — a probe may belong to several, or none. Landed with
  plan 002 rather than retrofitted later, since groups touch the same migration and the
  same dashboard restructure `Probe` itself needed. See `docs/ARCHITECTURE.md` §3.14.
- **`FailureThreshold` default of 2, 1–10 allowed per probe.** The brief names the
  consecutive-poll rule but not a default; plan 002 picked 2 (the smallest value that still
  distinguishes "unstable" from "down") as the `POST` default, with the full 1–10 range open
  to the admin on any probe. A value of 1 makes that probe strictly binary — `Unstable` is
  never reachable for it.
- **`GET /probes` admits an API key, not only an Administrator session.** Every probe
  *write* stays session-only, but a read-only household script (a status board on a second
  device, a monitoring tool that shells out to the API) can list probes with a key of either
  scope minted by plan 013. See `docs/ARCHITECTURE.md` §3.3 and §3.13.

**WYSIWYG.** Candidate: TipTap. Whatever is chosen, sanitise on the server (the stored body
is rendered verbatim) — an allow-list HTML sanitiser in `Homon.Infrastructure/Pages/`.

**Weather.** Candidate: Open-Meteo, which needs no API key — a self-hoster should not have to
register anywhere to see the weather. Cache the answer server-side.

**Calendar.** ICS URLs first (public or private links from Google/Apple/Nextcloud); CalDAV
later. Credentials, when needed, are encrypted like probe secrets.

**Alert recipients.** `Email:AlertRecipients` binds a list; `compose.prod.yaml` maps two
`.env` variables onto `__0` and `__1`. When the module lands, a Production host with a Resend
token and an empty list should refuse to start, the way a missing token does today.
