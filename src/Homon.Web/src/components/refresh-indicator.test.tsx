import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import { RefreshIndicator } from '@/components/refresh-indicator'
import { DashboardPage } from '@/pages/dashboard-page'
import { renderWithProviders } from '@/test/render'
import { stubFetch } from '@/test/fetch'

// Copied from dashboard-page.test.tsx's statusWithASharedProbe: DashboardPage fetches all
// four endpoints unconditionally, so all four must be declared or it throws on an
// unmatched path.
const statusWithASharedProbe = {
  '/api/v1/status': {
    body: {
      totals: { up: 1, unstable: 0, down: 0, unknown: 0, paused: 0, uptimePercent: 100 },
      probes: [
        {
          id: 'probe-1',
          name: 'Shared device',
          kind: 'ping',
          state: 'up',
          detail: null,
          lastCheckedAt: null,
          uptimePercent: 100,
          sparkline: [],
        },
      ],
      groups: [
        { id: 'group-hosts', name: 'Hosts', probeIds: ['probe-1'] },
        { id: 'group-storage', name: 'Storage', probeIds: ['probe-1'] },
      ],
      ungroupedProbeIds: [],
      generatedAt: '2026-01-01T00:00:00Z',
    },
  },
  '/api/v1/links': { body: [] },
  '/api/v1/pages': { body: [] },
  '/api/v1/weather': { status: 204 },
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('RefreshIndicator', () => {
  it('issues no request of its own', () => {
    const calls = stubFetch({})
    renderWithProviders(<RefreshIndicator />)

    expect(calls).toHaveLength(0)
  })

  it('the Refresh button renders with no cached data', () => {
    stubFetch({})
    renderWithProviders(<RefreshIndicator />)

    expect(screen.getByRole('button', { name: 'Refresh' })).toBeInTheDocument()
  })

  it('shows no timestamp with no cached data', () => {
    stubFetch({})
    renderWithProviders(<RefreshIndicator />)

    expect(screen.queryByText(/refreshed/)).not.toBeInTheDocument()
  })

  it('the timestamp appears once the status cache holds data', async () => {
    stubFetch(statusWithASharedProbe)
    renderWithProviders(
      <>
        <DashboardPage />
        <RefreshIndicator />
      </>,
    )

    expect(await screen.findByText(/refreshed \d+ s ago/)).toBeInTheDocument()
  })

  it('clicking Refresh refetches', async () => {
    const user = userEvent.setup()
    const calls = stubFetch(statusWithASharedProbe)
    renderWithProviders(
      <>
        <DashboardPage />
        <RefreshIndicator />
      </>,
    )

    await screen.findByText(/refreshed \d+ s ago/)

    const statusCallsBefore = calls.filter((call) => call.path === '/api/v1/status').length

    await user.click(screen.getByRole('button', { name: 'Refresh' }))

    await waitFor(() =>
      expect(calls.filter((call) => call.path === '/api/v1/status').length).toBeGreaterThan(statusCallsBefore),
    )
  })
})
