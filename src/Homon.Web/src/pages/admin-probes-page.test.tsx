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
  it("the kind select offers ping and http, defaults to ping, and the add form's failure threshold starts at 2", async () => {
    stubFetch(emptyProbes)
    renderWithProviders(<AdminProbesPage />)

    await waitFor(() => expect(screen.getByLabelText('Name')).toBeInTheDocument())

    const kindSelect = screen.getByRole('combobox', { name: 'Kind' }) as HTMLSelectElement
    expect(within(kindSelect).getAllByRole('option').map((option) => option.textContent)).toEqual([
      'Ping (ICMP)',
      'HTTP/HTTPS',
    ])
    expect(kindSelect.value).toBe('ping')

    expect((screen.getByLabelText('Failure threshold') as HTMLInputElement).value).toBe('2')
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
