import { useCallback, useRef, useState, type PointerEvent } from 'react'

import { formatDuration, formatLatency, niceCeiling, type LatencyBucket } from '@/lib/probe-detail'
import type { ProbeState } from '@/lib/status'

/*
 * The probe page's large latency chart: hand-drawn SVG, like `Sparkline`, because the repo has no
 * chart dependency (plan 023, D8 as revised by D8').
 *
 * It is drawn in real pixels, not in a viewBox stretched to the panel. The first version stretched
 * (`preserveAspectRatio="none"`), which forbade circles and in-SVG text — both would distort — and
 * so it could not draw a dot per sample or label its own axes. With three polls in a day that
 * produced three tiny dashes in a frame twice as tall as the data. Measuring the width and drawing
 * at it costs one small hook and buys dots, text, and an honest aspect ratio.
 *
 * The y axis starts at 0 and ends at a "nice" ceiling with a little headroom. That is deliberately
 * not what `Sparkline` does: it fits min-max because at 22px it only has to show shape, while this
 * chart answers "how slow, in ms", and a min-max fit would make a 12 ms wobble look like an outage.
 * So this is not `Sparkline` grown up.
 */

/** Used until the chart has measured itself, and always where there is no `ResizeObserver` (jsdom). */
const FALLBACK_WIDTH = 720
const HEIGHT = 224
const PADDING = { top: 12, right: 12, bottom: 28, left: 52 }

/** Above this many buckets with data, a dot per sample is noise and only the latest keeps one. */
const MAX_DOTS = 48

const TOOLTIP_WIDTH = 160

/** The space between the guide line and the tooltip beside it. */
const TOOLTIP_GAP = 8

/**
 * The latest point's colour is the probe's own status colour, as `sparkline.tsx` does; copied
 * rather than imported because that one is private there and keyed on a different type. A state
 * with no colour of its own (paused) falls back to `muted`.
 */
const DOT_COLOR: Partial<Record<ProbeState, string>> = {
  up: 'var(--color-up)',
  unstable: 'var(--color-unstable)',
  down: 'var(--color-down)',
  unknown: 'var(--color-unknown)',
}

const AXIS_TEXT = { className: 'mono', fontSize: 11, fill: 'var(--color-muted)' } as const

/**
 * The element's own width, kept current. Falls back to `FALLBACK_WIDTH` where there is no
 * `ResizeObserver`, and ignores a zero width (an element that is not laid out yet), so the
 * fallback is never what a real browser keeps after mount.
 *
 * A callback ref, not an effect that looks at a ref once on mount: the measured wrapper is rendered
 * only when there is data, so a chart that first mounts in its empty state (a new probe, a range
 * with no polls) and gets data on a later refetch would never have had an element to observe, and
 * would stay at the fallback inside a panel twice as wide. A callback ref runs whenever the element
 * itself appears or goes away, however the component got there.
 */
function useMeasuredWidth() {
  const [width, setWidth] = useState(FALLBACK_WIDTH)
  const observer = useRef<ResizeObserver | null>(null)

  const ref = useCallback((element: HTMLDivElement | null) => {
    observer.current?.disconnect()
    observer.current = null

    if (element === null || typeof ResizeObserver === 'undefined') {
      return
    }

    const measure = (measured: number) => {
      if (measured > 0) {
        setWidth(measured)
      }
    }

    measure(element.getBoundingClientRect().width)

    observer.current = new ResizeObserver((entries) => {
      for (const entry of entries) {
        measure(entry.contentRect.width)
      }
    })
    observer.current.observe(element)
  }, [])

  return { ref, width }
}

/** Zero and one decimal-free values read better as `0 ms` than the `0.0 ms` `formatLatency` gives. */
function axisLatency(ms: number): string {
  return ms === 0 ? '0 ms' : formatLatency(ms)
}

function clockTime(date: Date): string {
  return date.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' })
}

/** `14:30` while buckets are under an hour wide (the 24 h range), a date otherwise. */
function axisTime(date: Date, bucketSeconds: number): string {
  return bucketSeconds < 3600
    ? clockTime(date)
    : date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
}

/** `14:30–14:45`, or `6 Oct 12:00–18:00` for buckets wide enough that the date matters. */
function bucketSpan(bucket: LatencyBucket, bucketSeconds: number): string {
  const start = new Date(bucket.start)
  const end = new Date(start.getTime() + bucketSeconds * 1000)
  const span = `${clockTime(start)}–${clockTime(end)}`

  return bucketSeconds < 3600
    ? span
    : `${start.toLocaleDateString(undefined, { day: 'numeric', month: 'short' })} ${span}`
}

/**
 * What each bucket is. Three kinds, because "nobody looked" and "it was down" are different
 * things and the chart must not draw them the same: *data* has an average, *empty* had no polls
 * at all, *down* had polls and not one succeeded.
 */
type Kind = 'data' | 'empty' | 'down'

function kindOf(bucket: LatencyBucket): Kind {
  if (bucket.averageLatencyMs !== null) {
    return 'data'
  }

  return bucket.polls === 0 ? 'empty' : 'down'
}

/**
 * Mean latency per bucket, y from 0.
 *
 * - Consecutive data buckets are a solid line with a faint area under it.
 * - Two data buckets with only *empty* buckets between them are joined by a dashed *bridge*: the
 *   dashes say the stretch is interpolated, and joining them is what lets three polls spread over a
 *   day read as a line rather than as unrelated specks. Gaps in the data are usually the dashboard
 *   not running, not an outage.
 * - A *down* bucket breaks the line and is never bridged: that gap is not a lack of looking. A bar
 *   along the bottom marks it (and any bucket with failed polls).
 *
 * The table under it on the probe page is the accessible form of the same data.
 */
export function LatencyChart({
  buckets,
  bucketSeconds,
  rangeLabel,
  state,
}: {
  buckets: LatencyBucket[]
  bucketSeconds: number
  rangeLabel: string
  state: ProbeState
}) {
  const { ref, width } = useMeasuredWidth()
  const [hovered, setHovered] = useState<number | null>(null)

  const kinds = buckets.map(kindOf)
  const dataIndexes = kinds.flatMap((kind, index) => (kind === 'data' ? [index] : []))

  if (dataIndexes.length === 0) {
    return <p className="text-[13.5px] text-muted">No successful polls in the last {rangeLabel}.</p>
  }

  const averageAt = (index: number) => buckets[index].averageLatencyMs ?? 0
  const values = dataIndexes.map(averageAt)
  const lowest = Math.min(...values)
  const highest = Math.max(...values)
  const latestIndex = dataIndexes[dataIndexes.length - 1]
  const failed = buckets.reduce((sum, bucket) => sum + bucket.failures, 0)

  const summary =
    `Latency over the last ${rangeLabel}: lowest ${formatLatency(lowest)}, highest ${formatLatency(highest)}, latest ${formatLatency(averageAt(latestIndex))}` +
    (failed > 0 ? `, ${String(failed)} failed polls` : '')

  // 10% headroom, so the highest point does not sit on the top gridline; the nice ceiling's finer
  // steps then keep 260 ms under a 300 ms ceiling rather than a 500 ms one that wastes half the height.
  const yMax = niceCeiling(highest * 1.1)
  const n = buckets.length
  const plotW = Math.max(width - PADDING.left - PADDING.right, 1)
  const plotH = HEIGHT - PADDING.top - PADDING.bottom
  const baseline = PADDING.top + plotH
  const x = (index: number) => PADDING.left + ((index + 0.5) / n) * plotW
  const y = (value: number) => PADDING.top + plotH - (value / yMax) * plotH
  const point = (index: number) => `${x(index).toFixed(1)} ${y(averageAt(index)).toFixed(1)}`

  // Solid runs: maximal stretches of consecutive data buckets.
  const runs: number[][] = []
  for (const index of dataIndexes) {
    const run = runs[runs.length - 1]

    if (run !== undefined && run[run.length - 1] === index - 1) {
      run.push(index)
    } else {
      runs.push([index])
    }
  }

  // Bridges: neighbouring data buckets with nothing but empty buckets between them.
  const bridges: [number, number][] = []
  for (let i = 1; i < dataIndexes.length; i += 1) {
    const from = dataIndexes[i - 1]
    const to = dataIndexes[i]

    if (to - from > 1 && kinds.slice(from + 1, to).every((kind) => kind === 'empty')) {
      bridges.push([from, to])
    }
  }

  const linkedRuns = runs.filter((run) => run.length >= 2)
  const linePath = linkedRuns
    .map((run) => run.map((index, at) => `${at === 0 ? 'M' : 'L'}${point(index)}`).join(' '))
    .join(' ')
  const areaPath = linkedRuns
    .map((run) => {
      const first = run[0]
      const last = run[run.length - 1]

      return `M${x(first).toFixed(1)} ${String(baseline)} ${run.map((index) => `L${point(index)}`).join(' ')} L${x(last).toFixed(1)} ${String(baseline)} Z`
    })
    .join(' ')
  const bridgePath = bridges.map(([from, to]) => `M${point(from)} L${point(to)}`).join(' ')

  // A dot per sample while there are few enough to count; above that only the latest keeps one —
  // plus any lone bucket the line and bridges never touch, which would otherwise be invisible.
  const connected = new Set([...linkedRuns.flat(), ...bridges.flat()])
  const dotted = dataIndexes.filter(
    (index) => index === latestIndex || dataIndexes.length <= MAX_DOTS || !connected.has(index),
  )

  // Five tick labels across the window, three on a phone-width plot where five would collide.
  const tickCount = plotW < 360 ? 3 : 5
  const windowStart = new Date(buckets[0].start).getTime()
  const windowMs = n * bucketSeconds * 1000
  const ticks = Array.from({ length: tickCount }, (_, tick) => {
    const fraction = tick / (tickCount - 1)

    return {
      x: PADDING.left + fraction * plotW,
      label: tick === tickCount - 1 ? 'Now' : axisTime(new Date(windowStart + fraction * windowMs), bucketSeconds),
      anchor: tick === 0 ? 'start' : tick === tickCount - 1 ? 'end' : 'middle',
    } as const
  })

  function onPointerMove(event: PointerEvent<SVGSVGElement>) {
    const rect = event.currentTarget.getBoundingClientRect()
    const index = Math.floor(((event.clientX - rect.left - PADDING.left) / plotW) * n)

    setHovered(Math.min(Math.max(index, 0), n - 1))
  }

  const hoveredBucket = hovered === null ? null : buckets[hovered]
  const tooltipWidth = Math.min(TOOLTIP_WIDTH, width)
  // Beside the guide line, not centred on it: centred, it covered the very point being read, so
  // hovering a spike hid the spike. It flips to the line's left near the right edge, and is
  // clamped inside the chart either way.
  const guideX = hovered === null ? 0 : x(hovered)
  const tooltipLeft = Math.min(
    Math.max(guideX + TOOLTIP_GAP + tooltipWidth > width ? guideX - TOOLTIP_GAP - tooltipWidth : guideX + TOOLTIP_GAP, 0),
    Math.max(width - tooltipWidth, 0),
  )

  return (
    <figure className="flex flex-col gap-2">
      <div ref={ref} className="relative w-full min-w-0">
        <svg
          className="block max-w-full"
          width={width}
          height={HEIGHT}
          viewBox={`0 0 ${String(width)} ${String(HEIGHT)}`}
          role="img"
          aria-label={summary}
          // `pan-y`: a finger dragging sideways scrubs the chart, while a vertical drag still
          // scrolls the page rather than being swallowed by it.
          style={{ touchAction: 'pan-y' }}
          onPointerMove={onPointerMove}
          onPointerLeave={() => {
            setHovered(null)
          }}
        >
          {[0, yMax / 2, yMax].map((tick) => (
            <g key={tick}>
              <line
                x1={PADDING.left}
                x2={PADDING.left + plotW}
                y1={y(tick)}
                y2={y(tick)}
                stroke="var(--color-line)"
                strokeWidth={1}
              />
              <text {...AXIS_TEXT} x={PADDING.left - 8} y={y(tick)} dy="0.32em" textAnchor="end">
                {axisLatency(tick)}
              </text>
            </g>
          ))}
          {ticks.map((tick) => (
            <text key={tick.x} {...AXIS_TEXT} x={tick.x} y={HEIGHT - 8} textAnchor={tick.anchor}>
              {tick.label}
            </text>
          ))}
          {areaPath === '' ? null : (
            <path d={areaPath} fill="var(--color-muted)" fillOpacity={0.15} data-testid="latency-area" />
          )}
          {buckets.map((bucket, index) =>
            bucket.failures > 0 ? (
              <rect
                key={bucket.start}
                x={x(index) - Math.max(plotW / n - 2, 2) / 2}
                y={baseline - 6}
                width={Math.max(plotW / n - 2, 2)}
                height={6}
                data-failures=""
                fill={bucket.failures === bucket.polls ? 'var(--color-down)' : 'var(--color-unstable)'}
              />
            ) : null,
          )}
          {bridgePath === '' ? null : (
            <path
              d={bridgePath}
              fill="none"
              stroke="var(--color-muted)"
              strokeWidth={1.5}
              strokeDasharray="4 4"
              data-testid="latency-bridge"
            />
          )}
          {linePath === '' ? null : (
            <path
              d={linePath}
              fill="none"
              stroke="var(--color-text)"
              strokeWidth={2}
              strokeLinejoin="round"
              strokeLinecap="round"
              data-testid="latency-line"
            />
          )}
          {hovered === null ? null : (
            <line
              x1={x(hovered)}
              x2={x(hovered)}
              y1={PADDING.top}
              y2={baseline}
              stroke="var(--color-line-strong)"
              strokeWidth={1}
            />
          )}
          {dotted.map((index) => (
            <circle
              key={buckets[index].start}
              cx={x(index)}
              cy={y(averageAt(index))}
              r={index === latestIndex ? 4 : 3}
              fill={index === latestIndex ? (DOT_COLOR[state] ?? 'var(--color-muted)') : 'var(--color-text)'}
              data-testid="latency-dot"
            />
          ))}
        </svg>
        {hoveredBucket === null || hovered === null ? null : (
          // HTML, not SVG text, so it can be a bordered box; aria-hidden because the table under
          // the chart is the accessible form of every number in it.
          <div
            aria-hidden="true"
            data-testid="latency-tooltip"
            className="pointer-events-none absolute top-0 rounded-md border border-line bg-bg px-2 py-1 text-[12px] whitespace-nowrap"
            style={{ width: tooltipWidth, left: tooltipLeft }}
          >
            <p className="mono text-muted">{bucketSpan(hoveredBucket, bucketSeconds)}</p>
            <p>
              {hoveredBucket.averageLatencyMs === null
                ? 'No successful polls'
                : `Mean ${formatLatency(hoveredBucket.averageLatencyMs)}`}
            </p>
            <p className="text-muted">
              {`${String(hoveredBucket.polls)} ${hoveredBucket.polls === 1 ? 'poll' : 'polls'}`}
              {hoveredBucket.failures > 0 ? `, ${String(hoveredBucket.failures)} failed` : ''}
            </p>
          </div>
        )}
      </div>
      <figcaption className="text-[12.5px] text-muted">
        Mean latency per {formatDuration(bucketSeconds)}. A dashed line spans time with no polls; bars along
        the bottom mark failed polls.
      </figcaption>
    </figure>
  )
}
