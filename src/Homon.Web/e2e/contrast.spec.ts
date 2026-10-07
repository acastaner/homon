import AxeBuilder from '@axe-core/playwright'
import { expect, test } from '@playwright/test'

/**
 * `docs/design-brief.md`'s own contrast claim ("Every text pair … measures at least 4.5:1
 * … including the tinted rows") is exactly what axe's `color-contrast` rule checks against
 * rendered, computed styles — hand-verifying oklch pairs at design time (the token table)
 * does not catch a later CSS change quietly breaking a pair the brief promised. Scoped to
 * the dashboard and the weather page, both for speed and because they are the surfaces the
 * brief's contrast claim is about (readers, glancing, sometimes in sunlight) — and the
 * weather page is where the tinted rows and advisory banners put status-coloured text on
 * status-coloured grounds, which is the pair most likely to drift.
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

/**
 * The weather page needs a location before it renders anything but its empty state, and the
 * pairs worth checking — advisory text on a tinted banner, a tinted table row — only exist
 * once FakeWeatherProvider's planted gust run has produced an advisory.
 */
test.describe('colour contrast, weather page', () => {
  test.beforeEach(async ({ request }) => {
    await request.put('/api/v1/weather/settings', {
      data: { latitude: 51.5, longitude: -0.12, place: 'Test location', units: 'metric' },
    })
  })

  test.afterEach(async ({ page, request }) => {
    await request.delete('/api/v1/weather/settings', { data: {} })
    await page.evaluate(() => {
      window.localStorage.removeItem('homon-theme')
      delete document.documentElement.dataset.theme
    })
  })

  test('the weather page has no color-contrast violations, dark scheme', async ({ page }) => {
    await page.goto('/weather')
    await expect(page.getByRole('list', { name: 'Weather warnings' })).toBeVisible()

    const results = await new AxeBuilder({ page }).withRules(['color-contrast']).analyze()

    expect(results.violations).toEqual([])
  })

  test('the weather page has no color-contrast violations, light scheme', async ({ page }) => {
    await page.goto('/weather')
    await page.getByRole('button', { name: 'Switch to light theme' }).click()
    await expect(page.getByRole('list', { name: 'Weather warnings' })).toBeVisible()

    const results = await new AxeBuilder({ page }).withRules(['color-contrast']).analyze()

    expect(results.violations).toEqual([])
  })
})

/**
 * The admin front door and the API keys page (plan 025): the home page puts status chips and
 * tinted rows beside muted descriptions, and the keys page puts a chip, a link and muted metadata
 * in every row — the admin pairs most likely to drift. The other admin routes share the same
 * primitives and are left to the dashboard's coverage of them.
 */
test.describe('colour contrast, admin pages', () => {
  // Same reset as the dashboard block above, for the same reason: it must run after a failure too.
  test.afterEach(async ({ page }) => {
    await page.evaluate(() => {
      window.localStorage.removeItem('homon-theme')
      delete document.documentElement.dataset.theme
    })
  })

  for (const route of [
    { path: '/admin', heading: 'Admin' },
    { path: '/admin/api-keys', heading: 'API keys' },
    { path: '/admin/alerts', heading: 'Alerts' },
  ]) {
    test(`${route.path} has no color-contrast violations, dark scheme`, async ({ page }) => {
      await page.goto(route.path)
      await expect(page.getByRole('heading', { level: 1, name: route.heading })).toBeVisible()

      const results = await new AxeBuilder({ page }).withRules(['color-contrast']).analyze()

      expect(results.violations).toEqual([])
    })

    test(`${route.path} has no color-contrast violations, light scheme`, async ({ page }) => {
      await page.goto(route.path)
      await page.getByRole('button', { name: 'Switch to light theme' }).click()
      await expect(page.getByRole('heading', { level: 1, name: route.heading })).toBeVisible()

      const results = await new AxeBuilder({ page }).withRules(['color-contrast']).analyze()

      expect(results.violations).toEqual([])
    })
  }
})
