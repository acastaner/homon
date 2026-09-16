import type { StatusChipState } from '@/components/status-chip'

/** 88×22 in a table cell, 60×18 inline after the detail on phone (docs/design-brief.md's
 * Sparkline component rule). */
const SIZE: Record<'table' | 'inline', { width: number; height: number }> = {
  table: { width: 88, height: 22 },
  inline: { width: 60, height: 18 },
}

/** Only up/unstable/down/unknown get a stroke colour on the latest-point dot; paused rows
 * never show a sparkline (the brief: ping probes only, and a paused probe stops sampling). */
const DOT_COLOR: Partial<Record<StatusChipState, string>> = {
  up: 'var(--color-up)',
  unstable: 'var(--color-unstable)',
  down: 'var(--color-down)',
  unknown: 'var(--color-unknown)',
}

/**
 * Ping probes only; other probe kinds leave the cell empty (docs/design-brief.md). 2px
 * `muted` stroke, rounded joins, a 2.5px dot on the latest point in the row's status colour.
 * No axis, no fill.
 */
export function Sparkline({
  samples,
  state,
  size = 'table',
}: {
  samples: number[]
  state: StatusChipState
  size?: 'table' | 'inline'
}) {
  if (samples.length === 0) {
    return null
  }

  const { width, height } = SIZE[size]
  const min = Math.min(...samples)
  const max = Math.max(...samples)
  // A flat series (min === max, including a single sample) still draws a level line down the
  // vertical centre rather than dividing by zero.
  const range = max - min || 1

  const points = samples.map((value, index) => {
    const x = samples.length === 1 ? width : (index / (samples.length - 1)) * width
    const y = height - ((value - min) / range) * height
    return { x, y }
  })

  const path = points.map((point, index) => `${index === 0 ? 'M' : 'L'}${point.x.toFixed(1)} ${point.y.toFixed(1)}`).join(' ')
  const last = points[points.length - 1]
  const dotColor = DOT_COLOR[state] ?? 'var(--color-muted)'

  return (
    <svg
      className="block shrink-0"
      style={{ width, height }}
      viewBox={`0 0 ${width} ${height}`}
      aria-hidden="true"
    >
      <path d={path} fill="none" stroke="var(--color-muted)" strokeWidth={2} strokeLinejoin="round" strokeLinecap="round" />
      <circle cx={last.x} cy={last.y} r={2.5} fill={dotColor} />
    </svg>
  )
}
