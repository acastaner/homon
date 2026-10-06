import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes } from 'react-router'

import { ProbePage } from '@/pages/probe-page'
import { stubFetch } from '@/test/fetch'
import { renderWithProviders } from '@/test/render'

afterEach(() => {
  vi.unstubAllGlobals()
})

/** A ping probe `p1` named Internet: 96 buckets, a few with averages, one with a failure. */
function history(overrides: Record<string, unknown> = {}) {
  const latency = Array.from({ length: 96 }, (_, index) => ({
    start: new Date(Date.UTC(2026, 9, 6, 0, index * 15)).toISOString(),
    averageLatencyMs: index >= 90 && index !== 93 ? 10 + index : null,
    polls: index >= 90 ? 3 : 0,
    failures: index === 93 ? 3 : 0,
  }))

  return {
    id: 'p1',
    name: 'Internet',
    kind: 'ping',
    state: 'up',
    detail: null,
    lastCheckedAt: null,
    uptimePercent: 99.5,
    range: '24h',
    windowStart: latency[0].start,
    bucketSeconds: 900,
    rangeUptimePercent: 98.1,
    latency,
    recentObservations: [
      { observedAt: '2026-10-06T10:03:00Z', succeeded: true, latencyMs: 12.4, detail: null },
      { observedAt: '2026-10-06T10:02:00Z', succeeded: false, latencyMs: null, detail: 'Timed out' },
      { observedAt: '2026-10-06T10:01:00Z', succeeded: true, latencyMs: 11.9, detail: null },
    ],
    message: null,
    ...overrides,
  }
}

function renderPage(initialEntry = '/probes/p1') {
  return renderWithProviders(
    <Routes>
      <Route path="/probes/:id" element={<ProbePage />} />
    </Routes>,
    { initialEntries: [initialEntry] },
  )
}

/** An administrator's session and everything the Configuration block fetches, for an http probe `p1`. */
function stubAdministrator() {
  stubFetch({
    '/api/v1/auth/session': { body: { kind: 'administrator', name: 'Admin' } },
    '/api/v1/status/probes/p1?range=24h': { body: history({ name: 'Jellyfin', kind: 'http' }) },
    '/api/v1/probes/p1': {
      body: {
        id: 'p1',
        name: 'Jellyfin',
        host: 'jellyfin.test',
        kind: 'http',
        pollIntervalSeconds: 60,
        failureThreshold: 2,
        isPaused: false,
        position: 0,
        status: 'up',
        lastDetail: null,
        lastCheckedAt: null,
        groupIds: ['g1'],
        http: {
          method: 'get',
          path: 'health',
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
    },
    '/api/v1/probe-groups': { body: [{ id: 'g1', name: 'Media', probeIds: ['p1'] }] },
  })
}

describe('ProbePage', () => {
  it('shows an anonymous reader the history and no configuration, and never asks for it', async () => {
    const calls = stubFetch({
      '/api/v1/auth/session': { status: 204 },
      '/api/v1/status/probes/p1?range=24h': { body: history() },
    })

    renderPage()

    expect(await screen.findByRole('heading', { level: 1, name: 'Internet' })).toBeInTheDocument()
    expect(screen.getByText(/99\.50% uptime, 30 days/)).toBeInTheDocument()
    expect(screen.getByRole('img', { name: /Latency over the last 24 hours/ })).toBeInTheDocument()

    const table = screen.getByRole('table', { name: 'Recent polls' })
    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows).toHaveLength(3)
    expect(rows.some((row) => /Failed/.test(row.textContent ?? '') && /Timed out/.test(row.textContent ?? ''))).toBe(true)

    expect(screen.queryByRole('heading', { name: 'Configuration' })).not.toBeInTheDocument()
    expect(calls.map((call) => call.path)).not.toContain('/api/v1/probes/p1')
  })

  it("shows an administrator the probe's configuration", async () => {
    stubAdministrator()

    renderPage()

    expect(await screen.findByRole('heading', { name: 'Configuration' })).toBeInTheDocument()
    expect(await screen.findByText('https://jellyfin.test/health')).toBeInTheDocument()
    expect(screen.getByText('Every 1 min')).toBeInTheDocument()
    expect(screen.getByText('2 consecutive polls')).toBeInTheDocument()
    expect(screen.getByText('Bearer token (stored)')).toBeInTheDocument()
    expect(screen.getByText('Media')).toBeInTheDocument()
    expect(screen.getByText('Validated')).toBeInTheDocument()
  })

  it('puts the configuration above the recent polls', async () => {
    stubAdministrator()

    renderPage()

    const configuration = await screen.findByRole('heading', { name: 'Configuration' })
    const polls = screen.getByRole('heading', { name: 'Recent polls' })
    expect(configuration.compareDocumentPosition(polls) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('switches range, which is a request and a pressed button', async () => {
    const calls = stubFetch({
      '/api/v1/auth/session': { status: 204 },
      '/api/v1/status/probes/p1?range=24h': { body: history() },
      '/api/v1/status/probes/p1?range=7d': { body: history({ range: '7d', bucketSeconds: 3600 }) },
    })

    renderPage()
    await screen.findByRole('heading', { level: 1, name: 'Internet' })

    await userEvent.click(screen.getByRole('button', { name: '7 days' }))

    expect(await screen.findByRole('button', { name: '7 days', pressed: true })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '24 hours' })).toHaveAttribute('aria-pressed', 'false')
    expect(calls.map((call) => call.path)).toContain('/api/v1/status/probes/p1?range=7d')
  })

  it('gives a message probe its chip word and body, and no latency section or column', async () => {
    stubFetch({
      '/api/v1/auth/session': { status: 204 },
      '/api/v1/status/probes/p1?range=24h': {
        body: history({
          name: 'Backup',
          kind: 'message',
          state: 'down',
          latency: [],
          message: { status: 'failed', overdue: true, body: 'disk full' },
        }),
      },
    })

    renderPage()

    expect(await screen.findByRole('heading', { level: 1, name: 'Backup' })).toBeInTheDocument()
    expect(screen.getByText('Overdue')).toBeInTheDocument()
    expect(screen.getByText('disk full')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Latency' })).not.toBeInTheDocument()
    expect(screen.queryByRole('columnheader', { name: 'Latency' })).not.toBeInTheDocument()
  })

  it('says a deleted probe no longer exists, with a way back', async () => {
    stubFetch({
      '/api/v1/auth/session': { status: 204 },
      '/api/v1/status/probes/p1?range=24h': { status: 404, body: { title: 'Not Found', status: 404 } },
    })

    renderPage()

    expect(await screen.findByRole('heading', { level: 1, name: 'Probe not found' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Back to the dashboard' })).toHaveAttribute('href', '/')
  })
})
