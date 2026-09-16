import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'

import { apiFetch } from '@/lib/api'

/** Mirrors PageEndpoints.PageSummaryResponse in Homon.Api/Endpoints/PageEndpoints.cs. */
export interface PageSummary {
  slug: string
  title: string
}

/** Mirrors PageEndpoints.AdminPageSummaryResponse in Homon.Api/Endpoints/PageEndpoints.cs. */
export interface AdminPageSummary {
  id: string
  slug: string
  title: string
  isPublished: boolean
  updatedAt: string
}

/** Mirrors PageEndpoints.PageResponse in Homon.Api/Endpoints/PageEndpoints.cs. */
export interface Page {
  id: string
  slug: string
  title: string
  /** Sanitised HTML — safe to render verbatim; see page-page.tsx. */
  bodyHtml: string
  isPublished: boolean
  createdAt: string
  updatedAt: string
}

/** The editor form's submitted shape. */
export interface PageFields {
  slug: string
  title: string
  bodyHtml: string
  isPublished: boolean
}

export const PAGES_QUERY_KEY = ['pages'] as const
export const ADMIN_PAGES_QUERY_KEY = ['admin-pages'] as const
export const PAGE_QUERY_KEY = (slug: string) => ['page', slug] as const

export function fetchPublishedPages(): Promise<PageSummary[]> {
  return apiFetch<PageSummary[]>('/pages')
}

export function usePublishedPages() {
  return useQuery({ queryKey: PAGES_QUERY_KEY, queryFn: fetchPublishedPages })
}

export function fetchAdminPages(): Promise<AdminPageSummary[]> {
  return apiFetch<AdminPageSummary[]>('/admin/pages')
}

export function useAdminPages() {
  return useQuery({ queryKey: ADMIN_PAGES_QUERY_KEY, queryFn: fetchAdminPages })
}

export function fetchPage(slug: string): Promise<Page> {
  return apiFetch<Page>(`/pages/${slug}`)
}

/**
 * Reads a page by slug — the only lookup the API offers (there is no `GET /pages/{id}`), so
 * the editor's "edit" route carries the page's slug alongside its id and reuses this.
 */
export function usePage(slug: string | undefined) {
  return useQuery({
    queryKey: PAGE_QUERY_KEY(slug ?? ''),
    queryFn: () => fetchPage(slug ?? ''),
    enabled: !!slug,
  })
}

/** Every mutation invalidates the published list and the admin list; update also the single page. */
function invalidatePages(queryClient: QueryClient, slug?: string) {
  queryClient.invalidateQueries({ queryKey: PAGES_QUERY_KEY })
  queryClient.invalidateQueries({ queryKey: ADMIN_PAGES_QUERY_KEY })

  if (slug) {
    queryClient.invalidateQueries({ queryKey: PAGE_QUERY_KEY(slug) })
  }
}

export function useCreatePage() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (fields: PageFields) =>
      apiFetch<Page>('/pages', { method: 'POST', body: JSON.stringify(fields) }),
    onSuccess: (page) => invalidatePages(queryClient, page.slug),
  })
}

export function useUpdatePage() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ id, fields }: { id: string; fields: PageFields }) =>
      apiFetch<Page>(`/pages/${id}`, { method: 'PUT', body: JSON.stringify(fields) }),
    onSuccess: (page) => invalidatePages(queryClient, page.slug),
  })
}

export function useDeletePage() {
  const queryClient = useQueryClient()

  return useMutation({
    // No CSRF content-type guard needed here — DELETE has no bound body, and unlike
    // /links/{id} this endpoint carries no such check (an HTML form cannot issue DELETE at
    // all; see PageEndpoints.cs).
    mutationFn: (id: string) => apiFetch<void>(`/pages/${id}`, { method: 'DELETE' }),
    onSuccess: () => invalidatePages(queryClient),
  })
}
