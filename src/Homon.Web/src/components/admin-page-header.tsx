import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { ChevronLeft } from 'lucide-react'

/**
 * The header every admin page opens with: the h1 and a sentence on the left, a count and the
 * page's actions on the right, a strong rule underneath. Lifted from the Probes page (plan 024)
 * so the eight pages that followed it stop re-typing the markup.
 *
 * `back` exists for the page editor alone: it is a child of `/admin/pages`, not a section of its
 * own, so it says where up is instead of leaving the reader to find the sidebar.
 */
export function AdminPageHeader({
  title,
  description,
  count,
  back,
  children,
}: {
  title: string
  description?: ReactNode
  count?: string
  back?: { to: string; label: string }
  children?: ReactNode
}) {
  return (
    <div className="flex flex-col gap-4 border-b border-line-strong pb-4 lg:flex-row lg:items-end lg:justify-between">
      <div className="flex flex-col gap-1.5">
        {back ? (
          <Link
            to={back.to}
            className="inline-flex w-fit items-center gap-1 text-[13px] text-muted no-underline hover:text-text"
          >
            <ChevronLeft aria-hidden="true" size={14} strokeWidth={2} />
            {back.label}
          </Link>
        ) : null}
        <h1 className="text-[22px] font-semibold -tracking-[0.01em] sm:text-[26px]">{title}</h1>
        {description ? <p className="max-w-[680px] text-[14px] text-muted">{description}</p> : null}
      </div>
      {count !== undefined || children ? (
        <div className="flex flex-wrap items-center gap-3">
          {count !== undefined ? <span className="mono text-[13px] text-muted">{count}</span> : null}
          {children}
        </div>
      ) : null}
    </div>
  )
}

/**
 * The header's "New X" button: the add form sits at the bottom of what can be a long page, so the
 * button scrolls to it and puts the cursor in its first field (`inputId`). The form is the same
 * element either way; this only moves the reader to it.
 */
export function jumpToField(inputId: string) {
  const input = document.getElementById(inputId)
  input?.scrollIntoView({ behavior: 'smooth', block: 'center' })
  input?.focus({ preventScroll: true })
}
