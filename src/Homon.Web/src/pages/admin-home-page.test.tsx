import { describe, expect, it, vi, afterEach } from 'vitest'
import { screen, within } from '@testing-library/react'

import { AdminHomePage } from '@/pages/admin-home-page'
import { renderWithProviders } from '@/test/render'
import { stubFetch } from '@/test/fetch'

const SECTION_NAMES = ['Probes', 'Probe groups', 'Reporters', 'Links', 'Pages', 'Weather', 'API keys']

const probe = {
  id: 'probe-1',
  name: 'NAS',
  host: 'nas.invalid',
  kind: 'ping',
  pollIntervalSeconds: 60,
  failureThreshold: 2,
  isPaused: false,
  position: 0,
  status: 'down',
  lastDetail: null,
  lastCheckedAt: null,
  groupIds: [],
  http: null,
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('AdminHomePage', () => {
  it('links to every section by its plain name and summarises the probes that are down', async () => {
    stubFetch({
      '/api/v1/probes': { body: [probe] },
      '/api/v1/probe-groups': { body: [] },
      '/api/v1/reporters': { body: [] },
      '/api/v1/links': { body: [] },
      '/api/v1/admin/pages': { body: [] },
      '/api/v1/weather/settings': { status: 204 },
      '/api/v1/api-keys': { body: [] },
    })

    renderWithProviders(<AdminHomePage />)

    const nav = screen.getByRole('navigation', { name: 'Admin sections' })

    // The summaries sit outside the <a>, so each name is exactly the section's own.
    for (const name of SECTION_NAMES) {
      expect(within(nav).getByRole('link', { name })).toBeInTheDocument()
    }

    expect(await within(nav).findByText('1 down')).toBeInTheDocument()
    expect(within(nav).getByText('1 probe')).toBeInTheDocument()
  })

  it('still renders every link when every list fails', async () => {
    stubFetch({
      '/api/v1/probes': { status: 500 },
      '/api/v1/probe-groups': { status: 500 },
      '/api/v1/reporters': { status: 500 },
      '/api/v1/links': { status: 500 },
      '/api/v1/admin/pages': { status: 500 },
      '/api/v1/weather/settings': { status: 500 },
      '/api/v1/api-keys': { status: 500 },
    })

    renderWithProviders(<AdminHomePage />)

    const nav = screen.getByRole('navigation', { name: 'Admin sections' })

    for (const name of SECTION_NAMES) {
      expect(within(nav).getByRole('link', { name })).toBeInTheDocument()
    }
  })
})
