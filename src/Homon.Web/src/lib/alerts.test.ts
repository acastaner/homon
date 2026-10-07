import { describe, expect, it } from 'vitest'

import { formatDuration } from '@/lib/alerts'

describe('formatDuration', () => {
  it.each([
    [30, 'less than a minute'],
    [5 * 60, '5 min'],
    [60 * 60, '1 h'],
    [61 * 60, '1 h 1 min'],
    [72 * 60, '1 h 12 min'],
    [25 * 3600, '1 d 1 h'],
    [48 * 3600, '2 d'],
  ])('%i seconds is %s — the same table as AlertMessageBuilder.FormatDuration', (seconds, expected) => {
    expect(formatDuration(seconds * 1000)).toBe(expected)
  })
})
