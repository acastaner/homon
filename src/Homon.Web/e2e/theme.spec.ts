import { expect, test } from '@playwright/test'

import { expectTappable } from './helpers'

/**
 * The theme bootstrap script and `lib/theme.ts` share a storage key (`homon-theme`) as two
 * independent string literals — a rename on one side without the other silently breaks the
 * no-flash guarantee this file exists to catch. Runs at both viewport projects, matching
 * every other spec (playwright.config.ts).
 */
test.describe('theme', () => {
  test('dark is the resident scheme on first visit, no stored preference, and it actually renders dark', async ({
    page,
  }) => {
    await page.goto('/')

    // Read immediately after goto, before waiting on anything React has rendered — the
    // attribute must be correct before hydration, not merely eventually.
    const themeAttribute = await page.evaluate(() => document.documentElement.dataset.theme)
    expect(themeAttribute).toBeUndefined()

    // The attribute alone does not prove anything about what actually painted — a
    // regression that put light's colours on the bare :root (or a stylesheet that never
    // loaded) would leave this attribute correctly unset while rendering the wrong scheme,
    // and the assertion above would still pass. Compare the resident background against
    // what light actually renders (the same toggle mechanism the persistence test below
    // uses) so a colour regression fails this test on rendered reality, not only on an
    // attribute that no longer reflects it.
    const residentBackground = await page.evaluate(() => getComputedStyle(document.body).backgroundColor)

    await page.getByRole('button', { name: 'Switch to light theme' }).click()
    const lightBackground = await page.evaluate(() => getComputedStyle(document.body).backgroundColor)

    expect(residentBackground).not.toBe(lightBackground)
  })

  test("there is no flash of the wrong scheme — a stored choice is applied before Vite's module script runs", async ({
    page,
  }) => {
    // Seed a stored 'light' preference before navigation, using the literal key
    // public/theme-bootstrap.js reads (THEME_STORAGE_KEY in lib/theme.ts) — addInitScript
    // runs before any of the page's own scripts, so the bootstrap sees this value exactly
    // as it would on a real return visit.
    await page.addInitScript(() => {
      window.localStorage.setItem('homon-theme', 'light')
    })

    // A second init script reading the attribute at DOMContentLoaded, before /src/main.tsx
    // (Vite's injected module script) has mounted React — the automatable version of "no
    // flash", since a human eyeballing a screenshot cannot reliably catch a sub-16ms one.
    //
    // Asserting 'light' here — not merely "defined" — is the point: only the external
    // bootstrap script applies a stored choice this early; React has not mounted yet, so
    // nothing else could have set the attribute by DOMContentLoaded. Delete
    // public/theme-bootstrap.js and this assertion fails (dataset.theme stays unset until
    // useTheme()'s effect runs after mount, well after DOMContentLoaded) — verified by hand
    // by temporarily renaming the file and re-running this spec alone. An assertion that a
    // script ran is only a guard if removing that script breaks it.
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

    expect(themeAtDomContentLoaded).toBe('light')
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
