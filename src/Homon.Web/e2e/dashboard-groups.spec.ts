import { expect, test } from '@playwright/test'

import { expectNoHorizontalOverflow } from './helpers'

/**
 * Groups the dashboard by `ProbeGroup` and asserts the stat strip and section order — seeded
 * through the authenticated API (the storage state this project runs with already carries the
 * administrator's cookie), never through the UI, and always **paused**: the scheduler is live
 * in e2e (plan 002's Decision 3), so an unpaused probe here would mean real ICMP traffic and a
 * nondeterministic status word.
 *
 * Cleans up every seeded probe and group in `afterEach` — the e2e database is shared across
 * the whole run, and `layout.spec.ts` expects a bare "Services" heading on `/` when nothing
 * else has left groups behind.
 */
test.describe('dashboard grouped by ProbeGroup', () => {
  let hostsGroupId: string
  let storageGroupId: string
  let sharedProbeId: string
  let ungroupedProbeId: string

  test.beforeEach(async ({ request }) => {
    const hosts = await request.post('/api/v1/probe-groups', { data: { name: 'Hosts' } })
    hostsGroupId = (await hosts.json()).id

    const storage = await request.post('/api/v1/probe-groups', { data: { name: 'Storage' } })
    storageGroupId = (await storage.json()).id

    const shared = await request.post('/api/v1/probes', {
      data: {
        name: 'Shared device',
        host: 'shared.invalid',
        kind: 'ping',
        pollIntervalSeconds: 3600,
        failureThreshold: 2,
        groupIds: [hostsGroupId, storageGroupId],
      },
    })
    sharedProbeId = (await shared.json()).id
    await request.put(`/api/v1/probes/${sharedProbeId}/pause`, { data: { isPaused: true } })

    const ungrouped = await request.post('/api/v1/probes', {
      data: {
        name: 'Ungrouped device',
        host: 'ungrouped.invalid',
        kind: 'ping',
        pollIntervalSeconds: 3600,
        failureThreshold: 2,
        groupIds: [],
      },
    })
    ungroupedProbeId = (await ungrouped.json()).id
    await request.put(`/api/v1/probes/${ungroupedProbeId}/pause`, { data: { isPaused: true } })
  })

  test.afterEach(async ({ request }) => {
    await request.delete(`/api/v1/probes/${sharedProbeId}`)
    await request.delete(`/api/v1/probes/${ungroupedProbeId}`)
    await request.delete(`/api/v1/probe-groups/${hostsGroupId}`)
    await request.delete(`/api/v1/probe-groups/${storageGroupId}`)
  })

  test('sections appear in group order, then "Other" — not "Services"', async ({ page }) => {
    await page.goto('/')

    const headings = page.getByRole('heading', { level: 2 })

    // The Services section is split into "Hosts", "Storage" and "Other" (a group exists, so
    // the ungrouped section is not called "Services" — plan 002's Decision 7); "Links" is the
    // untouched placeholder section plan 006 fills in; "Pages" is plan 007's section, present
    // because auth.setup.ts seeds one published page ("welcome") for the whole e2e run.
    await expect(headings).toHaveText(['Hosts', 'Storage', 'Other', 'Links', 'Pages'])

    // The shared probe belongs to both groups, so it appears once in each of their tables.
    await expect(page.getByRole('row', { name: /Shared device/ })).toHaveCount(2)
    await expect(page.getByRole('row', { name: /Ungrouped device/ })).toHaveCount(1)

    await expectNoHorizontalOverflow(page)
  })
})
