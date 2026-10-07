import { describe, expect, it } from 'vitest'

import {
  countOf,
  summariseApiKeys,
  summariseGroups,
  summariseLinks,
  summarisePages,
  summariseProbes,
  summariseReporters,
  summariseWeather,
} from '@/lib/admin-summary'
import type { ApiKey } from '@/lib/api-keys'
import type { Link } from '@/lib/links'
import type { AdminPageSummary } from '@/lib/pages'
import type { ProbeGroup } from '@/lib/probe-groups'
import type { Probe } from '@/lib/probes'
import type { Reporter } from '@/lib/reporters'

function probe(overrides: Partial<Probe> = {}): Probe {
  return {
    id: 'p',
    name: 'P',
    host: 'h',
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

function reporter(nextExpectedAt: string | null | undefined): Reporter {
  return {
    id: 'r',
    identifier: 'r',
    name: 'R',
    description: null,
    bodyVisibility: 'administrator',
    createdAt: '',
    tokenId: 't',
    keyLastUsedAt: null,
    keyRevokedAt: null,
    latest:
      nextExpectedAt === undefined
        ? null
        : {
            id: 1,
            name: 'job',
            status: 'success',
            category: 'backup',
            receivedAt: '2026-10-01T00:00:00Z',
            nextExpectedAt,
            recurrence: null,
          },
    messageCount: 0,
    isWatched: false,
  }
}

function key(overrides: Partial<ApiKey> = {}): ApiKey {
  return {
    id: 'k',
    name: 'K',
    tokenId: 't',
    scope: 'read',
    createdAt: '',
    lastUsedAt: null,
    expiresAt: null,
    revokedAt: null,
    isExpired: false,
    reporterName: null,
    ...overrides,
  }
}

describe('countOf', () => {
  it('pluralises except at exactly one', () => {
    expect(countOf(0, 'link')).toBe('0 links')
    expect(countOf(1, 'link')).toBe('1 link')
    expect(countOf(2, 'link')).toBe('2 links')
  })
})

describe('summariseProbes', () => {
  it('counts probes, paused ones and the down and unstable ones', () => {
    const summary = summariseProbes([
      probe({ status: 'down' }),
      probe({ status: 'unstable' }),
      probe({ status: 'paused', isPaused: true }),
      probe(),
    ])

    expect(summary).toEqual({ text: '4 probes · 1 paused', down: 1, unstable: 1 })
  })

  it('has no paused part and a singular for one probe', () => {
    expect(summariseProbes([probe()]).text).toBe('1 probe')
    expect(summariseProbes([]).text).toBe('0 probes')
  })
})

function group(id: string): ProbeGroup {
  return { id, name: id, probeIds: [] }
}

function page(isPublished: boolean): AdminPageSummary {
  return { id: 'x', slug: 'x', title: 'X', isPublished, updatedAt: '' }
}

describe('summariseGroups', () => {
  it('counts probes in no group', () => {
    expect(summariseGroups([group('a'), group('b')], [probe(), probe({ groupIds: ['a'] })]).text).toBe(
      '2 groups · 1 probe in none',
    )
  })

  it('drops the second part when every probe is in a group, and is singular for one group', () => {
    expect(summariseGroups([group('a')], [probe({ groupIds: ['a'] })]).text).toBe('1 group')
    expect(summariseGroups([], []).text).toBe('0 groups')
  })
})

describe('summariseReporters', () => {
  const now = new Date('2026-10-07T12:00:00Z').getTime()

  it('counts the overdue ones as down', () => {
    const summary = summariseReporters(
      [reporter('2026-10-01T00:00:00Z'), reporter('2026-11-01T00:00:00Z'), reporter(null), reporter(undefined)],
      now,
    )

    expect(summary).toEqual({ text: '4 reporters', down: 1 })
  })

  it('is singular for one and has no overdue for none', () => {
    expect(summariseReporters([reporter(undefined)], now)).toEqual({ text: '1 reporter', down: 0 })
    expect(summariseReporters([], now).text).toBe('0 reporters')
  })
})

describe('summariseLinks', () => {
  it('counts links', () => {
    const link = { id: 'l' } as Link

    expect(summariseLinks([]).text).toBe('0 links')
    expect(summariseLinks([link]).text).toBe('1 link')
    expect(summariseLinks([link, link]).text).toBe('2 links')
  })
})

describe('summarisePages', () => {
  it('adds the draft count, singular and plural', () => {
    expect(summarisePages([page(true), page(false)]).text).toBe('2 pages · 1 draft')
    expect(summarisePages([page(false), page(false), page(true)]).text).toBe('3 pages · 2 drafts')
  })

  it('has no draft part when none are drafts, and handles zero', () => {
    expect(summarisePages([page(true)]).text).toBe('1 page')
    expect(summarisePages([]).text).toBe('0 pages')
  })
})

describe('summariseWeather', () => {
  it('says Not set when there is no location', () => {
    expect(summariseWeather(null).text).toBe('Not set')
  })

  it('shows the coordinates to two decimals and the units, prefixed by the place when named', () => {
    expect(summariseWeather({ latitude: 48.8566, longitude: 2.3522, place: null, units: 'metric' }).text).toBe(
      '48.86, 2.35 · metric',
    )
    expect(
      summariseWeather({ latitude: 48.8566, longitude: 2.3522, place: 'Paris', units: 'imperial' }).text,
    ).toBe('Paris · 48.86, 2.35 · imperial')
  })
})

describe('summariseApiKeys', () => {
  it('counts active, revoked and expired, with revoked taking precedence over expired', () => {
    const summary = summariseApiKeys([
      key(),
      key(),
      key({ revokedAt: '2026-10-01T00:00:00Z', isExpired: true }),
      key({ isExpired: true }),
    ])

    expect(summary.text).toBe('2 active · 1 revoked · 1 expired')
  })

  it('drops zero parts and handles no keys', () => {
    expect(summariseApiKeys([key()]).text).toBe('1 active')
    expect(summariseApiKeys([key({ revokedAt: '2026-10-01T00:00:00Z' })]).text).toBe('1 revoked')
    expect(summariseApiKeys([]).text).toBe('0 keys')
  })
})
