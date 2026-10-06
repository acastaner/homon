import AxeBuilder from '@axe-core/playwright'
import { expect, test } from '@playwright/test'

import { expectNoHorizontalOverflow, expectTappable } from './helpers'

/**
 * The probe page (plan 023). Probes are seeded through the authenticated API (the storage state
 * this project runs with already carries the administrator's cookie) and **paused immediately**:
 * the scheduler is live in e2e, so an unpaused probe would mean real traffic and a
 * nondeterministic status. There is also no way to seed observations from here, so the chart
 * shows its empty state throughout; the chart's drawing is the unit suite's to prove.
 *
 * Cleans up in `afterEach`, because the e2e database is shared across the whole run and
 * `layout.spec.ts` expects a bare "Services" heading on `/` when nothing else is left behind.
 */
test.describe('the probe page', () => {
  let pingId: string
  let httpId: string

  test.beforeEach(async ({ request }) => {
    const ping = await request.post('/api/v1/probes', {
      data: {
        name: 'Detail ping',
        host: 'detail-ping.invalid',
        kind: 'ping',
        pollIntervalSeconds: 3600,
        failureThreshold: 2,
        groupIds: [],
      },
    })
    pingId = (await ping.json()).id
    await request.put(`/api/v1/probes/${pingId}/pause`, { data: { isPaused: true } })

    const http = await request.post('/api/v1/probes', {
      data: {
        name: 'Detail http',
        host: 'detail-http.invalid',
        kind: 'http',
        pollIntervalSeconds: 3600,
        failureThreshold: 2,
        groupIds: [],
        http: { method: 'get', path: 'health', useHttps: true },
      },
    })
    httpId = (await http.json()).id
    await request.put(`/api/v1/probes/${httpId}/pause`, { data: { isPaused: true } })
  })

  test.afterEach(async ({ request }) => {
    await request.delete(`/api/v1/probes/${pingId}`)
    await request.delete(`/api/v1/probes/${httpId}`)
  })

  test('the dashboard name is a link to the probe page', async ({ page }) => {
    await page.goto('/')

    await page.getByRole('link', { name: 'Detail ping' }).click()

    await expect(page).toHaveURL(/\/probes\/[0-9a-f-]+$/)
    await expect(page.getByRole('heading', { level: 1, name: 'Detail ping' })).toBeVisible()
  })

  test('an administrator sees the configuration', async ({ page }) => {
    await page.goto(`/probes/${httpId}`)

    await expect(page.getByRole('heading', { name: 'Configuration' })).toBeVisible()
    await expect(page.getByText('https://detail-http.invalid/health')).toBeVisible()
  })

  test('an anonymous reader sees no configuration and no host', async ({ browser, baseURL }) => {
    // A separate, cookie-less context, with both options passed explicitly: Playwright merges the
    // project's `use` options (baseURL, and — critically — the administrator's storageState) into
    // every `browser.newContext()` call, so baseURL is needed for a relative `goto`, and an empty
    // storageState is what stops this "anonymous" context silently inheriting the administrator's
    // cookie and passing for the wrong reason (see pages.spec.ts).
    const anonymousContext = await browser.newContext({ baseURL, storageState: { cookies: [], origins: [] } })
    const anonymousPage = await anonymousContext.newPage()

    await anonymousPage.goto(`/probes/${httpId}`)

    await expect(anonymousPage.getByRole('heading', { level: 1, name: 'Detail http' })).toBeVisible()
    await expect(anonymousPage.getByRole('heading', { name: 'Configuration' })).toHaveCount(0)
    await expect(anonymousPage.getByText('detail-http.invalid')).toHaveCount(0)

    await anonymousContext.close()
  })

  test('the range switch changes the URL', async ({ page }) => {
    await page.goto(`/probes/${pingId}`)

    await page.getByRole('button', { name: '7 days' }).click()

    await expect(page).toHaveURL(/\?range=7d$/)
    await expect(page.getByRole('button', { name: '7 days' })).toHaveAttribute('aria-pressed', 'true')
  })

  test('it fits and its controls are tappable', async ({ page }) => {
    await page.goto(`/probes/${pingId}`)
    await expect(page.getByRole('heading', { level: 1, name: 'Detail ping' })).toBeVisible()

    await expectNoHorizontalOverflow(page)
    await expectTappable(page, 'main button')
  })

  test('it has no color-contrast violations', async ({ page }) => {
    await page.goto(`/probes/${httpId}`)
    await expect(page.getByRole('heading', { name: 'Configuration' })).toBeVisible()

    const results = await new AxeBuilder({ page }).withRules(['color-contrast']).analyze()

    expect(results.violations).toEqual([])
  })
})
