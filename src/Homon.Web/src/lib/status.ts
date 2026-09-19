import { useQuery } from '@tanstack/react-query'

import { apiFetch } from '@/lib/api'

/** Mirrors ProbeKind in Homon.Domain/Monitoring/ProbeKind.cs. */
export type ProbeKind = 'ping' | 'http' | 'smb' | 'snmp'

/** Mirrors ProbeStatus in Homon.Domain/Monitoring/ProbeStatus.cs. */
export type ProbeState = 'unknown' | 'up' | 'unstable' | 'down' | 'paused'

/** Mirrors StatusTotals in Homon.Api/Endpoints/StatusEndpoints.cs. */
export interface StatusTotals {
  up: number
  unstable: number
  down: number
  unknown: number
  paused: number
  uptimePercent: number | null
}

/** Mirrors ProbeStatusResponse in Homon.Api/Endpoints/StatusEndpoints.cs. */
export interface StatusProbe {
  id: string
  name: string
  kind: ProbeKind
  state: ProbeState
  detail: string | null
  lastCheckedAt: string | null
  uptimePercent: number | null
  sparkline: number[]
}

/** Mirrors ProbeGroupSummary in Homon.Api/Endpoints/StatusEndpoints.cs. */
export interface StatusGroup {
  id: string
  name: string
  probeIds: string[]
}

/** Mirrors StatusResponse in Homon.Api/Endpoints/StatusEndpoints.cs. */
export interface Status {
  totals: StatusTotals
  probes: StatusProbe[]
  groups: StatusGroup[]
  ungroupedProbeIds: string[]
  generatedAt: string
}

export const STATUS_QUERY_KEY = ['status'] as const

export function fetchStatus(): Promise<Status> {
  return apiFetch<Status>('/status')
}

/**
 * Polls every 30 seconds — frequent enough that a family glancing at the dashboard sees a
 * state change inside a poll interval or two, infrequent enough that forty open browser tabs
 * on a home network do not turn into a request storm.
 *
 * `refetchOnWindowFocus: true` overrides main.tsx's global `refetchOnWindowFocus: false` for
 * this query alone (plan 014, D5). TanStack pauses `refetchInterval` while the document is
 * hidden — the normal state of a household dashboard tab left in the background — so without
 * this a reader returning to the tab would see stale numbers for up to 30 more seconds with
 * no cue that they were stale. This is safe specifically because the global `staleTime:
 * 30_000` still gates it: a return inside 30 s finds the cache entry not yet stale and issues
 * no request; only a return after 30 s refetches immediately.
 */
export function useStatus() {
  return useQuery({
    queryKey: STATUS_QUERY_KEY,
    queryFn: fetchStatus,
    refetchInterval: 30_000,
    refetchOnWindowFocus: true,
  })
}

/** One rendered section of the dashboard's Services area: a group, or the ungrouped rest. */
export interface DashboardSection {
  /** Stable identity for a `key` prop — `group.id`, or `'ungrouped'`. */
  id: string
  /** The `<h2 id>` — `probe-group-{id}-heading`, never derived from the group's name, or `services-heading`. */
  headingId: string
  heading: string
  probes: StatusProbe[]
}

const PHONE_SEVERITY_ORDER: Record<ProbeState, number> = {
  down: 0,
  unstable: 1,
  unknown: 2,
  up: 3,
  paused: 4,
}

/**
 * One section per non-empty group (in group order), then the ungrouped section — labelled
 * "Services" when it is the only section shown, "Other" otherwise (plan 002's Decision 7).
 * With zero probes and zero groups this still returns one "Services" section, so the
 * dashboard's existing empty sentence renders unchanged.
 *
 * On phone, each section's rows are sorted by severity (down, unstable, unknown, up, paused)
 * with a stable sort, so a family scanning a narrow screen sees the worst news first; desktop
 * keeps each section's own order (group membership order, or `Probe.Position`).
 */
export function dashboardSections(status: Status | undefined, options: { phone: boolean }): DashboardSection[] {
  const probesById = new Map((status?.probes ?? []).map((probe) => [probe.id, probe]))
  const groups = status?.groups ?? []
  const ungroupedIds = status?.ungroupedProbeIds ?? []

  function resolve(ids: string[]): StatusProbe[] {
    return ids
      .map((id) => probesById.get(id))
      .filter((probe): probe is StatusProbe => probe !== undefined)
  }

  const sections: DashboardSection[] = groups.map((group) => ({
    id: group.id,
    headingId: `probe-group-${group.id}-heading`,
    heading: group.name,
    probes: resolve(group.probeIds),
  }))

  sections.push({
    id: 'ungrouped',
    headingId: 'services-heading',
    heading: groups.length === 0 ? 'Services' : 'Other',
    probes: resolve(ungroupedIds),
  })

  if (!options.phone) {
    return sections
  }

  return sections.map((section) =>
    Object.assign({}, section, {
      probes: section.probes.toSorted(
        (a, b) => PHONE_SEVERITY_ORDER[a.state] - PHONE_SEVERITY_ORDER[b.state],
      ),
    }),
  )
}

/**
 * `1 down · 4 up` — what a collapsed probe section keeps in its header so that folding a group
 * away cannot hide a service that is down. Worst state first (the order `PHONE_SEVERITY_ORDER`
 * already encodes), zero counts omitted, `''` for a section with no probes. The `·` separator
 * and the mono/muted treatment match the stat strip at the top of the dashboard.
 */
export function summariseProbeStates(probes: readonly StatusProbe[]): string {
  const counts = new Map<ProbeState, number>()

  for (const probe of probes) {
    counts.set(probe.state, (counts.get(probe.state) ?? 0) + 1)
  }

  return [...counts.entries()]
    .toSorted(([a], [b]) => PHONE_SEVERITY_ORDER[a] - PHONE_SEVERITY_ORDER[b])
    .map(([state, count]) => `${String(count)} ${state}`)
    .join(' · ')
}

/**
 * A plain relative string for `LastCheckedAt` — `'Never'` when the probe has not been polled,
 * otherwise `"N min ago"` under an hour and `"N h ago"` beyond it. Deliberately unstyled and
 * imprecise; plan 012's Decision 6 may replace it with something nicer.
 */
export function formatCheckedAt(lastCheckedAt: string | null, now: Date): string {
  if (lastCheckedAt === null) {
    return 'Never'
  }

  const elapsedMs = now.getTime() - new Date(lastCheckedAt).getTime()
  const minutes = Math.max(0, Math.round(elapsedMs / 60_000))

  if (minutes < 60) {
    return `${minutes} min ago`
  }

  const hours = Math.round(minutes / 60)
  return `${hours} h ago`
}
