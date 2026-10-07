/**
 * A timestamp for an admin list, in the reader's own locale and time zone: "Oct 1, 04:30", or
 * "Oct 1, 2025, 04:30" when the year is not the current one (a stamp from last winter is
 * ambiguous without it, one from this week is noise with it). `{ time: false }` is the date alone,
 * for created and expiry dates where the hour means nothing.
 *
 * It follows `formatObservedAt` in `probe-detail.ts` deliberately, `toLocaleString(undefined, …)`
 * and all, so the admin pages and the probe page read the same. Which is also why no test may
 * assert its exact text: the locale is the reader's. The raw ISO string is never lost, though —
 * `Stamp` keeps it in `dateTime` and `title`.
 *
 * `now` is a parameter so a test can pin "this year" without faking the clock.
 */
export function formatStamp(
  iso: string,
  options: { time?: boolean; now?: Date } = {},
): string {
  const { time = true, now = new Date() } = options
  const date = new Date(iso)
  const differentYear = date.getFullYear() !== now.getFullYear()

  return date.toLocaleString(undefined, {
    day: 'numeric',
    month: 'short',
    ...(time ? { hour: '2-digit', minute: '2-digit' } : {}),
    ...(differentYear ? { year: 'numeric' } : {}),
  })
}
