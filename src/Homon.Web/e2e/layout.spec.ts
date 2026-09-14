import { expect, test } from '@playwright/test'

import { ADMIN_ROUTES, READER_ROUTES, expectNoHorizontalOverflow } from './helpers'

/**
 * Every page fits the width it is given. Runs in both projects: the mobile one is where a
 * dashboard read on phones would break; the desktop one is there because a breakpoint fix
 * is the kind of change that fixes one width by quietly breaking the other.
 *
 * Unstyled in phase 0, so this passes trivially today. It exists so the design pass has a
 * net under it from its first commit.
 */
test.describe('every page fits its viewport', () => {
  for (const route of [...READER_ROUTES, ...ADMIN_ROUTES]) {
    test(`${route.path} — ${route.name}`, async ({ page }) => {
      await page.goto(route.path)

      // The banner is on every page and is the last thing to settle, so it doubles as the
      // signal that the SPA has rendered rather than that the HTML shell has arrived.
      await expect(page.getByRole('banner')).toBeVisible()
      await expect(page.getByRole('main')).toBeVisible()

      await expectNoHorizontalOverflow(page)
    })
  }
})

test('the dashboard names its sections', async ({ page }) => {
  await page.goto('/')

  await expect(page.getByRole('heading', { level: 1, name: 'Dashboard' })).toBeVisible()
  await expect(page.getByRole('heading', { level: 2, name: 'Services' })).toBeVisible()
  await expect(page.getByRole('heading', { level: 2, name: 'Links' })).toBeVisible()
})
