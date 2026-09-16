import { describe, expect, it, vi, afterEach } from 'vitest'
import { screen, waitFor } from '@testing-library/react'

import App from '@/App'
import { renderWithProviders } from '@/test/render'
import { stubFetch } from '@/test/fetch'

const anonymous = {
  '/api/v1/meta': { body: { name: 'Homon', release: '1.0.0', administratorConfigured: true } },
  '/api/v1/auth/session': { status: 204 },
  // Every DashboardPage render also fetches its Pages section (plan 007's usePublishedPages())
  // and its Weather section (plan 010's useWeather()).
  '/api/v1/pages': { body: [] },
  '/api/v1/weather': { status: 204 },
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('App', () => {
  it('renders the dashboard inside the shell with its landmarks', async () => {
    stubFetch(anonymous)
    renderWithProviders(<App />)

    expect(screen.getByRole('banner')).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Site' })).toBeInTheDocument()
    expect(screen.getByRole('main')).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 1, name: 'Dashboard' })).toBeInTheDocument()

    await waitFor(() => expect(screen.getByRole('contentinfo')).toHaveTextContent('API 1.0.0'))
  })

  it('sends an unknown path to the dashboard', () => {
    stubFetch(anonymous)
    renderWithProviders(<App />, { initialEntries: ['/no-such-page'] })

    expect(screen.getByRole('heading', { level: 1, name: 'Dashboard' })).toBeInTheDocument()
  })

  it('gates the admin area behind a sign-in prompt for anonymous visitors', async () => {
    stubFetch(anonymous)
    renderWithProviders(<App />, { initialEntries: ['/admin/probes'] })

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Administrators only' }),
    ).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Sign in' })).toHaveAttribute('href', '/admin/sign-in')
  })

  it('shows the admin area and a sign-out button to an administrator', async () => {
    stubFetch({
      ...anonymous,
      '/api/v1/auth/session': { body: { kind: 'administrator', name: 'admin@example.test' } },
    })
    renderWithProviders(<App />, { initialEntries: ['/admin'] })

    expect(await screen.findByRole('heading', { level: 1, name: 'Admin' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Admin sections' })).toBeInTheDocument()
  })
})
