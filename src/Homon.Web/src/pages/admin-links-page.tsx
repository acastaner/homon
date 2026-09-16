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

/**
 * The links admin page: reorder with up/down buttons, edit or delete a row, and add a new
 * one through a single shared form at the bottom of the list — not per-row or modal editing,
 * the simplest shape that satisfies "add, edit, delete, reorder" without a component the
 * design pass has to undo (plan 006).
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
      <h1>Links</h1>
      {orderedLinks.length === 0 ? (
        <p>No links yet.</p>
      ) : (
        <ol aria-label="Links">
          {orderedLinks.map((link, index) => (
            <li key={link.id}>
              <a href={link.url} target="_blank" rel="noopener noreferrer">
                {link.title}
              </a>
              {link.description ? <span> {link.description}</span> : null}{' '}
              <button
                type="button"
                onClick={() => moveLink(link.id, -1)}
                disabled={index === 0}
              >
                Move {link.title} up
              </button>{' '}
              <button
                type="button"
                onClick={() => moveLink(link.id, 1)}
                disabled={index === orderedLinks.length - 1}
              >
                Move {link.title} down
              </button>{' '}
              <button type="button" onClick={() => startEditing(link)}>
                Edit {link.title}
              </button>{' '}
              <button type="button" onClick={() => deleteLink.mutate(link.id)}>
                Delete {link.title}
              </button>
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
    <form onSubmit={onSubmit} aria-labelledby="link-form-heading">
      <h2 id="link-form-heading">{editingId === null ? 'Add a link' : 'Edit link'}</h2>
      <p>
        <label htmlFor="link-title">Title</label>
        <input
          id="link-title"
          required
          value={fields.title}
          onChange={(event) => onFieldsChange({ ...fields, title: event.target.value })}
        />
      </p>
      <p>
        <label htmlFor="link-url">URL</label>
        <input
          id="link-url"
          type="url"
          required
          value={fields.url}
          onChange={(event) => onFieldsChange({ ...fields, url: event.target.value })}
        />
      </p>
      <p>
        <label htmlFor="link-description">Description</label>
        <input
          id="link-description"
          value={fields.description}
          onChange={(event) => onFieldsChange({ ...fields, description: event.target.value })}
        />
      </p>
      {activeMutation.isError ? (
        <p role="alert">{problemDetail(activeMutation.error) ?? 'Could not save the link. Try again.'}</p>
      ) : null}
      <p>
        <button type="submit" disabled={activeMutation.isPending}>
          {editingId === null ? 'Add link' : 'Save changes'}
        </button>{' '}
        {editingId === null ? null : (
          <button type="button" onClick={onCancel}>
            Cancel
          </button>
        )}
      </p>
    </form>
  )
}
