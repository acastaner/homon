import { expect, test } from '@playwright/test'

import { expectTappable } from './helpers'

/**
 * Covers the browser behaviour Vitest cannot: `refetchOnWindowFocus` (Step 2 of plan 014) is
 * indistinguishable from the library default in the unit environment (see `src/test/
 * render.tsx`'s comment and `lib/status.ts`'s `useStatus`), so the real guarantee this file
 * makes is that the banner's freshness indicator and Refresh button actually render and work
 * in a real browser. Runs at both viewport projects automatically, matching every other spec
 * (playwright.config.ts).
 */
test.describe('refresh', () => {
  test('the dashboard shows its age', async ({ page }) => {
    await page.goto('/')

    await expect(page.getByText(/refreshed \d+ s ago/)).toBeVisible()
  })

  test('the Refresh button is there and is tappable', async ({ page }) => {
    await page.goto('/')

    await expectTappable(page, 'button')
  })

  test('clicking Refresh issues a new GET /status', async ({ page }) => {
    await page.goto('/')

    const response = page.waitForResponse((r) => r.url().includes('/api/v1/status'))
    await page.getByRole('button', { name: 'Refresh' }).click()

    expect((await response).ok()).toBe(true)
  })
})
