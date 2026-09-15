import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'

import { apiFetch } from '@/lib/api'
import { PROBES_QUERY_KEY } from '@/lib/probes'
import { STATUS_QUERY_KEY } from '@/lib/status'

/** Mirrors ProbeGroupEndpoints.ProbeGroupResponse in Homon.Api/Endpoints/ProbeGroupEndpoints.cs. */
export interface ProbeGroup {
  id: string
  name: string
  /** Every member probe's id, in group order. */
  probeIds: string[]
}

export const PROBE_GROUPS_QUERY_KEY = ['probe-groups'] as const

export function fetchProbeGroups(): Promise<ProbeGroup[]> {
  return apiFetch<ProbeGroup[]>('/probe-groups')
}

export function useProbeGroups() {
  return useQuery({ queryKey: PROBE_GROUPS_QUERY_KEY, queryFn: fetchProbeGroups })
}

/**
 * Every group mutation invalidates the group list, the probe list (each probe's `groupIds`
 * changes) and the dashboard's status (which sections exist and what they contain).
 */
function invalidateGroupsProbesAndStatus(queryClient: QueryClient) {
  queryClient.invalidateQueries({ queryKey: PROBE_GROUPS_QUERY_KEY })
  queryClient.invalidateQueries({ queryKey: PROBES_QUERY_KEY })
  queryClient.invalidateQueries({ queryKey: STATUS_QUERY_KEY })
}

export function useCreateProbeGroup() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (name: string) =>
      apiFetch<ProbeGroup>('/probe-groups', { method: 'POST', body: JSON.stringify({ name }) }),
    onSuccess: () => invalidateGroupsProbesAndStatus(queryClient),
  })
}

export function useRenameProbeGroup() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ id, name }: { id: string; name: string }) =>
      apiFetch<ProbeGroup>(`/probe-groups/${id}`, { method: 'PUT', body: JSON.stringify({ name }) }),
    onSuccess: () => invalidateGroupsProbesAndStatus(queryClient),
  })
}

export function useDeleteProbeGroup() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (id: string) => apiFetch<void>(`/probe-groups/${id}`, { method: 'DELETE' }),
    onSuccess: () => invalidateGroupsProbesAndStatus(queryClient),
  })
}

export function useReorderProbeGroups() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (groupIds: string[]) =>
      apiFetch<void>('/probe-groups/order', { method: 'PUT', body: JSON.stringify({ groupIds }) }),
    onSuccess: () => invalidateGroupsProbesAndStatus(queryClient),
  })
}

export function useSetProbeGroupMembers() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ id, probeIds }: { id: string; probeIds: string[] }) =>
      apiFetch<ProbeGroup>(`/probe-groups/${id}/members`, { method: 'PUT', body: JSON.stringify({ probeIds }) }),
    onSuccess: () => invalidateGroupsProbesAndStatus(queryClient),
  })
}
