import { expect, test } from '@playwright/test'

import { expectNoHorizontalOverflow, expectTappable } from './helpers'

/**
 * The full weather page, reached from the dashboard widget.
 *
 * The API runs with `Weather__Provider=Fake` (playwright.config.ts), so every assertion here
 * is against `FakeWeatherProvider` — today 20/12, seven more days, 24 hourly rows on the wire,
 * and one planted three-hour 94 km/h gust run two hours ahead of the clock, which is the only
 * advisory that fake produces.
 *
 * Seeds and cleans its own location: the e2e database is shared across the whole run, and a
 * location left behind would change what every other spec's dashboard shows.
 */
test.describe('the weather page', () => {
  test.beforeEach(async ({ request }) => {
    await request.put('/api/v1/weather/settings', {
      data: { latitude: 51.5, longitude: -0.12, place: 'Test location', units: 'metric' },
    })
  })

  test.afterEach(async ({ request }) => {
    await request.delete('/api/v1/weather/settings', { data: {} })
  })

  test('the dashboard widget is a link to it', async ({ page }) => {
    await page.goto('/')

    await page
      .getByRole('region', { name: 'Weather · Test location' })
      .getByRole('link', { name: 'Weather for Test location, full forecast' })
      .click()

    await expect(page).toHaveURL(/\/weather$/)
    await expect(page.getByRole('heading', { level: 1, name: 'Weather' })).toBeVisible()
  })

  test("today's summary carries the extremes the widget cannot", async ({ page }) => {
    await page.goto('/weather')

    const today = page.getByRole('region', { name: 'Today' })

    await expect(today.getByText(/20° \/ 12°/)).toBeVisible()
    await expect(today.getByText(/high 20°C, low 12°C/)).toBeVisible()
    await expect(today.getByText('07:10 / 18:53')).toBeVisible()
  })

  test('the hourly table steps six rows at a time up to twenty-four', async ({ page }) => {
    await page.goto('/weather')

    const body = page.getByRole('table', { name: 'Hourly forecast' }).locator('tbody')

    await expect(body.getByRole('row')).toHaveCount(6)

    await page.getByRole('button', { name: /^Show 6 more hours/ }).click()
    await expect(body.getByRole('row')).toHaveCount(12)

    await page.getByRole('button', { name: /^Show 6 more hours/ }).click()
    await page.getByRole('button', { name: /^Show 6 more hours/ }).click()
    await expect(body.getByRole('row')).toHaveCount(24)

    await expect(page.getByRole('button', { name: /more hour/ })).toHaveCount(0)
  })

  test('the daily table carries the next seven days', async ({ page }) => {
    await page.goto('/weather')

    await expect(
      page.getByRole('table', { name: 'Daily forecast' }).locator('tbody').getByRole('row'),
    ).toHaveCount(7)
  })

  test('the planted gust run becomes one severe advisory banner', async ({ page }) => {
    await page.goto('/weather')

    const warnings = page.getByRole('list', { name: 'Weather warnings' })
    const items = warnings.getByRole('listitem')

    await expect(items).toHaveCount(1)
    await expect(items.first().getByText('Severe')).toBeVisible()
    await expect(items.first().getByText(/Gale-force wind/)).toBeVisible()
    await expect(items.first().getByText(/Gusts to 94 km\/h/)).toBeVisible()
  })

  test('it fits its viewport and its one control is tappable', async ({ page }) => {
    await page.goto('/weather')

    await expect(page.getByRole('heading', { level: 1, name: 'Weather' })).toBeVisible()

    await expectNoHorizontalOverflow(page)
    await expectTappable(page, 'main button')
  })
})

/**
 * With no location configured the page must say where an administrator fixes that, rather
 * than rendering an empty shell — the same empty state the dashboard widget shows.
 */
test.describe('the weather page, unconfigured', () => {
  test.beforeEach(async ({ request }) => {
    await request.delete('/api/v1/weather/settings', { data: {} })
  })

  test('it points at the admin page', async ({ page }) => {
    await page.goto('/weather')

    await expect(page.getByText(/No weather location yet/)).toBeVisible()
    await expect(page.getByRole('link', { name: 'Weather' })).toHaveAttribute('href', '/admin/weather')
  })
})
