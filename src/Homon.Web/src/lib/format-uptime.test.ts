import { describe, expect, it } from 'vitest'

import { formatUptime } from '@/lib/format-uptime'

describe('formatUptime', () => {
  it('formats a percentage to two decimals', () => {
    expect(formatUptime(98.324)).toBe('98.32%')
    expect(formatUptime(100)).toBe('100.00%')
    expect(formatUptime(0)).toBe('0.00%')
  })

  it('renders an em dash when there is nothing to compute', () => {
    expect(formatUptime(null)).toBe('—')
  })
})
