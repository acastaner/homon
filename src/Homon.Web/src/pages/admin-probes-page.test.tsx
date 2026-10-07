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
  it("the kind select offers ping, http and message, defaults to ping, and the add form's failure threshold starts at 2", async () => {
    stubFetch(emptyProbes)
    renderWithProviders(<AdminProbesPage />)

    await waitFor(() => expect(screen.getByLabelText('Name')).toBeInTheDocument())

    const kindSelect = screen.getByRole('combobox', { name: 'Kind' }) as HTMLSelectElement
    expect(within(kindSelect).getAllByRole('option').map((option) => option.textContent)).toEqual([
      'Ping (ICMP)',
      'HTTP/HTTPS',
      'Message (a reporter pushes to us)',
    ])
    expect(kindSelect.value).toBe('ping')

    expect((screen.getByLabelText('Failure threshold') as HTMLInputElement).value).toBe('2')

    // The API's bounds (Probe.cs), so the browser refuses an out-of-range value before the
    // round trip instead of the user meeting a 400.
    expect(screen.getByLabelText('Poll interval (seconds)')).toHaveAttribute('min', '15')
    expect(screen.getByLabelText('Poll interval (seconds)')).toHaveAttribute('max', '86400')
    expect(screen.getByLabelText('Failure threshold')).toHaveAttribute('min', '1')
    expect(screen.getByLabelText('Failure threshold')).toHaveAttribute('max', '10')
  })

  it('selecting HTTP reveals the HTTP fieldset; selecting Ping again hides it', async () => {
    stubFetch(emptyProbes)
    const user = userEvent.setup()
    renderWithProviders(<AdminProbesPage />)

    await waitFor(() => expect(screen.getByLabelText('Name')).toBeInTheDocument())

    expect(screen.queryByRole('group', { name: 'HTTP' })).not.toBeInTheDocument()

    await user.selectOptions(screen.getByRole('combobox', { name: 'Kind' }), 'HTTP/HTTPS')
    expect(screen.getByRole('group', { name: 'HTTP' })).toBeInTheDocument()

    await user.selectOptions(screen.getByRole('combobox', { name: 'Kind' }), 'Ping (ICMP)')
    expect(screen.queryByRole('group', { name: 'HTTP' })).not.toBeInTheDocument()
  })

  it('submitting an HTTP probe sends the http payload with the configured timeout', async () => {
    const calls = stubFetch(emptyProbes)
    const user = userEvent.setup()
    renderWithProviders(<AdminProbesPage />)

    await waitFor(() => expect(screen.getByLabelText('Name')).toBeInTheDocument())

    await user.type(screen.getByLabelText('Name'), 'API')
    await user.type(screen.getByLabelText('Host'), 'api.test')
    await user.selectOptions(screen.getByRole('combobox', { name: 'Kind' }), 'HTTP/HTTPS')

    await user.type(screen.getByLabelText('Path'), 'api/health')
    const timeout = screen.getByLabelText('Timeout (seconds)') as HTMLInputElement
    await user.clear(timeout)
    await user.type(timeout, '15')

    await user.click(screen.getByRole('button', { name: 'Add probe' }))

    await waitFor(() => expect(calls.some((call) => call.path === '/api/v1/probes' && call.init?.method === 'POST')).toBe(true))

    const post = calls.find((call) => call.path === '/api/v1/probes' && call.init?.method === 'POST')
    const body = JSON.parse(String(post?.init?.body))

    expect(body).toMatchObject({
      name: 'API',
      host: 'api.test',
      kind: 'http',
      http: {
        method: 'get',
        path: 'api/health',
        timeoutSeconds: 15,
        credential: { type: 'none' },
      },
    })
  })

  it('editing an HTTP probe shows its kind as static text and never pre-fills its stored secret', async () => {
    stubFetch({
      '/api/v1/probes': {
        body: [
          {
            id: 'probe-http',
            name: 'API',
            host: 'api.test',
            kind: 'http',
            pollIntervalSeconds: 30,
            failureThreshold: 2,
            isPaused: false,
            position: 0,
            status: 'up',
            lastDetail: null,
            lastCheckedAt: null,
            groupIds: [],
            http: {
              method: 'get',
              path: 'api/health',
              useHttps: true,
              ignoreCertificateErrors: false,
              timeoutSeconds: 10,
              expectedStatusCode: null,
              expectedStatusCodeNegate: false,
              expectedBodyText: null,
              expectedBodyTextNegate: false,
              credential: { type: 'bearer', username: null, hasSecret: true },
            },
          },
        ],
      },
      '/api/v1/probe-groups': { body: [] },
    })
    const user = userEvent.setup()
    renderWithProviders(<AdminProbesPage />)

    await waitFor(() => expect(screen.getByText('API')).toBeInTheDocument())
    await user.click(screen.getByRole('button', { name: 'Edit API' }))

    expect(screen.queryByRole('combobox', { name: 'Kind' })).not.toBeInTheDocument()
    expect(screen.getByText(/Kind: HTTP\/HTTPS/)).toBeInTheDocument()

    expect(screen.queryByLabelText('Bearer token')).not.toBeInTheDocument()
    const replaceButton = screen.getByRole('button', { name: 'Replace credential' })
    expect(replaceButton.closest('p')).toHaveTextContent('A secret is already set.')

    await user.click(replaceButton)
    expect((screen.getByLabelText('Bearer token') as HTMLInputElement).value).toBe('')
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
    await user.click(screen.getByRole('checkbox', { name: 'Hosts' }))
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

  it('a message probe chooses a reporter instead of typing a host, and posts its identifier', async () => {
    const calls = stubFetch({
      ...emptyProbes,
      '/api/v1/reporters': {
        body: [
          {
            id: 'reporter-1',
            identifier: '9H4KQ2VBMTR4WXYZ',
            name: 'clockmaster backup',
            description: null,
            bodyVisibility: 'administrator',
            createdAt: '',
            tokenId: '7Q2KX9VBMTR4',
            keyLastUsedAt: null,
            keyRevokedAt: null,
            latest: null,
            messageCount: 0,
            isWatched: false,
          },
        ],
      },
    })
    const user = userEvent.setup()

    renderWithProviders(<AdminProbesPage />)

    await waitFor(() => expect(screen.getByLabelText('Name')).toBeInTheDocument())

    // While the kind is ping there is a Host textbox and no reporters request at all — the
    // `enabled` gate in useReporters is what keeps every other case in this file fixture-free.
    expect(screen.getByLabelText('Host')).toBeInTheDocument()
    expect(calls.some((call) => call.path === '/api/v1/reporters')).toBe(false)

    await user.selectOptions(screen.getByRole('combobox', { name: 'Kind' }), 'Message (a reporter pushes to us)')

    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Reporter' })).toBeInTheDocument())
    expect(screen.queryByLabelText('Host')).not.toBeInTheDocument()

    await user.type(screen.getByLabelText('Name'), 'Clockmaster backup')
    await user.selectOptions(screen.getByRole('combobox', { name: 'Reporter' }), '9H4KQ2VBMTR4WXYZ')
    await user.click(screen.getByRole('button', { name: 'Add probe' }))

    await waitFor(() =>
      expect(calls.some((call) => call.path === '/api/v1/probes' && call.init?.method === 'POST')).toBe(true),
    )

    const post = calls.find((call) => call.path === '/api/v1/probes' && call.init?.method === 'POST')
    const body = JSON.parse(String(post?.init?.body))

    expect(body.kind).toBe('message')
    expect(body.host).toBe('9H4KQ2VBMTR4WXYZ')
    expect(body.http).toBeUndefined()
  })

  it('a message probe with no reporters yet points at the reporters page instead', async () => {
    stubFetch({ ...emptyProbes, '/api/v1/reporters': { body: [] } })
    const user = userEvent.setup()

    renderWithProviders(<AdminProbesPage />)

    await waitFor(() => expect(screen.getByLabelText('Name')).toBeInTheDocument())

    await user.selectOptions(screen.getByRole('combobox', { name: 'Kind' }), 'Message (a reporter pushes to us)')

    await waitFor(() => expect(screen.getByRole('link', { name: 'Add a reporter' })).toBeInTheDocument())
    expect(screen.queryByRole('combobox', { name: 'Reporter' })).not.toBeInTheDocument()
  })

  describe('sections (plan 024)', () => {
    const base = oneProbe['/api/v1/probes'].body[0]
    const grouped = {
      '/api/v1/probes': {
        body: [
          { ...base, id: 'nas', name: 'NAS', position: 0, groupIds: ['hosts', 'media'] },
          { ...base, id: 'hv', name: 'Hypervisor', position: 1, groupIds: ['hosts'] },
          { ...base, id: 'web', name: 'Website', position: 2, groupIds: [] },
        ],
      },
      '/api/v1/probe-groups': {
        body: [
          { id: 'hosts', name: 'Hosts', probeIds: ['nas', 'hv'] },
          { id: 'media', name: 'Media', probeIds: ['nas'] },
        ],
      },
    }

    it('lists each group in its own order, then the ungrouped rest under "Other"', async () => {
      stubFetch(grouped)
      renderWithProviders(<AdminProbesPage />)

      await waitFor(() => expect(screen.getByText('Website')).toBeInTheDocument())

      expect(screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent)).toEqual([
        'Hosts',
        'Media',
        'Other',
        'Add a probe',
      ])

      const hosts = screen.getByRole('region', { name: 'Hosts' })
      expect(within(hosts).getAllByRole('listitem').map((item) => within(item).getAllByRole('link')[0].textContent)).toEqual([
        'NAS',
        'Hypervisor',
      ])
      expect(within(hosts).getByText('also in Media')).toBeInTheDocument()
    })

    it("moving a grouped probe reorders that group's members, not the global probe order", async () => {
      const calls = stubFetch({
        ...grouped,
        '/api/v1/probe-groups/hosts/members': { body: { id: 'hosts', name: 'Hosts', probeIds: ['hv', 'nas'] } },
      })
      const user = userEvent.setup()
      renderWithProviders(<AdminProbesPage />)

      await waitFor(() => expect(screen.getByText('Hypervisor')).toBeInTheDocument())
      await user.click(screen.getByRole('button', { name: 'Move Hypervisor up' }))

      await waitFor(() =>
        expect(calls.some((call) => call.path === '/api/v1/probe-groups/hosts/members')).toBe(true),
      )
      const call = calls.find((candidate) => candidate.path === '/api/v1/probe-groups/hosts/members')
      expect(call?.init?.method).toBe('PUT')
      expect(JSON.parse(String(call?.init?.body))).toEqual({ probeIds: ['hv', 'nas'] })
      expect(calls.some((candidate) => candidate.path === '/api/v1/probes/order')).toBe(false)
    })

    it('edit opens the form inside that row only, and the add form steps aside until it closes', async () => {
      stubFetch(grouped)
      const user = userEvent.setup()
      renderWithProviders(<AdminProbesPage />)

      await waitFor(() => expect(screen.getByText('Hypervisor')).toBeInTheDocument())
      const hosts = screen.getByRole('region', { name: 'Hosts' })
      await user.click(within(hosts).getByRole('button', { name: 'Edit NAS' }))

      const row = within(hosts).getAllByRole('listitem')[0]
      expect(within(row).getByRole('form', { name: 'Edit NAS' })).toBeInTheDocument()
      expect(within(row).getByLabelText('Name')).toHaveFocus()
      expect(screen.getAllByRole('form')).toHaveLength(1)
      expect(screen.queryByRole('button', { name: 'Add probe' })).not.toBeInTheDocument()

      await user.click(within(row).getByRole('button', { name: 'Cancel' }))
      expect(screen.getByRole('form', { name: 'Add a probe' })).toBeInTheDocument()
    })

    it('confirming the delete of a shared probe says it leaves its other groups too', async () => {
      stubFetch(grouped)
      const user = userEvent.setup()
      renderWithProviders(<AdminProbesPage />)

      await waitFor(() => expect(screen.getByText('Hypervisor')).toBeInTheDocument())
      const hosts = screen.getByRole('region', { name: 'Hosts' })
      await user.click(within(hosts).getByRole('button', { name: 'Delete NAS' }))

      expect(within(hosts).getByText('Delete NAS? It also leaves Media.')).toBeInTheDocument()
      expect(within(hosts).getByRole('button', { name: 'Confirm delete NAS' })).toBeInTheDocument()
    })
  })
})
