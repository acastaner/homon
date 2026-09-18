import { describe, expect, it } from 'vitest'

import { formatRefreshed } from '@/lib/format-refreshed'

describe('formatRefreshed', () => {
  it('formats seconds under a minute', () => {
    expect(formatRefreshed(0)).toBe('refreshed 0 s ago')
    expect(formatRefreshed(42_000)).toBe('refreshed 42 s ago')
    expect(formatRefreshed(59_999)).toBe('refreshed 59 s ago')
  })

  it('rolls up to minutes, then hours', () => {
    expect(formatRefreshed(60_000)).toBe('refreshed 1 min ago')
    expect(formatRefreshed(3_599_000)).toBe('refreshed 59 min ago')
    expect(formatRefreshed(3_600_000)).toBe('refreshed 1 h ago')
  })

  it('clamps a clock that stepped backwards', () => {
    expect(formatRefreshed(-5_000)).toBe('refreshed 0 s ago')
  })
})
