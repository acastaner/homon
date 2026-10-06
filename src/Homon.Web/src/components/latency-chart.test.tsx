import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'

import { LatencyChart } from '@/components/latency-chart'
import type { LatencyBucket } from '@/lib/probe-detail'

/** A bucket with an average: *data*. */
function data(index: number, average: number, polls = 1, failures = 0): LatencyBucket {
  return {
    start: new Date(Date.UTC(2026, 9, 6, 0, index * 15)).toISOString(),
    averageLatencyMs: average,
    polls,
    failures,
  }
}

/** A bucket nobody polled: *empty*. */
function empty(index: number): LatencyBucket {
  return { ...data(index, 0, 0), averageLatencyMs: null }
}

/** A bucket that was polled and never succeeded: *down*. */
function down(index: number, polls = 2): LatencyBucket {
  return { ...data(index, 0, polls, polls), averageLatencyMs: null }
}

function chart(buckets: LatencyBucket[]) {
  return render(<LatencyChart buckets={buckets} bucketSeconds={900} rangeLabel="24 hours" state="up" />)
}

/** The number of subpaths — `M` commands — in a drawn path, 0 when it is not drawn at all. */
function subpaths(testId: string): number {
  return (screen.queryByTestId(testId)?.getAttribute('d') ?? '').match(/M/g)?.length ?? 0
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('LatencyChart', () => {
  it('says so, and draws nothing, when no bucket has an average', () => {
    chart([empty(0), down(1)])

    expect(screen.getByText('No successful polls in the last 24 hours.')).toBeInTheDocument()
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
  })

  it('summarises the range in its accessible name', () => {
    chart([10, 20, 30, 40].map((average, index) => data(index, average)))

    const label = screen.getByRole('img').getAttribute('aria-label')
    expect(label).toContain('lowest 10 ms')
    expect(label).toContain('highest 40 ms')
    expect(label).toContain('latest 40 ms')
  })

  it('bridges time nobody polled with a dashed line instead of breaking the line', () => {
    chart([data(0, 10), data(1, 20), empty(2), data(3, 30), data(4, 40)])

    expect(subpaths('latency-line')).toBe(2)
    expect(subpaths('latency-bridge')).toBe(1)
    expect(screen.getByTestId('latency-bridge')).toHaveAttribute('stroke-dasharray')
  })

  it('never bridges a bucket that was polled and failed, and marks it with a bar', () => {
    const { container } = chart([data(0, 10), data(1, 20), down(2), data(3, 30), data(4, 40)])

    expect(subpaths('latency-line')).toBe(2)
    expect(subpaths('latency-bridge')).toBe(0)
    expect(container.querySelectorAll('rect[data-failures]')).toHaveLength(1)
  })

  it('draws three samples as three dots joined by two bridges, and no solid line', () => {
    chart([data(0, 250), empty(1), empty(2), data(3, 230), empty(4), data(5, 60)])

    expect(screen.getAllByTestId('latency-dot')).toHaveLength(3)
    expect(subpaths('latency-bridge')).toBe(2)
    expect(subpaths('latency-line')).toBe(0)
  })

  it("colours the latest dot with the probe's status and gives it the larger radius", () => {
    render(
      <LatencyChart buckets={[data(0, 10), data(1, 20)]} bucketSeconds={900} rangeLabel="24 hours" state="down" />,
    )

    const [first, latest] = screen.getAllByTestId('latency-dot')
    expect(first).toHaveAttribute('r', '3')
    expect(latest).toHaveAttribute('r', '4')
    expect(latest).toHaveAttribute('fill', 'var(--color-down)')
  })

  it('keeps only the latest dot once there are too many samples to count', () => {
    chart(Array.from({ length: 60 }, (_, index) => data(index, 10 + index)))

    expect(screen.getAllByTestId('latency-dot')).toHaveLength(1)
    expect(subpaths('latency-line')).toBe(1)
  })

  it('marks failed polls along the bottom, red for all failed and amber for some', () => {
    const { container } = chart([down(0, 2), data(1, 12, 3, 1), data(2, 14)])

    const bars = container.querySelectorAll('rect[data-failures]')
    expect(bars).toHaveLength(2)
    expect(bars[0].getAttribute('fill')).toBe('var(--color-down)')
    expect(bars[1].getAttribute('fill')).toBe('var(--color-unstable)')
    expect(screen.getByRole('img').getAttribute('aria-label')).toMatch(/, 3 failed polls$/)
  })

  it('shows a tooltip for the bucket under the pointer and hides it when the pointer leaves', () => {
    const buckets = [data(0, 10), data(1, 20), data(2, 182, 4, 1), data(3, 40)]
    chart(buckets)
    const svg = screen.getByRole('img')

    // jsdom lays nothing out: the rect is all zeros and the width is the 720 fallback, so a
    // bucket's centre is at left + plotW * (i + 0.5) / n with the chart's own padding.
    const plotW = 720 - 52 - 12
    fireEvent.pointerMove(svg, { clientX: 52 + (plotW * 2.5) / buckets.length })

    const tooltip = screen.getByTestId('latency-tooltip')
    expect(tooltip).toHaveTextContent('Mean 182 ms')
    expect(tooltip).toHaveTextContent('4 polls, 1 failed')
    expect(tooltip).toHaveTextContent('00:30–00:45')

    fireEvent.pointerLeave(svg)
    expect(screen.queryByTestId('latency-tooltip')).not.toBeInTheDocument()
  })

  it('labels the axes inside the SVG, ending at Now', () => {
    chart([data(0, 250), data(1, 230)])

    expect(screen.getByText('Now')).toBeInTheDocument()
    expect(screen.getByText('0 ms')).toBeInTheDocument()
    // 250 * 1.1 = 275, so a 300 ms ceiling and a 150 ms midline.
    expect(screen.getByText('300 ms')).toBeInTheDocument()
    expect(screen.getByText('150 ms')).toBeInTheDocument()
  })

  it('measures itself even when it mounts empty and only gets data on a later render', () => {
    // A minimal ResizeObserver that reports 1000px as soon as it is told to observe something.
    vi.stubGlobal(
      'ResizeObserver',
      class {
        callback: (entries: { contentRect: { width: number } }[]) => void
        constructor(callback: (entries: { contentRect: { width: number } }[]) => void) {
          this.callback = callback
        }
        observe() {
          this.callback([{ contentRect: { width: 1000 } }])
        }
        disconnect() {}
      },
    )

    // The measured wrapper exists only once there is data, so the chart first mounts without it;
    // a hook that looks for its element once, on mount, never sees it and stays at the 720 fallback.
    const props = { bucketSeconds: 900, rangeLabel: '24 hours', state: 'up' } as const
    const { rerender } = render(<LatencyChart buckets={[empty(0)]} {...props} />)
    expect(screen.queryByRole('img')).not.toBeInTheDocument()

    rerender(<LatencyChart buckets={[data(0, 10), data(1, 20)]} {...props} />)

    expect(screen.getByRole('img')).toHaveAttribute('width', '1000')
  })

  it('puts the tooltip beside the guide line, flipping to its left near the right edge', () => {
    const buckets = [data(0, 10), data(1, 20), data(2, 30), data(3, 40)]
    chart(buckets)
    const svg = screen.getByRole('img')
    const plotW = 720 - 52 - 12
    const centre = (index: number) => 52 + (plotW * (index + 0.5)) / buckets.length

    fireEvent.pointerMove(svg, { clientX: centre(0) })
    expect(screen.getByTestId('latency-tooltip').style.left).toBe(`${String(centre(0) + 8)}px`)

    fireEvent.pointerMove(svg, { clientX: centre(3) })
    expect(screen.getByTestId('latency-tooltip').style.left).toBe(`${String(centre(3) - 8 - 160)}px`)
  })
})
