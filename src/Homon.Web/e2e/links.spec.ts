import { expect, test } from '@playwright/test'

/**
 * A link seeded through the authenticated API (the storage state this project runs with
 * already carries the administrator's cookie) renders on the dashboard, always opening in a
 * new tab without leaking a referrer.
 *
 * Cleans up the seeded link in `afterEach` — the e2e database is shared across the whole
 * run, matching `dashboard-groups.spec.ts`'s own pattern.
 */
test.describe('dashboard links', () => {
  let linkId: string

  test.beforeEach(async ({ request }) => {
    const response = await request.post('/api/v1/links', {
      data: { title: 'NAS admin', url: 'https://nas.invalid/admin', description: 'Storage box' },
    })
    linkId = (await response.json()).id
  })

  test.afterEach(async ({ request }) => {
    await request.delete(`/api/v1/links/${linkId}`, { data: {} })
  })

  test('a seeded link renders opening in a new tab without a referrer', async ({ page }) => {
    await page.goto('/')

    const link = page.getByRole('link', { name: /NAS admin \(opens in a new tab\)/ })

    await expect(link).toBeVisible()
    await expect(link).toHaveAttribute('target', '_blank')
    await expect(link).toHaveAttribute('rel', 'noopener noreferrer')
    await expect(link).toHaveAttribute('href', 'https://nas.invalid/admin')
  })
})
