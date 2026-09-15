import { describe, expect, it, vi, afterEach } from 'vitest'
import { screen, within } from '@testing-library/react'

import { DashboardPage } from '@/pages/dashboard-page'
import { renderWithProviders } from '@/test/render'
import { stubFetch } from '@/test/fetch'

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
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('DashboardPage', () => {
  it('renders a region named after each non-empty probe group', async () => {
    stubFetch(statusWithASharedProbe)
    renderWithProviders(<DashboardPage />)

    expect(await screen.findByRole('region', { name: 'Hosts' })).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Storage' })).toBeInTheDocument()
  })

  it('a probe belonging to two groups appears in both of their regions', async () => {
    stubFetch(statusWithASharedProbe)
    renderWithProviders(<DashboardPage />)

    const hosts = await screen.findByRole('region', { name: 'Hosts' })
    const storage = screen.getByRole('region', { name: 'Storage' })

    expect(within(hosts).getByText('Shared device')).toBeInTheDocument()
    expect(within(storage).getByText('Shared device')).toBeInTheDocument()
  })

  it('the stat strip counts a probe shared by two groups once', async () => {
    stubFetch(statusWithASharedProbe)
    renderWithProviders(<DashboardPage />)

    await screen.findByRole('region', { name: 'Hosts' })

    expect(screen.getByText(/1 up · 0 unstable · 0 down · 0 paused · 100\.00% uptime, 30 days/)).toBeInTheDocument()
  })
})
