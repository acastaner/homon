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
  // Every DashboardPage render also fetches its Links section — declared here so the
  // existing group-related assertions below don't have to know that.
  '/api/v1/links': { body: [] },
}

const twoLinks = {
  '/api/v1/links': {
    body: [
      { id: 'link-1', title: 'NAS', url: 'https://nas.invalid', description: null, createdAt: '', updatedAt: '' },
      {
        id: 'link-2',
        title: 'Router',
        url: 'https://router.invalid',
        description: 'Admin UI',
        createdAt: '',
        updatedAt: '',
      },
    ],
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

  it('renders every link opening in a new tab, without leaking a referrer', async () => {
    stubFetch({ '/api/v1/status': { body: { totals: null, probes: [], groups: [], ungroupedProbeIds: [], generatedAt: '2026-01-01T00:00:00Z' } }, ...twoLinks })
    renderWithProviders(<DashboardPage />)

    const nas = await screen.findByRole('link', { name: /NAS \(opens in a new tab\)/ })
    const router = screen.getByRole('link', { name: /Router \(opens in a new tab\)/ })

    for (const link of [nas, router]) {
      expect(link).toHaveAttribute('target', '_blank')
      expect(link).toHaveAttribute('rel', 'noopener noreferrer')
    }
  })
})
