import { expect, test } from '@playwright/test'

import { expectNoHorizontalOverflow, expectNoOverlap } from './helpers'

/**
 * Creates a page as a draft through the admin editor, confirms it stays invisible — absent
 * from the dashboard, 404 to an anonymous reader — until published, then confirms an
 * anonymous reader sees it once it is.
 *
 * Cleans up the created page in `afterEach` — the e2e database is shared across the whole
 * run, matching `links.spec.ts`'s own pattern. The slug carries `Date.now()` so a run that
 * fails partway through never collides with the next one.
 */
test.describe('admin page editor', () => {
  const title = `E2E page ${Date.now()}`
  const slug = title
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')

  test.afterEach(async ({ request }) => {
    const pages: { id: string; slug: string }[] = await (await request.get('/api/v1/admin/pages')).json()
    const match = pages.find((p) => p.slug === slug)

    if (match) {
      await request.delete(`/api/v1/pages/${match.id}`)
    }
  })

  test('a draft is invisible everywhere until published, then visible anonymously', async ({ page, browser, baseURL }) => {
    await page.goto('/admin/pages/new')

    await page.getByLabel('Title').fill(title)
    // The slug auto-suggests from the title, unedited — this is what plans/007's Step 7 asks
    // the editor to do.
    await expect(page.getByLabel('Slug')).toHaveValue(slug)

    const body = page.getByRole('textbox', { name: 'Body' })
    await body.click()
    await body.pressSequentially('Some content.')
    await page.getByRole('button', { name: 'Bold' }).click()
    await body.pressSequentially(' Bold content.')

    await expectNoHorizontalOverflow(page)

    await page.getByRole('button', { name: 'Create page' }).click()

    await expect(page.getByRole('heading', { level: 1, name: `Edit ${title}` })).toBeVisible()

    // Not published: absent from the dashboard's Pages section for the very administrator
    // who just created it — GET /pages never lists a draft, regardless of caller.
    await page.goto('/')
    await expect(page.getByRole('link', { name: title })).toHaveCount(0)

    // A separate, cookie-less browser context — the anonymous half of this test — rather
    // than clearing the signed-in page's cookies, which would also end the admin session
    // this test still needs below. Playwright Test merges the project's `use` options
    // (baseURL, and — critically — `storageState: ADMIN_STATE`) into every `browser.newContext()`
    // call by default, so both are passed explicitly here: baseURL because a relative
    // `goto('/pages/...')` needs one, and an empty storageState because otherwise this
    // "anonymous" context would silently inherit the administrator's signed-in cookie and
    // this whole test would pass for the wrong reason (an admin preview, not a public read).
    const anonymousContext = await browser.newContext({ baseURL, storageState: { cookies: [], origins: [] } })
    const anonymousPage = await anonymousContext.newPage()

    await anonymousPage.goto(`/pages/${slug}`)
    await expect(anonymousPage.getByRole('heading', { level: 1, name: 'Page not found' })).toBeVisible()

    // Publish it.
    await page.goto('/admin/pages')
    const row = page.getByRole('listitem').filter({ hasText: title })
    await row.getByRole('link', { name: `Edit ${title}` }).click()
    await page.getByLabel('Published').check()

    // Waits for the PUT to actually land, not just for the click to register — a bare click
    // followed immediately by `goto('/')` races the mutation against the navigation, and on
    // a slower (mobile-emulated) run the navigation can win, leaving the save silently lost.
    await Promise.all([
      page.waitForResponse((response) => response.url().includes('/api/v1/pages/') && response.request().method() === 'PUT'),
      page.getByRole('button', { name: 'Save changes' }).click(),
    ])

    await page.goto('/')
    await expect(page.getByRole('link', { name: title })).toBeVisible()

    await anonymousPage.goto(`/pages/${slug}`)
    await expect(anonymousPage.getByRole('heading', { level: 1, name: title })).toBeVisible()
    await expect(anonymousPage.getByText('Some content.', { exact: false })).toBeVisible()
    await expect(anonymousPage.getByText('Bold content.', { exact: false })).toBeVisible()

    await expectNoHorizontalOverflow(anonymousPage)
    // Direct children of `main` only — `main *` would also catch the body's own <strong> mark
    // nested inside its wrapping <p>, which trivially "overlaps" its parent's box and is not
    // the layout bug this check exists to catch.
    await expectNoOverlap(anonymousPage, 'main > *')

    await anonymousContext.close()
  })
})
