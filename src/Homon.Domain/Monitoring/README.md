# Monitoring — module slot

Not implemented yet. This folder will hold the probe model once plan 002 lands.

**What it owns.** Everything about *is this thing up*: the probes an administrator
configures, what each poll observed, and the state each service is in right now.

**Requirements it answers (from the project brief).**

- Probe types: **ping/ICMP**, **SMB/CIFS**, **HTTP/HTTPS**, **SNMP** (SNMP scaffolded only in
  the first phase). Every type shares: name, destination host/IP, poll interval, and the
  *attempts before failure* threshold.
- State machine per probe: N or more consecutive failed polls → **down** (red); N or more
  consecutive successful polls → **up** (green); anything in between → **unstable**
  (orange). A probe that has never been polled is **unknown**; a probe the admin has
  switched off is **paused**.
- Uptime, shown on the card as a percentage with two decimals (`98.32%`), computed over
  the retained observation window.
- **Ping** keeps the round-trip time of every poll for a sliding **30-day** window; older
  samples are dropped from the database by a housekeeping job.
- **SMB/CIFS** takes credentials and a share path.
- **HTTP/HTTPS** takes a method (`HEAD`, `GET`, …), a path (`api/health`), an optional
  expected status code and/or body text — each negatable — and optional credentials
  (bearer token or basic auth) so it can reach the existing services' own health
  endpoints.
- **SNMP** takes community/version and an OID; first phase records the shape only.

**Shape the entities will take.** `Probe` (common fields + `ProbeKind`), per-kind option
types (`PingOptions`, `SmbOptions`, `HttpOptions`, `SnmpOptions`), `ProbeObservation`
(one row per poll: when, success, latency, detail), `ProbeStatus` (the current
up/unstable/down/unknown/paused plus the consecutive counters). Secrets held by a probe
(SMB password, HTTP bearer) are stored encrypted with ASP.NET Data Protection, never in
clear.

**Where the rest lands.** `Homon.Infrastructure/Monitoring/` — the scheduler
(`BackgroundService`), one `IProbeRunner` per kind, the retention job.
`Homon.Api/Endpoints/ProbeEndpoints.cs` (admin CRUD) and `StatusEndpoints.cs` (the
dashboard's read model). `Homon.Web/src/pages/admin-probes-page.tsx` and the dashboard
cards.

**Known constraints, recorded now so they are not rediscovered.** See `docs/MODULES.md`:
ICMP inside a rootless container needs `net.ipv4.ping_group_range`; SMB must use a managed
client rather than a kernel mount.
