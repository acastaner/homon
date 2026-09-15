import { expect, test } from '@playwright/test'

import { ADMIN_EMAIL } from './admin'

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
