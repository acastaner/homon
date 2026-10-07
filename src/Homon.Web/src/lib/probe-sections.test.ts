import { describe, expect, it } from 'vitest'

import type { ProbeGroup } from '@/lib/probe-groups'
import type { Probe } from '@/lib/probes'
import { moveWithinSection, probeSections, probeTarget } from '@/lib/probe-sections'

function probe(id: string, overrides: Partial<Probe> = {}): Probe {
  return {
    id,
    name: id,
    host: `${id}.lan`,
    kind: 'ping',
    pollIntervalSeconds: 60,
    failureThreshold: 2,
    isPaused: false,
    position: 0,
    status: 'up',
    lastDetail: null,
    lastCheckedAt: null,
    groupIds: [],
    http: null,
    ...overrides,
  }
}

function group(id: string, probeIds: string[]): ProbeGroup {
  return { id, name: id.toUpperCase(), probeIds }
}

describe('probeSections', () => {
  it('lists each group in its own member order, then the ungrouped rest in position order', () => {
    const probes = [probe('a'), probe('b'), probe('c'), probe('d')]
    const sections = probeSections(probes, [group('g1', ['c', 'a']), group('g2', ['b'])])

    expect(sections.map((section) => [section.heading, section.rows.map((row) => row.probe.id)])).toEqual([
      ['G1', ['c', 'a']],
      ['G2', ['b']],
      ['Other', ['d']],
    ])
  })

  it('names the other groups a shared probe also sits in, never its own', () => {
    const sections = probeSections([probe('nas')], [group('hosts', ['nas']), group('media', ['nas'])])

    expect(sections[0].rows[0].alsoIn).toEqual(['MEDIA'])
    expect(sections[1].rows[0].alsoIn).toEqual(['HOSTS'])
  })

  it('keeps an empty group, and calls the ungrouped rest "Services" while no group has members', () => {
    const sections = probeSections([probe('a')], [group('empty', [])])

    expect(sections.map((section) => [section.heading, section.rows.length])).toEqual([
      ['EMPTY', 0],
      ['Services', 1],
    ])
  })

  it('leaves the ungrouped section out when every probe is grouped', () => {
    const sections = probeSections([probe('a')], [group('g1', ['a'])])

    expect(sections.map((section) => section.id)).toEqual(['g1'])
  })
})

describe('moveWithinSection', () => {
  it('swaps a probe with its neighbour inside a group', () => {
    expect(moveWithinSection(['a', 'b', 'c'], ['a', 'b', 'c'], 'c', -1)).toEqual(['a', 'c', 'b'])
  })

  it('moves an ungrouped probe past its ungrouped neighbour without disturbing grouped probes between them', () => {
    // a and c are ungrouped; b is grouped and sits between them in Probe.Position order.
    expect(moveWithinSection(['a', 'b', 'c'], ['a', 'c'], 'c', -1)).toEqual(['c', 'b', 'a'])
  })

  it('returns null at either end', () => {
    expect(moveWithinSection(['a', 'b'], ['a', 'b'], 'a', -1)).toBeNull()
    expect(moveWithinSection(['a', 'b'], ['a', 'b'], 'b', 1)).toBeNull()
  })
})

describe('probeTarget', () => {
  it('spells an HTTP probe as its URL and everything else as its host', () => {
    const http = probe('api', {
      kind: 'http',
      host: 'api.lan:8080',
      http: {
        method: 'get',
        path: 'health',
        useHttps: true,
        ignoreCertificateErrors: false,
        timeoutSeconds: 10,
        expectedStatusCode: null,
        expectedStatusCodeNegate: false,
        expectedBodyText: null,
        expectedBodyTextNegate: false,
        credential: { type: 'none', username: null, hasSecret: false },
      },
    })

    expect(probeTarget(http)).toBe('https://api.lan:8080/health')
    expect(probeTarget(probe('nas'))).toBe('nas.lan')
  })
})
