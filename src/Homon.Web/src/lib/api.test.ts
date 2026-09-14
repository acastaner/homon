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
})
