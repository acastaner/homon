import { expect, test } from '@playwright/test'

import { expectNoHorizontalOverflow, expectTappable } from './helpers'

/**
 * The message gateway end to end (plan 021): register a reporter through the UI, report with the
 * key it reveals, watch it with a probe, and see the dashboard say so.
 *
 * The scheduler runs for real in this suite, so the probe's state is produced by the same tick
 * loop production uses — which is the point. Its host is a reporter identifier rather than a name
 * on the network, so nothing here reaches outside the process whatever the timing.
 */
test.describe('the message gateway', () => {
  const reporterName = `E2E reporter ${Date.now()}`
  const probeName = `E2E watcher ${Date.now()}`

  test.afterEach(async ({ request }) => {
    const probes: { id: string; name: string }[] = await (await request.get('/api/v1/probes')).json()
    const probe = probes.find((candidate) => candidate.name === probeName)

    if (probe) {
      await request.delete(`/api/v1/probes/${probe.id}`, { data: {} })
    }

    const reporters: { id: string; name: string }[] = await (await request.get('/api/v1/reporters')).json()
    const reporter = reporters.find((candidate) => candidate.name === reporterName)

    if (reporter) {
      await request.delete(`/api/v1/reporters/${reporter.id}`, { data: {} })
    }
  })

  test('a reporter reports, a probe watches it, and the dashboard shows the outcome', async ({
    page,
    request,
  }) => {
    test.setTimeout(60_000)

    await page.goto('/admin/reporters')

    await page.getByLabel('Name').fill(reporterName)
    await page.getByLabel('Description').fill('An end-to-end reporter')
    await page.getByLabel('Message visibility').selectOption('reader')
    await expectNoHorizontalOverflow(page)
    await page.getByRole('button', { name: 'Add reporter' }).click()

    // Revealed once, in an alert, with the sentence that says so.
    const reveal = page.getByRole('alert')
    await expect(reveal).toContainText('This key will not be shown again. Store it now.')

    const token = await page.getByLabel(`API key for ${reporterName}`).inputValue()
    expect(token).toMatch(/^hmn_/)

    const row = page.getByRole('listitem').filter({ hasText: reporterName })
    await expect(row).toBeVisible()
    await expect(row).toContainText('No report received yet')

    // Read from the API rather than scraped out of the row: the row's text runs the name,
    // the identifier and the key handle together, and a reporter named after Date.now() makes a
    // "sixteen Crockford characters" pattern match the wrong sixteen.
    const reporters: { name: string; identifier: string }[] = await (
      await request.get('/api/v1/reporters')
    ).json()
    const identifier = reporters.find((candidate) => candidate.name === reporterName)?.identifier
    expect(identifier).toBeTruthy()

    // The reporter's own half of the contract: its key, its own JSON, nothing of the browser's.
    const reported = await request.post('/api/v1/messages', {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        identifier,
        name: 'e2e restic wrapper',
        status: 'success',
        category: 'backup',
        recurrence: 'PT25H',
        message: 'NAS array healthy, 0 errors',
      },
    })

    expect(reported.status(), await reported.text()).toBe(201)
    expect((await reported.json()).nextExpectedAt).toBeTruthy()

    // Leaving the page and coming back: the key is gone, as promised.
    await page.reload()
    await expect(page.getByLabel(`API key for ${reporterName}`)).toHaveCount(0)

    await page.goto('/admin/probes')
    await page.getByLabel('Name').fill(probeName)
    await page.getByRole('combobox', { name: 'Kind' }).selectOption('message')
    await page.getByRole('combobox', { name: 'Reporter' }).selectOption(identifier!)
    await page.getByLabel('Poll interval (seconds)').fill('900')
    await page.getByRole('button', { name: 'Add probe' }).click()

    await expect(page.getByRole('listitem').filter({ hasText: probeName })).toBeVisible()

    // The real scheduler decides this, on its own tick, from the message that was pushed.
    await expect
      .poll(
        async () => {
          const status = await (await request.get('/api/v1/status')).json()
          const probe = status.probes.find((candidate: { name: string }) => candidate.name === probeName)

          return probe?.state ?? 'absent'
        },
        { timeout: 30_000, intervals: [500, 1_000, 2_000] },
      )
      .toBe('up')

    await page.goto('/')
    const dashboardRow = page.getByRole('row').filter({ hasText: probeName })
    await expect(dashboardRow).toContainText('Succeeded')
    // Reader-visible, because this reporter was created that way.
    await expect(dashboardRow).toContainText('NAS array healthy, 0 errors')

    await expectNoHorizontalOverflow(page)
  })

  test('a reporter in use cannot be deleted, and its page is tappable', async ({ page, request }) => {
    const created = await request.post('/api/v1/reporters', {
      data: { name: reporterName, bodyVisibility: 'administrator' },
    })
    expect(created.status()).toBe(201)
    const identifier = (await created.json()).reporter.identifier

    const probe = await request.post('/api/v1/probes', {
      data: { name: probeName, host: identifier, kind: 'message', pollIntervalSeconds: 900, groupIds: [] },
    })
    expect(probe.status()).toBe(201)

    await page.goto('/admin/reporters')
    await expectTappable(page, 'button')

    const row = page.getByRole('listitem').filter({ hasText: reporterName })
    await expect(row).toContainText('Watched by a probe')

    await row.getByRole('button', { name: `Delete ${reporterName}` }).click()
    await row.getByRole('button', { name: `Confirm delete ${reporterName}` }).click()

    // The refusal names the probe, so the administrator knows what to do about it.
    await expect(page.getByRole('alert')).toContainText(probeName)
  })
})
