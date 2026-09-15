import { describe, expect, it } from 'vitest'

import { dashboardSections, formatCheckedAt, type Status, type StatusProbe } from '@/lib/status'

function makeProbe(overrides: Partial<StatusProbe> & { id: string }): StatusProbe {
  return {
    name: overrides.id,
    kind: 'ping',
    state: 'up',
    detail: null,
    lastCheckedAt: null,
    uptimePercent: null,
    sparkline: [],
    ...overrides,
  }
}

function makeStatus(overrides: Partial<Status>): Status {
  return {
    totals: { up: 0, unstable: 0, down: 0, unknown: 0, paused: 0, uptimePercent: null },
    probes: [],
    groups: [],
    ungroupedProbeIds: [],
    generatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  }
}

describe('dashboardSections', () => {
  it('returns one "Services" section when there are no groups', () => {
    const status = makeStatus({
      probes: [makeProbe({ id: 'a' })],
      ungroupedProbeIds: ['a'],
    })

    const sections = dashboardSections(status, { phone: false })

    expect(sections).toHaveLength(1)
    expect(sections[0]).toMatchObject({ id: 'ungrouped', headingId: 'services-heading', heading: 'Services' })
  })

  it('labels the ungrouped section "Other" once a group exists', () => {
    const status = makeStatus({
      probes: [makeProbe({ id: 'a' }), makeProbe({ id: 'b' })],
      groups: [{ id: 'g1', name: 'Hosts', probeIds: ['a'] }],
      ungroupedProbeIds: ['b'],
    })

    const sections = dashboardSections(status, { phone: false })

    expect(sections.map((s) => s.heading)).toEqual(['Hosts', 'Other'])
  })

  it('omits the ungrouped section label entirely when there is nothing ungrouped, but still renders it', () => {
    const status = makeStatus({
      probes: [makeProbe({ id: 'a' })],
      groups: [{ id: 'g1', name: 'Hosts', probeIds: ['a'] }],
      ungroupedProbeIds: [],
    })

    const sections = dashboardSections(status, { phone: false })

    expect(sections).toHaveLength(2)
    expect(sections[1]).toMatchObject({ heading: 'Other', probes: [] })
  })

  it('a group heading id is derived from the group id, never the name', () => {
    const status = makeStatus({
      probes: [makeProbe({ id: 'a' })],
      groups: [{ id: 'abc-123', name: 'Links', probeIds: ['a'] }],
    })

    const sections = dashboardSections(status, { phone: false })

    expect(sections[0].headingId).toBe('probe-group-abc-123-heading')
  })

  it('a shared probe appears in both group sections', () => {
    const status = makeStatus({
      probes: [makeProbe({ id: 'shared' })],
      groups: [
        { id: 'g1', name: 'Hosts', probeIds: ['shared'] },
        { id: 'g2', name: 'Storage', probeIds: ['shared'] },
      ],
    })

    const sections = dashboardSections(status, { phone: false })

    expect(sections[0].probes.map((p) => p.id)).toEqual(['shared'])
    expect(sections[1].probes.map((p) => p.id)).toEqual(['shared'])
  })

  it('keeps section order on desktop and sorts by severity on phone, stably', () => {
    const status = makeStatus({
      probes: [
        makeProbe({ id: 'up-1', state: 'up' }),
        makeProbe({ id: 'down-1', state: 'down' }),
        makeProbe({ id: 'unstable-1', state: 'unstable' }),
        makeProbe({ id: 'up-2', state: 'up' }),
        makeProbe({ id: 'paused-1', state: 'paused' }),
      ],
      ungroupedProbeIds: ['up-1', 'down-1', 'unstable-1', 'up-2', 'paused-1'],
    })

    const desktop = dashboardSections(status, { phone: false })
    expect(desktop[0].probes.map((p) => p.id)).toEqual(['up-1', 'down-1', 'unstable-1', 'up-2', 'paused-1'])

    const phone = dashboardSections(status, { phone: true })
    expect(phone[0].probes.map((p) => p.id)).toEqual(['down-1', 'unstable-1', 'up-1', 'up-2', 'paused-1'])
  })

  it('returns one empty "Services" section with zero probes', () => {
    const sections = dashboardSections(makeStatus({}), { phone: false })

    expect(sections).toEqual([{ id: 'ungrouped', headingId: 'services-heading', heading: 'Services', probes: [] }])
  })

  it('returns the same empty "Services" section when status is not loaded yet', () => {
    const sections = dashboardSections(undefined, { phone: false })

    expect(sections).toEqual([{ id: 'ungrouped', headingId: 'services-heading', heading: 'Services', probes: [] }])
  })
})

describe('formatCheckedAt', () => {
  const now = new Date('2026-01-01T12:00:00Z')

  it('reads "Never" when the probe has not been polled', () => {
    expect(formatCheckedAt(null, now)).toBe('Never')
  })

  it('reads "N min ago" under an hour', () => {
    expect(formatCheckedAt(new Date('2026-01-01T11:58:00Z').toISOString(), now)).toBe('2 min ago')
  })

  it('reads "N h ago" beyond an hour', () => {
    expect(formatCheckedAt(new Date('2026-01-01T10:00:00Z').toISOString(), now)).toBe('2 h ago')
  })
})
