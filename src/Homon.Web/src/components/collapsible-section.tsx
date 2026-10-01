import { ArrowDown, ArrowUp, ChevronDown, ChevronRight } from 'lucide-react'
import type { ReactNode } from 'react'

/** `docs/design-brief.md`'s Section label rule: 12px/600/0.12em/uppercase. */
const SECTION_LABEL = 'text-[12px] font-semibold uppercase tracking-[0.12em] text-muted'

/**
 * h-10/w-10 is the same 40px tap-target floor the heading button carries, and it applies to the
 * DISABLED first and last buttons too: `e2e/helpers.ts`'s `expectTappable` filters on a measured
 * width above zero, not on `disabled`, so a shrunken greyed-out button fails the gate exactly like
 * an enabled one. An arrow, not a chevron — `ChevronDown`/`ChevronRight` already mean
 * expanded/collapsed two elements to the left.
 */
const ARRANGE_BUTTON =
  'inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-md border border-line text-muted hover:border-line-strong hover:text-text disabled:pointer-events-none disabled:opacity-50'

/** What arrange mode needs from the page: where this section can go, and how to get it there. */
export interface SectionArrangeControls {
  canMoveUp: boolean
  canMoveDown: boolean
  onMoveUp: () => void
  onMoveDown: () => void
}

/**
 * One foldable section of the dashboard, and the only disclosure pattern in this app.
 *
 * The button lives INSIDE the <h2> and its text is the heading's text and nothing else. That is
 * not a style choice — it is the one arrangement that satisfies four existing assertions at
 * once: `e2e/dashboard-groups.spec.ts` matches every level-2 heading's textContent EXACTLY
 * against the section names, `dashboard-page.test.tsx` finds each region by the accessible name
 * this heading supplies through aria-labelledby, `e2e/layout.spec.ts` needs the headings
 * visible, and `e2e/refresh.spec.ts` runs expectTappable over every <button> on `/`. Adding an
 * sr-only span, a count or a text chevron inside the <h2> breaks the first two; shrinking the
 * button breaks the last. The lucide icon is safe there because an <svg> contributes nothing to
 * textContent or to the accessible name.
 *
 * `aria-expanded` on the button is the whole announcement — deliberately no role="status" and no
 * aria-live anywhere in this component. role="status" in this app belongs to the session check
 * (components/require-administrator.tsx).
 *
 * Plan 019 added the optional `arrange` controls. They are siblings of the <h2>, never children, for
 * the first two reasons above; and `undefined` renders NOTHING — not an empty wrapper, not a
 * disabled pair — so on every page view that is not actively rearranging the markup is byte-for-byte
 * what plan 018 shipped. They are typed props rather than a `headerActions?: ReactNode` slot on
 * purpose: this component's comment claims ownership of the 40px floor, and a free-form slot would
 * let a caller drop a 24px button in here and turn e2e/refresh.spec.ts red from another file.
 */
export function CollapsibleSection({
  headingId,
  heading,
  label,
  collapsed,
  onToggle,
  summary,
  arrange,
  children,
}: {
  headingId: string
  heading: string
  /**
   * The name the move buttons are addressed by, when it must not be the heading. Defaults to
   * `heading`. Weather's heading carries the configured place — "Weather · Kitchen" — and an
   * aria-label built from household data is a name that moves under the tests' feet and changes the
   * moment an administrator renames the location.
   */
  label?: string
  collapsed: boolean
  onToggle: () => void
  /** Shown beside the heading while collapsed, for probe sections only. */
  summary?: string
  /** Present only while the dashboard is in arrange mode. */
  arrange?: SectionArrangeControls
  children: ReactNode
}) {
  const panelId = `${headingId}-panel`
  const Chevron = collapsed ? ChevronRight : ChevronDown

  return (
    <section aria-labelledby={headingId} className="flex flex-col gap-[10px]">
      {/* flex-wrap, not a fixed row: a long group name and its summary must wrap rather than push
          the document sideways — e2e/layout.spec.ts asserts no horizontal overflow at a Pixel 7. */}
      <div className="flex flex-wrap items-center gap-x-3">
        <h2 id={headingId} className={SECTION_LABEL}>
          <button
            type="button"
            onClick={onToggle}
            aria-expanded={!collapsed}
            aria-controls={panelId}
            // min-h-10/min-w-10 is the 40px tap-target floor e2e/helpers.ts's expectTappable
            // enforces over EVERY button on `/`, and a 12px label does not reach it on its own.
            // The taller header row costs the design brief's "section label sits 10px above its
            // panel"; the floor is enforced by the gate and the 10px is enforced by nobody.
            // -mx-2 puts the padded hit area back in optical alignment with the panel below.
            className="-mx-2 inline-flex min-h-10 min-w-10 items-center gap-2 rounded-md px-2 text-left hover:text-text"
          >
            <Chevron aria-hidden="true" size={14} className="shrink-0" />
            {heading}
          </button>
        </h2>
        {collapsed && summary !== undefined && summary !== '' ? (
          <span className="mono text-[12.5px] text-muted">{summary}</span>
        ) : null}
        {/* The name lives in an sr-only span INSIDE each button — the icon-only pattern
            components/theme-toggle.tsx already uses. Inside the <h2> instead, it would break
            e2e/dashboard-groups.spec.ts's exact textContent match and every
            getByRole('region', { name }) that reads this heading through aria-labelledby. */}
        {arrange === undefined ? null : (
          <span className="ml-auto flex shrink-0 items-center gap-1">
            <button type="button" onClick={arrange.onMoveUp} disabled={!arrange.canMoveUp} className={ARRANGE_BUTTON}>
              <ArrowUp aria-hidden="true" size={16} />
              <span className="sr-only">Move {label ?? heading} up</span>
            </button>
            <button
              type="button"
              onClick={arrange.onMoveDown}
              disabled={!arrange.canMoveDown}
              className={ARRANGE_BUTTON}
            >
              <ArrowDown aria-hidden="true" size={16} />
              <span className="sr-only">Move {label ?? heading} down</span>
            </button>
          </span>
        )}
      </div>
      {/* `hidden`, not a conditional render: aria-controls must point at an element that exists.
          This wrapper takes NO className — a Tailwind display utility (flex/grid/block) beats
          [hidden]'s display:none and would leave the panel on screen while the button announced
          it collapsed. */}
      <div id={panelId} hidden={collapsed}>
        {children}
      </div>
    </section>
  )
}
