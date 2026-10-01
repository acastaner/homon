import { expect, test } from '@playwright/test'

import { expectNoHorizontalOverflow, expectTappable } from './helpers'

/**
 * Arranging the dashboard's sections, and the empty ungrouped section staying away (plan 019) —
 * seeded through the authenticated API, and always **paused**: the scheduler is live in e2e, so an
 * unpaused probe would mean real ICMP traffic and a nondeterministic status word.
 *
 * Named "Shuffle" / "Shuffle device", which no other spec uses — and deliberately NOT "Arrange",
 * which would collide with this page's own Arrange button under `getByRole('button', { name })`.
 *
 * `afterEach` removes the seeded group AND `homon-section-order`, and both matter more here than
 * they did for collapse: this file sorts before `layout.spec.ts`, which asserts a bare "Services"
 * heading on `/`, and `dashboard-groups.spec.ts` asserts an exact heading *sequence* — a leaked
 * group or a leaked order would fail one of those somewhere else in the run, where the cause is
 * not obvious from the failure.
 */
test.describe('arranging dashboard sections', () => {
  let shuffleGroupId: string
  let shuffleProbeId: string

  test.beforeEach(async ({ request }) => {
    const group = await request.post('/api/v1/probe-groups', { data: { name: 'Shuffle' } })
    shuffleGroupId = (await group.json()).id

    const probe = await request.post('/api/v1/probes', {
      data: {
        name: 'Shuffle device',
        host: 'shuffle.invalid',
        kind: 'ping',
        pollIntervalSeconds: 3600,
        failureThreshold: 2,
        groupIds: [shuffleGroupId],
      },
    })
    shuffleProbeId = (await probe.json()).id
    await request.put(`/api/v1/probes/${shuffleProbeId}/pause`, { data: { isPaused: true } })
  })

  test.afterEach(async ({ request, page }) => {
    await request.delete(`/api/v1/probes/${shuffleProbeId}`)
    await request.delete(`/api/v1/probe-groups/${shuffleGroupId}`)
    await page.evaluate(() => {
      window.localStorage.removeItem('homon-section-order')
    })
  })

  test('no ungrouped section when every probe belongs to a group', async ({ page }) => {
    await page.goto('/')

    // The only end-to-end cover for plan 019's guard: dashboard-groups.spec.ts cannot provide it,
    // because its beforeEach always seeds an ungrouped probe.
    await expect(page.getByRole('heading', { level: 2 })).toHaveText(['Shuffle', 'Links', 'Pages', 'Weather'])
    await expect(page.getByRole('heading', { level: 2, name: 'Other' })).toHaveCount(0)
  })

  test('arrange mode reveals the move controls and leaves the headings alone', async ({ page }) => {
    await page.goto('/')

    await expect(page.getByRole('button', { name: 'Move Shuffle down' })).toHaveCount(0)

    await page.getByRole('button', { name: 'Arrange' }).click()

    await expect(page.getByRole('button', { name: 'Move Weather up' })).toBeVisible()
    // §3.22's markup contract: the move buttons are siblings of the <h2>, so the exact heading text
    // is unchanged in arrange mode.
    await expect(page.getByRole('heading', { level: 2 })).toHaveText(['Shuffle', 'Links', 'Pages', 'Weather'])

    await page.getByRole('button', { name: 'Done' }).click()

    await expect(page.getByRole('button', { name: 'Move Weather up' })).toHaveCount(0)
  })

  test('moving Weather to the top survives a reload', async ({ page }) => {
    await page.goto('/')

    await page.getByRole('button', { name: 'Arrange' }).click()

    // Three presses from the bottom of four sections — the maintainer's own example of what this
    // feature is for. The button disables itself once Weather is first, which is the other half of
    // the assertion below.
    for (let press = 0; press < 3; press += 1) {
      await page.getByRole('button', { name: 'Move Weather up' }).click()
    }

    await expect(page.getByRole('heading', { level: 2 })).toHaveText(['Weather', 'Shuffle', 'Links', 'Pages'])
    await expect(page.getByRole('button', { name: 'Move Weather up' })).toBeDisabled()

    await page.reload()

    await expect(page.getByRole('heading', { level: 2 })).toHaveText(['Weather', 'Shuffle', 'Links', 'Pages'])

    const stored = await page.evaluate(() => window.localStorage.getItem('homon-section-order'))
    expect(stored).toContain('weather')
    expect(stored).toContain(shuffleGroupId)
  })

  test('arrange mode itself does not survive a reload', async ({ page }) => {
    await page.goto('/')

    await page.getByRole('button', { name: 'Arrange' }).click()
    await expect(page.getByRole('button', { name: 'Move Weather up' })).toBeVisible()

    await page.reload()

    await expect(page.getByRole('button', { name: 'Arrange' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Move Weather up' })).toHaveCount(0)
  })

  test('Reset order restores the natural order and removes the key', async ({ page }) => {
    await page.goto('/')

    await page.getByRole('button', { name: 'Arrange' }).click()
    await page.getByRole('button', { name: 'Move Weather up' }).click()
    await expect(page.getByRole('heading', { level: 2 })).toHaveText(['Shuffle', 'Links', 'Weather', 'Pages'])

    await page.getByRole('button', { name: 'Reset order' }).click()

    await expect(page.getByRole('heading', { level: 2 })).toHaveText(['Shuffle', 'Links', 'Pages', 'Weather'])

    const stored = await page.evaluate(() => window.localStorage.getItem('homon-section-order'))
    expect(stored).toBeNull()
  })

  test('a dashboard in arrange mode still fits and is still tappable', async ({ page }) => {
    await page.goto('/')

    await page.getByRole('button', { name: 'Arrange' }).click()

    // The only place the move buttons meet the 40px floor and the Pixel 7 width — they are on
    // screen in this mode and nowhere else, so e2e/refresh.spec.ts cannot reach them. Note
    // expectTappable measures DISABLED buttons too (it filters on width, not on disabled), which
    // is what covers the greyed-out first-up and last-down pair.
    await expectNoHorizontalOverflow(page)
    await expectTappable(page, 'button')
  })
})
