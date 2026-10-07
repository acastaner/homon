import { describe, expect, it, vi, afterEach } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import { AdminReportersPage } from '@/pages/admin-reporters-page'
import { renderWithProviders } from '@/test/render'
import { stubFetch } from '@/test/fetch'

const oneReporter = {
  '/api/v1/reporters': {
    body: [
      {
        id: 'reporter-1',
        identifier: '9H4KQ2VBMTR4WXYZ',
        name: 'clockmaster backup',
        description: 'Daily restic backup',
        bodyVisibility: 'administrator',
        createdAt: '2026-10-01T00:00:00Z',
        tokenId: '7Q2KX9VBMTR4',
        keyLastUsedAt: null,
        keyRevokedAt: null,
        latest: {
          id: 12,
          name: 'clockmaster restic wrapper',
          status: 'success',
          category: 'backup',
          receivedAt: '2026-10-01T04:30:00Z',
          nextExpectedAt: '2099-01-01T00:00:00Z',
          recurrence: 'PT25H',
        },
        messageCount: 4,
        isWatched: false,
      },
    ],
  },
  // The page finds a reporter's watching probe from the probe list; stubFetch throws on a path a
  // test did not declare.
  '/api/v1/probes': { body: [] },
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('AdminReportersPage', () => {
  it('shows a reporter with its identifier and the chip word for what it last claimed', async () => {
    stubFetch(oneReporter)

    renderWithProviders(<AdminReportersPage />)

    await waitFor(() => expect(screen.getByText('clockmaster backup')).toBeInTheDocument())

    expect(screen.getByText('9H4KQ2VBMTR4WXYZ')).toBeInTheDocument()
    expect(screen.getByText('Succeeded')).toBeInTheDocument()
    expect(screen.getByText(/No probe watches this reporter/)).toBeInTheDocument()
  })

  it('says so when a reporter declared no recurrence, because it can then never be overdue', async () => {
    stubFetch({
      '/api/v1/reporters': {
        body: [
          {
            ...oneReporter['/api/v1/reporters'].body[0],
            latest: { ...oneReporter['/api/v1/reporters'].body[0].latest, nextExpectedAt: null, recurrence: null },
          },
        ],
      },
      '/api/v1/probes': { body: [] },
    })

    renderWithProviders(<AdminReportersPage />)

    await waitFor(() =>
      expect(screen.getByText(/can never be overdue/)).toBeInTheDocument(),
    )
  })

  it('posts the trimmed fields and the visibility the form chose', async () => {
    const calls = stubFetch({
      '/api/v1/reporters': { body: [] },
      '/api/v1/probes': { body: [] },
      // The POST answers with the created shape, not the list's: its success handler reads the
      // reporter's name and its one-time token out of it.
      'POST /api/v1/reporters': {
        status: 201,
        body: {
          reporter: { ...oneReporter['/api/v1/reporters'].body[0], latest: null, messageCount: 0 },
          token: 'hmn_7Q2KX9VBMTR4_s3cr3t',
        },
      },
    })
    const user = userEvent.setup()

    renderWithProviders(<AdminReportersPage />)

    await waitFor(() => expect(screen.getByLabelText('Name')).toBeInTheDocument())

    await user.type(screen.getByLabelText('Name'), '  remontoire nas  ')
    await user.type(screen.getByLabelText('Description'), '  Array health  ')
    await user.selectOptions(screen.getByLabelText('Message visibility'), 'reader')
    await user.click(screen.getByRole('button', { name: 'Add reporter' }))

    await waitFor(() =>
      expect(calls.some((call) => call.path === '/api/v1/reporters' && call.init?.method === 'POST')).toBe(true),
    )

    const post = calls.find((call) => call.path === '/api/v1/reporters' && call.init?.method === 'POST')
    expect(JSON.parse(String(post?.init?.body))).toEqual({
      name: 'remontoire nas',
      description: 'Array health',
      bodyVisibility: 'reader',
    })
  })

  it('reveals the minted key once, out of its own state rather than the query cache', async () => {
    const created = {
      reporter: { ...oneReporter['/api/v1/reporters'].body[0], latest: null, messageCount: 0 },
      token: 'hmn_7Q2KX9VBMTR4_s3cr3t',
    }

    // The list answers without a token, exactly as the real endpoint does, so a page reading the
    // secret back out of the cache instead of its own state would fail here.
    stubFetch({
      '/api/v1/reporters': { body: [] },
      '/api/v1/probes': { body: [] },
      'POST /api/v1/reporters': { status: 201, body: created },
    })
    const user = userEvent.setup()

    renderWithProviders(<AdminReportersPage />)
    await waitFor(() => expect(screen.getByLabelText('Name')).toBeInTheDocument())

    await user.type(screen.getByLabelText('Name'), 'clockmaster backup')
    await user.click(screen.getByRole('button', { name: 'Add reporter' }))

    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument())

    expect(screen.getByRole('alert')).toHaveTextContent('This key will not be shown again. Store it now.')
    expect(screen.getByLabelText('API key for clockmaster backup')).toHaveValue('hmn_7Q2KX9VBMTR4_s3cr3t')

    // Dismissing it is the end of it: there is nowhere left to read it from.
    await user.click(screen.getByRole('button', { name: 'Done' }))
    expect(screen.queryByLabelText('API key for clockmaster backup')).not.toBeInTheDocument()
  })

  it('deleting is a two-step confirmation', async () => {
    const calls = stubFetch({ ...oneReporter, '/api/v1/reporters/reporter-1': { status: 204 } })
    const user = userEvent.setup()

    renderWithProviders(<AdminReportersPage />)

    await waitFor(() => expect(screen.getByText('clockmaster backup')).toBeInTheDocument())

    await user.click(screen.getByRole('button', { name: 'Delete clockmaster backup' }))

    expect(calls.some((call) => call.init?.method === 'DELETE')).toBe(false)

    await user.click(screen.getByRole('button', { name: 'Confirm delete clockmaster backup' }))

    await waitFor(() =>
      expect(
        calls.some((call) => call.path === '/api/v1/reporters/reporter-1' && call.init?.method === 'DELETE'),
      ).toBe(true),
    )
  })

  it('shows a reporter history with the body, which is the only place one appears', async () => {
    const calls = stubFetch({
      ...oneReporter,
      '/api/v1/reporters/reporter-1/messages': {
        body: [
          {
            id: 12,
            name: 'clockmaster restic wrapper',
            description: null,
            body: 'snapshot a1b2c3 — 412 files',
            truncated: false,
            status: 'success',
            category: 'backup',
            receivedAt: '2026-10-01T04:30:00Z',
            nextExpectedAt: null,
            recurrence: null,
          },
        ],
      },
    })
    const user = userEvent.setup()

    renderWithProviders(<AdminReportersPage />)

    await waitFor(() => expect(screen.getByText('clockmaster backup')).toBeInTheDocument())

    await user.click(screen.getByRole('button', { name: 'Messages from clockmaster backup' }))

    await waitFor(() =>
      expect(screen.getByRole('table', { name: 'Messages from clockmaster backup' })).toBeInTheDocument(),
    )
    expect(screen.getByText('snapshot a1b2c3 — 412 files')).toBeInTheDocument()
    expect(calls.some((call) => call.path === '/api/v1/reporters/reporter-1/messages')).toBe(true)
  })

  it('replacing a key is a two-step confirmation and reveals the replacement', async () => {
    const calls = stubFetch({
      ...oneReporter,
      '/api/v1/reporters/reporter-1/key': {
        status: 200,
        body: { reporterId: 'reporter-1', tokenId: 'NEWTOKEN1234', token: 'hmn_NEWTOKEN1234_s3cr3t' },
      },
    })
    const user = userEvent.setup()

    renderWithProviders(<AdminReportersPage />)

    await waitFor(() => expect(screen.getByText('clockmaster backup')).toBeInTheDocument())

    await user.click(screen.getByRole('button', { name: 'Replace the key for clockmaster backup' }))
    expect(calls.some((call) => call.path === '/api/v1/reporters/reporter-1/key')).toBe(false)

    await user.click(screen.getByRole('button', { name: 'Confirm replace the key for clockmaster backup' }))

    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument())
    expect(screen.getByLabelText('API key for clockmaster backup')).toHaveValue('hmn_NEWTOKEN1234_s3cr3t')
  })

  it('a watched reporter explains instead of attempting: names the probe, offers no confirm, sends no DELETE', async () => {
    const reporter = { ...oneReporter['/api/v1/reporters'].body[0], isWatched: true }
    const calls = stubFetch({
      '/api/v1/reporters': { body: [reporter] },
      '/api/v1/probes': {
        body: [
          {
            id: 'probe-1',
            name: 'Nightly restic',
            host: reporter.identifier,
            kind: 'message',
            pollIntervalSeconds: 900,
            failureThreshold: 2,
            isPaused: false,
            position: 0,
            status: 'up',
            lastDetail: null,
            lastCheckedAt: null,
            groupIds: [],
            http: null,
          },
        ],
      },
    })
    const user = userEvent.setup()

    renderWithProviders(<AdminReportersPage />)

    await user.click(await screen.findByRole('button', { name: 'Delete clockmaster backup' }))

    expect(screen.getByRole('link', { name: 'Nightly restic' })).toHaveAttribute('href', '/probes/probe-1')
    expect(screen.queryByRole('button', { name: 'Confirm delete clockmaster backup' })).not.toBeInTheDocument()
    expect(calls.some((call) => call.init?.method === 'DELETE')).toBe(false)
  })

  it("an overdue reporter's chip reads Overdue", async () => {
    stubFetch({
      '/api/v1/reporters': {
        body: [
          {
            ...oneReporter['/api/v1/reporters'].body[0],
            latest: { ...oneReporter['/api/v1/reporters'].body[0].latest, nextExpectedAt: '2020-01-01T00:00:00Z' },
          },
        ],
      },
      '/api/v1/probes': { body: [] },
    })

    renderWithProviders(<AdminReportersPage />)

    expect(await screen.findByText('Overdue')).toBeInTheDocument()
  })
})
