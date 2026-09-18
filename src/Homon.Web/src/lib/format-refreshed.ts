/**
 * `refreshed 42 s ago` — docs/design-brief.md's Banner rule. Seconds under a minute, whole
 * minutes under an hour, whole hours beyond.
 *
 * `elapsedMs` is measured browser-clock to browser-clock (TanStack's `dataUpdatedAt` against
 * `Date.now()`), never against the API's `Status.generatedAt`: those are two different clocks,
 * and a drifted household machine would otherwise render "refreshed -3 s ago". A negative
 * elapsed value is still clamped to 0 here, because a clock stepped backwards mid-session
 * (NTP correction, a laptop waking) can produce one from the same clock.
 */
export function formatRefreshed(elapsedMs: number): string {
  const seconds = Math.max(0, Math.floor(elapsedMs / 1000))

  if (seconds < 60) {
    return `refreshed ${String(seconds)} s ago`
  }

  const minutes = Math.floor(seconds / 60)

  if (minutes < 60) {
    return `refreshed ${String(minutes)} min ago`
  }

  return `refreshed ${String(Math.floor(minutes / 60))} h ago`
}
