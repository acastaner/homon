import { describe, expect, it, vi, afterEach } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import { AdminProbesPage } from '@/pages/admin-probes-page'
import { renderWithProviders } from '@/test/render'
import { stubFetch } from '@/test/fetch'

const emptyProbes = { '/api/v1/probes': { body: [] }, '/api/v1/probe-groups': { body: [] } }

const oneProbe = {
  '/api/v1/probes': {
    body: [
      {
        id: 'probe-1',
        name: 'NAS',
        host: 'nas.local',
        kind: 'ping',
        pollIntervalSeconds: 30,
        failureThreshold: 2,
        isPaused: false,
        position: 0,
        status: 'up',
        lastDetail: null,
        lastCheckedAt: null,
        groupIds: [],
      },
    ],
  },
  '/api/v1/probe-groups': { body: [] },
}

const twoProbes = {
  '/api/v1/probes': {
    body: [
      { ...oneProbe['/api/v1/probes'].body[0], id: 'probe-1', name: 'NAS', position: 0 },
      { ...oneProbe['/api/v1/probes'].body[0], id: 'probe-2', name: 'Router', position: 1 },
    ],
  },
  '/api/v1/probe-groups': { body: [] },
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('AdminProbesPage', () => {
  it("the kind select has exactly one option, and the add form's failure threshold starts at 2", async () => {
    stubFetch(emptyProbes)
    renderWithProviders(<AdminProbesPage />)

    await waitFor(() => expect(screen.getByLabelText('Name')).toBeInTheDocument())

    const kindSelect = screen.getByLabelText('Kind') as HTMLSelectElement
    expect(within(kindSelect).getAllByRole('option')).toHaveLength(1)
    expect(kindSelect.value).toBe('ping')

    expect((screen.getByLabelText('Failure threshold') as HTMLInputElement).value).toBe('2')
  })

  it('submitting posts the trimmed field values with groupIds', async () => {
    const calls = stubFetch({
      ...emptyProbes,
      '/api/v1/probe-groups': { body: [{ id: 'group-1', name: 'Hosts', probeIds: [] }] },
    })
    const user = userEvent.setup()

    renderWithProviders(<AdminProbesPage />)

    await waitFor(() => expect(screen.getByLabelText('Name')).toBeInTheDocument())

    await user.type(screen.getByLabelText('Name'), '  NAS  ')
    await user.type(screen.getByLabelText('Host'), '  nas.local  ')
    await user.click(screen.getByLabelText('Hosts'))
    await user.click(screen.getByRole('button', { name: 'Add probe' }))

    await waitFor(() => expect(calls.some((call) => call.path === '/api/v1/probes' && call.init?.method === 'POST')).toBe(true))

    const post = calls.find((call) => call.path === '/api/v1/probes' && call.init?.method === 'POST')
    const body = JSON.parse(String(post?.init?.body))

    expect(body).toMatchObject({
      name: 'NAS',
      host: 'nas.local',
      kind: 'ping',
      failureThreshold: 2,
      groupIds: ['group-1'],
    })
  })

  it('the move, pause and delete buttons send the right request', async () => {
    const calls = stubFetch({
      ...oneProbe,
      '/api/v1/probes/probe-1/pause': { status: 200, body: { ...oneProbe['/api/v1/probes'].body[0], isPaused: true } },
      '/api/v1/probes/probe-1': { status: 204 },
    })
    const user = userEvent.setup()

    renderWithProviders(<AdminProbesPage />)

    await waitFor(() => expect(screen.getByText('NAS')).toBeInTheDocument())

    await user.click(screen.getByRole('button', { name: 'Pause NAS' }))
    await waitFor(() =>
      expect(
        calls.some((call) => call.path === '/api/v1/probes/probe-1/pause' && call.init?.method === 'PUT'),
      ).toBe(true),
    )
    const pauseCall = calls.find((call) => call.path === '/api/v1/probes/probe-1/pause')
    expect(JSON.parse(String(pauseCall?.init?.body))).toEqual({ isPaused: true })

    await user.click(screen.getByRole('button', { name: 'Delete NAS' }))
    await user.click(screen.getByRole('button', { name: 'Confirm delete NAS' }))

    await waitFor(() =>
      expect(
        calls.some((call) => call.path === '/api/v1/probes/probe-1' && call.init?.method === 'DELETE'),
      ).toBe(true),
    )
  })

  it('the move button sends the swapped order', async () => {
    const calls = stubFetch({ ...twoProbes, '/api/v1/probes/order': { status: 204 } })
    const user = userEvent.setup()

    renderWithProviders(<AdminProbesPage />)

    await waitFor(() => expect(screen.getByText('Router')).toBeInTheDocument())

    await user.click(screen.getByRole('button', { name: 'Move Router up' }))

    await waitFor(() =>
      expect(calls.some((call) => call.path === '/api/v1/probes/order' && call.init?.method === 'PUT')).toBe(true),
    )

    const orderCall = calls.find((call) => call.path === '/api/v1/probes/order')
    expect(JSON.parse(String(orderCall?.init?.body))).toEqual({ probeIds: ['probe-2', 'probe-1'] })
  })
})
