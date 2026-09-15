# Monitoring

Everything about *is this thing up*: the probes an administrator configures, what each poll
observed, the state each service is in right now, and the named groups probes are arranged
into on the dashboard. Landed by plan 002 (core, groups, the ping probe); 003–005 add the
HTTP, SMB and SNMP runners on top of the same model.

## Entities (this folder)

- `Probe` — one monitored thing: `Name`, `Host`, `ProbeKind Kind` (immutable after creation),
  `PollInterval`, `FailureThreshold`, `IsPaused`, `Position` (the admin-set order for the
  ungrouped section), plus its own live state: `Status`, `ConsecutiveSuccessCount`,
  `ConsecutiveFailureCount`, `LastObservedAt`, `LastLatencyMs`, `LastDetail`. No separate
  state table — every read that needs "is this probe up right now" needs both halves
  together. `RecordObservation`/`Pause`/`Unpause`/`ChangeFailureThreshold` are the only ways
  its live state changes.
- `ProbeKind` — `Ping`, `Http`, `Smb`, `Snmp`. All four exist from plan 002; only `Ping` has a
  runner until 003–005 land.
- `ProbeStatus` — `Unknown`, `Up`, `Unstable`, `Down`, `Paused`.
- `ProbeObservation` — one append-only row per poll: `ObservedAt`, `Succeeded`, `LatencyMs`,
  `Detail`. What the hourly retention sweep prunes (30-day window) and what uptime and the
  sparkline are computed over. `Id` is a `long` identity, not a `Guid` — the one high-volume,
  time-ordered table here.
- `ProbeStateMachine` — two pure functions, `Apply` (one poll outcome) and `Derive`
  (re-derives `Status` from the counters alone, used by `Unpause`/`ChangeFailureThreshold`).
  N-or-more consecutive failures is down, N-or-more consecutive successes is up, anything in
  between is unstable.
- `ProbeGroup` / `ProbeGroupMembership` — a named, admin-created, many-to-many arrangement of
  probes for the dashboard ("Hosts", "Services", …). Optional: a probe in no group appears in
  the dashboard's final, ungrouped section (labelled "Services" when it is the only section,
  "Other" otherwise). Flat — groups do not nest. Deleting a group removes its memberships and
  leaves the probes alone; deleting a probe removes it from every group. An empty group is
  left out of the dashboard's status payload.

## Where the rest lives

- `Homon.Infrastructure/Monitoring/` — `IProbeRunner` (one per kind) and `ProbeResult`;
  `IIcmpPinger`/`SystemIcmpPinger`/`PingProbeRunner` (the ping runner, behind a fake-able
  seam so no test ever sends real ICMP); `ProbeScheduler` (one tick-based `BackgroundService`
  that scans for due probes and dispatches polls under a bounded-concurrency gate, rather
  than a per-probe timer); `ProbeObservationRetentionService` (an hourly sweep that deletes
  observations older than the retention window); `ProbeUptimeCalculator`; `MonitoringOptions`
  (the tick interval, concurrency limit, per-poll timeout, retention window, sparkline bucket
  count — all environment-configurable, none admin-UI configurable yet).
- `Homon.Infrastructure/Persistence/Configurations/` — one `IEntityTypeConfiguration<T>` per
  entity above. `ProbeKind`/`ProbeStatus` map to `varchar` (`HasConversion<string>()`), not
  the default `int` — readable in `psql`, immune to a later member reordering the enum.
- `Homon.Api/Endpoints/ProbeEndpoints.cs` — admin CRUD, pause and reorder under `/probes`.
  `GET` admits an Administrator session or a valid, unexpired API key of either scope; every
  write stays Administrator-session-only. `ProbeGroupEndpoints.cs` — CRUD, reorder and
  membership under `/probe-groups`, `Reader`-gated for `GET`. `StatusEndpoints.cs` —
  `GET /status`, the dashboard's read model: totals, every probe, non-empty groups, the
  ungrouped ids.
- `Homon.Web/src/pages/admin-probes-page.tsx` and `admin-probe-groups-page.tsx` — the admin
  forms. `Homon.Web/src/pages/dashboard-page.tsx` — the grouped Services section and the stat
  strip. `Homon.Web/src/lib/{probes,probe-groups,status}.ts` — the data layer.

## Per-kind options (003–005)

`Probe` carries no per-kind options column from plan 002 — `Ping` needs none, only `Host`
and the shared fields. Each later plan adds its own nullable owned-type property (e.g.
`HttpProbeOptions? HttpOptions`) mapped with `OwnsOne(...).ToJson()` — additive to the
`AddMonitoring` migration, never a reshape of it. Secrets a probe carries (SMB password, HTTP
bearer token) are held encrypted, never in clear — see 003's secret-protector decision.

## Known constraints

ICMP inside a rootless container needs `net.ipv4.ping_group_range` set (see
`compose.prod.yaml` and `docs/deployment-runbook.md`'s "ICMP" section). SMB (004) must use a
managed client rather than a kernel mount.
