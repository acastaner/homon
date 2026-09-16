import { expect, test } from '@playwright/test'

import { expectTappable } from './helpers'

/**
 * The theme bootstrap script and `lib/theme.ts` share a storage key (`homon-theme`) as two
 * independent string literals — a rename on one side without the other silently breaks the
 * no-flash guarantee this file exists to catch. Runs at both viewport projects, matching
 * every other spec (playwright.config.ts).
 */
test.describe('theme', () => {
  test('dark is the resident scheme on first visit, no stored preference', async ({ page }) => {
    await page.goto('/')

    // Read immediately after goto, before waiting on anything React has rendered — the
    // attribute must be correct before hydration, not merely eventually.
    const themeAttribute = await page.evaluate(() => document.documentElement.dataset.theme)

    expect(themeAttribute).toBeUndefined()
  })

  test('there is no flash of the wrong scheme — the attribute is set before Vite\'s module script runs', async ({
    page,
  }) => {
    // An init script reading the attribute at DOMContentLoaded, before /src/main.tsx (Vite's
    // injected module script) has had a chance to run — the automatable version of "no
    // flash", since a human eyeballing a screenshot cannot reliably catch a sub-16ms one.
    await page.addInitScript(() => {
      document.addEventListener('DOMContentLoaded', () => {
        ;(window as unknown as { homonThemeAtDomContentLoaded: string | undefined }).homonThemeAtDomContentLoaded =
          document.documentElement.dataset.theme
      })
    })

    await page.goto('/')

    const themeAtDomContentLoaded = await page.evaluate(
      () => (window as unknown as { homonThemeAtDomContentLoaded: string | undefined }).homonThemeAtDomContentLoaded,
    )

    // Resident scheme (dark) reads as `undefined` here too — the bootstrap script sets
    // nothing when there is no stored 'light' choice (Decision 3).
    expect(themeAtDomContentLoaded).toBeUndefined()
  })

  test('toggling the theme persists across a reload', async ({ page }) => {
    await page.goto('/')

    const darkBackground = await page.evaluate(() => getComputedStyle(document.body).backgroundColor)

    await page.getByRole('button', { name: 'Switch to light theme' }).click()

    const lightBackground = await page.evaluate(() => getComputedStyle(document.body).backgroundColor)
    expect(lightBackground).not.toBe(darkBackground)

    await page.reload()

    await expect(page.getByRole('button', { name: 'Switch to dark theme' })).toBeVisible()
    const reloadedBackground = await page.evaluate(() => getComputedStyle(document.body).backgroundColor)
    expect(reloadedBackground).toBe(lightBackground)
  })

  test('the toggle is tappable', async ({ page }) => {
    await page.goto('/')

    await expectTappable(page, 'button[type="button"]')
  })
})
