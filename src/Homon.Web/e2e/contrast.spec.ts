import AxeBuilder from '@axe-core/playwright'
import { expect, test } from '@playwright/test'

/**
 * `docs/design-brief.md`'s own contrast claim ("Every text pair … measures at least 4.5:1
 * … including the tinted rows") is exactly what axe's `color-contrast` rule checks against
 * rendered, computed styles — hand-verifying oklch pairs at design time (the token table)
 * does not catch a later CSS change quietly breaking a pair the brief promised. Scoped to
 * the dashboard, both for speed and because it is the surface the brief's contrast claim is
 * about (readers, glancing, sometimes in sunlight).
 */
test.describe('colour contrast', () => {
  // Unconditional, not a step inside the light-scheme test itself: if that test's own
  // expectation fails, execution never reaches a cleanup click at the end of the test body,
  // and the browser is left on light for whatever runs next against the same storage
  // state. afterEach still runs after a failed test, so the reset happens either way.
  test.afterEach(async ({ page }) => {
    await page.evaluate(() => {
      window.localStorage.removeItem('homon-theme')
      delete document.documentElement.dataset.theme
    })
  })

  test('the dashboard has no color-contrast violations, dark scheme', async ({ page }) => {
    await page.goto('/')

    const results = await new AxeBuilder({ page }).withRules(['color-contrast']).analyze()

    expect(results.violations).toEqual([])
  })

  test('the dashboard has no color-contrast violations, light scheme', async ({ page }) => {
    await page.goto('/')
    // The same mechanism e2e/theme.spec.ts uses to switch schemes — a real click on the
    // banner's toggle, not a direct localStorage/attribute poke.
    await page.getByRole('button', { name: 'Switch to light theme' }).click()

    const results = await new AxeBuilder({ page }).withRules(['color-contrast']).analyze()

    expect(results.violations).toEqual([])
  })
})
