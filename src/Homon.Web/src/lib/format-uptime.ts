/**
 * `98.32%` (mono, two decimals) or `—` when there is nothing to compute — docs/design-
 * brief.md's Uptime component rule.
 */
export function formatUptime(value: number | null): string {
  return value === null ? '—' : `${value.toFixed(2)}%`
}
