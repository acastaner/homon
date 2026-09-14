import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { apiFetch } from '@/lib/api'

/** Mirrors SessionResponse in Homon.Api/Endpoints/AuthenticationEndpoints.cs. */
export interface Session {
  kind: 'administrator' | 'user' | 'apiKey'
  name: string
}

export interface SignInRequest {
  email: string
  password: string
  keepSignedIn: boolean
}

export const SESSION_QUERY_KEY = ['session'] as const

/**
 * The API answers 204 when nobody is signed in, which apiFetch turns into `undefined`.
 * TanStack Query treats `undefined` as "no data" rather than a value and rejects the query,
 * so anonymity would surface as a failed query instead of a successful one. Normalising to
 * `null` keeps it an ordinary, cacheable value: `useSession()` resolves with `data: null`,
 * never `isError`, which is what lets the header render without a loading flash.
 */
export async function fetchSession(): Promise<Session | null> {
  return (await apiFetch<Session | undefined>('/auth/session')) ?? null
}

export function signIn(request: SignInRequest): Promise<void> {
  return apiFetch<void>('/auth/sign-in', {
    method: 'POST',
    body: JSON.stringify(request),
  })
}

/**
 * The body is empty but deliberately present: apiFetch only sets the JSON content type
 * when there is one, and the API rejects a sign-out that does not carry it. That is the
 * CSRF guard — a cross-site form post cannot produce an application/json request.
 */
export function signOut(): Promise<void> {
  return apiFetch<void>('/auth/sign-out', { method: 'POST', body: '{}' })
}

export function useSession() {
  return useQuery({
    queryKey: SESSION_QUERY_KEY,
    queryFn: fetchSession,
    retry: false,
    staleTime: 0,
  })
}

export function useSignIn() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: signIn,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: SESSION_QUERY_KEY }),
  })
}

export function useSignOut() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: signOut,
    onSuccess: () => queryClient.setQueryData(SESSION_QUERY_KEY, null),
  })
}
