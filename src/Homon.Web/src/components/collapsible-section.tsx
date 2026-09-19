import { ChevronDown, ChevronRight } from 'lucide-react'
import type { ReactNode } from 'react'

/** `docs/design-brief.md`'s Section label rule: 12px/600/0.12em/uppercase. */
const SECTION_LABEL = 'text-[12px] font-semibold uppercase tracking-[0.12em] text-muted'

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
 */
export function CollapsibleSection({
  headingId,
  heading,
  collapsed,
  onToggle,
  summary,
  children,
}: {
  headingId: string
  heading: string
  collapsed: boolean
  onToggle: () => void
  /** Shown beside the heading while collapsed, for probe sections only. */
  summary?: string
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
