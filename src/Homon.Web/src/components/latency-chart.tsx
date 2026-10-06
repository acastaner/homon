import { formatDuration, formatLatency, niceCeiling, type LatencyBucket } from '@/lib/probe-detail'

/*
 * The probe page's large latency chart: hand-drawn SVG, like `Sparkline`, because the repo has
 * no chart dependency (plan 023, D8).
 *
 * The plot SVG uses `preserveAspectRatio="none"` so it stretches to the panel's width, and that
 * is why every stroke carries `vector-effect="non-scaling-stroke"` (a stretched 2px line would
 * otherwise be fat on one axis and thin on the other) and why there is NO text and NO circle
 * inside it (both would distort). Axis labels are HTML around the SVG.
 *
 * The y axis starts at 0 and ends at a "nice" ceiling. That is deliberately not what `Sparkline`
 * does: it fits min-max because at 22px it only has to show shape, while this chart answers
 * "how slow, in ms", and a min-max fit would make a 12 ms wobble look like an outage. So this is
 * not `Sparkline` grown up.
 */

/** `1.2` rather than `1.200000001`; SVG path data does not need more than a hundredth of a bucket. */
function coordinate(value: number): string {
  return String(Number(value.toFixed(2)))
}

/**
 * Path data for the line. Every run of consecutive buckets with an average starts its own `M`,
 * so a bucket with no average is a visible gap rather than a line drawn through it. A run of one
 * would be invisible, so it is drawn as a short horizontal tick across the bucket.
 */
function linePath(buckets: LatencyBucket[], yMax: number): string {
  const y = (value: number) => 100 - (value / yMax) * 100
  const commands: string[] = []
  let run: { x: number; y: number }[] = []

  function flush() {
    if (run.length === 1) {
      commands.push(`M${coordinate(run[0].x - 0.3)} ${coordinate(run[0].y)} L${coordinate(run[0].x + 0.3)} ${coordinate(run[0].y)}`)
    } else if (run.length > 1) {
      commands.push(run.map((point, index) => `${index === 0 ? 'M' : 'L'}${coordinate(point.x)} ${coordinate(point.y)}`).join(' '))
    }

    run = []
  }

  buckets.forEach((bucket, index) => {
    if (bucket.averageLatencyMs === null) {
      flush()
    } else {
      run.push({ x: index + 0.5, y: y(bucket.averageLatencyMs) })
    }
  })

  flush()

  return commands.join(' ')
}

/** The window's first label: a clock time while buckets are under an hour wide (the 24 h range), a date otherwise. */
function windowStartLabel(start: string, bucketSeconds: number): string {
  const date = new Date(start)

  return bucketSeconds < 3600
    ? date.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', hour12: false })
    : date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
}

/**
 * Mean latency per bucket, y from 0, the line broken at buckets with no average, and a bar along
 * the bottom of every bucket that had failed polls (`down` when all failed, `unstable` when only
 * some did). The table under it on the probe page is the accessible form of the same data.
 */
export function LatencyChart({
  buckets,
  bucketSeconds,
  rangeLabel,
}: {
  buckets: LatencyBucket[]
  bucketSeconds: number
  rangeLabel: string
}) {
  const values = buckets.flatMap((bucket) => (bucket.averageLatencyMs === null ? [] : [bucket.averageLatencyMs]))

  if (values.length === 0) {
    return (
      // Just the sentence: the page already wraps the chart in a panel, and a second dashed box
      // inside it would be a box in a box.
      <p className="text-[13.5px] text-muted">No successful polls in the last {rangeLabel}.</p>
    )
  }

  const lowest = Math.min(...values)
  const highest = Math.max(...values)
  const latest = values[values.length - 1]
  const yMax = niceCeiling(highest)
  const failed = buckets.reduce((sum, bucket) => sum + bucket.failures, 0)

  const summary =
    `Latency over the last ${rangeLabel}: lowest ${formatLatency(lowest)}, highest ${formatLatency(highest)}, latest ${formatLatency(latest)}` +
    (failed > 0 ? `, ${String(failed)} failed polls` : '')

  return (
    <figure className="flex flex-col gap-2">
      <div className="grid grid-cols-[auto_1fr] gap-x-3">
        <div className="mono flex flex-col justify-between text-[12px] text-muted" aria-hidden="true">
          <span>{formatLatency(yMax)}</span>
          <span>0 ms</span>
        </div>
        <div>
          <svg
            className="block h-48 w-full sm:h-56"
            viewBox={`0 0 ${String(buckets.length)} 100`}
            preserveAspectRatio="none"
            role="img"
            aria-label={summary}
          >
            {[0, 50, 100].map((gridY) => (
              <line
                key={gridY}
                x1={0}
                x2={buckets.length}
                y1={gridY}
                y2={gridY}
                stroke="var(--color-line)"
                strokeWidth={1}
                vectorEffect="non-scaling-stroke"
              />
            ))}
            {buckets.map((bucket, index) =>
              bucket.failures > 0 ? (
                <rect
                  key={bucket.start}
                  x={index + 0.1}
                  y={94}
                  width={0.8}
                  height={6}
                  data-failures=""
                  fill={bucket.failures === bucket.polls ? 'var(--color-down)' : 'var(--color-unstable)'}
                />
              ) : null,
            )}
            <path
              d={linePath(buckets, yMax)}
              fill="none"
              stroke="var(--color-muted)"
              strokeWidth={2}
              strokeLinejoin="round"
              strokeLinecap="round"
              vectorEffect="non-scaling-stroke"
              data-testid="latency-line"
            />
          </svg>
          <div className="mono mt-1 flex justify-between text-[12px] text-muted" aria-hidden="true">
            <span>{windowStartLabel(buckets[0].start, bucketSeconds)}</span>
            <span>Now</span>
          </div>
        </div>
      </div>
      <figcaption className="text-[12.5px] text-muted">
        Mean latency per {formatDuration(bucketSeconds)}. Bars along the bottom mark failed polls.
      </figcaption>
    </figure>
  )
}
