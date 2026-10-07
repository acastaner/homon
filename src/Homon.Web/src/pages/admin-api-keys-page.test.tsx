import { describe, expect, it, vi, afterEach } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import { AdminApiKeysPage } from '@/pages/admin-api-keys-page'
import { renderWithProviders } from '@/test/render'
import { stubFetch } from '@/test/fetch'

const twoKeys = {
  '/api/v1/api-keys': {
    body: [
      {
        id: 'key-1',
        name: 'clockmaster restic',
        tokenId: '7Q2KX9VBMTR4',
        scope: 'readWrite',
        createdAt: '2026-09-01T00:00:00Z',
        lastUsedAt: '2026-10-01T04:30:00Z',
        expiresAt: null,
        revokedAt: null,
        isExpired: false,
        reporterName: 'clockmaster backup',
      },
      {
        id: 'key-2',
        name: 'a retired script',
        tokenId: 'MNPQRSTVWXYZ',
        scope: 'read',
        createdAt: '2026-08-01T00:00:00Z',
        lastUsedAt: null,
        expiresAt: '2026-09-01T00:00:00Z',
        revokedAt: '2026-09-02T00:00:00Z',
        isExpired: true,
        reporterName: null,
      },
    ],
  },
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('AdminApiKeysPage', () => {
  it('lists keys with their scope, freshness and what they are paired with', async () => {
    stubFetch(twoKeys)

    renderWithProviders(<AdminApiKeysPage />)

    await waitFor(() => expect(screen.getByText('clockmaster restic')).toBeInTheDocument())

    // Scoped to the list: the form's own scope <select> offers the same words. The fixture's
    // readWrite key is reporter-paired, so it sits under "Reporter keys" (plan 025's D12).
    const list = within(screen.getByRole('list', { name: 'Reporter keys' }))
    expect(list.getByText('Read and write')).toBeInTheDocument()
    expect(screen.getByText(/Paired with the reporter clockmaster backup/)).toBeInTheDocument()
    expect(screen.getByText(/Never used/)).toBeInTheDocument()
    expect(screen.getByText(/Revoked/)).toBeInTheDocument()
  })

  it('an already-revoked key cannot be revoked again', async () => {
    stubFetch(twoKeys)

    renderWithProviders(<AdminApiKeysPage />)

    await waitFor(() => expect(screen.getByText('a retired script')).toBeInTheDocument())

    expect(screen.getByRole('button', { name: 'Revoke a retired script (MNPQRSTVWXYZ)' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Revoke clockmaster restic (7Q2KX9VBMTR4)' })).toBeEnabled()
  })

  it('revoking is a two-step confirmation', async () => {
    const calls = stubFetch({ ...twoKeys, '/api/v1/api-keys/key-1': { status: 204 } })
    const user = userEvent.setup()

    renderWithProviders(<AdminApiKeysPage />)

    await waitFor(() => expect(screen.getByText('clockmaster restic')).toBeInTheDocument())

    await user.click(screen.getByRole('button', { name: 'Revoke clockmaster restic (7Q2KX9VBMTR4)' }))
    expect(calls.some((call) => call.init?.method === 'DELETE')).toBe(false)

    await user.click(screen.getByRole('button', { name: 'Confirm revoke clockmaster restic (7Q2KX9VBMTR4)' }))

    await waitFor(() =>
      expect(calls.some((call) => call.path === '/api/v1/api-keys/key-1' && call.init?.method === 'DELETE')).toBe(true),
    )
  })

  it('minting sends the chosen scope, turns an empty expiry into null, and reveals the key once', async () => {
    const calls = stubFetch({
      '/api/v1/api-keys': { body: [] },
      'POST /api/v1/api-keys': {
        status: 201,
        body: {
          id: 'key-3',
          name: 'a reading script',
          tokenId: 'ABCDEFGHJKMN',
          scope: 'read',
          createdAt: '2026-10-01T00:00:00Z',
          expiresAt: null,
          token: 'hmn_ABCDEFGHJKMN_s3cr3t',
        },
      },
    })
    const user = userEvent.setup()

    renderWithProviders(<AdminApiKeysPage />)

    await waitFor(() => expect(screen.getByLabelText('Name')).toBeInTheDocument())

    await user.type(screen.getByLabelText('Name'), '  a reading script  ')
    await user.click(screen.getByRole('button', { name: 'Create key' }))

    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument())

    const post = calls.find((call) => call.path === '/api/v1/api-keys' && call.init?.method === 'POST')
    expect(JSON.parse(String(post?.init?.body))).toEqual({
      name: 'a reading script',
      scope: 'read',
      expiresAt: null,
    })

    expect(screen.getByLabelText('API key for a reading script')).toHaveValue('hmn_ABCDEFGHJKMN_s3cr3t')
  })
})
