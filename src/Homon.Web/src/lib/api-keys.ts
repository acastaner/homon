import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { apiFetch } from '@/lib/api'

/** Mirrors ApiKeyScope in Homon.Domain/Auth/ApiKeyScope.cs. */
export type ApiKeyScope = 'read' | 'readWrite'

/** Mirrors ApiKeyEndpoints.ApiKeyResponse. Never the secret — `tokenId` is the public half. */
export interface ApiKey {
  id: string
  name: string
  tokenId: string
  scope: ApiKeyScope
  createdAt: string
  lastUsedAt: string | null
  expiresAt: string | null
  revokedAt: string | null
  isExpired: boolean
  reporterName: string | null
}

/** Mirrors ApiKeyEndpoints.ApiKeyCreatedResponse. The token appears here and nowhere else. */
export interface ApiKeyCreated extends Omit<ApiKey, 'lastUsedAt' | 'revokedAt' | 'isExpired' | 'reporterName'> {
  token: string
}

export interface ApiKeyFields {
  name: string
  scope: ApiKeyScope
  /** An ISO instant, or an empty string for "never expires". */
  expiresAt: string
}

export const API_KEYS_QUERY_KEY = ['api-keys'] as const

export function fetchApiKeys(): Promise<ApiKey[]> {
  return apiFetch<ApiKey[]>('/api-keys')
}

export function useApiKeys() {
  return useQuery({ queryKey: API_KEYS_QUERY_KEY, queryFn: fetchApiKeys })
}

function useInvalidateApiKeys() {
  const queryClient = useQueryClient()
  return () => queryClient.invalidateQueries({ queryKey: API_KEYS_QUERY_KEY })
}

export function useCreateApiKey() {
  const invalidateApiKeys = useInvalidateApiKeys()

  return useMutation({
    // The minted key travels back to the caller's own state and never into the query cache: a
    // refetch or a stale read must not be able to resurface a secret the administrator was told
    // they would see exactly once.
    mutationFn: ({ name, scope, expiresAt }: ApiKeyFields) =>
      apiFetch<ApiKeyCreated>('/api-keys', {
        method: 'POST',
        body: JSON.stringify({ name, scope, expiresAt: expiresAt === '' ? null : expiresAt }),
      }),
    onSuccess: () => invalidateApiKeys(),
  })
}

export function useRevokeApiKey() {
  const invalidateApiKeys = useInvalidateApiKeys()

  return useMutation({
    mutationFn: (id: string) => apiFetch<void>(`/api-keys/${id}`, { method: 'DELETE', body: '{}' }),
    onSuccess: () => invalidateApiKeys(),
  })
}
