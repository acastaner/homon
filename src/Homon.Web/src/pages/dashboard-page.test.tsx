import { describe, expect, it, vi, afterEach, beforeEach } from 'vitest'
import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import { COLLAPSED_SECTIONS_STORAGE_KEY, writeCollapsedSections } from '@/lib/collapsed-sections'
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
  // Every DashboardPage render also fetches its Links, Pages and Weather sections —
  // declared here so the existing group-related assertions below don't have to know that.
  '/api/v1/links': { body: [] },
  '/api/v1/pages': { body: [] },
  '/api/v1/weather': { status: 204 },
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

beforeEach(() => {
  window.localStorage.clear()
})

afterEach(() => {
  vi.unstubAllGlobals()
  window.localStorage.clear()
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
    stubFetch({
      '/api/v1/status': { body: { totals: null, probes: [], groups: [], ungroupedProbeIds: [], generatedAt: '2026-01-01T00:00:00Z' } },
      '/api/v1/pages': { body: [] },
      '/api/v1/weather': { status: 204 },
      ...twoLinks,
    })
    renderWithProviders(<DashboardPage />)

    const nas = await screen.findByRole('link', { name: /NAS \(opens in a new tab\)/ })
    const router = screen.getByRole('link', { name: /Router \(opens in a new tab\)/ })

    for (const link of [nas, router]) {
      expect(link).toHaveAttribute('target', '_blank')
      expect(link).toHaveAttribute('rel', 'noopener noreferrer')
    }
  })

  it('shows no Pages section when there are zero published pages', async () => {
    stubFetch(statusWithASharedProbe)
    renderWithProviders(<DashboardPage />)

    await screen.findByRole('region', { name: 'Hosts' })

    expect(screen.queryByRole('region', { name: 'Pages' })).not.toBeInTheDocument()
  })

  it('renders a Pages section linking to each published page', async () => {
    stubFetch({
      '/api/v1/status': { body: { totals: null, probes: [], groups: [], ungroupedProbeIds: [], generatedAt: '2026-01-01T00:00:00Z' } },
      '/api/v1/links': { body: [] },
      '/api/v1/pages': { body: [{ slug: 'welcome', title: 'Welcome' }] },
      '/api/v1/weather': { status: 204 },
    })
    renderWithProviders(<DashboardPage />)

    const pages = await screen.findByRole('region', { name: 'Pages' })

    expect(within(pages).getByRole('link', { name: 'Welcome' })).toHaveAttribute('href', '/pages/welcome')
  })

  it('shows the empty-state sentence and a link to the admin form when unconfigured', async () => {
    stubFetch(statusWithASharedProbe)
    renderWithProviders(<DashboardPage />)

    const weather = await screen.findByRole('region', { name: 'Weather' })

    expect(await within(weather).findByText(/No weather location yet/)).toBeInTheDocument()
    expect(within(weather).getByRole('link', { name: 'Weather' })).toHaveAttribute('href', '/admin/weather')
  })

  it('renders the current conditions, temperature and each forecast row as visible text', async () => {
    stubFetch({
      ...statusWithASharedProbe,
      '/api/v1/weather': {
        body: {
          place: 'Test location',
          units: 'metric',
          current: { temperature: 18.4, apparentTemperature: 17.2, windSpeed: 12.1, condition: 'clear', isDay: true },
          forecast: [
            { date: '2026-01-02', condition: 'partlyCloudy', high: 19, low: 11 },
            { date: '2026-01-03', condition: 'clear', high: 21, low: 12 },
            { date: '2026-01-04', condition: 'rain', high: 16, low: 9 },
          ],
          fetchedAt: '2026-01-01T00:00:00Z',
          stale: false,
        },
      },
    })
    renderWithProviders(<DashboardPage />)

    const weather = await screen.findByRole('region', { name: 'Weather · Test location' })

    expect(within(weather).getByText(/18°C/)).toBeInTheDocument()
    // The condition word is visible text beside the icon, not only inside its aria-hidden SVG —
    // matched with a regex rather than an exact string, since it sits alongside other text
    // (the temperature, the day) within the same element.
    expect(within(weather).getAllByText(/Clear/).length).toBeGreaterThan(0)
    expect(within(weather).getByText(/Partly cloudy/)).toBeInTheDocument()
    expect(within(weather).getByText(/Rain/)).toBeInTheDocument()
    expect(within(weather).getByText(/19°C\/11°C/)).toBeInTheDocument()
    expect(within(weather).getByText(/16°C\/9°C/)).toBeInTheDocument()
    expect(within(weather).getByRole('link', { name: 'Open-Meteo.com' })).toHaveAttribute(
      'href',
      'https://open-meteo.com/',
    )
  })

  it('shows an unavailable message on a 503 problem response', async () => {
    stubFetch({
      ...statusWithASharedProbe,
      '/api/v1/weather': {
        status: 503,
        body: { title: 'Weather unavailable', detail: 'No recent answer is available.' },
      },
    })
    renderWithProviders(<DashboardPage />)

    const weather = await screen.findByRole('region', { name: 'Weather' })

    expect(await within(weather).findByText('Weather is temporarily unavailable.')).toBeInTheDocument()
  })

  it('sections start expanded and say so', async () => {
    stubFetch(statusWithASharedProbe)
    renderWithProviders(<DashboardPage />)

    const hosts = await screen.findByRole('region', { name: 'Hosts' })

    expect(screen.getByRole('button', { name: 'Hosts' })).toHaveAttribute('aria-expanded', 'true')
    expect(within(hosts).getByText('Shared device')).toBeVisible()
  })

  it('clicking a heading collapses it and writes the choice, per-section', async () => {
    const user = userEvent.setup()
    stubFetch(statusWithASharedProbe)
    renderWithProviders(<DashboardPage />)

    await screen.findByRole('region', { name: 'Hosts' })
    await user.click(screen.getByRole('button', { name: 'Hosts' }))

    const hosts = screen.getByRole('region', { name: 'Hosts' })
    expect(within(hosts).getByText('Shared device')).not.toBeVisible()
    expect(screen.getByRole('button', { name: 'Hosts' })).toHaveAttribute('aria-expanded', 'false')
    expect(window.localStorage.getItem(COLLAPSED_SECTIONS_STORAGE_KEY)).toBe('["group-hosts"]')

    const storage = screen.getByRole('region', { name: 'Storage' })
    expect(within(storage).getByText('Shared device')).toBeVisible()
  })

  it('a stored id collapses a section on first paint, with its summary', async () => {
    writeCollapsedSections(['group-hosts'])
    stubFetch(statusWithASharedProbe)
    renderWithProviders(<DashboardPage />)

    await screen.findByRole('region', { name: 'Storage' })

    const hosts = screen.getByRole('region', { name: 'Hosts' })
    expect(screen.getByRole('button', { name: 'Hosts' })).toHaveAttribute('aria-expanded', 'false')
    expect(within(hosts).getByText('1 up')).toBeVisible()
  })
})
