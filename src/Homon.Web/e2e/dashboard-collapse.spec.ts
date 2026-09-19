import { expect, test } from '@playwright/test'

import { expectNoHorizontalOverflow, expectTappable } from './helpers'

/**
 * Collapsing a dashboard section and having the choice survive a reload (plan 018) — seeded
 * through the authenticated API, and always **paused**: the scheduler is live in e2e, so an
 * unpaused probe would mean real ICMP traffic and a nondeterministic status word, and this spec
 * asserts on the collapsed summary text derived from that word.
 *
 * Named "Collapse" / "Collapse device" — no other spec uses those names — so the section and
 * its row can be addressed regardless of what else the shared e2e database holds at the time.
 *
 * Clears `homon-collapsed-sections` in `afterEach` too, following `e2e/contrast.spec.ts`'s reset
 * of the theme key — a value left behind would change what other specs see on first paint.
 */
test.describe('collapsible dashboard sections', () => {
  let collapseGroupId: string
  let collapseProbeId: string

  test.beforeEach(async ({ request }) => {
    const group = await request.post('/api/v1/probe-groups', { data: { name: 'Collapse' } })
    collapseGroupId = (await group.json()).id

    const probe = await request.post('/api/v1/probes', {
      data: {
        name: 'Collapse device',
        host: 'collapse.invalid',
        kind: 'ping',
        pollIntervalSeconds: 3600,
        failureThreshold: 2,
        groupIds: [collapseGroupId],
      },
    })
    collapseProbeId = (await probe.json()).id
    await request.put(`/api/v1/probes/${collapseProbeId}/pause`, { data: { isPaused: true } })
  })

  test.afterEach(async ({ request, page }) => {
    await request.delete(`/api/v1/probes/${collapseProbeId}`)
    await request.delete(`/api/v1/probe-groups/${collapseGroupId}`)
    await page.evaluate(() => {
      window.localStorage.removeItem('homon-collapsed-sections')
    })
  })

  test('collapsing folds the section away and keeps its heading', async ({ page }) => {
    await page.goto('/')

    await expect(page.getByRole('row', { name: /Collapse device/ })).toBeVisible()

    await page.getByRole('button', { name: 'Collapse' }).click()

    await expect(page.getByRole('row', { name: /Collapse device/ })).toBeHidden()
    await expect(page.getByRole('heading', { level: 2, name: 'Collapse' })).toBeVisible()
    // Scoped to the region: an unscoped getByText matches a substring by default and the stat
    // strip at the top of the page also reads "… · 1 paused · …" whenever exactly one probe is
    // paused, which is precisely what this spec seeds — a bare locator would resolve to two
    // elements and die on strict mode.
    await expect(page.getByRole('region', { name: 'Collapse' }).getByText('1 paused')).toBeVisible()
  })

  test('the choice survives a reload', async ({ page }) => {
    await page.goto('/')

    await page.getByRole('button', { name: 'Collapse' }).click()
    await expect(page.getByRole('row', { name: /Collapse device/ })).toBeHidden()

    await page.reload()

    await expect(page.getByRole('row', { name: /Collapse device/ })).toBeHidden()
    await expect(page.getByRole('button', { name: 'Collapse' })).toHaveAttribute('aria-expanded', 'false')

    const stored = await page.evaluate(() => window.localStorage.getItem('homon-collapsed-sections'))
    expect(stored).toContain(collapseGroupId)
  })

  test('expanding again removes the key', async ({ page }) => {
    await page.goto('/')

    await page.getByRole('button', { name: 'Collapse' }).click()
    await page.getByRole('button', { name: 'Collapse' }).click()

    await page.reload()

    await expect(page.getByRole('row', { name: /Collapse device/ })).toBeVisible()

    const stored = await page.evaluate(() => window.localStorage.getItem('homon-collapsed-sections'))
    expect(stored).toBeNull()
  })

  test('a collapsed dashboard still fits and is still tappable', async ({ page }) => {
    await page.goto('/')

    await page.getByRole('button', { name: 'Collapse' }).click()

    await expectNoHorizontalOverflow(page)
    await expectTappable(page, 'button')
  })
})
