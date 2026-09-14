/**
 * The API client.
 *
 * Development and production both serve the SPA and the API from one origin — the Vite dev
 * server's proxy locally, the SPA container's nginx in production — so the request is
 * same-origin everywhere and the session cookie is plainly first-party. That is why the
 * relative `/api/v1` fallback below is the only value in every environment.
 */
export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '/api/v1'

export class ApiError extends Error {
  readonly status: number
  readonly problem: unknown

  constructor(status: number, message: string, problem?: unknown) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }
}

/**
 * The `detail` sentence of an RFC 9457 problem, or null when the response carried none.
 * The API writes `detail` to be read by whoever hit it, so rendering it verbatim beats a
 * local paraphrase that goes stale.
 */
export function problemDetail(error: unknown): string | null {
  if (!(error instanceof ApiError) || typeof error.problem !== 'object' || error.problem === null) {
    return null
  }

  const detail = (error.problem as { detail?: unknown }).detail

  return typeof detail === 'string' && detail.length > 0 ? detail : null
}

export async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    ...init,
    credentials: 'include',
    headers: {
      Accept: 'application/json',
      // Only when there is a body: that content type is the API's CSRF guard, and a GET
      // must not carry it.
      ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
      ...init?.headers,
    },
  })

  if (!response.ok) {
    // The API answers failures with RFC 9457 problem details.
    const problem: unknown = await response.json().catch(() => undefined)
    throw new ApiError(response.status, `Request to ${path} failed`, problem)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}
