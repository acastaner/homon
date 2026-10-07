import AxeBuilder from '@axe-core/playwright'
import { expect, test, type APIRequestContext } from '@playwright/test'

import { expectNoHorizontalOverflow } from './helpers'

/**
 * The Alerts admin page (plan 026). The API runs with `Email__Transport=Log`
 * (playwright.config.ts), so a test email "sends" by being written to the log: the row turns
 * Sent, and nothing is mailed. The key typed below is obviously fake for the same reason.
 *
 * The settings are one singleton row in a database shared by the whole run. The two viewport
 * projects never race on it: playwright.config.ts pins `workers: 1` and `fullyParallel: false`,
 * so `mobile` finishes before `desktop` starts. Each test puts the defaults back in `afterEach`,
 * which also runs after a failure.
 */
async function resetSettings(request: APIRequestContext) {
  await request.put('/api/v1/alerts/settings', {
    data: { isEnabled: false, apiKey: '', fromAddress: '', fromName: 'Homon', recipients: [] },
  })
}

test.describe('admin alerts', () => {
  test.beforeEach(async ({ request }) => {
    await resetSettings(request)
  })

  test.afterEach(async ({ request }) => {
    await resetSettings(request)
  })

  test('set up, switch on, then a test email turns Sent', async ({ page }) => {
    await page.goto('/admin/alerts')

    await expect(page.getByRole('heading', { level: 1, name: 'Alerts' })).toBeVisible()
    await expect(page.getByRole('list', { name: 'Delivery' })).toContainText('Not set up')

    await page.getByLabel('Resend API key').fill('re_e2e_not_real')
    await page.getByLabel('From address').fill('alerts@example.test')

    await page.getByLabel('Add recipient').fill('first@example.test')
    await page.getByRole('button', { name: 'Add', exact: true }).click()
    await page.getByLabel('Add recipient').fill('second@example.test')
    await page.getByRole('button', { name: 'Add', exact: true }).click()
    await page.getByRole('button', { name: 'Remove second@example.test' }).click()
    await expect(page.getByRole('list', { name: 'Recipients' }).getByRole('listitem')).toHaveCount(1)

    await page.getByLabel('Send alert emails').check()

    await expectNoHorizontalOverflow(page)

    await Promise.all([
      page.waitForResponse(
        (response) => response.url().includes('/api/v1/alerts/settings') && response.request().method() === 'PUT',
      ),
      page.getByRole('button', { name: 'Save alert settings' }).click(),
    ])

    await expect(page.getByRole('list', { name: 'Delivery' })).toContainText('On')
    await expect(page.getByRole('list', { name: 'Delivery' })).toContainText('1 recipient')
    // The key is write-only: once saved, the page offers to replace it and never shows it.
    await expect(page.getByText('A key is already set.')).toBeVisible()
    await expect(page.getByLabel('Resend API key')).toHaveCount(0)

    const startedAt = Date.now() - 2000
    await page.getByRole('button', { name: 'Send test email' }).click()

    // Newest first, so the row this click queued is the first one. A Test row from an earlier
    // project is older than `startedAt` and must not satisfy the wait below.
    const history = page.getByRole('region', { name: 'Recent alerts' })
    const newest = history.getByRole('listitem').first()

    await expect
      .poll(async () => {
        const iso = await newest.locator('time').first().getAttribute('dateTime')

        return iso !== null && Date.parse(iso) >= startedAt
      })
      .toBe(true)
    await expect(newest).toContainText('Test')
    await expect(newest).toContainText('Sent', { timeout: 15_000 })
  })

  test('the saved settings round-trip through a reload', async ({ page, request }) => {
    await request.put('/api/v1/alerts/settings', {
      data: {
        isEnabled: false,
        apiKey: 're_e2e_not_real',
        fromAddress: 'alerts@example.test',
        fromName: 'Household',
        recipients: ['first@example.test', 'second@example.test'],
      },
    })

    await page.goto('/admin/alerts')

    await expect(page.getByLabel('From address')).toHaveValue('alerts@example.test')
    await expect(page.getByLabel('From name')).toHaveValue('Household')
    await expect(page.getByRole('list', { name: 'Recipients' }).getByRole('listitem')).toHaveCount(2)
    await expect(page.getByRole('list', { name: 'Delivery' })).toContainText('Off')
    await expect(page.getByLabel('Send alert emails')).not.toBeChecked()
  })
})

/**
 * Plan 023's lesson: a page checked only in its empty state ships with its busiest state
 * unlooked-at. Settings are real here; the delivery list is stubbed with a row in every state
 * and a deliberately long probe name and error, so the folded and the wide row are both asked
 * to hold the worst case.
 */
test.describe('admin alerts, with a busy history', () => {
  const ROWS = [
    {
      id: 6,
      kind: 'up',
      probeId: '11111111-1111-1111-1111-111111111111',
      probeName: 'NAS',
      occurredAt: '2026-10-01T12:12:00Z',
      downSince: '2026-10-01T11:00:00Z',
      state: 'sent',
      attempts: 0,
      sentAt: '2026-10-01T12:12:05Z',
      lastError: null,
    },
    {
      id: 5,
      kind: 'down',
      probeId: '11111111-1111-1111-1111-111111111111',
      probeName: 'NAS',
      occurredAt: '2026-10-01T11:00:00Z',
      downSince: '2026-10-01T11:00:00Z',
      state: 'failed',
      attempts: 5,
      sentAt: null,
      lastError: 'Resend rejected or failed the send. The sender domain is not verified, so every attempt was refused.',
    },
    {
      id: 4,
      kind: 'down',
      probeId: '22222222-2222-2222-2222-222222222222',
      probeName: 'A probe with a name long enough to test how a row truncates it on a phone',
      occurredAt: '2026-09-30T11:00:00Z',
      downSince: '2026-09-30T11:00:00Z',
      state: 'skipped',
      attempts: 0,
      sentAt: null,
      lastError: 'Alerts were switched off.',
    },
    {
      id: 3,
      kind: 'down',
      probeId: '33333333-3333-3333-3333-333333333333',
      probeName: 'Camera',
      occurredAt: '2026-09-29T11:00:00Z',
      downSince: '2026-09-29T11:00:00Z',
      state: 'pending',
      attempts: 2,
      sentAt: null,
      lastError: 'Resend did not answer within 10 seconds.',
    },
    {
      id: 2,
      kind: 'up',
      probeId: null,
      probeName: 'A deleted probe',
      occurredAt: '2026-09-28T11:00:00Z',
      downSince: '2026-09-25T09:00:00Z',
      state: 'sent',
      attempts: 1,
      sentAt: '2026-09-28T11:00:09Z',
      lastError: null,
    },
    {
      id: 1,
      kind: 'test',
      probeId: null,
      probeName: 'Test alert',
      occurredAt: '2026-09-27T11:00:00Z',
      downSince: null,
      state: 'sent',
      attempts: 0,
      sentAt: '2026-09-27T11:00:03Z',
      lastError: null,
    },
  ]

  test.beforeEach(async ({ page, request }) => {
    await request.put('/api/v1/alerts/settings', {
      data: {
        isEnabled: true,
        apiKey: 're_e2e_not_real',
        fromAddress: 'alerts@example.test',
        fromName: 'Homon',
        recipients: ['first@example.test', 'second@example.test', 'a-rather-long-address-for-a-household-member@example.test'],
      },
    })
    await page.route('**/api/v1/alerts/deliveries', (route) => route.fulfill({ json: ROWS }))
  })

  test.afterEach(async ({ page, request }) => {
    await page.evaluate(() => {
      window.localStorage.removeItem('homon-theme')
      delete document.documentElement.dataset.theme
    })
    await resetSettings(request)
  })

  test('every state reads in words, fits the viewport, and is not colour alone', async ({ page }) => {
    await page.goto('/admin/alerts')

    const history = page.getByRole('region', { name: 'Recent alerts' })
    const rows = history.getByRole('listitem')

    await expect(rows).toHaveCount(6)
    await expect(history).toContainText('Back up')
    await expect(history).toContainText('after 1 h 12 min')
    await expect(history).toContainText('Failed — Resend rejected or failed the send.')
    await expect(history).toContainText('Skipped — Alerts were switched off.')
    await expect(history).toContainText('Pending (attempt 3)')
    await expect(history).toContainText('after 3 d 2 h')

    await expectNoHorizontalOverflow(page)
  })

  for (const scheme of ['dark', 'light'] as const) {
    test(`has no color-contrast violations with a busy history, ${scheme} scheme`, async ({ page }) => {
      await page.goto('/admin/alerts')

      if (scheme === 'light') {
        await page.getByRole('button', { name: 'Switch to light theme' }).click()
      }

      await expect(page.getByRole('region', { name: 'Recent alerts' }).getByRole('listitem')).toHaveCount(6)

      const results = await new AxeBuilder({ page }).withRules(['color-contrast']).analyze()

      expect(results.violations).toEqual([])
    })
  }
})
