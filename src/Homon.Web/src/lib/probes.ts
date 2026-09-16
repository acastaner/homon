import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'

import { apiFetch } from '@/lib/api'
import { STATUS_QUERY_KEY, type ProbeKind, type ProbeState } from '@/lib/status'

/** Mirrors ProbeEndpoints.HttpCredentialResponse. Never carries a secret — only `hasSecret`. */
export interface HttpCredentialResponse {
  type: 'none' | 'bearer' | 'basic'
  /** Basic auth only — not a secret, round-trips in the clear. */
  username: string | null
  hasSecret: boolean
}

/** Mirrors ProbeEndpoints.HttpProbeOptionsResponse. */
export interface HttpProbeOptionsResponse {
  method: 'head' | 'get'
  path: string
  useHttps: boolean
  ignoreCertificateErrors: boolean
  timeoutSeconds: number
  expectedStatusCode: number | null
  expectedStatusCodeNegate: boolean
  expectedBodyText: string | null
  expectedBodyTextNegate: boolean
  credential: HttpCredentialResponse
}

/** Mirrors ProbeResponse in Homon.Api/Endpoints/ProbeEndpoints.cs. */
export interface Probe {
  id: string
  name: string
  host: string
  kind: ProbeKind
  pollIntervalSeconds: number
  failureThreshold: number
  isPaused: boolean
  position: number
  status: ProbeState
  lastDetail: string | null
  lastCheckedAt: string | null
  groupIds: string[]
  /** Present only when `kind === 'http'`. */
  http: HttpProbeOptionsResponse | null
}

/** Mirrors ProbeEndpoints.HttpCredentialRequest. */
export interface HttpCredentialInput {
  type: 'none' | 'bearer' | 'basic'
  username?: string | null
  /**
   * Write-only (plan 003's Decision 2): absent/undefined keeps the stored secret, `''`
   * clears it, non-empty replaces it. Never pre-filled from a `GET` response — there is
   * nothing in one to pre-fill from.
   */
  secret?: string
}

/** Mirrors ProbeEndpoints.HttpProbeOptionsRequest, the shape both `POST` and `PUT` take. */
export interface HttpProbeOptionsInput {
  method: 'head' | 'get'
  path: string
  useHttps: boolean
  ignoreCertificateErrors: boolean
  /** Omitted on create defaults server-side to 10; omitted on update keeps the current value. */
  timeoutSeconds?: number
  expectedStatusCode?: number | null
  expectedStatusCodeNegate?: boolean
  expectedBodyText?: string | null
  expectedBodyTextNegate?: boolean
  credential: HttpCredentialInput
}

/** Mirrors ProbeEndpoints.ProbeRequest, the shape both `POST` and `PUT` take. */
export interface ProbeInput {
  name: string
  host: string
  /**
   * `'ping'` or `'http'` — the only kinds this phase accepts on create. Ignored by `PUT`
   * (kind is immutable after creation).
   */
  kind: 'ping' | 'http'
  pollIntervalSeconds: number
  /** Omitted on create defaults server-side to `Probe.DefaultFailureThreshold`; required on update. */
  failureThreshold?: number
  groupIds: string[]
  /** Required when `kind` (create) or the probe's stored kind (update) is `'http'`. */
  http?: HttpProbeOptionsInput
}

export const PROBES_QUERY_KEY = ['probes'] as const

export function fetchProbes(): Promise<Probe[]> {
  return apiFetch<Probe[]>('/probes')
}

export function useProbes() {
  return useQuery({ queryKey: PROBES_QUERY_KEY, queryFn: fetchProbes })
}

/**
 * Every probe mutation invalidates both the probe list and the dashboard's status — a probe
 * edit changes what `/status` reports (name, kind-eligibility for a sparkline, group
 * membership), not just the admin list.
 */
function invalidateProbesAndStatus(queryClient: QueryClient) {
  queryClient.invalidateQueries({ queryKey: PROBES_QUERY_KEY })
  queryClient.invalidateQueries({ queryKey: STATUS_QUERY_KEY })
}

export function useCreateProbe() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (input: ProbeInput) =>
      apiFetch<Probe>('/probes', { method: 'POST', body: JSON.stringify(input) }),
    onSuccess: () => invalidateProbesAndStatus(queryClient),
  })
}

export function useUpdateProbe() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ id, ...input }: { id: string } & Omit<ProbeInput, 'kind'>) =>
      apiFetch<Probe>(`/probes/${id}`, { method: 'PUT', body: JSON.stringify(input) }),
    onSuccess: () => invalidateProbesAndStatus(queryClient),
  })
}

export function useDeleteProbe() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (id: string) => apiFetch<void>(`/probes/${id}`, { method: 'DELETE' }),
    onSuccess: () => invalidateProbesAndStatus(queryClient),
  })
}

export function useReorderProbes() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (probeIds: string[]) =>
      apiFetch<void>('/probes/order', { method: 'PUT', body: JSON.stringify({ probeIds }) }),
    onSuccess: () => invalidateProbesAndStatus(queryClient),
  })
}

export function useSetProbePaused() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ id, isPaused }: { id: string; isPaused: boolean }) =>
      apiFetch<Probe>(`/probes/${id}/pause`, { method: 'PUT', body: JSON.stringify({ isPaused }) }),
    onSuccess: () => invalidateProbesAndStatus(queryClient),
  })
}
