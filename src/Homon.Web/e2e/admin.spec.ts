import { expect, test } from '@playwright/test'

import { ADMIN_EMAIL } from './admin'
import { expectNoHorizontalOverflow } from './helpers'

/**
 * The admin gate, from both sides. The signed-in half runs with the storage state the setup
 * project saved; the anonymous half throws that state away for one test.
 */
test('an administrator sees the admin home and can reach every section', async ({ page }) => {
  await page.goto('/admin')

  await expect(page.getByRole('heading', { level: 1, name: 'Admin' })).toBeVisible()
  await expect(page.getByText(`Signed in as ${ADMIN_EMAIL}`)).toBeVisible()

  const sections = page.getByRole('navigation', { name: 'Admin sections' })

  for (const name of ['Probes', 'Probe groups', 'Links', 'Pages', 'API keys']) {
    await sections.getByRole('link', { name }).click()
    await expect(page.getByRole('heading', { level: 1, name })).toBeVisible()
    await page.goBack()
  }
})

test('signing out ends the session', async ({ page }) => {
  await page.goto('/admin')
  await page.getByRole('button', { name: 'Sign out' }).click()

  await expect(page.getByRole('heading', { level: 1, name: 'Administrators only' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Sign out' })).toHaveCount(0)
})

test.describe('the HTTP probe form (plan 003)', () => {
  const probeName = `E2E HTTP probe ${Date.now()}`

  // The e2e suite runs the scheduler for real (plan 002's Decision 3) and a freshly created,
  // unpaused probe is immediately due (ProbeScheduler.TickAsync: LastObservedAt is null).
  // This probe is paused through the UI the moment it exists, and its host is under the
  // reserved .invalid TLD (RFC 2606) so even the worst-case race between creation and
  // pausing never reaches a real network — the gate must never make a real outbound HTTP
  // request.
  test.afterEach(async ({ request }) => {
    const probes: { id: string; name: string }[] = await (await request.get('/api/v1/probes')).json()
    const match = probes.find((probe) => probe.name === probeName)

    if (match) {
      await request.delete(`/api/v1/probes/${match.id}`)
    }
  })

  test('creating an HTTP probe through the admin form adds it to the list, then pausing it', async ({
    page,
  }) => {
    await page.goto('/admin/probes')

    await page.getByLabel('Name').fill(probeName)
    await page.getByLabel('Host').fill('api-e2e.invalid')
    await page.getByRole('combobox', { name: 'Kind' }).selectOption('http')
    await page.getByLabel('Path').fill('api/health')

    // Not expectTappable: Phase 0 is deliberately unstyled (CLAUDE.md — "do not add classes
    // to make something look right" before plan 012), and no existing spec calls it for the
    // same reason — native, unstyled checkboxes and <select>s are far under 40px today.
    await expectNoHorizontalOverflow(page)

    await page.getByRole('button', { name: 'Add probe' }).click()

    const row = page.getByRole('listitem').filter({ hasText: probeName })
    await expect(row).toBeVisible()

    await row.getByRole('button', { name: `Pause ${probeName}` }).click()
    await expect(row.getByRole('button', { name: `Unpause ${probeName}` })).toBeVisible()

    await expectNoHorizontalOverflow(page)
  })
})

test.describe('anonymous', () => {
  test.use({ storageState: { cookies: [], origins: [] } })

  test('an anonymous visitor reads the dashboard but not the admin area', async ({ page }) => {
    await page.goto('/')
    await expect(page.getByRole('heading', { level: 1, name: 'Dashboard' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Sign out' })).toHaveCount(0)

    await page.goto('/admin/probes')
    await expect(page.getByRole('heading', { level: 1, name: 'Administrators only' })).toBeVisible()

    // The prompt keeps the address the visitor wanted, and the link leads to the form.
    expect(new URL(page.url()).pathname).toBe('/admin/probes')
    await page.getByRole('link', { name: 'Sign in' }).click()
    await expect(page.getByRole('heading', { level: 1, name: 'Sign in' })).toBeVisible()
  })

  test('a wrong password is refused with the server\'s sentence', async ({ page }) => {
    await page.goto('/admin/sign-in')
    await page.getByLabel('Email').fill(ADMIN_EMAIL)
    await page.getByLabel('Password').fill('not-the-password')
    await page.getByRole('button', { name: 'Sign in' }).click()

    await expect(page.getByRole('alert')).toHaveText('The email address or password is incorrect.')
  })
})
