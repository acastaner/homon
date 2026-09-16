import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { apiFetch } from '@/lib/api'

/** Mirrors LinkEndpoints.LinkResponse in Homon.Api/Endpoints/LinkEndpoints.cs. */
export interface Link {
  id: string
  title: string
  url: string
  description: string | null
  createdAt: string
  updatedAt: string
}

/** The admin form's shape — always strings, trimmed and validated server-side. */
export interface LinkFields {
  title: string
  url: string
  description: string
}

export const LINKS_QUERY_KEY = ['links'] as const

export function fetchLinks(): Promise<Link[]> {
  return apiFetch<Link[]>('/links')
}

export function useLinks() {
  return useQuery({ queryKey: LINKS_QUERY_KEY, queryFn: fetchLinks })
}

function useInvalidateLinks() {
  const queryClient = useQueryClient()
  return () => queryClient.invalidateQueries({ queryKey: LINKS_QUERY_KEY })
}

export function useCreateLink() {
  const invalidateLinks = useInvalidateLinks()

  return useMutation({
    mutationFn: (fields: LinkFields) =>
      apiFetch<Link>('/links', { method: 'POST', body: JSON.stringify(fields) }),
    onSuccess: () => invalidateLinks(),
  })
}

export function useUpdateLink() {
  const invalidateLinks = useInvalidateLinks()

  return useMutation({
    mutationFn: ({ id, fields }: { id: string; fields: LinkFields }) =>
      apiFetch<Link>(`/links/${id}`, { method: 'PUT', body: JSON.stringify(fields) }),
    onSuccess: () => invalidateLinks(),
  })
}

export function useDeleteLink() {
  const invalidateLinks = useInvalidateLinks()

  return useMutation({
    // The body is empty but deliberately present: apiFetch only sets the JSON content type
    // when there is one, and the endpoint rejects a delete that does not carry it — the
    // same CSRF guard as /auth/sign-out.
    mutationFn: (id: string) => apiFetch<void>(`/links/${id}`, { method: 'DELETE', body: '{}' }),
    onSuccess: () => invalidateLinks(),
  })
}

export function useReorderLinks() {
  const invalidateLinks = useInvalidateLinks()

  return useMutation({
    mutationFn: (linkIds: string[]) =>
      apiFetch<void>('/links/order', { method: 'PUT', body: JSON.stringify({ linkIds }) }),
    onSuccess: () => invalidateLinks(),
  })
}
