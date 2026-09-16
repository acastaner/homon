import { expect, test as setup } from '@playwright/test'

import { ADMIN_STATE } from '../playwright.config'
import { ADMIN_EMAIL, ADMIN_PASSWORD } from './admin'

/**
 * Signs in as the administrator through the page — not through the API context's cookie
 * jar — so the saved state is what a browser would hold, and the sign-in form is exercised
 * once per run.
 */
setup('the administrator is signed in', async ({ page }) => {
  await page.goto('/admin/sign-in')
  await page.getByLabel('Email').fill(ADMIN_EMAIL)
  await page.getByLabel('Password').fill(ADMIN_PASSWORD)
  await page.getByRole('button', { name: 'Sign in' }).click()

  // Assert on the thing that only a signed-in session produces. A URL assertion is the
  // obvious check and a weak one: the sign-in page's own URL contains `/admin`.
  await expect(
    page.getByRole('button', { name: 'Sign out' }),
    'Signing in through the form did not produce a session. The hash in playwright.config.ts ' +
      'must be of the password in e2e/admin.ts.',
  ).toBeVisible()

  await page.context().storageState({ path: ADMIN_STATE })

  // Seeds `/pages/welcome`, the page READER_ROUTES (helpers.ts) walks — idempotent, because
  // the CI database is shared across every spec in a run and this setup project itself only
  // runs once, but a developer re-running `npx playwright test auth.setup` by hand against a
  // database `--keep` left up must not fail on a duplicate slug.
  const existing = await page.request.get('/api/v1/admin/pages')
  const already = (await existing.json()).some((p: { slug: string }) => p.slug === 'welcome')

  if (!already) {
    await page.request.post('/api/v1/pages', {
      data: {
        slug: 'welcome',
        title: 'Welcome',
        bodyHtml: '<p>This is a seeded page for the end-to-end suite.</p>',
        isPublished: true,
      },
    })
  }
})
