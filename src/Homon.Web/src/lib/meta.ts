import { useQuery } from '@tanstack/react-query'

import { apiFetch } from '@/lib/api'

/** Mirrors ApiMetaResponse in Homon.Api/Endpoints/MetaEndpoints.cs. */
export interface Meta {
  name: string
  apiVersion: string
  release: string
  environment: string
  requireSignInForReaders: boolean
  administratorConfigured: boolean
  openapi: string
}

export const META_QUERY_KEY = ['meta'] as const

export function fetchMeta(): Promise<Meta> {
  return apiFetch<Meta>('/meta')
}

/** Polled once per load; it changes only on deploy. */
export function useMeta() {
  return useQuery({ queryKey: META_QUERY_KEY, queryFn: fetchMeta, staleTime: Infinity })
}
