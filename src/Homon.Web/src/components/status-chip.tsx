import { Check, CircleHelp, Pause, TriangleAlert, X, type LucideIcon } from 'lucide-react'

/**
 * The five states `docs/design-brief.md`'s Status chip component rule covers — plus the
 * Backups module's own words, which reuse this exact component once 008 lands (Maintenance
 * notes): Succeeded/Late/Failed map to the same glyph+colour as up/unstable/down.
 */
export type StatusChipState = 'up' | 'unstable' | 'down' | 'unknown' | 'paused'

const GLYPH: Record<StatusChipState, LucideIcon> = {
  up: Check,
  unstable: TriangleAlert,
  down: X,
  unknown: CircleHelp,
  paused: Pause,
}

const WORD: Record<StatusChipState, string> = {
  up: 'Up',
  unstable: 'Unstable',
  down: 'Down',
  unknown: 'Unknown',
  paused: 'Paused',
}

const COLOR: Record<StatusChipState, string> = {
  up: 'text-up',
  unstable: 'text-unstable',
  down: 'text-down',
  unknown: 'text-unknown',
  paused: 'text-muted',
}

/**
 * Glyph and word together, never one without the other ("Status is not colour alone",
 * docs/design-brief.md's Constraints) — a state passed as `state` alone renders the fixed
 * word for it; `word` overrides only for a caller reusing the same glyph+colour under a
 * different label (Backups' Succeeded/Late/Failed).
 */
export function StatusChip({ state, word }: { state: StatusChipState; word?: string }) {
  const Glyph = GLYPH[state]

  return (
    <span className={`inline-flex items-center gap-1.5 text-[13px] font-semibold ${COLOR[state]}`}>
      <Glyph aria-hidden="true" size={16} strokeWidth={2.25} className="shrink-0" />
      {word ?? WORD[state]}
    </span>
  )
}
