import { describe, expect, it } from 'vitest'

import { formatStamp } from '@/lib/format-stamp'

// The locale is the reader's, so none of these assert the exact text — only what the options
// decide: whether a year, a time or an error string appears.
const NOW = new Date('2026-10-07T12:00:00Z')

describe('formatStamp', () => {
  it('leaves the year out when it is the current one', () => {
    expect(formatStamp('2026-10-01T04:30:00+00:00', { now: NOW })).not.toContain('2026')
  })

  it('adds the year when it differs from now', () => {
    expect(formatStamp('2025-10-01T04:30:00+00:00', { now: NOW })).toContain('2025')
  })

  it('gives a date only for time: false', () => {
    expect(formatStamp('2026-10-01T04:30:00+00:00', { time: false, now: NOW })).not.toContain(':')
  })

  it('never returns Invalid Date for a well-formed stamp', () => {
    expect(formatStamp('2026-10-01T04:30:00+00:00')).not.toBe('Invalid Date')
  })
})
