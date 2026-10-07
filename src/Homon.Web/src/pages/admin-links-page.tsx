import { useEffect, useRef, useState, type FormEvent } from 'react'
import { ExternalLink, Pencil, Plus, Trash2 } from 'lucide-react'

import {
  ALERT,
  BUTTON_PRIMARY,
  BUTTON_SECONDARY,
  CARD_FORM,
  COLUMN_HEAD,
  EMPTY_STATE,
  FIELD_INPUT,
  FIELD_LABEL,
  INLINE_FORM,
  PANEL,
  ROW,
  ROW_CELLS,
} from '@/components/admin-classes'
import { AdminPageHeader, jumpToField } from '@/components/admin-page-header'
import { AdminSection } from '@/components/admin-section'
import { ConfirmStrip } from '@/components/confirm-strip'
import { IconButton, MoveButtons } from '@/components/icon-button'
import { countOf } from '@/lib/admin-summary'
import { problemDetail } from '@/lib/api'
import {
  useCreateLink,
  useDeleteLink,
  useLinks,
  useReorderLinks,
  useUpdateLink,
  type Link,
  type LinkFields,
} from '@/lib/links'
import { useDocumentTitle, pageTitle } from '@/lib/use-document-title'

/*
 * One grid per row, so every row and the column head share this template or the columns stop
 * lining up — hence the fixed 176px action column (four 40px buttons and a divider) rather than
 * `auto`. Below `lg` a row folds, as the Probes page's does: position and title on the first line,
 * address and description on the second, actions on the third.
 */
const ROW_GRID =
  'grid grid-cols-[24px_minmax(0,1fr)_auto] items-center gap-x-3 gap-y-1 lg:grid-cols-[28px_minmax(0,0.9fr)_minmax(0,1.1fr)_minmax(0,1.2fr)_176px] lg:gap-x-3.5'

/**
 * The links admin page: the dashboard's link list in the order shown, each row with move / edit /
 * delete. Edit opens the form inside the row and Delete asks first; the add form waits at the
 * bottom.
 */
export function AdminLinksPage() {
  useDocumentTitle(pageTitle('Links', 'Admin'))

  const links = useLinks()
  const reorderLinks = useReorderLinks()

  const orderedLinks = links.data ?? []

  const [editingId, setEditingId] = useState<string | null>(null)
  const [confirmingDeleteId, setConfirmingDeleteId] = useState<string | null>(null)

  function moveLink(id: string, direction: -1 | 1) {
    const ids = orderedLinks.map((link) => link.id)
    const index = ids.indexOf(id)
    const swapWith = index + direction

    if (swapWith < 0 || swapWith >= ids.length) {
      return
    }

    const next = [...ids]
    ;[next[index], next[swapWith]] = [next[swapWith], next[index]]
    reorderLinks.mutate(next)
  }

  return (
    <>
      <AdminPageHeader
        title="Links"
        description="The link list on the dashboard, in the order shown here."
        count={countOf(orderedLinks.length, 'link')}
      >
        {editingId === null ? (
          <button type="button" onClick={() => jumpToField('link-title')} className={BUTTON_PRIMARY}>
            <Plus aria-hidden="true" size={16} strokeWidth={2.25} />
            New link
          </button>
        ) : null}
      </AdminPageHeader>
      <AdminSection id="links" heading="On the dashboard" meta="Each opens in a new tab">
        {orderedLinks.length === 0 ? (
          <p className={EMPTY_STATE}>No links yet. Add one below.</p>
        ) : (
          <div className={PANEL}>
            <div aria-hidden="true" className={`${ROW_GRID} ${COLUMN_HEAD}`}>
              <span className="text-right">#</span>
              <span>Link</span>
              <span>Address</span>
              <span>Description</span>
              <span className="text-right">Actions</span>
            </div>
            <ol aria-label="Links" className="flex list-none flex-col">
              {orderedLinks.map((link, index) => (
                <LinkRow
                  key={link.id}
                  link={link}
                  position={index + 1}
                  isFirst={index === 0}
                  isLast={index === orderedLinks.length - 1}
                  isEditing={editingId === link.id}
                  isConfirmingDelete={confirmingDeleteId === link.id}
                  onMove={(direction) => moveLink(link.id, direction)}
                  onEdit={() => {
                    setConfirmingDeleteId(null)
                    setEditingId((current) => (current === link.id ? null : link.id))
                  }}
                  onDoneEditing={() => setEditingId(null)}
                  onAskDelete={() => {
                    setEditingId(null)
                    setConfirmingDeleteId((current) => (current === link.id ? null : link.id))
                  }}
                  onCancelDelete={() => setConfirmingDeleteId(null)}
                />
              ))}
            </ol>
          </div>
        )}
      </AdminSection>
      {/*
        Not rendered while a row is being edited: LinkForm's inputs carry fixed ids, so two at once
        would hand both "Title" labels to whichever input came first.
      */}
      {editingId === null ? <LinkForm key="new-link" link={null} variant="card" onDoneEditing={() => undefined} /> : null}
    </>
  )
}

function LinkRow({
  link,
  position,
  isFirst,
  isLast,
  isEditing,
  isConfirmingDelete,
  onMove,
  onEdit,
  onDoneEditing,
  onAskDelete,
  onCancelDelete,
}: {
  link: Link
  position: number
  isFirst: boolean
  isLast: boolean
  isEditing: boolean
  isConfirmingDelete: boolean
  onMove: (direction: -1 | 1) => void
  onEdit: () => void
  onDoneEditing: () => void
  onAskDelete: () => void
  onCancelDelete: () => void
}) {
  const deleteLink = useDeleteLink()
  const editorId = `link-editor-${link.id}`
  const confirmId = `link-confirm-delete-${link.id}`

  return (
    <li className={ROW}>
      <div className={`${ROW_GRID} ${ROW_CELLS} ${isEditing ? 'bg-bg' : ''}`}>
        <span className="mono col-start-1 row-start-1 text-right text-[13px] text-muted lg:col-start-auto lg:row-start-auto">
          {String(position).padStart(2, '0')}
        </span>
        <span className="col-span-2 col-start-2 row-start-1 flex min-w-0 lg:col-span-1 lg:col-start-auto lg:row-start-auto">
          <a
            href={link.url}
            target="_blank"
            rel="noopener noreferrer"
            className="inline-flex min-w-0 items-center gap-1.5 text-[15px] font-semibold text-text no-underline hover:underline"
          >
            <span className="truncate">{link.title}</span>
            <ExternalLink aria-hidden="true" size={13} strokeWidth={2} className="shrink-0 text-muted" />
          </a>
        </span>
        {/* One line under the title below lg; two cells of the grid at lg (`contents`). */}
        <span className="col-span-2 col-start-2 flex min-w-0 gap-1.5 text-[13px] text-muted lg:contents">
          <span className="mono min-w-0 truncate" title={link.url}>
            {link.url}
          </span>
          {link.description ? (
            <>
              <span aria-hidden="true" className="lg:hidden">
                ·
              </span>
              <span className="min-w-0 truncate lg:text-[13.5px]" title={link.description}>
                {link.description}
              </span>
            </>
          ) : (
            <span aria-hidden="true" className="hidden lg:inline">
              —
            </span>
          )}
        </span>
        <span className="col-span-3 flex items-center justify-end lg:col-span-1">
          <MoveButtons name={link.title} isFirst={isFirst} isLast={isLast} onMove={onMove} />
          <IconButton
            icon={Pencil}
            label={`Edit ${link.title}`}
            title="Edit"
            onClick={onEdit}
            aria-expanded={isEditing}
            aria-controls={isEditing ? editorId : undefined}
          />
          <IconButton
            icon={Trash2}
            tone="danger"
            label={`Delete ${link.title}`}
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
          question={`Delete ${link.title}?`}
          confirmLabel={`Confirm delete ${link.title}`}
          cancelLabel={`Cancel delete ${link.title}`}
          onConfirm={() => deleteLink.mutate(link.id, { onSuccess: onCancelDelete })}
          onCancel={onCancelDelete}
          isPending={deleteLink.isPending}
        />
      ) : null}
      {isEditing ? (
        <div id={editorId}>
          <LinkForm link={link} variant="inline" onDoneEditing={onDoneEditing} />
        </div>
      ) : null}
    </li>
  )
}

/**
 * Adds a link (`variant="card"`, the panel at the bottom of the page) or edits one
 * (`variant="inline"`, opened inside that link's row). Same fields either way; the inline one
 * drops the panel chrome and takes focus, since the button that opened it is right above.
 */
function LinkForm({
  link,
  variant,
  onDoneEditing,
}: {
  link: Link | null
  variant: 'card' | 'inline'
  onDoneEditing: () => void
}) {
  const createLink = useCreateLink()
  const updateLink = useUpdateLink()
  const isEditing = link !== null
  const isInline = variant === 'inline'
  const Heading = isInline ? 'h3' : 'h2'
  const activeMutation = isEditing ? updateLink : createLink
  const titleRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    if (isInline) {
      titleRef.current?.focus()
    }
  }, [isInline])

  const [title, setTitle] = useState(link?.title ?? '')
  const [url, setUrl] = useState(link?.url ?? '')
  const [description, setDescription] = useState(link?.description ?? '')

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const trimmed: LinkFields = {
      title: title.trim(),
      url: url.trim(),
      description: description.trim(),
    }

    if (trimmed.title.length === 0 || trimmed.url.length === 0) {
      return
    }

    if (isEditing) {
      updateLink.mutate({ id: link.id, fields: trimmed }, { onSuccess: onDoneEditing })
      return
    }

    createLink.mutate(trimmed, {
      onSuccess: () => {
        setTitle('')
        setUrl('')
        setDescription('')
      },
    })
  }

  return (
    <form
      onSubmit={onSubmit}
      aria-labelledby="link-form-heading"
      className={isInline ? INLINE_FORM : CARD_FORM}
    >
      <Heading id="link-form-heading" className={isInline ? 'text-[14px] font-semibold' : 'text-[15px] font-semibold'}>
        {isEditing ? `Edit ${link.title}` : 'New link'}
      </Heading>
      {/* As many 200px-or-wider columns as fit, so three fields never strand one on its own row. */}
      <div className="grid grid-cols-[repeat(auto-fit,minmax(min(100%,200px),1fr))] gap-4">
        <p className="flex flex-col gap-1">
          <label htmlFor="link-title" className={FIELD_LABEL}>
            Title
          </label>
          <input
            ref={titleRef}
            id="link-title"
            required
            value={title}
            onChange={(event) => setTitle(event.target.value)}
            className={FIELD_INPUT}
          />
        </p>
        <p className="flex flex-col gap-1">
          <label htmlFor="link-url" className={FIELD_LABEL}>
            URL
          </label>
          <input
            id="link-url"
            type="url"
            required
            value={url}
            onChange={(event) => setUrl(event.target.value)}
            className={FIELD_INPUT}
          />
        </p>
        <p className="flex flex-col gap-1">
          <label htmlFor="link-description" className={FIELD_LABEL}>
            Description
          </label>
          <input
            id="link-description"
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            className={FIELD_INPUT}
          />
        </p>
      </div>
      {activeMutation.isError ? (
        <p role="alert" className={ALERT}>
          {problemDetail(activeMutation.error) ?? 'Could not save the link. Try again.'}
        </p>
      ) : null}
      <p className="flex flex-wrap items-center gap-2">
        <button type="submit" disabled={activeMutation.isPending} className={BUTTON_PRIMARY}>
          {isEditing ? 'Save changes' : 'Add link'}
        </button>
        {isEditing ? (
          <button type="button" onClick={onDoneEditing} className={BUTTON_SECONDARY}>
            Cancel
          </button>
        ) : (
          <span className="text-[13px] text-muted">Goes to the bottom of the list.</span>
        )}
      </p>
    </form>
  )
}
