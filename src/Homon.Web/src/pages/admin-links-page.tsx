import { useState, type FormEvent } from 'react'

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

const EMPTY_FIELDS: LinkFields = { title: '', url: '', description: '' }

const PAGE_H1 = 'border-b border-line-strong pb-4 text-[22px] font-semibold -tracking-[0.01em] sm:text-[26px]'
const FIELD_LABEL = 'text-[13px] font-medium text-text'
const FIELD_INPUT =
  'h-10 w-full rounded-md border border-line bg-bg px-3 text-[14px] text-text outline-none focus:border-line-strong'
const BUTTON_SECONDARY =
  'inline-flex h-10 items-center justify-center rounded-md border border-line px-3 text-[13.5px] font-medium text-text hover:border-line-strong disabled:pointer-events-none disabled:opacity-50'
const BUTTON_PRIMARY =
  'inline-flex h-10 items-center justify-center rounded-md bg-text px-4 text-[14px] font-medium text-bg hover:opacity-90 disabled:pointer-events-none disabled:opacity-50'
const BUTTON_DANGER =
  'inline-flex h-10 items-center justify-center rounded-md border border-down/40 bg-down-bg px-3 text-[13.5px] font-medium text-down hover:bg-down/20'
const ALERT = 'rounded-md border border-down/40 bg-down-bg px-3 py-2 text-[14px] font-medium text-down'

/**
 * The links admin page: reorder with up/down buttons, edit or delete a row, and add a new
 * one through a single shared form at the bottom of the list.
 */
export function AdminLinksPage() {
  useDocumentTitle(pageTitle('Links', 'Admin'))

  const links = useLinks()
  const reorderLinks = useReorderLinks()
  const deleteLink = useDeleteLink()

  const orderedLinks = links.data ?? []

  const [editingId, setEditingId] = useState<string | null>(null)
  const [fields, setFields] = useState<LinkFields>(EMPTY_FIELDS)

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

  function startEditing(link: Link) {
    setEditingId(link.id)
    setFields({ title: link.title, url: link.url, description: link.description ?? '' })
  }

  function cancelEditing() {
    setEditingId(null)
    setFields(EMPTY_FIELDS)
  }

  return (
    <>
      <h1 className={PAGE_H1}>Links</h1>
      {orderedLinks.length === 0 ? (
        <p className="rounded-md border border-dashed border-line-strong bg-surface px-4 py-3.5 text-[13.5px] text-muted">
          No links yet.
        </p>
      ) : (
        <ol aria-label="Links" className="flex list-none flex-col divide-y divide-line rounded-md border border-line bg-surface px-4">
          {orderedLinks.map((link, index) => (
            <li key={link.id} className="flex flex-col gap-2 py-3">
              <p className="text-[15px]">
                <a
                  href={link.url}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="font-semibold text-text underline decoration-line-strong underline-offset-[3px] hover:decoration-text"
                >
                  {link.title}
                </a>
                {link.description ? <span className="ml-2 text-[14px] text-muted">{link.description}</span> : null}
              </p>
              <p className="flex flex-wrap gap-2">
                <button type="button" onClick={() => moveLink(link.id, -1)} disabled={index === 0} className={BUTTON_SECONDARY}>
                  Move {link.title} up
                </button>
                <button
                  type="button"
                  onClick={() => moveLink(link.id, 1)}
                  disabled={index === orderedLinks.length - 1}
                  className={BUTTON_SECONDARY}
                >
                  Move {link.title} down
                </button>
                <button type="button" onClick={() => startEditing(link)} className={BUTTON_SECONDARY}>
                  Edit {link.title}
                </button>
                <button type="button" onClick={() => deleteLink.mutate(link.id)} className={BUTTON_DANGER}>
                  Delete {link.title}
                </button>
              </p>
            </li>
          ))}
        </ol>
      )}
      <LinkForm
        fields={fields}
        onFieldsChange={setFields}
        editingId={editingId}
        onCancel={cancelEditing}
      />
    </>
  )
}

function LinkForm({
  fields,
  onFieldsChange,
  editingId,
  onCancel,
}: {
  fields: LinkFields
  onFieldsChange: (fields: LinkFields) => void
  editingId: string | null
  onCancel: () => void
}) {
  const createLink = useCreateLink()
  const updateLink = useUpdateLink()

  const activeMutation = editingId === null ? createLink : updateLink

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const trimmed: LinkFields = {
      title: fields.title.trim(),
      url: fields.url.trim(),
      description: fields.description.trim(),
    }

    if (trimmed.title.length === 0 || trimmed.url.length === 0) {
      return
    }

    if (editingId === null) {
      createLink.mutate(trimmed, { onSuccess: () => onFieldsChange(EMPTY_FIELDS) })
    } else {
      updateLink.mutate(
        { id: editingId, fields: trimmed },
        { onSuccess: () => { onFieldsChange(EMPTY_FIELDS); onCancel() } },
      )
    }
  }

  return (
    <form onSubmit={onSubmit} aria-labelledby="link-form-heading" className="flex flex-col gap-4 rounded-md border border-line bg-surface p-5">
      <h2 id="link-form-heading" className="text-[15px] font-semibold">
        {editingId === null ? 'Add a link' : 'Edit link'}
      </h2>
      <p className="flex flex-col gap-1">
        <label htmlFor="link-title" className={FIELD_LABEL}>
          Title
        </label>
        <input
          id="link-title"
          required
          value={fields.title}
          onChange={(event) => onFieldsChange({ ...fields, title: event.target.value })}
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
          value={fields.url}
          onChange={(event) => onFieldsChange({ ...fields, url: event.target.value })}
          className={FIELD_INPUT}
        />
      </p>
      <p className="flex flex-col gap-1">
        <label htmlFor="link-description" className={FIELD_LABEL}>
          Description
        </label>
        <input
          id="link-description"
          value={fields.description}
          onChange={(event) => onFieldsChange({ ...fields, description: event.target.value })}
          className={FIELD_INPUT}
        />
      </p>
      {activeMutation.isError ? (
        <p role="alert" className={ALERT}>
          {problemDetail(activeMutation.error) ?? 'Could not save the link. Try again.'}
        </p>
      ) : null}
      <p className="flex gap-2">
        <button type="submit" disabled={activeMutation.isPending} className={BUTTON_PRIMARY}>
          {editingId === null ? 'Add link' : 'Save changes'}
        </button>
        {editingId === null ? null : (
          <button type="button" onClick={onCancel} className={BUTTON_SECONDARY}>
            Cancel
          </button>
        )}
      </p>
    </form>
  )
}
