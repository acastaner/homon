import { describe, expect, it } from 'vitest'

import { formatDuration, formatLatency, niceCeiling, parseProbeRange } from '@/lib/probe-detail'

describe('formatLatency', () => {
  it('uses one decimal below 10 ms, whole ms to 999, and seconds beyond', () => {
    expect(formatLatency(0.84)).toBe('0.8 ms')
    expect(formatLatency(12.4)).toBe('12 ms')
    expect(formatLatency(999.4)).toBe('999 ms')
    expect(formatLatency(1234)).toBe('1.23 s')
  })
})

describe('formatDuration', () => {
  it('uses whole units only', () => {
    expect(formatDuration(15)).toBe('15 s')
    expect(formatDuration(60)).toBe('1 min')
    expect(formatDuration(90)).toBe('1 min 30 s')
    expect(formatDuration(900)).toBe('15 min')
    expect(formatDuration(3600)).toBe('1 h')
    expect(formatDuration(21_600)).toBe('6 h')
    expect(formatDuration(86_400)).toBe('1 day')
    expect(formatDuration(172_800)).toBe('2 days')
  })
})

describe('niceCeiling', () => {
  it('rounds up to the next step of 1, 1.5, 2, 2.5, 3, 4, 5, 6, 8 or 10 times a power of ten', () => {
    expect(niceCeiling(0)).toBe(1)
    expect(niceCeiling(7)).toBe(8)
    expect(niceCeiling(12)).toBe(15)
    expect(niceCeiling(23)).toBe(25)
    expect(niceCeiling(41)).toBe(50)
    expect(niceCeiling(100)).toBe(100)
    expect(niceCeiling(286)).toBe(300)
    expect(niceCeiling(0.3)).toBe(0.3)
  })
})

describe('parseProbeRange', () => {
  it('falls back to 24h for an absent or unknown range', () => {
    expect(parseProbeRange(null)).toBe('24h')
    expect(parseProbeRange('7d')).toBe('7d')
    expect(parseProbeRange('1y')).toBe('24h')
  })
})
