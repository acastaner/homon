import { useState } from 'react'
import { Link } from 'react-router'

import { useAdminPages, useDeletePage } from '@/lib/pages'
import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'

const PAGE_H1 = 'border-b border-line-strong pb-4 text-[22px] font-semibold -tracking-[0.01em] sm:text-[26px]'
const BUTTON_SECONDARY =
  'inline-flex h-10 items-center justify-center rounded-md border border-line px-3 text-[13.5px] font-medium text-text hover:border-line-strong disabled:pointer-events-none disabled:opacity-50'
const BUTTON_DANGER =
  'inline-flex h-10 items-center justify-center rounded-md border border-down/40 bg-down-bg px-3 text-[13.5px] font-medium text-down hover:bg-down/20'
const BUTTON_PRIMARY =
  'inline-flex h-10 items-center justify-center rounded-md bg-text px-4 text-[14px] font-medium text-bg hover:opacity-90 disabled:pointer-events-none disabled:opacity-50'

/**
 * The pages admin page: a list of every page (published and draft) with per-row edit/delete
 * actions, and a link to the editor's own create route. Unlike Links, the editor is not a
 * shared form at the bottom of this list — a TipTap editor needs its own room, so create and
 * edit are separate routes (admin-page-editor-page.tsx).
 */
export function AdminPagesPage() {
  useDocumentTitle(pageTitle('Pages', 'Admin'))

  const pages = useAdminPages()
  const deletePage = useDeletePage()

  const orderedPages = pages.data ?? []

  const [confirmingId, setConfirmingId] = useState<string | null>(null)

  return (
    <>
      <h1 className={PAGE_H1}>Pages</h1>
      {orderedPages.length === 0 ? (
        <p className="rounded-md border border-dashed border-line-strong bg-surface px-4 py-3.5 text-[13.5px] text-muted">
          No pages yet. Add one below.
        </p>
      ) : (
        <ol aria-label="Pages" className="flex list-none flex-col divide-y divide-line rounded-md border border-line bg-surface px-4">
          {orderedPages.map((page) => (
            <li key={page.id} className="flex flex-col gap-2 py-3">
              <p className="text-[15px]">
                <strong className="font-semibold">{page.title}</strong>{' '}
                <span className="text-muted">— {page.isPublished ? 'Published' : 'Draft'}</span>
              </p>
              <p className="flex flex-wrap items-center gap-2">
                <Link to={`/admin/pages/${page.id}`} className={BUTTON_SECONDARY}>
                  Edit {page.title}
                </Link>
                {confirmingId === page.id ? (
                  <>
                    <button
                      type="button"
                      onClick={() =>
                        deletePage.mutate(page.id, { onSuccess: () => setConfirmingId(null) })
                      }
                      className={BUTTON_DANGER}
                    >
                      Confirm delete {page.title}
                    </button>
                    <button type="button" onClick={() => setConfirmingId(null)} className={BUTTON_SECONDARY}>
                      Cancel delete {page.title}
                    </button>
                  </>
                ) : (
                  <button type="button" onClick={() => setConfirmingId(page.id)} className={BUTTON_SECONDARY}>
                    Delete {page.title}
                  </button>
                )}
              </p>
            </li>
          ))}
        </ol>
      )}
      <p>
        <Link to="/admin/pages/new" className={BUTTON_PRIMARY}>
          New page
        </Link>
      </p>
    </>
  )
}
