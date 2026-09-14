import { describe, expect, it, vi, afterEach } from 'vitest'

import { fetchSession, signOut } from '@/lib/session'
import { stubFetch } from '@/test/fetch'

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('session', () => {
  it('normalises the anonymous 204 to null rather than undefined', async () => {
    stubFetch({ '/api/v1/auth/session': { status: 204 } })

    await expect(fetchSession()).resolves.toBeNull()
  })

  it('returns the session when there is one', async () => {
    stubFetch({ '/api/v1/auth/session': { body: { kind: 'apiKey', name: 'restic' } } })

    await expect(fetchSession()).resolves.toEqual({ kind: 'apiKey', name: 'restic' })
  })

  it('signs out with an empty JSON body, which is the CSRF guard', async () => {
    const calls = stubFetch({ '/api/v1/auth/sign-out': { status: 204 } })

    await signOut()

    expect(calls[0]?.init?.method).toBe('POST')
    expect(calls[0]?.init?.body).toBe('{}')
    expect(calls[0]?.init?.headers).toMatchObject({ 'Content-Type': 'application/json' })
  })
})
