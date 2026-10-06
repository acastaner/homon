import { keepPreviousData, useQuery } from '@tanstack/react-query'

import { ApiError, apiFetch } from '@/lib/api'
import { STATUS_QUERY_KEY, type ProbeKind, type ProbeState, type StatusProbeMessage } from '@/lib/status'

/** The three ranges the probe page offers; the server's table is `StatusEndpoints.ProbeHistoryRanges`. */
export const PROBE_RANGES = ['24h', '7d', '30d'] as const
export type ProbeRange = (typeof PROBE_RANGES)[number]

/** The range switch's button names. */
export const PROBE_RANGE_LABEL: Record<ProbeRange, string> = {
  '24h': '24 hours',
  '7d': '7 days',
  '30d': '30 days',
}

/** The range in the page URL, or `'24h'` when it is absent or not one of ours. */
export function parseProbeRange(value: string | null): ProbeRange {
  return PROBE_RANGES.find((range) => range === value) ?? '24h'
}

/** Mirrors LatencyBucketResponse in Homon.Api/Endpoints/StatusEndpoints.cs. */
export interface LatencyBucket {
  start: string
  averageLatencyMs: number | null
  polls: number
  failures: number
}

/** Mirrors ProbeObservationResponse in Homon.Api/Endpoints/StatusEndpoints.cs. */
export interface ProbeObservationRow {
  observedAt: string
  succeeded: boolean
  latencyMs: number | null
  detail: string | null
}

/**
 * Mirrors ProbeHistoryResponse in Homon.Api/Endpoints/StatusEndpoints.cs. It has no host and no
 * other configuration, on purpose: that is the administrator-only `Probe` in `lib/probes.ts`
 * (plan 023, D1).
 */
export interface ProbeHistory {
  id: string
  name: string
  kind: ProbeKind
  state: ProbeState
  detail: string | null
  lastCheckedAt: string | null
  uptimePercent: number | null
  range: ProbeRange
  windowStart: string
  bucketSeconds: number
  rangeUptimePercent: number | null
  latency: LatencyBucket[]
  recentObservations: ProbeObservationRow[]
  message: StatusProbeMessage | null
}

/**
 * One probe's page data. The key nests under `['status']`, so every probe mutation's existing
 * `invalidateQueries({ queryKey: STATUS_QUERY_KEY })` refreshes it as well; `RefreshIndicator`
 * observes the exact key `['status']`, so this nested key does not disturb it.
 *
 * `placeholderData: keepPreviousData` keeps the old chart on screen while another range loads, so
 * switching range does not flash. A 404 is not retried: TanStack's default of three retries would
 * leave a reader of a deleted probe's bookmark on a blank page for about seven seconds (D11).
 */
export function useProbeHistory(id: string, range: ProbeRange) {
  return useQuery({
    queryKey: [...STATUS_QUERY_KEY, 'probe', id, range],
    queryFn: () => apiFetch<ProbeHistory>(`/status/probes/${id}?range=${range}`),
    refetchInterval: 30_000,
    refetchOnWindowFocus: true,
    placeholderData: keepPreviousData,
    retry: (failureCount, error) => !(error instanceof ApiError && error.status === 404) && failureCount < 3,
  })
}

/** `0.8 ms` below 10, `12 ms` up to 999, `1.23 s` from 1000. */
export function formatLatency(ms: number): string {
  if (ms >= 1000) {
    return `${(ms / 1000).toFixed(2)} s`
  }

  if (ms >= 10) {
    return `${String(Math.round(ms))} ms`
  }

  return `${ms.toFixed(1)} ms`
}

/**
 * Whole units only, for poll intervals and bucket widths: `15 s`, `1 min`, `1 min 30 s`, `1 h`,
 * `6 h`, `1 day`, `2 days`. Days if divisible by 86400, else hours if divisible by 3600, else
 * minutes plus the remaining seconds.
 */
export function formatDuration(seconds: number): string {
  if (seconds > 0 && seconds % 86_400 === 0) {
    const days = seconds / 86_400
    return days === 1 ? '1 day' : `${String(days)} days`
  }

  if (seconds > 0 && seconds % 3600 === 0) {
    return `${String(seconds / 3600)} h`
  }

  if (seconds >= 60) {
    const minutes = Math.floor(seconds / 60)
    const rest = seconds % 60
    return rest === 0 ? `${String(minutes)} min` : `${String(minutes)} min ${String(rest)} s`
  }

  return `${String(seconds)} s`
}

/**
 * The smallest "nice" number (1, 1.5, 2, 2.5, 3, 4, 5, 6, 8 or 10 times a power of ten) that is at
 * least `value`, for the chart's y axis; 1 when there is nothing to scale. The steps are finer than
 * 1/2/2.5/5 on purpose: with those, 260 ms went up to 500 and half the chart's height was empty.
 * Rounded, so `3 * 0.1` reads `0.3` and the axis label never prints a long float.
 */
export function niceCeiling(value: number): number {
  if (value <= 0) {
    return 1
  }

  const magnitude = 10 ** Math.floor(Math.log10(value))
  const step = [1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10].find((candidate) => candidate * magnitude >= value)

  // `10 * magnitude >= value` always holds, so `find` cannot miss; the fallback is for the type.
  return Number(((step ?? 10) * magnitude).toPrecision(12))
}

/** The reader's own locale and time zone, so tests must not assert its exact text. */
export function formatObservedAt(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}
