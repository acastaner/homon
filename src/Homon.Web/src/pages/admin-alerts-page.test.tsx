import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import { AdminAlertsPage } from '@/pages/admin-alerts-page'
import { renderWithProviders } from '@/test/render'
import { stubFetch } from '@/test/fetch'

const DEFAULTS = {
  isEnabled: false,
  hasApiKey: false,
  fromAddress: '',
  fromName: 'Homon',
  recipients: [],
  updatedAt: null,
}

const CONFIGURED = {
  isEnabled: true,
  hasApiKey: true,
  fromAddress: 'alerts@example.test',
  fromName: 'Homon',
  recipients: ['a@example.test'],
  updatedAt: '2026-10-01T08:00:00Z',
}

const SETTINGS = '/api/v1/alerts/settings'
const DELIVERIES = '/api/v1/alerts/deliveries'
const TEST = '/api/v1/alerts/test'

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('AdminAlertsPage', () => {
  it('reads Not set up while the defaults are stored', async () => {
    stubFetch({ [SETTINGS]: { body: DEFAULTS }, [DELIVERIES]: { body: [] } })

    renderWithProviders(<AdminAlertsPage />)

    const delivery = await screen.findByRole('list', { name: 'Delivery' })
    expect(within(delivery).getByText('Not set up')).toBeInTheDocument()
    expect(within(delivery).getByText(/no Resend key/)).toBeInTheDocument()
    expect(screen.getByLabelText('Resend API key')).toHaveAttribute('type', 'password')
  })

  it('reads On, with the sender and a count, once alerts are set up and enabled', async () => {
    stubFetch({ [SETTINGS]: { body: CONFIGURED }, [DELIVERIES]: { body: [] } })

    renderWithProviders(<AdminAlertsPage />)

    const delivery = await screen.findByRole('list', { name: 'Delivery' })
    expect(within(delivery).getByText('On')).toBeInTheDocument()
    expect(within(delivery).getByText(/Homon <alerts@example.test> · 1 recipient · Resend key set/)).toBeInTheDocument()
  })

  it('a stored key is never shown: Replace key reveals an empty input, Keep puts it back', async () => {
    stubFetch({ [SETTINGS]: { body: CONFIGURED }, [DELIVERIES]: { body: [] } })
    const user = userEvent.setup()

    renderWithProviders(<AdminAlertsPage />)

    expect(await screen.findByText(/A key is already set\./)).toBeInTheDocument()
    expect(screen.queryByLabelText('Resend API key')).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Replace key' }))
    expect(screen.getByLabelText('Resend API key')).toHaveValue('')

    await user.click(screen.getByRole('button', { name: 'Keep the current key' }))
    expect(screen.queryByLabelText('Resend API key')).not.toBeInTheDocument()
  })

  it('saves the exact body, with no apiKey property while the key is not being replaced', async () => {
    const calls = stubFetch({
      [SETTINGS]: { body: CONFIGURED },
      [DELIVERIES]: { body: [] },
    })
    const user = userEvent.setup()

    renderWithProviders(<AdminAlertsPage />)

    await screen.findByText(/A key is already set\./)

    await user.type(screen.getByLabelText('Add recipient'), 'b@example.test')
    await user.click(screen.getByRole('button', { name: 'Add' }))
    await user.type(screen.getByLabelText('Add recipient'), 'c@example.test{Enter}')
    await user.click(screen.getByRole('button', { name: 'Remove a@example.test' }))
    await user.click(screen.getByRole('button', { name: 'Save alert settings' }))

    await waitFor(() => expect(calls.some((call) => call.init?.method === 'PUT')).toBe(true))

    const put = calls.find((call) => call.init?.method === 'PUT')
    expect(put?.path).toBe(SETTINGS)
    expect(JSON.parse(String(put?.init?.body))).toEqual({
      isEnabled: true,
      fromAddress: 'alerts@example.test',
      fromName: 'Homon',
      recipients: ['b@example.test', 'c@example.test'],
    })
  })

  it('sends the new key when the stored one is replaced', async () => {
    const calls = stubFetch({ [SETTINGS]: { body: CONFIGURED }, [DELIVERIES]: { body: [] } })
    const user = userEvent.setup()

    renderWithProviders(<AdminAlertsPage />)

    await user.click(await screen.findByRole('button', { name: 'Replace key' }))
    await user.type(screen.getByLabelText('Resend API key'), 're_fake_not_real')
    await user.click(screen.getByRole('button', { name: 'Save alert settings' }))

    await waitFor(() => expect(calls.some((call) => call.init?.method === 'PUT')).toBe(true))
    const put = calls.find((call) => call.init?.method === 'PUT')
    expect(JSON.parse(String(put?.init?.body))).toMatchObject({ apiKey: 're_fake_not_real' })
  })

  it('sends an empty key to remove the stored one when Replace key is left empty', async () => {
    const calls = stubFetch({ [SETTINGS]: { body: CONFIGURED }, [DELIVERIES]: { body: [] } })
    const user = userEvent.setup()

    renderWithProviders(<AdminAlertsPage />)

    await user.click(await screen.findByRole('button', { name: 'Replace key' }))
    await user.click(screen.getByRole('button', { name: 'Save alert settings' }))

    await waitFor(() => expect(calls.some((call) => call.init?.method === 'PUT')).toBe(true))
    const put = calls.find((call) => call.init?.method === 'PUT')
    expect(JSON.parse(String(put?.init?.body))).toMatchObject({ apiKey: '' })
  })

  it('ignores a duplicate recipient, whatever its case', async () => {
    stubFetch({ [SETTINGS]: { body: CONFIGURED }, [DELIVERIES]: { body: [] } })
    const user = userEvent.setup()

    renderWithProviders(<AdminAlertsPage />)

    await screen.findByText(/A key is already set\./)
    await user.type(screen.getByLabelText('Add recipient'), 'A@Example.test')
    await user.click(screen.getByRole('button', { name: 'Add' }))

    expect(within(screen.getByRole('list', { name: 'Recipients' })).getAllByRole('listitem')).toHaveLength(1)
  })

  it('shows the problem the API gave when a save is refused', async () => {
    stubFetch({
      [SETTINGS]: { body: DEFAULTS },
      'PUT /api/v1/alerts/settings': {
        status: 400,
        body: { title: 'Bad', errors: { isEnabled: ['Add a Resend API key, a From address and at least one recipient before switching alerts on.'] } },
      },
      [DELIVERIES]: { body: [] },
    })
    const user = userEvent.setup()

    renderWithProviders(<AdminAlertsPage />)

    await screen.findByLabelText('Resend API key')
    await user.click(screen.getByRole('button', { name: 'Save alert settings' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Add a Resend API key, a From address')
  })

  it('lists every delivery state in words', async () => {
    stubFetch({
      [SETTINGS]: { body: CONFIGURED },
      [DELIVERIES]: {
        body: [
          {
            id: 5,
            kind: 'up',
            probeId: 'p1',
            probeName: 'NAS',
            occurredAt: '2026-10-01T12:12:00Z',
            downSince: '2026-10-01T11:00:00Z',
            state: 'sent',
            attempts: 0,
            sentAt: '2026-10-01T12:12:05Z',
            lastError: null,
          },
          {
            id: 4,
            kind: 'down',
            probeId: 'p1',
            probeName: 'NAS',
            occurredAt: '2026-10-01T11:00:00Z',
            downSince: '2026-10-01T11:00:00Z',
            state: 'failed',
            attempts: 5,
            sentAt: null,
            lastError: 'Resend rejected or failed the send.',
          },
          {
            id: 3,
            kind: 'down',
            probeId: 'p2',
            probeName: 'Router',
            occurredAt: '2026-09-30T11:00:00Z',
            downSince: '2026-09-30T11:00:00Z',
            state: 'skipped',
            attempts: 0,
            sentAt: null,
            lastError: 'Alerts were switched off.',
          },
          {
            id: 2,
            kind: 'down',
            probeId: 'p3',
            probeName: 'Camera',
            occurredAt: '2026-09-29T11:00:00Z',
            downSince: '2026-09-29T11:00:00Z',
            state: 'pending',
            attempts: 1,
            sentAt: null,
            lastError: 'Resend did not answer within 10 seconds.',
          },
          {
            id: 1,
            kind: 'test',
            probeId: null,
            probeName: 'Test alert',
            occurredAt: '2026-09-28T11:00:00Z',
            downSince: null,
            state: 'sent',
            attempts: 0,
            sentAt: '2026-09-28T11:00:03Z',
            lastError: null,
          },
        ],
      },
    })

    renderWithProviders(<AdminAlertsPage />)

    const history = await screen.findByRole('region', { name: 'Recent alerts' })
    const rows = within(await within(history).findByRole('list', { name: 'Alert deliveries' })).getAllByRole('listitem')

    expect(rows).toHaveLength(5)
    expect(within(rows[0]).getByText('Back up')).toBeInTheDocument()
    expect(within(rows[0]).getByText(/after 1 h 12 min/)).toBeInTheDocument()
    expect(within(rows[0]).getByText(/^Sent/)).toBeInTheDocument()
    expect(within(rows[1]).getByText('Failed — Resend rejected or failed the send.')).toBeInTheDocument()
    expect(within(rows[2]).getByText('Skipped — Alerts were switched off.')).toBeInTheDocument()
    expect(within(rows[3]).getByText(/Pending \(attempt 2\)/)).toBeInTheDocument()
    expect(within(rows[4]).getByText('Test')).toBeInTheDocument()
    expect(within(history).getByText('5 alerts')).toBeInTheDocument()
  })

  it('shows the empty-state text when nothing has been mailed', async () => {
    stubFetch({ [SETTINGS]: { body: DEFAULTS }, [DELIVERIES]: { body: [] } })

    renderWithProviders(<AdminAlertsPage />)

    expect(await screen.findByText(/Nothing has been mailed yet\./)).toBeInTheDocument()
  })

  it('Send test email posts an empty JSON body', async () => {
    const calls = stubFetch({
      [SETTINGS]: { body: CONFIGURED },
      [DELIVERIES]: { body: [] },
      [TEST]: { status: 202, body: {} },
    })
    const user = userEvent.setup()

    renderWithProviders(<AdminAlertsPage />)

    await user.click(await screen.findByRole('button', { name: 'Send test email' }))

    await waitFor(() => expect(calls.some((call) => call.path === TEST)).toBe(true))
    const post = calls.find((call) => call.path === TEST)
    expect(post?.init?.method).toBe('POST')
    expect(post?.init?.body).toBe('{}')
  })

  it('shows the API detail when the test email cannot be queued', async () => {
    stubFetch({
      [SETTINGS]: { body: DEFAULTS },
      [DELIVERIES]: { body: [] },
      [TEST]: {
        status: 400,
        body: { title: 'Alerts are not set up', detail: 'Save a Resend API key, a From address and at least one recipient first.' },
      },
    })
    const user = userEvent.setup()

    renderWithProviders(<AdminAlertsPage />)

    await user.click(await screen.findByRole('button', { name: 'Send test email' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Save a Resend API key, a From address')
  })
})
