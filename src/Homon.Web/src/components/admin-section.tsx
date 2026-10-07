import type { ReactNode } from 'react'

/**
 * One titled region of an admin page: an uppercase heading, a muted line of meta on its right,
 * then whatever the section holds. A `<section>` named by its own heading, so it is a landmark
 * a screen reader can jump to and a test can scope to with `getByRole('region', { name })`.
 * Lifted from the Probes page's section panel (plan 024).
 */
export function AdminSection({
  id,
  heading,
  meta,
  label,
  children,
}: {
  /** Prefix for the heading's id (`${id}-heading`); unique within the page. */
  id: string
  heading: string
  meta?: ReactNode
  /**
   * Names the region something other than its heading. Only for the one case that needs it: the
   * Pages list's "Published" section. `e2e/pages.spec.ts` (a net file, not to be edited) does
   * `getByLabel('Published')` right after clicking Edit, while the list page is still on screen;
   * getByLabel matches `aria-labelledby` too, so a region named "Published" is found there and
   * the checkbox lookup fails strict mode before the editor has rendered.
   */
  label?: string
  children: ReactNode
}) {
  const headingId = `${id}-heading`

  return (
    <section
      {...(label === undefined ? { 'aria-labelledby': headingId } : { 'aria-label': label })}
      className="flex flex-col gap-2.5"
    >
      <div className="flex items-baseline justify-between gap-3">
        <h2 id={headingId} className="text-[12px] font-semibold uppercase tracking-[0.12em] text-muted">
          {heading}
        </h2>
        {meta ? <span className="text-[12.5px] text-muted">{meta}</span> : null}
      </div>
      {children}
    </section>
  )
}
