import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'

import { LatencyChart } from '@/components/latency-chart'
import type { LatencyBucket } from '@/lib/probe-detail'

function bucket(index: number, average: number | null, polls = 1, failures = 0): LatencyBucket {
  return {
    start: new Date(Date.UTC(2026, 9, 6, 0, index * 15)).toISOString(),
    averageLatencyMs: average,
    polls,
    failures,
  }
}

function chart(buckets: LatencyBucket[]) {
  return render(<LatencyChart buckets={buckets} bucketSeconds={900} rangeLabel="24 hours" />)
}

describe('LatencyChart', () => {
  it('says so, and draws nothing, when no bucket has an average', () => {
    chart([bucket(0, null, 0), bucket(1, null, 0)])

    expect(screen.getByText('No successful polls in the last 24 hours.')).toBeInTheDocument()
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
  })

  it('summarises the range in its accessible name and breaks the line at a gap', () => {
    chart([10, 20, null, 30, 40].map((average, index) => bucket(index, average)))

    const label = screen.getByRole('img').getAttribute('aria-label')
    expect(label).toContain('lowest 10 ms')
    expect(label).toContain('highest 40 ms')
    expect(label).toContain('latest 40 ms')

    const d = screen.getByTestId('latency-line').getAttribute('d') ?? ''
    expect(d.match(/M/g)).toHaveLength(2)
  })

  it('draws a lone bucket between gaps as a short tick', () => {
    chart([null, 20, null, 30, 40].map((average, index) => bucket(index, average)))

    const d = screen.getByTestId('latency-line').getAttribute('d') ?? ''
    const segments = d.split('M').filter(Boolean)
    expect(segments).toHaveLength(2)
    // The lone bucket: one M and exactly one L, both at the same height.
    const [lone] = segments
    expect(lone.match(/L/g)).toHaveLength(1)
    const heights = lone.trim().split('L').map((point) => point.trim().split(' ')[1])
    expect(heights[0]).toBe(heights[1])
  })

  it('marks failed polls along the bottom, red for all failed and amber for some', () => {
    const { container } = chart([bucket(0, null, 2, 2), bucket(1, 12, 3, 1), bucket(2, 14)])

    const bars = container.querySelectorAll('rect[data-failures]')
    expect(bars).toHaveLength(2)
    expect(bars[0].getAttribute('fill')).toBe('var(--color-down)')
    expect(bars[1].getAttribute('fill')).toBe('var(--color-unstable)')
    expect(screen.getByRole('img').getAttribute('aria-label')).toMatch(/, 3 failed polls$/)
  })
})
