import { describe, expect, it, vi, afterEach } from 'vitest'

import { ApiError, apiFetch, problemDetail } from '@/lib/api'
import { stubFetch } from '@/test/fetch'

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('apiFetch', () => {
  it('addresses the relative /api/v1 prefix with credentials and no content type on a GET', async () => {
    const calls = stubFetch({ '/api/v1/meta': { body: { name: 'Homon' } } })

    await expect(apiFetch('/meta')).resolves.toEqual({ name: 'Homon' })

    expect(calls[0]?.path).toBe('/api/v1/meta')
    expect(calls[0]?.init?.credentials).toBe('include')
    expect(calls[0]?.init?.headers).not.toHaveProperty('Content-Type')
  })

  it('throws an ApiError carrying the problem document', async () => {
    stubFetch({ '/api/v1/thing': { status: 403, body: { detail: 'Administrators only.' } } })

    const error = await apiFetch('/thing').catch((caught: unknown) => caught)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(403)
    expect(problemDetail(error)).toBe('Administrators only.')
    expect(problemDetail(new Error('plain'))).toBeNull()
  })

  it("falls back to a validation problem's field messages when it has no detail", async () => {
    stubFetch({
      '/api/v1/probes': {
        status: 400,
        body: {
          title: 'One or more validation errors occurred.',
          errors: {
            pollIntervalSeconds: ['Poll interval must be between 15 and 86400 seconds.'],
            failureThreshold: ['Failure threshold must be between 1 and 10.'],
          },
        },
      },
    })

    const error = await apiFetch('/probes').catch((caught: unknown) => caught)

    expect(problemDetail(error)).toBe(
      'Poll interval must be between 15 and 86400 seconds. Failure threshold must be between 1 and 10.',
    )
    expect(problemDetail(new ApiError(400, 'empty', { errors: {} }))).toBeNull()
  })
})
