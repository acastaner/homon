import { useState } from 'react'
import { Link } from 'react-router'

import { useAdminPages, useDeletePage } from '@/lib/pages'
import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'

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
      <h1>Pages</h1>
      {orderedPages.length === 0 ? (
        <p>No pages yet. Add one below.</p>
      ) : (
        <ol aria-label="Pages">
          {orderedPages.map((page) => (
            <li key={page.id}>
              <strong>{page.title}</strong> — {page.isPublished ? 'Published' : 'Draft'}{' '}
              <Link to={`/admin/pages/${page.id}`}>Edit {page.title}</Link>{' '}
              {confirmingId === page.id ? (
                <>
                  <button
                    type="button"
                    onClick={() =>
                      deletePage.mutate(page.id, { onSuccess: () => setConfirmingId(null) })
                    }
                  >
                    Confirm delete {page.title}
                  </button>{' '}
                  <button type="button" onClick={() => setConfirmingId(null)}>
                    Cancel delete {page.title}
                  </button>
                </>
              ) : (
                <button type="button" onClick={() => setConfirmingId(page.id)}>
                  Delete {page.title}
                </button>
              )}
            </li>
          ))}
        </ol>
      )}
      <p>
        <Link to="/admin/pages/new">New page</Link>
      </p>
    </>
  )
}
