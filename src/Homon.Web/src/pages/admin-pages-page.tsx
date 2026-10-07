import { useState } from 'react'
import { Link } from 'react-router'
import { ExternalLink, Eye, FilePen, Pencil, Plus, Trash2 } from 'lucide-react'

import {
  BUTTON_PRIMARY,
  COLUMN_HEAD,
  EMPTY_STATE,
  ICON_BUTTON,
  PANEL,
  ROW,
  ROW_CELLS,
  ROW_TINT,
} from '@/components/admin-classes'
import { AdminPageHeader } from '@/components/admin-page-header'
import { AdminSection } from '@/components/admin-section'
import { ConfirmStrip } from '@/components/confirm-strip'
import { IconButton } from '@/components/icon-button'
import { Stamp } from '@/components/stamp'
import { StatusChip } from '@/components/status-chip'
import { summarisePages } from '@/lib/admin-summary'
import { useAdminPages, useDeletePage, type AdminPageSummary } from '@/lib/pages'
import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'

/*
 * One grid per row, so every row and the column head share this template or the columns stop
 * lining up. Below `lg` a row folds as the Probes page's does: title and state on the first line,
 * address and last-saved on the second, actions on the third. The state column comes first at
 * `lg` and last on the phone, which is why its cell carries explicit column and row starts.
 */
const ROW_GRID =
  'grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-3 gap-y-1 lg:grid-cols-[132px_minmax(0,1fr)_minmax(0,1fr)_160px_176px] lg:gap-x-3.5'

/**
 * The pages admin page: every page, published ones first and drafts under them, each with open /
 * edit / delete. Unlike Links, the editor is not a form inside the row — a TipTap editor needs its
 * own room, so create and edit are separate routes (admin-page-editor-page.tsx), and "Edit" here
 * is a link to one.
 */
export function AdminPagesPage() {
  useDocumentTitle(pageTitle('Pages', 'Admin'))

  const pages = useAdminPages()
  const orderedPages = pages.data ?? []
  const published = orderedPages.filter((page) => page.isPublished)
  const drafts = orderedPages.filter((page) => !page.isPublished)

  const [confirmingId, setConfirmingId] = useState<string | null>(null)

  return (
    <>
      <AdminPageHeader
        title="Pages"
        description="Free-form pages readers open from the dashboard. A draft is visible only here until it is published."
        count={summarisePages(orderedPages).text}
      >
        <Link to="/admin/pages/new" className={BUTTON_PRIMARY}>
          <Plus aria-hidden="true" size={16} strokeWidth={2.25} />
          New page
        </Link>
      </AdminPageHeader>
      {orderedPages.length === 0 ? <p className={EMPTY_STATE}>No pages yet.</p> : null}
      {published.length > 0 ? (
        <PageSection
          id="published"
          heading="Published"
          meta="Linked from the dashboard"
          label="Published pages"
          pages={published}
          confirmingId={confirmingId}
          onConfirmingChange={setConfirmingId}
        />
      ) : null}
      {drafts.length > 0 ? (
        <PageSection
          id="drafts"
          heading="Drafts"
          meta="Only administrators see these"
          label="Draft pages"
          pages={drafts}
          confirmingId={confirmingId}
          onConfirmingChange={setConfirmingId}
        />
      ) : null}
    </>
  )
}

function PageSection({
  id,
  heading,
  meta,
  label,
  pages,
  confirmingId,
  onConfirmingChange,
}: {
  id: string
  heading: string
  meta: string
  label: string
  pages: AdminPageSummary[]
  confirmingId: string | null
  onConfirmingChange: (id: string | null) => void
}) {
  return (
    <AdminSection id={id} heading={heading} meta={meta}>
      <div className={PANEL}>
        <div aria-hidden="true" className={`${ROW_GRID} ${COLUMN_HEAD}`}>
          <span>State</span>
          <span>Page</span>
          <span>Address</span>
          <span>Last saved</span>
          <span className="text-right">Actions</span>
        </div>
        <ol aria-label={label} className="flex list-none flex-col">
          {pages.map((page) => (
            <PageRow
              key={page.id}
              page={page}
              isConfirmingDelete={confirmingId === page.id}
              onAskDelete={() => onConfirmingChange(confirmingId === page.id ? null : page.id)}
              onCancelDelete={() => onConfirmingChange(null)}
            />
          ))}
        </ol>
      </div>
    </AdminSection>
  )
}

function PageRow({
  page,
  isConfirmingDelete,
  onAskDelete,
  onCancelDelete,
}: {
  page: AdminPageSummary
  isConfirmingDelete: boolean
  onAskDelete: () => void
  onCancelDelete: () => void
}) {
  const deletePage = useDeletePage()
  const confirmId = `page-confirm-delete-${page.id}`

  return (
    <li className={ROW}>
      <div className={`${ROW_GRID} ${ROW_CELLS} ${page.isPublished ? '' : ROW_TINT.paused}`}>
        <span className="col-start-2 row-start-1 flex lg:col-start-auto lg:row-start-auto">
          {page.isPublished ? (
            <StatusChip state="up" word="Published" glyph={Eye} />
          ) : (
            <StatusChip state="paused" word="Draft" glyph={FilePen} />
          )}
        </span>
        <span className="col-start-1 row-start-1 flex min-w-0 lg:col-start-auto lg:row-start-auto">
          <Link
            to={`/admin/pages/${page.id}`}
            className={`truncate text-[15px] font-semibold no-underline hover:underline ${page.isPublished ? 'text-text' : 'text-muted'}`}
          >
            {page.title}
          </Link>
        </span>
        {/* One line under the title below lg; two cells of the grid at lg (`contents`). */}
        <span className="col-span-2 flex min-w-0 gap-1.5 text-[13px] text-muted lg:contents">
          <span className="mono min-w-0 truncate" title={`/pages/${page.slug}`}>
            /pages/{page.slug}
          </span>
          <span aria-hidden="true" className="lg:hidden">
            ·
          </span>
          <span className="shrink-0">
            <Stamp iso={page.updatedAt} />
          </span>
        </span>
        <span className="col-span-2 flex items-center justify-end lg:col-span-1">
          {page.isPublished ? (
            <>
              <a
                href={`/pages/${page.slug}`}
                target="_blank"
                rel="noopener noreferrer"
                aria-label={`Open ${page.title} as readers see it`}
                title="Open as readers see it"
                className={ICON_BUTTON}
              >
                <ExternalLink aria-hidden="true" size={16} strokeWidth={2} />
              </a>
              <span aria-hidden="true" className="mx-1.5 h-5 w-px bg-line" />
            </>
          ) : null}
          {/* A link, not a button: it navigates to the editor's own route (e2e/pages.spec.ts reaches it as one). */}
          <Link to={`/admin/pages/${page.id}`} aria-label={`Edit ${page.title}`} title="Edit" className={ICON_BUTTON}>
            <Pencil aria-hidden="true" size={16} strokeWidth={2} />
          </Link>
          <IconButton
            icon={Trash2}
            tone="danger"
            label={`Delete ${page.title}`}
            title="Delete"
            onClick={onAskDelete}
            aria-expanded={isConfirmingDelete}
            aria-controls={isConfirmingDelete ? confirmId : undefined}
          />
        </span>
      </div>
      {isConfirmingDelete ? (
        <ConfirmStrip
          id={confirmId}
          question={`Delete ${page.title}?`}
          confirmLabel={`Confirm delete ${page.title}`}
          cancelLabel={`Cancel delete ${page.title}`}
          onConfirm={() => deletePage.mutate(page.id, { onSuccess: onCancelDelete })}
          onCancel={onCancelDelete}
          isPending={deletePage.isPending}
        />
      ) : null}
    </li>
  )
}
