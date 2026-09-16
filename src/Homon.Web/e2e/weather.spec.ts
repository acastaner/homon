import { expect, test } from '@playwright/test'

import { expectNoHorizontalOverflow } from './helpers'

/**
 * The API runs with `Weather__Provider=Fake` (playwright.config.ts), so every assertion
 * here is against `FakeWeatherProvider`'s fixed forecast — Clear, 18 °C — never the live
 * network.
 *
 * Cleans up the seeded location in `afterEach` — the e2e database is shared across the whole
 * run, matching `links.spec.ts`'s own pattern; a location left behind would change what
 * other specs (and their own weather section) see on the dashboard.
 */
test.describe('dashboard weather, configured', () => {
  test.beforeEach(async ({ request }) => {
    await request.put('/api/v1/weather/settings', {
      data: { latitude: 51.5, longitude: -0.12, place: 'Test location', units: 'metric' },
    })
  })

  test.afterEach(async ({ request }) => {
    await request.delete('/api/v1/weather/settings', { data: {} })
  })

  test('a seeded location renders the fixed forecast', async ({ page }) => {
    await page.goto('/')

    const weather = page.getByRole('region', { name: 'Weather · Test location' })

    await expect(weather.getByText(/18°C/)).toBeVisible()
    await expect(weather.getByRole('listitem')).toHaveCount(3)
    await expect(weather.getByRole('link', { name: 'Open-Meteo.com' })).toHaveAttribute(
      'href',
      'https://open-meteo.com/',
    )

    // Not expectTappable: Phase 0 is deliberately unstyled (CLAUDE.md — "do not add classes
    // to make something look right" before plan 012), and no existing spec calls it for the
    // same reason — the attribution link is native, unstyled text far under 40px today.
    await expectNoHorizontalOverflow(page)
  })
})

/**
 * Cleans up in `afterEach` too — a failed run partway through this test must not leave the
 * round-trip location behind for the next spec.
 */
test.describe('admin weather form', () => {
  test.afterEach(async ({ request }) => {
    await request.delete('/api/v1/weather/settings', { data: {} })
  })

  test('fill and submit the form, then reload and the values round-trip', async ({ page }) => {
    await page.goto('/admin/weather')

    await page.getByLabel('Latitude').fill('51.5')
    await page.getByLabel('Longitude').fill('-0.12')
    await page.getByLabel('Place (optional)').fill('Round-trip location')
    await page.getByLabel('Units').selectOption('metric')

    await expectNoHorizontalOverflow(page)

    await Promise.all([
      page.waitForResponse(
        (response) => response.url().includes('/api/v1/weather/settings') && response.request().method() === 'PUT',
      ),
      page.getByRole('button', { name: 'Save location' }).click(),
    ])

    await expect(page.getByRole('button', { name: 'Remove location' })).toBeVisible()

    await page.reload()

    await expect(page.getByLabel('Latitude')).toHaveValue('51.5')
    await expect(page.getByLabel('Longitude')).toHaveValue('-0.12')
    await expect(page.getByLabel('Place (optional)')).toHaveValue('Round-trip location')
  })
})

/**
 * Deletes settings first — the e2e database is shared, and another spec may have left a
 * location behind (or this suite may simply run after `dashboard weather, configured` above,
 * whose own `afterEach` already cleans up, but a fresh delete here makes the state this test
 * needs explicit rather than assumed).
 */
test.describe('dashboard weather, unconfigured', () => {
  test.beforeEach(async ({ request }) => {
    await request.delete('/api/v1/weather/settings', { data: {} })
  })

  test('shows the empty state and a link to the admin form', async ({ page }) => {
    await page.goto('/')

    const weather = page.getByRole('region', { name: 'Weather' })

    await expect(weather.getByText(/No weather location yet/)).toBeVisible()
    await expect(weather.getByRole('link', { name: 'Weather' })).toHaveAttribute('href', '/admin/weather')
  })
})
