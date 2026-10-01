import { vi } from 'vitest'

/**
 * A `fetch` stub keyed on the request path. Each entry answers with a status and a JSON
 * body; a 204 answers with no body, as the API does. Unmatched paths fail loudly, so a test
 * that reaches an endpoint it did not declare says which one.
 *
 * A key may also be method-qualified — `'POST /api/v1/reporters'` — and takes precedence over the
 * bare path. That is for the handful of routes whose GET and POST answer with different shapes:
 * creating a reporter returns the reporter *and its one-time key*, while listing them returns an
 * array, and a test that needs both cannot express it with one body.
 */
export function stubFetch(routes: Record<string, { status?: number; body?: unknown }>) {
  const calls: { path: string; init?: RequestInit }[] = []

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: string | URL | Request, init?: RequestInit) => {
      const path = typeof input === 'string' ? input : input instanceof URL ? input.pathname : input.url
      const method = (init?.method ?? 'GET').toUpperCase()
      const route = routes[`${method} ${path}`] ?? routes[path]

      calls.push({ path, init })

      if (!route) {
        throw new Error(`Unexpected fetch: ${path}`)
      }

      const status = route.status ?? 200

      return new Response(status === 204 ? null : JSON.stringify(route.body ?? {}), {
        status,
        headers: status === 204 ? {} : { 'Content-Type': 'application/json' },
      })
    }),
  )

  return calls
}
