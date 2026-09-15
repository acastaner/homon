import { describe, expect, it, vi, afterEach } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import { AdminProbeGroupsPage } from '@/pages/admin-probe-groups-page'
import { renderWithProviders } from '@/test/render'
import { stubFetch } from '@/test/fetch'

const twoGroups = {
  '/api/v1/probe-groups': {
    body: [
      { id: 'group-1', name: 'Hosts', probeIds: [] },
      { id: 'group-2', name: 'Storage', probeIds: [] },
    ],
  },
  '/api/v1/probes': { body: [] },
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('AdminProbeGroupsPage', () => {
  it('the move button sends the swapped order to PUT /probe-groups/order', async () => {
    const calls = stubFetch({ ...twoGroups, '/api/v1/probe-groups/order': { status: 204 } })
    const user = userEvent.setup()

    renderWithProviders(<AdminProbeGroupsPage />)

    await waitFor(() => expect(screen.getByText('Storage')).toBeInTheDocument())

    await user.click(screen.getByRole('button', { name: 'Move Storage up' }))

    await waitFor(() =>
      expect(
        calls.some((call) => call.path === '/api/v1/probe-groups/order' && call.init?.method === 'PUT'),
      ).toBe(true),
    )

    const orderCall = calls.find((call) => call.path === '/api/v1/probe-groups/order')
    expect(JSON.parse(String(orderCall?.init?.body))).toEqual({ groupIds: ['group-2', 'group-1'] })
  })

  it('the create form posts the trimmed name', async () => {
    const calls = stubFetch({
      '/api/v1/probe-groups': { body: [] },
      '/api/v1/probes': { body: [] },
    })
    const user = userEvent.setup()

    renderWithProviders(<AdminProbeGroupsPage />)

    await waitFor(() => expect(screen.getByLabelText('Group name')).toBeInTheDocument())

    await user.type(screen.getByLabelText('Group name'), '  Hosts  ')
    await user.click(screen.getByRole('button', { name: 'Add group' }))

    await waitFor(() =>
      expect(calls.some((call) => call.path === '/api/v1/probe-groups' && call.init?.method === 'POST')).toBe(true),
    )

    const post = calls.find((call) => call.path === '/api/v1/probe-groups' && call.init?.method === 'POST')
    expect(JSON.parse(String(post?.init?.body))).toEqual({ name: 'Hosts' })
  })
})
